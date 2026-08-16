using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

public class DiningTable : BaseCounter
{
    public enum TableState
    {
        Free,
        Occupied,
        Dirty
    }

    public event EventHandler OnStateChanged;

    [Header("Настройки стульев")]
    [SerializeField] private Chair[] chairs;

    // Стулья, убранные через E (см. ToggleNearestChair) — ждут возврата тем же способом
    private readonly List<Chair> storedChairs = new List<Chair>();

    [Header("Настройки Грязной Посуды")]
    [SerializeField] private KitchenObjectSO dirtyPlateKitchenObjectSO; // Ссылка на Scriptable Object грязной тарелки
    [SerializeField] private Transform dirtyPlateVisualPrefab; // Префаб визуальной модели тарелки для стопки

    private TableState tableState;

    // Списки для отслеживания группы клиентов за столом
    private readonly List<CustomerAI> currentCustomers = new List<CustomerAI>();
    private readonly List<CustomerAI> finishedEatingCustomers = new List<CustomerAI>();
    
    // Список для хранения ссылок на заспавненные визуальные тарелки на столе
    private readonly List<GameObject> dirtyPlateVisualGameObjectList = new List<GameObject>();

    private int finishedEatingCountCached = 0; // Сколько гостей реально поели (для точного количества посуды)
    private int dirtyPlatesCount = 0; // Сколько грязных тарелок сейчас физически находится на столе

    protected override void Awake()
    {
        // ВАЖНО: раньше здесь был "private void Awake()" без base.Awake() —
        // это молча скрывало Awake() из BaseCounter (Unity вызывает только его),
        // и обеденные столы вообще не регистрировались в GridPositioningSystem.
        base.Awake();

        tableState = TableState.Free;

        // Делаем стулья дочерними объектами стола programmatically — тогда при переносе
        // стола (FurnitureMovingController или вообще любое изменение transform.position)
        // они едут вместе с ним автоматически через обычную иерархию Unity, независимо
        // от того, как стулья были расставлены в сцене изначально. worldPositionStays: true
        // сохраняет их текущее место в мире — при запуске сцены ничего визуально не дёрнется,
        // просто у объекта поменяется родитель в иерархии.
        if (chairs != null)
        {
            foreach (Chair chair in chairs)
            {
                if (chair != null)
                {
                    chair.transform.SetParent(transform, true);
                }
            }
        }
    }

    public bool HasFreeChair()
    {
        foreach (Chair chair in chairs)
        {
            if (!chair.IsOccupied())
            {
                return true;
            }
        }
        return false;
    }

    public Chair GetFreeChair()
    {
        foreach (Chair chair in chairs)
        {
            if (!chair.IsOccupied())
            {
                return chair;
            }
        }
        return null;
    }

    public bool IsAvailable()
    {
        return tableState == TableState.Free && dirtyPlatesCount == 0;
    }

    public bool CanAccommodateGroup(int groupSize)
    {
        return tableState == TableState.Free && chairs.Length >= groupSize && dirtyPlatesCount == 0;
    }

    public void OccupyTable(List<CustomerAI> customers)
    {
        if (customers.Count > chairs.Length)
        {
            Debug.LogError($"Стол {name} не может вместить группу из {customers.Count} человек!");
            return;
        }

        currentCustomers.Clear();
        finishedEatingCustomers.Clear();
        finishedEatingCountCached = 0;

        currentCustomers.AddRange(customers);
        SetOccupied();

        for (int i = 0; i < customers.Count; i++)
        {
            customers[i].SetTargetSeat(chairs[i], this);
        }
    }

    public void CustomerSeated(CustomerAI customer)
    {
        if (!currentCustomers.Contains(customer))
        {
            currentCustomers.Add(customer);
        }
        SetOccupied();
    }

    public void OnCustomerLeft(CustomerAI customer)
    {
        currentCustomers.Remove(customer);

        // Защита: Если один гость уходит (например, разозлился), принудительно уводим всю оставшуюся группу
        if (currentCustomers.Count > 0)
        {
            List<CustomerAI> remaining = new List<CustomerAI>(currentCustomers);
            foreach (CustomerAI remainingCustomer in remaining)
            {
                remainingCustomer.LeaveTable();
            }
        }

        if (currentCustomers.Count == 0)
        {
            // Если хотя бы один клиент за столом успел покушать — стол становится грязным
            if (finishedEatingCountCached > 0)
            {
                SetDirty();
                // Спавним стопку грязных тарелок по количеству реально покушавших гостей
                SpawnDirtyPlatesStack(finishedEatingCountCached);
                finishedEatingCountCached = 0;
            }
            else
            {
                // Если все ушли злыми и никто не ел, просто освобождаем стол без грязной посуды
                CleanTable();
            }
        }
    }

