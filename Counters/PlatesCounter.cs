using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
public class PlatesCounter : BaseCounter
{
public event EventHandler OnPlateSpawned;
public event EventHandler OnPlateRemoved;
[SerializeField] private KitchenObjectSO plateKitchenObjectSO;
private float spawnPlateTimer;
private float spawnPlateTimerMax=4f;


private int plateSpawnAmount=0;
private int platesSpawnAmountMax=4;
private void Update()
    {
        spawnPlateTimer+=Time.deltaTime;
        if (spawnPlateTimer > spawnPlateTimerMax)
        {
        spawnPlateTimer=0;
            if (plateSpawnAmount < platesSpawnAmountMax)
            {
                plateSpawnAmount++;
                OnPlateSpawned?.Invoke(this,EventArgs.Empty);
            }
        }
    }
public override void Interact(Player player)
    {
        if (!player.HasKitchenObject())
        {
            //Player Empty handed
            if (plateSpawnAmount > 0)
            {
                //atleast 1 plate
                plateSpawnAmount--;
                KitchenObject.SpawnKitchenObject(plateKitchenObjectSO,player);
                OnPlateRemoved?.Invoke(this,EventArgs.Empty);
            }
        }
    }
}
