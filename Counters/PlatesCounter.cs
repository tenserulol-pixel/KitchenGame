using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

public class PlatesCounter : BaseCounter
{
    public event EventHandler OnPlateSpawned;
    public event EventHandler OnPlateRemoved;

    [SerializeField] private KitchenObjectSO plateKitchenObjectSO;

    private int plateSpawnAmount = 4; // Начинаем сразу с 4 тарелками
    private int platesSpawnAmountMax = 4; // Максимальная вместимость стопки
    private bool isInitialized = false;

    private void Update()
    {
        // На первом кадре игры вызываем события спавна для визуального отображения 4 тарелок.
        // Это предотвращает баг порядка выполнения Start() между этим скриптом и PlatesCounterVisual.
        if (!isInitialized)
        {
            isInitialized = true;
            for (int i = 0; i < plateSpawnAmount; i++)
            {
                OnPlateSpawned?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public override void Interact(Player player)
    {
        if (!player.HasKitchenObject())
        {
            // У игрока пустые руки — он берет одну чистую тарелку из стопки
            if (plateSpawnAmount > 0)
            {
                plateSpawnAmount--;
                KitchenObject.SpawnKitchenObject(plateKitchenObjectSO, player);
                OnPlateRemoved?.Invoke(this, EventArgs.Empty);
            }
        }
        else
        {
            // У игрока что-то есть в руках. Проверяем, тарелка ли это
            if (player.GetKitchenObject().TryGetPlate(out PlateKitchenObject plateKitchenObject))
            {
                // Проверяем, что тарелка абсолютно чистая (на ней нет никаких ингредиентов)
                if (plateKitchenObject.GetKitchenObjectSOList().Count == 0)
                {
                    // Проверяем, не переполнена ли стопка тарелок на столе
                    if (plateSpawnAmount < platesSpawnAmountMax)
                    {
                        // Игрок успешно кладет чистую тарелку обратно на стопку
                        player.GetKitchenObject().DestroySelf(); // Уничтожаем объект тарелки в руках игрока
                        
                        plateSpawnAmount++;
                        OnPlateSpawned?.Invoke(this, EventArgs.Empty); // Визуально добавляем тарелку обратно в стопку
                    }
                    else
                    {
                        Debug.Log("Стопка тарелок уже заполнена!");
                    }
                }
                else
                {
                    Debug.Log("Нельзя положить грязную тарелку или тарелку с едой обратно в стопку!");
                }
            }
        }
    }
}