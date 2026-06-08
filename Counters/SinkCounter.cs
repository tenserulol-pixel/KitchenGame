using UnityEngine;
using System;

public class SinkCounter : BaseCounter, IHasProgress
{
    public event EventHandler<IHasProgress.OnProgressChangedEventArgs> OnProgressChanged;

    [Header("Настройки Раковины")]
    [SerializeField] private KitchenObjectSO cleanPlateKitchenObjectSO; // Scriptable Object чистой тарелки
    [SerializeField] private float washTimeMax = 3.5f; // Сколько секунд мыть тарелку
    [SerializeField] private string dirtyPlateObjectName = "Dirty Plate"; // Имя грязного объекта для проверки

    private float washProgress = 0f;
    private bool isWashing = false;

    private void Update()
    {
        // Если игрок моет и грязная тарелка в раковине
        if (isWashing && HasKitchenObject())
        {
            washProgress += Time.deltaTime;

            OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs
            {
                progressNormalized = washProgress / washTimeMax
            });

            // Мытье завершено!
            if (washProgress >= washTimeMax)
            {
                washProgress = 0f;
                isWashing = false;

                GetKitchenObject().DestroySelf(); // Удаляем грязную тарелку

                // Спавним чистую тарелку прямо в раковину
                KitchenObject.SpawnKitchenObject(cleanPlateKitchenObjectSO, this);

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
                if (player.GetKitchenObject().GetKitchenObjectSo().objectName == dirtyPlateObjectName)
                {
                    player.GetKitchenObject().SetKitchenObjectParent(this);
                }
            }
        }
        else
        {
            // В раковине что-то лежит — забираем руками (например, уже отмытую тарелку)
            if (!player.HasKitchenObject())
            {
                isWashing = false;
                washProgress = 0f;
                OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs { progressNormalized = 0f });

                GetKitchenObject().SetKitchenObjectParent(player);
            }
        }
    }

    public override void InteractAlternate(Player player)
    {
        // Проверяем, зажата ли кнопка (Пробел) и лежит ли грязная посуда
        if (HasKitchenObject() && GetKitchenObject().GetKitchenObjectSo().objectName == dirtyPlateObjectName)
        {
            isWashing = GameInput.Instance.IsInteractAlternatePressed();
            
            // Если игрок отпустил Пробел — сбрасываем состояние мытья
            if (!isWashing)
            {
                OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs { progressNormalized = washProgress / washTimeMax });
            }
        }
    }
}