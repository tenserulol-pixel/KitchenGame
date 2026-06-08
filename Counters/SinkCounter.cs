using UnityEngine;
using System;

public class SinkCounter : BaseCounter, IHasProgress
{
    public event EventHandler<IHasProgress.OnProgressChangedEventArgs> OnProgressChanged;
    
    // Событие для анимаций или эффектов (например, брызги воды)
    public event EventHandler<OnWashingChangedEventArgs> OnWashingChanged;
    public class OnWashingChangedEventArgs : EventArgs {
        public bool isWashing;
    }

    [Header("Настройки Раковины")]
    [SerializeField] private KitchenObjectSO dirtyPlateKitchenObjectSO; // Ссылка на грязную тарелку
    [SerializeField] private KitchenObjectSO cleanPlateKitchenObjectSO; // Ссылка на чистую тарелку
    [SerializeField] private float washTimeMax = 3.5f; // Сколько секунд мыть тарелку

    private float washProgress = 0f;
    private bool isWashing = false;

    private void Update()
    {
        // Если игрок моет и грязная тарелка находится в раковине
        if (isWashing && HasKitchenObject())
        {
            washProgress += Time.deltaTime;

            OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs
            {
                progressNormalized = washProgress / washTimeMax
            });

            // Мытье успешно завершено!
            if (washProgress >= washTimeMax)
            {
                washProgress = 0f;
                isWashing = false;
                
                OnWashingChanged?.Invoke(this, new OnWashingChangedEventArgs { isWashing = false });

                // Уничтожаем грязную тарелку
                GetKitchenObject().DestroySelf(); 

                // Спавним чистую тарелку прямо в раковину
                KitchenObject.SpawnKitchenObject(cleanPlateKitchenObjectSO, this);

                // Сбрасываем ползунок прогресса
                OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs { progressNormalized = 0f });
            }
        }
    }

    public override void Interact(Player player)
    {
        if (!HasKitchenObject())
        {
            // Раковина пуста — кладем грязную посуду из рук игрока
            if (player.HasKitchenObject())
            {
                // Проверяем, что игрок держит именно ту грязную тарелку, которую мы указали в инспекторе
                if (player.GetKitchenObject().GetKitchenObjectSo() == dirtyPlateKitchenObjectSO)
                {
                    player.GetKitchenObject().SetKitchenObjectParent(this);
                    
                    // Сбрасываем прогресс для новой тарелки
                    washProgress = 0f;
                    OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs { progressNormalized = 0f });
                }
            }
        }
        else
        {
            // В раковине что-то лежит
            if (!player.HasKitchenObject())
            {
                // У игрока пустые руки — он может забрать то, что в раковине (например, чистую тарелку)
                isWashing = false;
                OnWashingChanged?.Invoke(this, new OnWashingChangedEventArgs { isWashing = false });
                OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs { progressNormalized = 0f });

                GetKitchenObject().SetKitchenObjectParent(player);
            }
        }
    }

    public override void InteractAlternate(Player player)
    {
        // Проверяем, лежит ли в раковине именно грязная тарелка
        if (HasKitchenObject() && GetKitchenObject().GetKitchenObjectSo() == dirtyPlateKitchenObjectSO)
        {
            isWashing = GameInput.Instance.IsInteractAlternatePressed();
            
            OnWashingChanged?.Invoke(this, new OnWashingChangedEventArgs { isWashing = isWashing });

            // Если игрок отпустил кнопку — обновляем UI на текущее значение, чтобы полоска не прыгала
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