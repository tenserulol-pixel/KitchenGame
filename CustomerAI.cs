using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class CustomerAI : MonoBehaviour
{
    [Header("Список доступных рецептов (Меню)")]
    [SerializeField] private RecipeListSO recipeListSO; 

    [Header("UI заказа над головой")]
    [SerializeField] private Transform orderVisualPrefab; 

    private NavMeshAgent agent;
    private Animator animator;
    private Chair targetChair;
    private DiningTable diningTable;
    private bool isSeated = false;

    private RecipeSO orderedRecipe; // Выбранное блюдо
    private GameObject orderVisualInstance;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();
    }

    private void Update()
    {
        if (animator != null)
        {
            float currentSpeed = agent.remainingDistance > 0.1f ? agent.speed : 0f;
            animator.SetFloat("Speed", currentSpeed);
        }

        if (!isSeated && targetChair != null && agent.remainingDistance <= 0.2f && !agent.pathPending)
        {
            SitDown();
        }
    }

    public void SetTargetSeat(Chair chair, DiningTable table)
    {
        targetChair = chair;
        diningTable = table;
        targetChair.SetCustomer(this);
        
        agent.enabled = true;
        agent.SetDestination(chair.GetPosition());
    }

    private void SitDown()
    {
        isSeated = true;
        agent.enabled = false;

        Vector3 tableDirection = diningTable.transform.position - transform.position;
        tableDirection.y = 0;
        if (tableDirection != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(tableDirection);
        }

        // Заранее тихо выбираем рецепт, который захочет этот клиент
        PreSelectRecipe();

        if (diningTable != null)
        {
            diningTable.CustomerSeated();
        }
    }

    private void PreSelectRecipe()
    {
        if (recipeListSO != null && recipeListSO.recipeSOList != null && recipeListSO.recipeSOList.Count > 0)
        {
            orderedRecipe = recipeListSO.recipeSOList[Random.Range(0, recipeListSO.recipeSOList.Count)];
        }
        else
        {
            Debug.LogError($"[CustomerAI] На объекте {gameObject.name} не назначен RecipeListSO или список рецептов пуст!", gameObject);
        }
    }

    // МЕТОД ОБЪЕДИНЕН И ЗАЩИЩЕН (ДУБЛИКАТ УДАЛЕН)
    public void ShowIndividualOrder()
    {
        // ПРОВЕРКА 1: Выбрал ли клиент рецепт заранее?
        if (orderedRecipe == null)
        {
            Debug.LogError($"[CustomerAI] У клиента {gameObject.name} не выбран рецепт! Проверьте, заполнен ли ScriptableObject меню.", gameObject);
            return;
        }

        // ПРОВЕРКА 2: Назначен ли префаб облачка заказа?
        if (orderVisualPrefab == null)
        {
            Debug.LogError($"[CustomerAI] На префабе клиента {gameObject.name} отсутствует ссылка на 'Order Visual Prefab' в инспекторе!", gameObject);
            return;
        }

        // Спавним UI над головой (теперь безопасно берем orderVisualPrefab.gameObject)
        orderVisualInstance = Instantiate(orderVisualPrefab.gameObject, transform.position + Vector3.up * 4f, Quaternion.identity, transform);
        
        if (orderVisualInstance.TryGetComponent<DeliveryManagerSingleUI>(out var orderUI))
        {
            orderUI.SetRecipeSO(orderedRecipe);
        }
        else
        {
            Debug.LogError($"[CustomerAI] На спавнящемся префабе {orderVisualPrefab.name} не найден компонент DeliveryManagerSingleUI!", orderVisualPrefab);
        }
        
        if (!orderVisualInstance.GetComponent<LookAtCamera>())
        {
            orderVisualInstance.AddComponent<LookAtCamera>();
        }
    }

    public RecipeSO GetOrderedRecipe()
    {
        return orderedRecipe;
    }

    public void DeliverOrder()
    {
        orderedRecipe = null;

        if (orderVisualInstance != null)
        {
            Destroy(orderVisualInstance);
        }

        LeaveTable();
    }

    public void LeaveTable()
    {
        if (diningTable != null)
        {
            diningTable.OnCustomerLeft(this);
        }

        if (targetChair != null)
        {
            targetChair.ClearCustomer();
        }

        agent.enabled = true;
        Destroy(gameObject, 2f); 
    }
}