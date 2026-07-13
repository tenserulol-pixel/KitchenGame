using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class CustomerAI : MonoBehaviour
{
    [Header("Настройки меню")]
    [SerializeField] private RecipeListSO recipeListSO; 

    [Header("Облачко заказа")]
    [SerializeField] private Transform orderVisualPrefab; 

    private NavMeshAgent agent;
    private Animator animator;
    private Chair targetChair;
    private DiningTable diningTable;
    private bool isSeated = false;

    private RecipeSO orderedRecipe; 
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
            orderedRecipe = recipeListSO.recipeSOList[UnityEngine.Random.Range(0, recipeListSO.recipeSOList.Count)];
        }
    }

    public void ShowIndividualOrder()
    {
        if (orderedRecipe == null || orderVisualPrefab == null) return;

        orderVisualInstance = Instantiate(orderVisualPrefab.gameObject, transform.position + Vector3.up * 4f, Quaternion.identity, transform);
        
        if (orderVisualInstance.TryGetComponent<DeliveryManagerSingleUI>(out var orderUI))
        {
            orderUI.SetRecipeSO(orderedRecipe);
        }
        
        if (!orderVisualInstance.GetComponent<LookAtCamera>())
        {
            orderVisualInstance.AddComponent<LookAtCamera>();
        }
    }

    public RecipeSO GetOrderedRecipe() => orderedRecipe;

    public void DeliverOrder()
    {
        orderedRecipe = null;

        if (orderVisualInstance != null)
        {
            Destroy(orderVisualInstance);
        }

        if (animator != null)
        {
            animator.SetTrigger("Eat"); // Запуск анимации поедания в Unity Animator
        }
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
        agent.SetDestination(Vector3.zero); // Направление к выходу

        Destroy(gameObject, 2f); 
    }

    public void LeaveTableAngry()
    {
        if (diningTable != null)
        {
            diningTable.OnCustomerLeft(this);
        }

        // Вычитаем золото за проваленный заказ из кошелька игрока
        if (GameLoopManager.Instance != null)
        {
            GameLoopManager.Instance.DeductOrderGold();
        }

        LeaveTable();
    }
}