    public void OnCustomerFinishedEating(CustomerAI customer)
    {
        if (!finishedEatingCustomers.Contains(customer))
        {
            finishedEatingCustomers.Add(customer);
            finishedEatingCountCached++;
        }

        // Если абсолютно все сидящие за столом гости закончили кушать
        if (finishedEatingCustomers.Count >= currentCustomers.Count && currentCustomers.Count > 0)
        {
            List<CustomerAI> customersToLeave = new List<CustomerAI>(currentCustomers);
            foreach (CustomerAI c in customersToLeave)
            {
                c.LeaveTable();
            }
            finishedEatingCustomers.Clear();
        }
    }

    private void SpawnDirtyPlatesStack(int amount)
    {
        ClearDirtyPlatesVisuals();

        dirtyPlatesCount = amount;
        float plateOffsetY = 0.08f; // Высота смещения каждой тарелки в стопке

        for (int i = 0; i < amount; i++)
        {
            // Спавним визуальный префаб тарелки дочерним объектом к точке стола counterTopPoint (GetKitchenObjectFollowTransform)
            Transform plateVisualTransform = Instantiate(dirtyPlateVisualPrefab, GetKitchenObjectFollowTransform());
            plateVisualTransform.localPosition = new Vector3(0, plateOffsetY * i, 0);
            plateVisualTransform.localRotation = Quaternion.identity;

            dirtyPlateVisualGameObjectList.Add(plateVisualTransform.gameObject);
        }

        Debug.Log($"На столе {name} появилась стопка из {amount} грязных тарелок.");
    }

    private void ClearDirtyPlatesVisuals()
    {
        foreach (GameObject visual in dirtyPlateVisualGameObjectList)
        {
            if (visual != null) Destroy(visual);
        }
        dirtyPlateVisualGameObjectList.Clear();
        dirtyPlatesCount = 0;
    }

    public bool TryServe(RecipeSO recipeSO)
    {
        foreach (CustomerAI customer in currentCustomers)
        {
            if (customer.TryDeliver(recipeSO))
            {
                return true;
            }
        }
        return false;
    }

    public override void Interact(Player player)
    {
        if (player == null) return;

        // В фазе подготовки, с пустыми руками, E на столе переключает ближайший стул
        // (убрать/вернуть) вместо обычной подачи блюда — подавать всё равно некому,
        // клиентов в этой фазе не бывает. Если в руках что-то есть — ведём себя как раньше,
        // мало ли для чего это понадобится (например, забрать что-то со стола).
        if (GameLoopManager.Instance != null
            && GameLoopManager.Instance.IsPreparationActive()
            && !player.HasKitchenObject())
        {
            ToggleNearestChair(player.transform.position);
            return;
        }

        // === СОСТОЯНИЕ 1: ЗА СТОЛОМ СИДЯТ КЛИЕНТЫ ===
        if (IsOccupied())
        {
            // Если игрок принес еду на тарелке
            if (player.HasKitchenObject())
            {
                if (player.GetKitchenObject().TryGetPlate(out PlateKitchenObject plateKitchenObject))
                {
                    // Передаем проверку и доставку менеджеру доставки
                    if (DeliveryManager.Instance != null)
                    {
                        if (DeliveryManager.Instance.TryDeliverRecipeToTable(plateKitchenObject, this))
                        {
                            // Золото начисляется внутри DeliveryManager.TryDeliverRecipeToTable —
                            // там же, где известна реальная стоимость рецепта (recipeSO.Cost).
                            Debug.Log("Заказ успешно передан клиенту за столом!");
                        }
                        else
                        {
                            Debug.Log("Никто за этим столом не заказывал такое блюдо!");
                        }
                    }
                }
            }
            return;
        }

        // === СОСТОЯНИЕ 2: КЛИЕНТЫ УШЛИ, НА СТОЛЕ ГРЯЗНАЯ ПОСУДА ===
        if (IsDirty() && dirtyPlatesCount > 0)
        {
            if (!player.HasKitchenObject())
            {
                // У игрока пустые руки — берем ОДНУ верхнюю грязную тарелку
                dirtyPlatesCount--;

                // Спавним реальную грязную тарелку в руки игроку
                KitchenObject.SpawnKitchenObject(dirtyPlateKitchenObjectSO, player);

                // Удаляем визуальную верхнюю тарелку со стола
                if (dirtyPlateVisualGameObjectList.Count > 0)
                {
                    GameObject topPlateVisual = dirtyPlateVisualGameObjectList[dirtyPlateVisualGameObjectList.Count - 1];
                    dirtyPlateVisualGameObjectList.Remove(topPlateVisual);
                    Destroy(topPlateVisual);
                }

                // Если все тарелки убраны со стола — он чист!
                if (dirtyPlatesCount <= 0)
                {
                    CleanTable();
                }

                Debug.Log($"Игрок забрал грязную тарелку. Осталось на столе: {dirtyPlatesCount}");
            }
            else
            {
                Debug.Log("Ваши руки заняты, вы не можете взять посуду!");
            }
        }
    }

