using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
public class StoveCounter : BaseCounter,IHasProgress
{
public event EventHandler<IHasProgress.OnProgressChangedEventArgs> OnProgressChanged;
public event EventHandler <OnStateChangedEventArgs> OnStateChanged;
public class OnStateChangedEventArgs: EventArgs
    {
        public State state;
    }
public enum State
    {
        Idle,
        Frying,
        Fried,
        Burned,
    }
[SerializeField] private FryingRecipeSO[] fryingRecipeSOarray;
[SerializeField] private BurningRecipeSO[] burningRecipeSOarray;
private State state;
private float fryingTimer;
private float burningTimer;
private FryingRecipeSO fryingRecipeSO;
private BurningRecipeSO burningRecipeSO;
private void Start()
    {
        state=State.Idle;
    }
private void Update()
    {
    if (HasKitchenObject()){
        switch (state)
        {
            case State.Idle:
            break;
            case State.Frying:     
            fryingTimer+=Time.deltaTime;
            OnProgressChanged?.Invoke(this,new IHasProgress.OnProgressChangedEventArgs
                {
                   progressNormalized=fryingTimer/fryingRecipeSO.fryingTimerMax
                });
            if (fryingTimer > fryingRecipeSO.fryingTimerMax)
            {
                //fired
                GetKitchenObject().DestroySelf();
                KitchenObject.SpawnKitchenObject(fryingRecipeSO.output,this);
                Debug.Log("Fryed"); 
                
        
                burningTimer=0f;
                burningRecipeSO=GetBurningRecipeSOWithInput(GetKitchenObject().GetKitchenObjectSo());
                state=State.Fried;
                OnStateChanged?.Invoke(this,new OnStateChangedEventArgs
                {
                    state=state
                });
            }
            break;
            case State.Fried:
            burningTimer+=Time.deltaTime;
            OnProgressChanged?.Invoke(this,new IHasProgress.OnProgressChangedEventArgs
                {
                   progressNormalized=burningTimer/burningRecipeSO.burningTimerMax
                });
            if (burningTimer > burningRecipeSO.burningTimerMax)
            {
                //fired
                GetKitchenObject().DestroySelf();
                KitchenObject.SpawnKitchenObject(burningRecipeSO.output,this);
                Debug.Log("Burned"); 
                state=State.Burned;
                 OnStateChanged?.Invoke(this,new OnStateChangedEventArgs
                {
                    state=state
                });
                OnProgressChanged?.Invoke(this,new IHasProgress.OnProgressChangedEventArgs
                {
                   progressNormalized=0f
                });
            }
            break;
            case State.Burned:
            break;

        }

    }
}
        
public override void Interact(Player player)
    {
        if(!HasKitchenObject()){
            if(player.HasKitchenObject()){
                if(HasRecipeWithInput(player.GetKitchenObject().GetKitchenObjectSo())){
                player.GetKitchenObject().SetKitchenObjectParent(this);
                fryingRecipeSO=GetFryingRecipeSOWithInput(GetKitchenObject().GetKitchenObjectSo());  
                state=State.Frying;
                fryingTimer=0f;
                 OnStateChanged?.Invoke(this,new OnStateChangedEventArgs
                {
                    state=state
                });
                }
                //player carry smth that can be Fried
                
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
                       state=State.Idle;
                    OnStateChanged?.Invoke(this,new OnStateChangedEventArgs
                    {
                        state=state
                    });
                    OnProgressChanged?.Invoke(this,new IHasProgress.OnProgressChangedEventArgs
                    {
                    progressNormalized=0f
                    }); 
                    }
                    
                }
                //player carry smth
            }else{
                //player not carry anything
                GetKitchenObject().SetKitchenObjectParent(player);
                state=State.Idle;
                 OnStateChanged?.Invoke(this,new OnStateChangedEventArgs
                {
                    state=state
                });
                OnProgressChanged?.Invoke(this,new IHasProgress.OnProgressChangedEventArgs
                {
                   progressNormalized=0f
                });
            }
        }
    }
    private bool HasRecipeWithInput(KitchenObjectSO inputkitchenObjectSO){
        FryingRecipeSO fryingRecipeSO=GetFryingRecipeSOWithInput(inputkitchenObjectSO);
        return fryingRecipeSO != null;
    }


    
    private KitchenObjectSO GetOutputForInput(KitchenObjectSO inputkitchenObjectSO){
        FryingRecipeSO fryingRecipeSO=GetFryingRecipeSOWithInput(inputkitchenObjectSO);
        if (fryingRecipeSO != null)
        {
            return fryingRecipeSO.output;
        }
        else
        {
            return null;
        }
    }
    private FryingRecipeSO GetFryingRecipeSOWithInput(KitchenObjectSO inputkitchenObjectSO)
    {
        foreach(FryingRecipeSO fryingRecipeSO in fryingRecipeSOarray){
            if(fryingRecipeSO.input==inputkitchenObjectSO){
                return fryingRecipeSO;
            }
        }
        return null; 
    }
    private BurningRecipeSO GetBurningRecipeSOWithInput(KitchenObjectSO inputkitchenObjectSO)
    {
        foreach(BurningRecipeSO burningRecipeSO in burningRecipeSOarray){
            if(burningRecipeSO.input==inputkitchenObjectSO){
                return burningRecipeSO;
            }
        }
        return null; 
    }  
}
