using UnityEngine;
using System;
using System.Collections.Generic;

public class DeliveryManager : MonoBehaviour
{
    public event EventHandler OnRecipeSpawned;
    public event EventHandler OnRecipeCompleted;
    public static DeliveryManager Instance { get; private set; }

    public struct Order {
        public RecipeSO recipeSO;
        public DiningTable targetTable;
    }

    private List<Order> waitingOrderList;

    private void Awake()
    {
        waitingOrderList = new List<Order>();
        Instance = this;
    }

    public void AddOrderFromTable(RecipeSO recipeSO, DiningTable diningTable)
    {
        Order newOrder = new Order {
            recipeSO = recipeSO,
            targetTable = diningTable
        };
        
        waitingOrderList.Add(newOrder);
        OnRecipeSpawned?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveOrderFromTable(DiningTable diningTable)
    {
        for (int i = 0; i < waitingOrderList.Count; i++)
        {
            if (waitingOrderList[i].targetTable == diningTable)
            {
                waitingOrderList.RemoveAt(i);
                OnRecipeCompleted?.Invoke(this, EventArgs.Empty);
                break;
            }
        }
    }

    public void RemoveOrder(RecipeSO recipeSO, DiningTable diningTable)
    {
        for (int i = 0; i < waitingOrderList.Count; i++)
        {
            if (waitingOrderList[i].targetTable == diningTable && waitingOrderList[i].recipeSO == recipeSO)
            {
                waitingOrderList.RemoveAt(i);
                OnRecipeCompleted?.Invoke(this, EventArgs.Empty);
                break;
            }
        }
    }

    public bool TryDeliverRecipeToTable(PlateKitchenObject plateKitchenObject, DiningTable diningTable)
    {
        for (int i = 0; i < waitingOrderList.Count; i++)
        {
            Order order = waitingOrderList[i];

            // Ищем заказ, привязанный именно к этому столу
            if (order.targetTable == diningTable)
            {
                bool plateContentsMatchesRecipe = true;
                
                if (plateKitchenObject.GetKitchenObjectSOList().Count != order.recipeSO.kitchenObjectSOList.Count)
                {
                    plateContentsMatchesRecipe = false;
                }
                else
                {
                    foreach (KitchenObjectSO recipeKitchenObjectSO in order.recipeSO.kitchenObjectSOList)
                    {
                        bool ingredientFound = false;
                        foreach (KitchenObjectSO plateKitchenObjectSO in plateKitchenObject.GetKitchenObjectSOList())
                        {
                            if (plateKitchenObjectSO == recipeKitchenObjectSO)
                            {
                                ingredientFound = true;
                                break;
                            }
                        }
                        if (!ingredientFound)
                        {
                            plateContentsMatchesRecipe = false;
                            break;
                        }
                    }
                }

                // Если ингредиенты совпали со спецификацией заказа стола
                if (plateContentsMatchesRecipe)
                {
                    // Пытаемся передать еду клиентам за этим столом
                    if (diningTable.TryServe(order.recipeSO))
                    {
                    waitingOrderList.RemoveAt(i);

                    if (GameLoopManager.Instance != null)
                    {
                        int payout = order.recipeSO.Cost;
                        if (Player.Instance != null)
                        {
                            payout = Mathf.RoundToInt(payout * Player.Instance.GetRecipeCostMultiplier());
                        }
                        GameLoopManager.Instance.AddOrderGold(payout);
                    }

                    OnRecipeCompleted?.Invoke(this, EventArgs.Empty);

                     plateKitchenObject.DestroySelf();

                        return true;
                        }
                }
            }
        }
        return false; 
    }

    public List<RecipeSO> GetWaitingRecipeSOList()
    {
        List<RecipeSO> recipeSOList = new List<RecipeSO>();
        foreach (Order order in waitingOrderList)
        {
            recipeSOList.Add(order.recipeSO);
        }
        return recipeSOList;
    }
}