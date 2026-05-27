using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
public class PlateKitchenObject : KitchenObject
{
public event EventHandler<OnIngredientAddedEventArgs> OnIngredientAdded;
public class OnIngredientAddedEventArgs: EventArgs
    {
        public KitchenObjectSO kitchenObjectSO;
    }
[SerializeField] private List<KitchenObjectSO> validKitchenObjectSOList;

private List<KitchenObjectSO> kitchenObjectSOList;
private void Awake()
    {
        kitchenObjectSOList=new List<KitchenObjectSO>();
    }
public bool AddIngredient(KitchenObjectSO kitchenObjectSO)
    {
        if (!validKitchenObjectSOList.Contains(kitchenObjectSO))
        {
            return false;
        }
        if (kitchenObjectSOList.Contains(kitchenObjectSO))
        {
            //already has this type
            return false;
        }
        else
        {
            kitchenObjectSOList.Add(kitchenObjectSO);
            OnIngredientAdded?.Invoke(this, new OnIngredientAddedEventArgs
            {
               kitchenObjectSO=kitchenObjectSO 
            });
            return true;
        }
        
               
    }
    public List<KitchenObjectSO> GetKitchenObjectSOList()
    {
        return kitchenObjectSOList;
    }
}
