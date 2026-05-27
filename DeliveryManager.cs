using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
public class DeliveryManager : MonoBehaviour
{
public event EventHandler OnRecipeSpawned;
public event EventHandler OnRecipeCompleted;
public static DeliveryManager Instance{get;private set;}
[SerializeField] private RecipeListSO recipeListSO;
private List<RecipeSO> waitingRecipeSOList;
private float spawnRecipeTimer;
private int waitingRecipesMax=4;
private float spawnRecipeTimerMax=4f;

private void Awake()
    {
        waitingRecipeSOList=new List<RecipeSO>();
        Instance=this;
    }
private void Update()
    {
        spawnRecipeTimer-=Time.deltaTime;
        if (spawnRecipeTimer <= 0f)
        {
            spawnRecipeTimer=spawnRecipeTimerMax;
            if (waitingRecipeSOList.Count < waitingRecipesMax)
            {
            RecipeSO waitingRecipeSO = recipeListSO.recipeSOList[UnityEngine.Random.Range(0,recipeListSO.recipeSOList.Count)];
            
            waitingRecipeSOList.Add(waitingRecipeSO); 
            OnRecipeSpawned?.Invoke(this,EventArgs.Empty);  
            }
   
        }
    }
public void DeliverRecipe(PlateKitchenObject plateKitchenObject)
    {
        for(int i=0;i<waitingRecipeSOList.Count; i++)
        {
            RecipeSO waitingRecipeSO=waitingRecipeSOList[i];
            if (waitingRecipeSO.kitchenObjectSOList.Count == plateKitchenObject.GetKitchenObjectSOList().Count)
            {
                //has same number of ingredients
                bool plateContentsMatchesRecipe =true;
                foreach(KitchenObjectSO recipeKitchenObjectSO in waitingRecipeSO.kitchenObjectSOList)
                {
                    bool ingredientFound=false;
                    //cyclig through all ingredients
                    foreach(KitchenObjectSO plateKitchenObjectSO in plateKitchenObject.GetKitchenObjectSOList())
                    {
                        //cyclig through all ingredients
                        if (plateKitchenObjectSO == recipeKitchenObjectSO)
                        {
                            //ingredient match
                            ingredientFound=true;
                            break;
                        }
                    }
                    if (!ingredientFound)
                    {
                        plateContentsMatchesRecipe=false;
                        //ingredient was not found on plate
                    }
                }
                if (plateContentsMatchesRecipe)
                {
                    //player deliver correct recipe
                    
                    
                    waitingRecipeSOList.RemoveAt(i);
                    OnRecipeCompleted?.Invoke(this,EventArgs.Empty);
                    return;
                }
            }
        }
        //No matches
        //player didn't deliver correct recipe
        
    }
    public List<RecipeSO> GetWaitingRecipeSOList()
    {
        return waitingRecipeSOList;
    }
}
