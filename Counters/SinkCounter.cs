using UnityEngine;
using System;

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

    private float washProgress = 0f;
    private bool isWashing = false;

    private void Update()
    {
        if (isWashing && HasKitchenObject())
        {
            washProgress += Time.deltaTime;

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
                    OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs { progressNormalized = 0f });
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

    // Мытьё теперь управляется через SetWashingState, вызываемый из Player.Update() каждый кадр,
    // пока зажата альтернативная кнопка — тем же способом, что и резка в CuttingCounter.
    // Отдельный InteractAlternate больше не переопределяем: одиночное нажатие само по себе
    // ничего не запускает (используется пустая реализация по умолчанию из BaseCounter).

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
}
