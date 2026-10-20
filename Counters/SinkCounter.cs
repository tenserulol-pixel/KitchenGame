using UnityEngine;
using System;
using System.Collections.Generic;

public class SinkCounter : BaseCounter, IHasProgress
{
    public event EventHandler<IHasProgress.OnProgressChangedEventArgs> OnProgressChanged;

    public event EventHandler<OnWashingChangedEventArgs> OnWashingChanged;
    public class OnWashingChangedEventArgs : EventArgs {
        public bool isWashing;
    }

    [Header("Настройки Раковины")]
    [SerializeField] private KitchenObjectSO dirtyPlateKitchenObjectSO; // Ссылка на SO грязной тарелки
    [SerializeField] private KitchenObjectSO cleanPlateKitchenObjectSO; // Ссылка на SO чистой тарелки
    [SerializeField] private float washTimeMax = 3.5f;

    [Header("Магазинный апгрейд «Очищенная вода» (PurifyingWater)")]
    [Tooltip("Во сколько раз быстрее мойка после покупки апгрейда в лавке")]
    [SerializeField] private float purifyingSpeedMultiplier = 2f;

    private float washProgress = 0f;
    private bool isWashing = false;

    // ★ Магазинный апгрейд: флаг (а не умножение поля) — метод UpgradeToPurifyingWater()
    // идемпотентен, поэтому безопасно вызывать его повторно при восстановлении сохранения.
    private bool isPurifyingUpgraded = false;

    // === Реестр «грязных тарелок» (общий для всех SinkCounter) ===
    // ★ Нужен TrashCounter'у: грязная тарелка — обычный KitchenObject, отличить её можно
    // только по KitchenObjectSO. Каждая раковина при Awake регистрирует свой
    // dirtyPlateKitchenObjectSO в общий статический набор, а TrashCounter через
    // IsDirtyPlateSO() отказывается уничтожать такую посуду (иначе — потерянная тарелка
    // навсегда и путь к софтлоку).
    private static readonly HashSet<KitchenObjectSO> knownDirtyPlateSOs = new HashSet<KitchenObjectSO>();

    /// <summary>True, если этот KitchenObjectSO — грязная тарелка (посуда, а не мусор).</summary>
    public static bool IsDirtyPlateSO(KitchenObjectSO kitchenObjectSO) =>
        kitchenObjectSO != null && knownDirtyPlateSOs.Contains(kitchenObjectSO);

    protected override void Awake()
    {
        // ВАЖНО: base.Awake() — регистрация в GridPositioningSystem. Без него сетка
        // не узнает об этой станции (тот же класс ошибок, что уже ловили на DiningTable/Puddle).
        base.Awake();

        if (dirtyPlateKitchenObjectSO != null)
        {
            knownDirtyPlateSOs.Add(dirtyPlateKitchenObjectSO);
        }
    }

    private void Update()
    {
        if (isWashing && HasKitchenObject())
        {
            // ★ Магазинный апгрейд «Очищенная вода»: мойка быстрее в purifyingSpeedMultiplier раз.
            washProgress += Time.deltaTime * GetWashSpeed();

            OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs
            {
                progressNormalized = washProgress / washTimeMax
            });

            if (washProgress >= washTimeMax)
            {
                washProgress = 0f;
                isWashing = false;

                OnWashingChanged?.Invoke(this, new OnWashingChangedEventArgs { isWashing = false });

                GetKitchenObject().DestroySelf();

                KitchenObject.SpawnKitchenObject(cleanPlateKitchenObjectSO, this);

                OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs { progressNormalized = 0f });
            }
        }
    }

    public override void Interact(Player player)
    {
        if (!HasKitchenObject())
        {
            if (player.HasKitchenObject())
            {
                if (player.GetKitchenObject().GetKitchenObjectSo() == dirtyPlateKitchenObjectSO)
                {
                    player.GetKitchenObject().SetKitchenObjectParent(this);

                    washProgress = 0f;
                    OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs
                    {
                        progressNormalized = 0f
                    });
                }
            }
        }
        else
        {
            if (!player.HasKitchenObject())
            {
                isWashing = false;
                OnWashingChanged?.Invoke(this, new OnWashingChangedEventArgs { isWashing = false });
                OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs { progressNormalized = 0f });

                GetKitchenObject().SetKitchenObjectParent(player);
            }
        }
    }

    // Мытьё управляется через SetWashingState, вызываемый из Player.Update() каждый кадр,
    // пока зажата альтернативная кнопка — тем же способом, что и резка в CuttingCounter.
    // ★ Player.cs (багфикс): при уходе игрока от раковины SetWashingState(false) теперь
    // гарантированно приходит — состояние «моют, пока игрока нет» больше невозможно.

    /// <summary>
    /// Вызывается из Player.cs каждый кадр с текущим состоянием кнопки (зажата/отпущена).
    /// </summary>
    public void SetWashingState(bool isHeld)
    {
        bool canWash = HasKitchenObject() && GetKitchenObject().GetKitchenObjectSo() == dirtyPlateKitchenObjectSO;

        if (isHeld && canWash)
        {
            if (!isWashing)
            {
                OnWashingChanged?.Invoke(this, new OnWashingChangedEventArgs { isWashing = true });
            }
            isWashing = true;
        }
        else
        {
            bool wasWashing = isWashing;
            isWashing = false;

            if (wasWashing)
            {
                OnWashingChanged?.Invoke(this, new OnWashingChangedEventArgs { isWashing = false });
            }

            // Если тарелку убрали (или это больше не грязная тарелка) — сбрасываем прогресс.
            // Если она всё ещё на месте, мытьё просто ставится на паузу, прогресс сохраняется,
            // как и при отпускании кнопки на разделочном столе.
            if (!canWash)
            {
                washProgress = 0f;
                OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs { progressNormalized = 0f });
            }
        }
    }

    /// <summary>
    /// ★ Магазинный апгрейд «Очищенная вода» (ShopManager → UpgradeEffect.PurifyingWater).
    /// Идемпотентен: повторный вызов (восстановление сохранения) ничего не ломает.
    /// </summary>
    public void UpgradeToPurifyingWater()
    {
        if (isPurifyingUpgraded)
        {
            Debug.Log($"[SinkCounter] '{name}': «Очищенная вода» уже установлена.");
            return;
        }

        isPurifyingUpgraded = true;
        Debug.Log($"[SinkCounter] '{name}': куплена «Очищенная вода» — мойка быстрее в {purifyingSpeedMultiplier}x.");
    }

    private float GetWashSpeed() => isPurifyingUpgraded ? purifyingSpeedMultiplier : 1f;
}
