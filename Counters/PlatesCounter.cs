using UnityEngine;
using System;

public class PlatesCounter : BaseCounter
{
    public event EventHandler OnPlateSpawned;
    public event EventHandler OnPlateRemoved;

    [SerializeField] private KitchenObjectSO plateKitchenObjectSO;

    private int plateSpawnAmount = 4; // Начальный стек посуды
    private int platesSpawnAmountMax = 4; 
    private bool isInitialized = false;

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
