using UnityEngine;
using System.Collections.Generic;

public class DiningTable : BaseCounter
{
    [Header("Посадочные места стола")]
    [SerializeField] private List<Chair> chairsList = new List<Chair>();

    private bool isOccupiedByGroup = false;
    private List<CustomerAI> currentGroupCustomers = new List<CustomerAI>();
    private int seatedCustomersCount = 0;
    
    private bool hasOrdersBeenTaken = false; // Принял ли игрок заказ у этого стола

    public bool CanAccommodateGroup(int groupSize)
    {
        return !isOccupiedByGroup && chairsList.Count >= groupSize;
    }

    public void OccupyTable(List<CustomerAI> group)
    {
        isOccupiedByGroup = true;
        currentGroupCustomers = group;
        seatedCustomersCount = 0;
        hasOrdersBeenTaken = false;

        for (int i = 0; i < group.Count; i++)
        {
            Chair targetChair = chairsList[i];
            group[i].SetTargetSeat(targetChair, this);
        }
    }

    public void CustomerSeated()
    {
        seatedCustomersCount++;
        // Теперь мы НЕ вызываем автоматический заказ здесь. 
        // Клиенты просто сели и ждут игрока.
    }

    // Проверяем, вся ли группа уже расселась по местам
    public bool IsWholeGroupSeated()
    {
        return isOccupiedByGroup && seatedCustomersCount == currentGroupCustomers.Count;
    }

    public void OnCustomerLeft(CustomerAI customer)
    {
        if (currentGroupCustomers.Contains(customer))
        {
            currentGroupCustomers.Remove(customer);
        }

        if (currentGroupCustomers.Count == 0)
        {
            isOccupiedByGroup = false;
            seatedCustomersCount = 0;
            hasOrdersBeenTaken = false;
            foreach (Chair chair in chairsList)
            {
                chair.ClearCustomer();
            }
        }
    }

    public override void Interact(Player player)
    {
        if (!isOccupiedByGroup) return;

        // ЛОГИКА 1: Заказы еще не приняты, и вся группа уже сидит
        if (!hasOrdersBeenTaken && IsWholeGroupSeated())
        {
            // Игрок должен подойти без еды (или мы просто приоритетно принимаем заказ)
            if (!player.HasKitchenObject())
            {
                TakeOrderFromGroup();
                return;
            }
        }

        // ЛОГИКА 2: Заказы уже приняты, игрок принес еду на тарелке
        if (hasOrdersBeenTaken && player.HasKitchenObject())
        {
            if (player.GetKitchenObject().TryGetPlate(out PlateKitchenObject plateKitchenObject))
            {
                CustomerAI waitingCustomer = FindCustomerWhoOrdered(plateKitchenObject);

                if (waitingCustomer != null)
                {
                    Debug.Log("Заказ передан конкретному клиенту за столом!");
                    player.GetKitchenObject().DestroySelf();
                    waitingCustomer.DeliverOrder();
                }
                else
                {
                    Debug.Log("Никто за этим столом не заказывал такое блюдо!");
                }
            }
        }
    }

    // Метод «Взять заказ у стола»
    private void TakeOrderFromGroup()
    {
        hasOrdersBeenTaken = true;
        Debug.Log($"Игрок принял заказ у стола {gameObject.name}! Каждый клиент показывает своё блюдо.");
        
        foreach (CustomerAI customer in currentGroupCustomers)
        {
            customer.ShowIndividualOrder(); // Каждый клиент генерирует и показывает UI
        }
    }

    private CustomerAI FindCustomerWhoOrdered(PlateKitchenObject plateKitchenObject)
    {
        foreach (CustomerAI customer in currentGroupCustomers)
        {
            if (customer.GetOrderedRecipe() != null && DoesPlateMatchRecipe(plateKitchenObject, customer.GetOrderedRecipe()))
            {
                return customer;
            }
        }
        return null;
    }

    private bool DoesPlateMatchRecipe(PlateKitchenObject plateKitchenObject, RecipeSO recipeSO)
    {
        if (plateKitchenObject.GetKitchenObjectSOList().Count != recipeSO.kitchenObjectSOList.Count) return false;

        foreach (KitchenObjectSO recipeKitchenObjectSO in recipeSO.kitchenObjectSOList)
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
            if (!ingredientFound) return false;
        }
        return true;
    }
    private void OnDrawGizmosSelected()
    {
    // Работает только в редакторе Unity и только когда игра НЕ запущена
        if (!Application.isPlaying)
        {
            Grid grid = Object.FindFirstObjectByType<Grid>();
            if (grid != null)
            {
                // Находим ячейку, в которой сейчас находится курсор/стол
                Vector3Int cellPos = grid.WorldToCell(transform.position);
                
                // Получаем мировой центр этой ячейки
                Vector3 centerPos = grid.GetCellCenterWorld(cellPos);
                centerPos.y = 0.75f; // Фиксируем на полу
                
                // Принудительно выравниваем стол по центру клетки
                transform.position = centerPos;
            }
        }
    }
}