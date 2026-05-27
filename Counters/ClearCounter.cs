using UnityEngine;
using System.Collections;
using System.Collections.Generic;
public class ClearCounter : BaseCounter
{
    [SerializeField] private KitchenObjectSO kitchenObjectSO;
    

    
    public override void Interact(Player player){
        if(!HasKitchenObject()){
            if(player.HasKitchenObject()){
                //player carry smth
                player.GetKitchenObject().SetKitchenObjectParent(this);
            }else{
                //player has nothing
            }
            //NoObject
        }else{
            //Object
            if(player.HasKitchenObject()){
                if(player.GetKitchenObject().TryGetPlate(out PlateKitchenObject plateKitchenObject))
                {
                        //player holding plate
                    if(plateKitchenObject.AddIngredient(GetKitchenObject().GetKitchenObjectSo())){
                       GetKitchenObject().DestroySelf(); 
                    }

                }
                else
                {
                    //Player is not holding plate but smthg else
                    if(GetKitchenObject().TryGetPlate(out plateKitchenObject))
                    {
                        //Counter holding a plate
                        if(plateKitchenObject.AddIngredient(player.GetKitchenObject().GetKitchenObjectSo())){
                             player.GetKitchenObject().DestroySelf();
                        } 
                    }
                }
                //player carry smth
            }else{
                //player not carry anything
                GetKitchenObject().SetKitchenObjectParent(player);
            }
        }
    }

}
