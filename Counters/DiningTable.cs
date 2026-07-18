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

    private void Awake()
    {
        tableState = TableState.Free;
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
                            Debug.Log("Заказ успешно передан клиенту за столом!");
                            // За успешную выдачу начисляем золото
                            if (GameLoopManager.Instance != null)
                            {
                                GameLoopManager.Instance.AddOrderGold();
                            }
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
}