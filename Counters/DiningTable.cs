using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class DiningTable : BaseCounter
{
    [Header("Посадочные места стола")]
    [SerializeField] private List<Chair> chairsList = new List<Chair>();
    
    [Header("Настройки Грязной Посуды")]
    [SerializeField] private KitchenObjectSO dirtyPlateKitchenObjectSO; // Ссылка на SO грязной тарелки для спавна в руку
    [SerializeField] private Transform dirtyPlateVisualPrefab; // Префаб визуальной модели тарелки для стопки на столе

    private bool isOccupiedByGroup = false;
    private List<CustomerAI> currentGroupCustomers = new List<CustomerAI>();
    private int seatedCustomersCount = 0;
    private int servedCustomersCount = 0; 
    private bool allCustomersServed = false; 
    private bool hasOrdersBeenTaken = false; 

    private List<GameObject> dirtyPlateVisualGameObjectList = new List<GameObject>();
    private int dirtyPlatesCount = 0; 

    public bool CanAccommodateGroup(int groupSize)
    {
        return !isOccupiedByGroup && chairsList.Count >= groupSize && dirtyPlatesCount == 0;
    }

    public void OccupyTable(List<CustomerAI> group)
    {
        isOccupiedByGroup = true;
        currentGroupCustomers = new List<CustomerAI>(group); 
        seatedCustomersCount = 0;
        servedCustomersCount = 0;
        allCustomersServed = false;
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
    }

    public bool IsWholeGroupSeated()
    {
        return isOccupiedByGroup && seatedCustomersCount == currentGroupCustomers.Count;
    }

    private IEnumerator CustomersEatingAndLeavingRoutine()
    {
        // Клиенты едят 4 секунды
        yield return new WaitForSeconds(4f);

        List<CustomerAI> customersToLeave = new List<CustomerAI>(currentGroupCustomers);
        foreach (CustomerAI customer in customersToLeave)
        {
            if (customer != null)
            {
                customer.LeaveTable();
            }
        }
    }

    public void OnCustomerLeft(CustomerAI customer)
    {
        if (currentGroupCustomers.Contains(customer))
        {
            currentGroupCustomers.Remove(customer);
        }

        if (currentGroupCustomers.Count == 0)
        {
            foreach (Chair chair in chairsList)
            {
                chair.ClearCustomer();
            }

            if (allCustomersServed)
            {
                SpawnDirtyPlatesStack(seatedCustomersCount);
            }

            isOccupiedByGroup = false;
            hasOrdersBeenTaken = false;
            seatedCustomersCount = 0;
            servedCustomersCount = 0;
            allCustomersServed = false;
        }
    }

    private void SpawnDirtyPlatesStack(int amount)
    {
        if (HasKitchenObject())
        {
            GetKitchenObject().DestroySelf();
        }

        dirtyPlatesCount = amount;
        float plateOffsetY = 0.08f; 

        for (int i = 0; i < amount; i++)
        {
            Transform plateVisualTransform = Instantiate(dirtyPlateVisualPrefab, GetKitchenObjectFollowTransform());
            plateVisualTransform.localPosition = new Vector3(0, plateOffsetY * i, 0);
            plateVisualTransform.localRotation = Quaternion.identity;

            dirtyPlateVisualGameObjectList.Add(plateVisualTransform.gameObject);
        }
    }

    public override void Interact(Player player)
    {
        if (isOccupiedByGroup)
        {
            if (!hasOrdersBeenTaken && IsWholeGroupSeated())
            {
                TakeOrderFromGroup();
                return;
            }

            if (hasOrdersBeenTaken && player.HasKitchenObject())
            {
                if (player.GetKitchenObject().TryGetPlate(out PlateKitchenObject plateKitchenObject))
                {
                    CustomerAI waitingCustomer = FindCustomerWhoOrdered(plateKitchenObject);

                    if (waitingCustomer != null)
                    {
                        player.GetKitchenObject().DestroySelf(); // Уничтожаем собранное блюдо из рук игрока
                        waitingCustomer.DeliverOrder(); // Клиент приступает к еде

                        // Начисляем награду в золоте
                        if (GameLoopManager.Instance != null)
                        {
                            GameLoopManager.Instance.AddOrderGold();
                        }

                        servedCustomersCount++;
                        if (servedCustomersCount >= seatedCustomersCount)
                        {
                            allCustomersServed = true;
                            StartCoroutine(CustomersEatingAndLeavingRoutine());
                        }
                    }
                }
            }
            return; 
        }

        // Если клиенты ушли — игрок забирает грязную посуду поштучно
        if (dirtyPlatesCount > 0)
        {
            if (!player.HasKitchenObject())
            {
                dirtyPlatesCount--;

                // Спавним грязную тарелку в руку игрока
                KitchenObject.SpawnKitchenObject(dirtyPlateKitchenObjectSO, player);

                if (dirtyPlateVisualGameObjectList.Count > 0)
                {
                    GameObject topPlateVisual = dirtyPlateVisualGameObjectList[dirtyPlateVisualGameObjectList.Count - 1];
                    dirtyPlateVisualGameObjectList.Remove(topPlateVisual);
                    Destroy(topPlateVisual);
                }
            }
        }
    }

    private void TakeOrderFromGroup()
    {
        hasOrdersBeenTaken = true;
        foreach (CustomerAI customer in currentGroupCustomers)
        {
            customer.ShowIndividualOrder(); 
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
}