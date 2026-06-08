using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class DiningTable : BaseCounter
{
    [Header("Посадочные места стола")]
    [SerializeField] private List<Chair> chairsList = new List<Chair>();
    
    [Header("Настройки Грязной Посуды")]
    [SerializeField] private KitchenObjectSO dirtyPlateKitchenObjectSO; // Ссылка на Scriptable Object грязной тарелки для спавна в руку
    [SerializeField] private Transform dirtyPlateVisualPrefab; // Префаб визуальной модели грязной тарелки для стопки на столе

    private bool isOccupiedByGroup = false;
    private List<CustomerAI> currentGroupCustomers = new List<CustomerAI>();
    private int seatedCustomersCount = 0;
    private int servedCustomersCount = 0; // Число успешно обслуженных клиентов за текущую сессию
    private bool allCustomersServed = false; // Флаг: была ли обслужена вся группа полностью
    private bool hasOrdersBeenTaken = false; // Принял ли игрок заказ у этого стола

    // Список визуальных объектов грязных тарелок, лежащих на столе стопкой
    private List<GameObject> dirtyPlateVisualGameObjectList = new List<GameObject>();
    private int dirtyPlatesCount = 0; // Текущее количество грязных тарелок на столе

    public bool CanAccommodateGroup(int groupSize)
    {
        return !isOccupiedByGroup && chairsList.Count >= groupSize && dirtyPlatesCount == 0;
    }

    public void OccupyTable(List<CustomerAI> group)
    {
        isOccupiedByGroup = true;
        currentGroupCustomers = new List<CustomerAI>(group); // Копируем список во избежание ошибок модификации коллекций
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

    // Проверяем, вся ли группа расселась по местам
    public bool IsWholeGroupSeated()
    {
        return isOccupiedByGroup && seatedCustomersCount == currentGroupCustomers.Count;
    }

    // Корутина, управляющая временем поедания и последующим уходом
    private IEnumerator CustomersEatingAndLeavingRoutine()
    {
        // Даем клиентам спокойно покушать 4 секунды
        yield return new WaitForSeconds(4f);

        // Отправляем всю группу домой одновременно
        List<CustomerAI> customersToLeave = new List<CustomerAI>(currentGroupCustomers);
        foreach (CustomerAI customer in customersToLeave)
        {
            if (customer != null)
            {
                customer.LeaveTable();
            }
        }
    }

    // Метод вызывается, когда клиент встает и уходит со стула
    public void OnCustomerLeft(CustomerAI customer)
    {
        if (currentGroupCustomers.Contains(customer))
        {
            currentGroupCustomers.Remove(customer);
        }

        // Когда абсолютно ВСЕ клиенты встали и ушли со своих мест
        if (currentGroupCustomers.Count == 0)
        {
            // Освобождаем стулья
            foreach (Chair chair in chairsList)
            {
                chair.ClearCustomer();
            }

            // Если группа ушла сытой, спавним стопку грязной посуды по количеству гостей
            if (allCustomersServed)
            {
                SpawnDirtyPlatesStack(seatedCustomersCount);
            }

            // Сбрасываем все параметры стола для будущих клиентов
            isOccupiedByGroup = false;
            hasOrdersBeenTaken = false;
            seatedCustomersCount = 0;
            servedCustomersCount = 0;
            allCustomersServed = false;

            Debug.Log("Стол полностью освобожден и готов к приему новых гостей.");
        }
    }

    // Спавн стопки грязных тарелок на столе по количеству сидевших гостей
    private void SpawnDirtyPlatesStack(int amount)
    {
        // Если вдруг на столе остался какой-то одиночный объект — уничтожаем его
        if (HasKitchenObject())
        {
            GetKitchenObject().DestroySelf();
        }

        dirtyPlatesCount = amount;
        float plateOffsetY = 0.08f; // Высота смещения каждой тарелки в стопке

        for (int i = 0; i < amount; i++)
        {
            // Спавним визуальный префаб тарелки дочерним объектом к точке стола counterTopPoint
            Transform plateVisualTransform = Instantiate(dirtyPlateVisualPrefab, GetKitchenObjectFollowTransform());
            
            // Сдвигаем каждую следующую тарелку чуть выше по оси Y
            plateVisualTransform.localPosition = new Vector3(0, plateOffsetY * i, 0);
            plateVisualTransform.localRotation = Quaternion.identity;

            dirtyPlateVisualGameObjectList.Add(plateVisualTransform.gameObject);
        }

        Debug.Log($"На столе появилась стопка из {amount} грязных тарелок.");
    }

    public override void Interact(Player player)
    {
        // === СОСТОЯНИЕ 1: ЗА СТОЛОМ СИДЯТ КЛИЕНТЫ ===
        if (isOccupiedByGroup)
        {
            // ЛОГИКА 1: Заказы еще не приняты, и вся группа уже сидит
            if (!hasOrdersBeenTaken && IsWholeGroupSeated())
            {
                TakeOrderFromGroup();
                return;
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
                        
                        // Уничтожаем тарелку с едой из рук игрока
                        player.GetKitchenObject().DestroySelf();
                        
                        // Клиент принимает еду (меняет внутреннее состояние на "обслужен")
                        waitingCustomer.DeliverOrder();

                        // Увеличиваем счетчик обслуженных клиентов
                        servedCustomersCount++;
                        if (servedCustomersCount >= seatedCustomersCount)
                        {
                            allCustomersServed = true;
                            // Начинаем корутину поедания для всей группы разом
                            StartCoroutine(CustomersEatingAndLeavingRoutine());
                        }
                    }
                    else
                    {
                        Debug.Log("Никто за этим столом не заказывал такое блюдо!");
                    }
                }
            }
            return; 
        }

        // === СОСТОЯНИЕ 2: КЛИЕНТЫ УШЛИ, НО НА СТОЛЕ ОСТАЛАСЬ ГРЯЗНАЯ ПОСУДА ===
        if (dirtyPlatesCount > 0)
        {
            if (!player.HasKitchenObject())
            {
                // У игрока пустые руки — он берет одну грязную тарелку из стопки
                dirtyPlatesCount--;

                // Спавним реальный функциональный объект грязной тарелки прямо в руку игрока
                KitchenObject.SpawnKitchenObject(dirtyPlateKitchenObjectSO, player);

                // Удаляем верхнюю визуальную модельку тарелки со стола
                if (dirtyPlateVisualGameObjectList.Count > 0)
                {
                    GameObject topPlateVisual = dirtyPlateVisualGameObjectList[dirtyPlateVisualGameObjectList.Count - 1];
                    dirtyPlateVisualGameObjectList.Remove(topPlateVisual);
                    Destroy(topPlateVisual);
                }

                Debug.Log($"Игрок взял грязную тарелку со стола. Осталось в стопке: {dirtyPlatesCount}");
            }
            else
            {
                Debug.Log("Ваши руки заняты, вы не можете убрать грязную посуду!");
            }
        }
    }

    private void TakeOrderFromGroup()
    {
        hasOrdersBeenTaken = true;
        Debug.Log($"Игрок принял заказ у стола {gameObject.name}! Каждый клиент показывает своё блюдо.");
        
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

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying)
        {
            Grid grid = Object.FindFirstObjectByType<Grid>();
            if (grid != null)
            {
                Vector3Int cellPos = grid.WorldToCell(transform.position);
                Vector3 centerPos = grid.GetCellCenterWorld(cellPos);
                centerPos.y = 0.75f; 
                transform.position = centerPos;
            }
        }
    }
}