    public void SetFree()
    {
        tableState = TableState.Free;
        OnStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetOccupied()
    {
        tableState = TableState.Occupied;
        OnStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetDirty()
    {
        tableState = TableState.Dirty;
        OnStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CleanTable()
    {
        foreach (Chair chair in chairs)
        {
            if (chair != null) chair.ClearCustomer();
        }

        currentCustomers.Clear();
        finishedEatingCustomers.Clear();
        finishedEatingCountCached = 0;
        ClearDirtyPlatesVisuals();

        SetFree();
    }

    public bool IsDirty() => tableState == TableState.Dirty;
    public bool IsOccupied() => tableState == TableState.Occupied;
    public TableState GetState() => tableState;
    public int GetCustomerCount() => currentCustomers.Count;
    public List<CustomerAI> GetCustomers() => currentCustomers;
    public Chair[] GetChairs() => chairs;

    /// <summary>
    /// Ищет ближайший к указанной позиции СВОБОДНЫЙ (на месте) стул, ничего не меняя.
    /// </summary>
    public Chair GetNearestFreeChair(Vector3 fromPosition)
    {
        if (chairs == null || chairs.Length == 0) return null;

        Chair nearest = null;
        float nearestDistance = float.MaxValue;

        foreach (Chair chair in chairs)
        {
            if (chair == null || chair.IsOccupied()) continue;

            float distance = Vector3.Distance(chair.transform.position, fromPosition);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = chair;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Ищет ближайший к указанной позиции УБРАННЫЙ (отложенный) стул. Стул неактивен,
    /// но его transform.position всё равно доступен для запроса — SetActive(false)
    /// не обнуляет позицию, просто выключает рендер/коллайдер/обновления.
    /// </summary>
    public Chair GetNearestStoredChair(Vector3 fromPosition)
    {
        if (storedChairs.Count == 0) return null;

        Chair nearest = null;
        float nearestDistance = float.MaxValue;

        foreach (Chair chair in storedChairs)
        {
            if (chair == null) continue;

            float distance = Vector3.Distance(chair.transform.position, fromPosition);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = chair;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Решает, что должно произойти при нажатии E рядом с указанной позицией: убрать
    /// ближайший стул на месте или вернуть ближайший убранный — смотря что физически
    /// ближе к игроку прямо сейчас. Общая точка правды и для ToggleNearestChair(),
    /// и для подсветки (IsRemoveTarget) — чтобы они не могли разойтись между собой.
    /// </summary>
    private Chair GetCurrentChairActionTarget(Vector3 fromPosition, out bool isRemove)
    {
        Chair nearestFree = GetNearestFreeChair(fromPosition);
        Chair nearestStored = GetNearestStoredChair(fromPosition);

        if (nearestFree == null && nearestStored == null)
        {
            isRemove = false;
            return null;
        }

        float freeDistance = nearestFree != null ? Vector3.Distance(nearestFree.transform.position, fromPosition) : float.MaxValue;
        float storedDistance = nearestStored != null ? Vector3.Distance(nearestStored.transform.position, fromPosition) : float.MaxValue;

        if (storedDistance < freeDistance)
        {
            isRemove = false;
            return nearestStored;
        }

        isRemove = true;
        return nearestFree;
    }

    /// <summary>
    /// Убирает/возвращает стул, смотря что ближе к игроку — по одной кнопке E можно
    /// убрать НЕСКОЛЬКО разных стульев подряд (просто подходя к каждому по очереди),
    /// а не только один: раньше любой отложенный стул блокировал повторное убирание,
    /// теперь решение принимается per-стулу, а не "есть ли вообще что-то отложенное".
    /// </summary>
    public void ToggleNearestChair(Vector3 fromPosition)
    {
        Chair target = GetCurrentChairActionTarget(fromPosition, out bool isRemove);

        if (target == null)
        {
            Debug.Log($"[DiningTable] '{name}': стульев нет вообще — ни на месте, ни убранных.");
            return;
        }

        if (isRemove)
        {
            RemoveSpecificChair(target);
        }
        else
        {
            ReturnSpecificChair(target);
        }
    }

    /// <summary>
    /// Для подсветки (ChairHighlightVisual): является ли конкретный стул тем, который
    /// уберётся при нажатии E прямо сейчас.
    /// </summary>
    public bool IsRemoveTarget(Chair chair, Vector3 fromPosition)
    {
        Chair target = GetCurrentChairActionTarget(fromPosition, out bool isRemove);
        return isRemove && target == chair;
    }

    /// <summary>
    /// Убирает конкретный стул (а не "ближайший к позиции") и откладывает его —
    /// чтобы позже можно было вернуть тем же стулом. Массив chairs пересобирается
    /// без него, а не просто зануляется — HasFreeChair()/GetFreeChair() выше по файлу
    /// не проверяют chairs на null, дыра в массиве была бы риском вылета в рантайме.
    /// </summary>
    private void RemoveSpecificChair(Chair chair)
    {
        if (chair == null) return;

        List<Chair> remaining = new List<Chair>(chairs);
        if (!remaining.Remove(chair)) return;
        chairs = remaining.ToArray();

        // Стул остаётся дочерним объектом стола (просто неактивным), поэтому его позиция
        // относительно стола не теряется — даже если стол потом передвинут, стул при
        // возврате появится там же, где и должен, без отдельного хранения координат.
        chair.gameObject.SetActive(false);
        storedChairs.Add(chair);

        Debug.Log($"[DiningTable] '{name}': стул убран, осталось мест — {chairs.Length}.");
    }

    /// <summary>
    /// Возвращает конкретный убранный стул (а не обязательно последний по очереди).
    /// </summary>
    private void ReturnSpecificChair(Chair chair)
    {
        if (chair == null) return;
        if (!storedChairs.Remove(chair)) return;

        chair.gameObject.SetActive(true);

        List<Chair> updated = new List<Chair>(chairs);
        updated.Add(chair);
        chairs = updated.ToArray();

        Debug.Log($"[DiningTable] '{name}': стул возвращён, мест теперь — {chairs.Length}.");
    }

    public int GetStoredChairCount() => storedChairs.Count;

    [Header("Проверка места для стульев (после переноса стола)")]
    [Tooltip("Радиус проверки — примерный физический размер стула")]
    [SerializeField] private float chairFitCheckRadius = 0.3f;
    [Tooltip("На какой высоте от пола проверяется место — чтобы не задеть коллайдер самого пола")]
    [SerializeField] private float chairFitCheckHeightOffset = 0.4f;
    [Tooltip("Какие слои считаются помехой; по умолчанию — все. Сузьте, если пол/декор даёт ложные срабатывания")]
    [SerializeField] private LayerMask chairFitCheckMask = ~0;

    /// <summary>
    /// Физическая проверка через Physics.OverlapSphere — есть ли что-то постороннее
    /// (стена, другой стол, прилавок) там, где сейчас стоит стул. Коллайдеры самого
    /// стола и соседних стульев игнорируются через IsChildOf — все они дочерние
    /// объекты этого же стола, иначе стул считал бы помехой собственный стол.
    /// </summary>
    private bool HasRoomForChair(Chair chair)
    {
        Vector3 checkPosition = chair.transform.position + Vector3.up * chairFitCheckHeightOffset;
        Collider[] overlaps = Physics.OverlapSphere(checkPosition, chairFitCheckRadius, chairFitCheckMask);

        foreach (Collider col in overlaps)
        {
            if (col.transform == chair.transform) continue;
            if (col.transform == transform || col.transform.IsChildOf(transform)) continue;

            return false;
        }

        return true;
    }

    /// <summary>
    /// Вызывается FurnitureMovingController'ом сразу после того, как стол успешно
    /// переставлен. Каждый текущий (не убранный ранее) стул проверяется физически —
    /// если на новом месте стола рядом с его позицией что-то мешает, стул убирается
    /// автоматически, тем же способом, что и вручную через E. Снимок списка нужен,
    /// поскольку RemoveSpecificChair меняет chairs изнутри перебора.
    /// </summary>
    public void RemoveChairsWithoutRoom()
    {
        if (chairs == null || chairs.Length == 0) return;

        List<Chair> chairsSnapshot = new List<Chair>(chairs);

        foreach (Chair chair in chairsSnapshot)
        {
            if (chair == null) continue;

            if (!HasRoomForChair(chair))
            {
                Debug.Log($"[DiningTable] '{name}': стулу не хватило места после переноса стола — убран автоматически.");
                RemoveSpecificChair(chair);
            }
        }
    }
}