using UnityEngine;
using UnityEngine.AI;
using System;

[RequireComponent(typeof(NavMeshAgent))]
public class CustomerAI : MonoBehaviour
{
    public enum CustomerState
    {
        Walking,
        Sitting,
        WaitingForFood,
        Eating,
        FinishedEating, // Ожидание окончания трапезы остальными членами группы
        Leaving,
        WalkingToQueue, // Идёт к месту в очереди (свободного стола не нашлось)
        Queueing        // Стоит в очереди, ждёт освободившийся стол — терпение тикает отдельно
    }

    public event EventHandler OnStateChanged;

    [Header("Настройки времени")]
    [SerializeField] private float eatingTime = 10f;
    [SerializeField] private float maxPatience = 60f;
    [Tooltip("Отдельное от maxPatience значение — сколько группа готова стоять в очереди без стола")]
    [SerializeField] private float maxQueuePatience = 30f;

    [Header("Заказы")]
    [SerializeField] private RecipeListSO recipeListSO;
    [SerializeField] private Transform orderVisualPrefab;

    private NavMeshAgent agent;
    private Animator animator;

    private Chair targetChair;
    private DiningTable diningTable;

    private CustomerState state;

    private RecipeSO orderedRecipe;

    private float patienceTimer;
    private float queuePatienceTimer;
    private float eatingTimer;

