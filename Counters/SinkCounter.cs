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

    public override void InteractAlternate(Player player)
    {
        if (HasKitchenObject() && GetKitchenObject().GetKitchenObjectSo() == dirtyPlateKitchenObjectSO)
        {
            isWashing = GameInput.Instance.IsInteractAlternatePressed();
            OnWashingChanged?.Invoke(this, new OnWashingChangedEventArgs { isWashing = isWashing });

            if (!isWashing)
            {
                OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs 
                { 
                    progressNormalized = washProgress / washTimeMax 
                });
            }
        }
    }
}
