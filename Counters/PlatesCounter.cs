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

    public override void Interact(Player player)
    {
        if (!player.HasKitchenObject())
        {
            if (plateSpawnAmount > 0)
            {
                plateSpawnAmount--;
                KitchenObject.SpawnKitchenObject(plateKitchenObjectSO, player);
                OnPlateRemoved?.Invoke(this, EventArgs.Empty);
            }
        }
        else
        {
            // Возврат чистой тарелки в стопку
            if (player.GetKitchenObject().TryGetPlate(out PlateKitchenObject plateKitchenObject))
            {
                if (plateKitchenObject.GetKitchenObjectSOList().Count == 0)
                {
                    if (plateSpawnAmount < platesSpawnAmountMax)
                    {
                        player.GetKitchenObject().DestroySelf(); 
                        
                        plateSpawnAmount++;
                        OnPlateSpawned?.Invoke(this, EventArgs.Empty); 
                    }
                }
            }
        }
    }
}
