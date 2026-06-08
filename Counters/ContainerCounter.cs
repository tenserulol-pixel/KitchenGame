using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
public class ContainerCounter : BaseCounter
{
    public event EventHandler OnPlayerGrabbedObject;
    [SerializeField] private KitchenObjectSO kitchenObjectSO;
    
    
    public override void Interact(Player player){ 
        if(!player.HasKitchenObject()){
        KitchenObject.SpawnKitchenObject(kitchenObjectSO,player);
        OnPlayerGrabbedObject?.Invoke(this,EventArgs.Empty);
        }
}
}
