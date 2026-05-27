using UnityEngine;
using System.Collections;
using System.Collections.Generic;
public class BaseCounter : MonoBehaviour,IKitchenObjectParent
{

    [SerializeField] private Transform counterTopPoint;

    private KitchenObject kitchenObject;
     public virtual void InteractAlternate(Player player){

    }
    public virtual void Interact(Player player){

    }
    public Transform GetKitchenObjectFollowTransform(){
        return counterTopPoint;
    }
    
    public void SetKitchenObject(KitchenObject kitchenObject){
        this.kitchenObject=kitchenObject;
    }

    public KitchenObject GetKitchenObject(){
        return this.kitchenObject;
    }

    public void ClearKitchenObject(){
        kitchenObject=null;
    }

    public bool HasKitchenObject(){
        return kitchenObject!=null;
    }
}
