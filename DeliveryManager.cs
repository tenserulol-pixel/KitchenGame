using UnityEngine;
using System;
using System.Collections.Generic;

public class DeliveryManager : MonoBehaviour
{
    public event EventHandler OnRecipeSpawned;
    public event EventHandler OnRecipeCompleted;
    public static DeliveryManager Instance { get; private set; }

    // Новая структура для связи рецепта и стола, который его ждет
    public struct Order {
        public RecipeSO recipeSO;
        public DiningTable targetTable;
    }

    private List<Order> waitingOrderList; // Вместо старого waitingRecipeSOList

    private void Awake()
    {
        waitingOrderList = new List<Order>();
        Instance = this;
    }

    // Вызывается столом, когда за него садится клиент
    public void AddOrderFromTable(RecipeSO recipeSO, DiningTable diningTable)
    {
        Order newOrder = new Order {
            recipeSO = recipeSO,
            targetTable = diningTable
        };
        
        waitingOrderList.Add(newOrder);
        OnRecipeSpawned?.Invoke(this, EventArgs.Empty);
    }

    // Вызывается столом, если клиент ушел по таймеру (кастомная очистка)
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

    // Попытка сдать блюдо конкретному столу
    public bool TryDeliverRecipeToTable(PlateKitchenObject plateKitchenObject, DiningTable diningTable)
    {
        for (int i = 0; i < waitingOrderList.Count; i++)
        {
            Order order = waitingOrderList[i];

            // Ищем заказ, привязанный именно к этому столу
            if (order.targetTable == diningTable)
            {
                // Проверяем состав ингредиентов (ваша оригинальная логика совпадения)
                bool plateContentsMatchesRecipe = true;
                
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

                // Если ингредиенты совпали со спецификацией заказа стола
                if (plateContentsMatchesRecipe && plateKitchenObject.GetKitchenObjectSOList().Count == order.recipeSO.kitchenObjectSOList.Count)
                {
                    waitingOrderList.RemoveAt(i);
                    OnRecipeCompleted?.Invoke(this, EventArgs.Empty);
                    return true; // Успешная доставка!
                }
            }
        }
        return false; // Не совпало
    }

    // Метод для UI элементов, чтобы они могли читать текущие заказы
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