    private GameObject orderVisualInstance;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();
    }

    private void Start()
    {
        SetState(CustomerState.Walking);
    }

    private void Update()
    {
        HandleAnimation();

        switch (state)
        {
            case CustomerState.Walking:
                UpdateWalking();
                break;

            case CustomerState.WaitingForFood:
                UpdateWaiting();
                break;

            case CustomerState.Eating:
                UpdateEating();
                break;

            case CustomerState.FinishedEating:
                // В этом состоянии гость спокойно сидит на стуле и ждет остальных членов группы
                break;

            case CustomerState.Leaving:
                break;

            case CustomerState.WalkingToQueue:
                UpdateWalkingToQueue();
                break;

            case CustomerState.Queueing:
                UpdateQueueing();
                break;
        }
    }

    private void HandleAnimation()
    {
        if (animator == null) return;

        animator.SetFloat(
            "Speed",
            agent.enabled ? agent.velocity.magnitude : 0f
        );
    }

    private void UpdateWalking()
    {
        if (targetChair == null)
            return;

        // Защита: Проверяем, запечен ли NavMesh
        if (agent.enabled && !agent.pathPending && agent.remainingDistance <= 0.2f)
        {
            SitDown();
        }
    }

    private void UpdateWaiting()
    {
        patienceTimer -= Time.deltaTime;

        if (patienceTimer <= 0)
        {
            LeaveTableAngry();
        }
    }

    private void UpdateEating()
    {
        eatingTimer -= Time.deltaTime;

        if (eatingTimer <= 0)
        {
            // Переходим в состояние ожидания всей группы вместо мгновенного самостоятельного ухода
            SetState(CustomerState.FinishedEating);
            if (diningTable != null)
            {
                diningTable.OnCustomerFinishedEating(this);
            }
        }
    }

    /// <summary>
    /// Устанавливает стул и стол-цель для данного клиента и отправляет его туда.
    /// Явно переключает состояние на Walking — важно не только для только что
    /// заспавненных (для них это и так сделает Start()), но и для клиента, которого
    /// пересаживают из очереди: без этого он остался бы в состоянии Queueing и
    /// никогда бы не пошёл к новому месту, несмотря на то что агенту уже задали путь.
    /// </summary>
    public void SetTargetSeat(Chair chair, DiningTable table)
    {
        targetChair = chair;
        diningTable = table;

        if (targetChair != null)
        {
            targetChair.SetCustomer(this);
        }

        // Перед установкой назначения проверяем, активен ли агент навигации
        if (agent != null)
        {
            agent.enabled = true;
            if (agent.isOnNavMesh)
            {
                agent.SetDestination(chair.GetPosition());
            }
            else
            {
                Debug.LogWarning($"[CustomerAI] Объект {name} заспавнился вне сетки NavMesh! Пожалуйста, запеките навигацию (Navigation window).");
                // Тест-телепортация к стулу, чтобы игра не ломалась, если сетка не запечена
                transform.position = chair.GetPosition();
            }
        }

        SetState(CustomerState.Walking);
    }

    /// <summary>
    /// Отправляет клиента к точке в очереди вместо стула — вызывается CustomerManager,
    /// когда свободного стола не нашлось при спавне группы.
    /// </summary>
    public void SetTargetQueueSpot(Vector3 queuePosition)
    {
        if (agent != null)
        {
            agent.enabled = true;
            if (agent.isOnNavMesh)
            {
                agent.SetDestination(queuePosition);
            }
            else
            {
                Debug.LogWarning($"[CustomerAI] Объект {name} заспавнился вне сетки NavMesh! Пожалуйста, запеките навигацию (Navigation window).");
                transform.position = queuePosition;
            }
        }

        SetState(CustomerState.WalkingToQueue);
    }

    private void UpdateWalkingToQueue()
    {
        if (agent.enabled && !agent.pathPending && agent.remainingDistance <= 0.2f)
        {
            queuePatienceTimer = maxQueuePatience;
            SetState(CustomerState.Queueing);
        }
    }

    private void UpdateQueueing()
    {
        queuePatienceTimer -= Time.deltaTime;

        if (queuePatienceTimer <= 0f)
        {
            LeaveQueueAngry();
        }
    }

    /// <summary>
    /// Терпение в очереди кончилось у ЭТОГО конкретного клиента — штраф начисляется
    /// один раз, а вся группа уходит вместе (см. CustomerManager.DisbandQueuedGroup),
    /// тем же принципом, что уже работает у рассаженных групп в DiningTable.
    /// </summary>
    private void LeaveQueueAngry()
    {
        if (GameLoopManager.Instance != null)
        {
            GameLoopManager.Instance.DeductOrderGold();
        }

        if (CustomerManager.Instance != null)
        {
            CustomerManager.Instance.DisbandQueuedGroup(this);
        }
    }

    /// <summary>
    /// Уход конкретно из очереди — без стула, стола и заказа, поэтому не переиспользует
    /// LeaveTable() напрямую (та явно рассчитана на уже посаженных гостей). Публичный,
    /// поскольку вызывается для КАЖДОГО члена расформировываемой группы из CustomerManager,
    /// включая тех, кто сам ещё не терял терпение — их просто утянули за собой.
    /// </summary>
    public void LeaveQueue()
    {
        SetState(CustomerState.Leaving);

        if (CustomerManager.Instance != null)
        {
            CustomerManager.Instance.RemoveCustomer(this);
        }

        if (agent != null)
        {
            agent.enabled = true;
            if (agent.isOnNavMesh)
            {
                agent.SetDestination(Vector3.zero); // Направление к выходу
            }
        }

        Destroy(gameObject, 5f);
    }

    private void SitDown()
    {
        SetState(CustomerState.Sitting);

        if (agent != null)
        {
            agent.enabled = false;
        }

        Vector3 direction = diningTable.transform.position - transform.position;
        direction.y = 0;

        if (direction != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(direction);
        }

        SelectRecipe();

        patienceTimer = maxPatience;

        diningTable.CustomerSeated(this);

        ShowOrder();

        // Регистрируем заказ в менеджере доставки
        if (DeliveryManager.Instance != null && orderedRecipe != null)
        {
            DeliveryManager.Instance.AddOrderFromTable(orderedRecipe, diningTable);
        }

        SetState(CustomerState.WaitingForFood);
    }

    private void SelectRecipe()
    {
        if (recipeListSO == null || recipeListSO.recipeSOList.Count == 0)
            return;

        orderedRecipe = recipeListSO.recipeSOList[UnityEngine.Random.Range(0, recipeListSO.recipeSOList.Count)];
    }

    public bool TryDeliver(RecipeSO recipeSO)
    {
        if (state != CustomerState.WaitingForFood)
            return false;

        if (orderedRecipe != recipeSO)
            return false;

        DeliverOrder();
        return true;
    }

    private void DeliverOrder()
    {
        orderedRecipe = null;

        if (orderVisualInstance != null)
        {
            Destroy(orderVisualInstance);
        }

        if (animator != null)
        {
            animator.SetTrigger("Eat");
        }

        eatingTimer = eatingTime;
        SetState(CustomerState.Eating);
    }

    private void ShowOrder()
    {
        if (orderVisualPrefab == null || orderedRecipe == null)
            return;

        orderVisualInstance = Instantiate(
            orderVisualPrefab.gameObject,
            transform.position + Vector3.up * 4f,
            Quaternion.identity,
            transform
        );

        if (orderVisualInstance.TryGetComponent(out DeliveryManagerSingleUI ui))
        {
            ui.SetRecipeSO(orderedRecipe);
        }

        if (!orderVisualInstance.GetComponent<LookAtCamera>())
        {
            orderVisualInstance.AddComponent<LookAtCamera>();
        }
    }

    public void LeaveTable()
    {
        SetState(CustomerState.Leaving);

        // Если уходим сердитыми (или по ошибке с недоеденной едой), удаляем заказ из системы
        if (DeliveryManager.Instance != null && orderedRecipe != null)
        {
            DeliveryManager.Instance.RemoveOrder(orderedRecipe, diningTable);
        }

        if (targetChair != null)
        {
            targetChair.ClearCustomer();
        }

        if (diningTable != null)
        {
            diningTable.OnCustomerLeft(this);
        }

        // Удаляем из глобального списка CustomerManager
        if (CustomerManager.Instance != null)
        {
            CustomerManager.Instance.RemoveCustomer(this);
        }

        if (agent != null)
        {
            agent.enabled = true;
            if (agent.isOnNavMesh)
            {
                agent.SetDestination(Vector3.zero); // Направление к выходу
            }
        }

        Destroy(gameObject, 5f);
    }

    public void LeaveTableAngry()
    {
        if (GameLoopManager.Instance != null)
        {
            GameLoopManager.Instance.DeductOrderGold();
        }

        LeaveTable();
    }

    private void SetState(CustomerState newState)
    {
        state = newState;
        OnStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public CustomerState GetState() => state;
    public float GetPatienceNormalized() => patienceTimer / maxPatience;
    public float GetQueuePatienceNormalized() => queuePatienceTimer / maxQueuePatience;
    public RecipeSO GetOrderedRecipe() => orderedRecipe;
}