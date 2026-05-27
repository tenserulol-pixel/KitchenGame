using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
public class CuttingCounter : BaseCounter,IHasProgress
{
    public event EventHandler<IHasProgress.OnProgressChangedEventArgs> OnProgressChanged;

    public event EventHandler OnCut; 
    [SerializeField] private CuttingRecipeSO[] cuttingRecipeSOarray; 
    private int cuttingProgress;
    public override void Interact(Player player){
        if(!HasKitchenObject()){
            if(player.HasKitchenObject()){
                if(HasRecipeWithInput(player.GetKitchenObject().GetKitchenObjectSo())){
                player.GetKitchenObject().SetKitchenObjectParent(this);
                cuttingProgress=0;
                  CuttingRecipeSO cuttingRecipeSO=GetCuttingRecipeSOWithInput(GetKitchenObject().GetKitchenObjectSo());
                OnProgressChanged?.Invoke(this,new IHasProgress.OnProgressChangedEventArgs
                {
                    progressNormalized=(float)cuttingProgress/cuttingRecipeSO.cuttingProgressMax
                });   
                }
                //player carry smth that can be cut
                
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
                //player carry smth
            }else{
                //player not carry anything
                GetKitchenObject().SetKitchenObjectParent(player);
            }
        }
    }
    public override void InteractAlternate(Player player){
        if(HasKitchenObject()&&HasRecipeWithInput(GetKitchenObject().GetKitchenObjectSo())){
        cuttingProgress++;
        OnCut?.Invoke(this,EventArgs.Empty);

        CuttingRecipeSO cuttingRecipeSO=GetCuttingRecipeSOWithInput(GetKitchenObject().GetKitchenObjectSo());
        OnProgressChanged?.Invoke(this,new IHasProgress.OnProgressChangedEventArgs
                {
                    progressNormalized=(float)cuttingProgress/cuttingRecipeSO.cuttingProgressMax
                });  
            if (cuttingProgress >= cuttingRecipeSO.cuttingProgressMax)
            {
                KitchenObjectSO outputKitchenObjectSO=GetOutputForInput( GetKitchenObject().GetKitchenObjectSo());
                GetKitchenObject().DestroySelf();
                KitchenObject.SpawnKitchenObject(outputKitchenObjectSO,this);   
            }

        
    }
    }
    private bool HasRecipeWithInput(KitchenObjectSO inputkitchenObjectSO){
        CuttingRecipeSO cuttingRecipeSO=GetCuttingRecipeSOWithInput(inputkitchenObjectSO);
        return cuttingRecipeSO != null;
    }


    
    private KitchenObjectSO GetOutputForInput(KitchenObjectSO inputkitchenObjectSO){
        CuttingRecipeSO cuttingRecipeSO=GetCuttingRecipeSOWithInput(inputkitchenObjectSO);
        if (cuttingRecipeSO != null)
        {
            return cuttingRecipeSO.output;
        }
        else
        {
            return null;
        }
    }
    private CuttingRecipeSO GetCuttingRecipeSOWithInput(KitchenObjectSO inputkitchenObjectSO)
    {
        foreach(CuttingRecipeSO cuttingRecipeSO in cuttingRecipeSOarray){
            if(cuttingRecipeSO.input==inputkitchenObjectSO){
                return cuttingRecipeSO;
            }
        }
        return null; 
    }
}

