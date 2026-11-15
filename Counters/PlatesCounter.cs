using UnityEngine;
using System;

public class PlatesCounter : BaseCounter
{
    public event EventHandler OnPlateSpawned;
    public event EventHandler OnPlateRemoved;

    [SerializeField] private KitchenObjectSO plateKitchenObjectSO;

    [Header("Магазинный апгрейд «Самонаполняющаяся стопка» (SelfRefilling)")]
    [Tooltip("На сколько тарелок увеличивается максимум стопки после покупки апгрейда в лавке")]
    [SerializeField] private int selfRefillingBonusPlates = 4;

    private int plateSpawnAmount = 4; // Начальный стек посуды
    private int platesSpawnAmountMax = 4; 
    private bool isInitialized = false;

    // ★ Магазинный апгрейд: флаг (а не прибавление к полям) — метод UpgradeToSelfRefilling()
    // идемпотентен, поэтому безопасно вызывать его повторно при восстановлении сохранения.
    private bool isSelfRefillingUpgraded = false;

    private void Update()
    {
        // Инициализация визуальной стопки на первом кадре (для избежания рассинхронизации событий)
        if (!isInitialized)
        {
            isInitialized = true;
            for (int i = 0; i < plateSpawnAmount; i++)
            {
                OnPlateSpawned?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// ★ Магазинный апгрейд «Самонаполняющаяся стопка» (ShopManager → UpgradeEffect.SelfRefilling):
    /// максимум стопки растёт на selfRefillingBonusPlates, и эти тарелки сразу появляются
    /// (события OnPlateSpawned поднимают и визуальную стопку). Идемпотентен: повторный
    /// вызов (восстановление сохранения) ничего не добавляет второй раз.
    /// </summary>
    public void UpgradeToSelfRefilling()
    {
        if (isSelfRefillingUpgraded)
        {
            Debug.Log($"[PlatesCounter] '{name}': стопка уже расширена.");
            return;
        }

        isSelfRefillingUpgraded = true;

        platesSpawnAmountMax += selfRefillingBonusPlates;
        plateSpawnAmount = Mathf.Min(plateSpawnAmount + selfRefillingBonusPlates, platesSpawnAmountMax);

        // Двигаем визуальную стопку: сначала — тарелки, которые уже числились,
        // но не были показаны (isInitialized ещё не успел отработать), затем — бонусные.
        // isInitialized == false значит Update ещё не рисовал стек — тогда он сам
        // покажет всё по актуальному plateSpawnAmount, дёргать события нельзя (будет дубль).
        if (isInitialized)
        {
            for (int i = 0; i < selfRefillingBonusPlates; i++)
            {
                OnPlateSpawned?.Invoke(this, EventArgs.Empty);
            }
        }

        Debug.Log($"[PlatesCounter] '{name}': стопка расширена до {platesSpawnAmountMax} тарелок.");
    }

    /// <summary>
    /// ★ Конец дня: стопка чистой посуды пополняется ДО МАКСИМУМА — новая смена
    /// начинается с полным запасом тарелок, сколько бы их ни ушло на столы вчера.
    /// Вызывается из GameLoopManager.FinishActiveDay (вместе с очисткой столов
    /// и сбросом раковин). Грязная посуда при этом НЕ возвращается в стопку —
    /// она просто исчезает (см. ResetDishesForNewDay в GameLoopManager).
    /// </summary>
    public void RefillForNewDay()
    {
        int missing = platesSpawnAmountMax - plateSpawnAmount;
        if (missing <= 0)
        {
            return; // стопка уже полна
        }

        plateSpawnAmount = platesSpawnAmountMax;

        // Визуальную стопку растим только если она уже построена (isInitialized):
        // если первый Update ещё не отработал, он сам отрисует полный стек по
        // актуальному plateSpawnAmount — дёргать события нельзя (дубль тарелок).
        if (isInitialized)
        {
            for (int i = 0; i < missing; i++)
            {
                OnPlateSpawned?.Invoke(this, EventArgs.Empty);
            }
        }

        Debug.Log($"[PlatesCounter] '{name}': новая смена — стопка пополнена до " +
                  $"{plateSpawnAmount} тарелок (+{missing}).");
    }

    public override void Interact(Player player)
    {
        if (!player.HasKitchenObject())
        {
            // Пустые руки — берём чистую тарелку из стопки (как раньше)
            if (plateSpawnAmount > 0)
            {
                plateSpawnAmount--;
                KitchenObject.SpawnKitchenObject(plateKitchenObjectSO, player);
                OnPlateRemoved?.Invoke(this, EventArgs.Empty);
            }
            return;
        }

        KitchenObject heldObject = player.GetKitchenObject();

        // В руках тарелка — возвращаем её в стопку, только если она чистая (как раньше)
        if (heldObject.TryGetPlate(out PlateKitchenObject playerPlate))
        {
            if (playerPlate.GetKitchenObjectSOList().Count == 0
                && plateSpawnAmount < platesSpawnAmountMax)
            {
                playerPlate.DestroySelf();
                plateSpawnAmount++;
                OnPlateSpawned?.Invoke(this, EventArgs.Empty);
            }
            return;
        }

        // === НОВОЕ: предмет из рук сразу на тарелку ===
        // Игрок держит не тарелку (зелье из котла, готовый ингредиент и т.п.). Если в
        // стопке есть чистая тарелка — предмет кладётся прямо на неё, и тарелка с
        // блюдом остаётся в руках игрока: одна кнопка вместо двух (сначала брать
        // пустую тарелку, потом нести её к прилавку с едой).
        if (plateSpawnAmount <= 0)
        {
            return; // стопка пуста — класть не на что, ведём себя как обычный отказ
        }

        // Спавним тарелку из стопки на прилавок (на один кадр) — так проверка
        // валидности ингредиента идёт по СПИСКУ САМОЙ тарелки (validKitchenObjectSOList),
        // как и при обычном комбинировании на ClearCounter/котле.
        PlateKitchenObject freshPlate = KitchenObject.SpawnKitchenObject(plateKitchenObjectSO, this) as PlateKitchenObject;

        if (freshPlate == null)
        {
            // Сломанная конфигурация префаба: убираем заспавнившийся объект, чтобы
            // прилавок не остался занят «не-тарелкой» (кнопка E на нём перестала бы работать)
            if (HasKitchenObject())
            {
                GetKitchenObject().DestroySelf();
            }
            Debug.LogError($"[PlatesCounter] '{name}': префаб plateKitchenObjectSO не содержит " +
                           "компонент PlateKitchenObject — прямое выкладывание на тарелку невозможно.");
            return;
        }

        if (!freshPlate.AddIngredient(heldObject.GetKitchenObjectSo()))
        {
            // Предмет не предназначен для тарелки (нет в validKitchenObjectSOList) —
            // тарелка бесследно возвращается в стопку (счётчик не менялся)
            freshPlate.DestroySelf();
            KitchenObjectSO heldSO = heldObject.GetKitchenObjectSo();
            Debug.Log($"[PlatesCounter] '{(heldSO != null ? heldSO.objectName : heldObject.name)}' " +
                      "нельзя положить на тарелку.");
            return;
        }

        // Успех: предмет уходит на тарелку, тарелка с блюдом — в руки игроку.
        // Порядок важен: сначала освобождаем руки (DestroySelf), затем отдаём тарелку,
        // чтобы SetKitchenObjectParent не увидел занятый holdPoint игрока.
        heldObject.DestroySelf();

        plateSpawnAmount--;
        OnPlateRemoved?.Invoke(this, EventArgs.Empty);

        freshPlate.SetKitchenObjectParent(player);
    }
}
