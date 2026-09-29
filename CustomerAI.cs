using UnityEngine;
using UnityEngine.AI;
using System;
using System.Collections.Generic;

[RequireComponent(typeof(NavMeshAgent))]
public class CustomerAI : MonoBehaviour, IHasProgress
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

    // === IHasProgress: прогресс-бар терпения ===
    // Стреляет при изменении терпения — как обычного (patienceTimer / maxPatience),
    // так и очередного (queuePatienceTimer / maxQueuePatience).
    //
    // Один прогресс-бар используется для обоих случаев:
    // - В состоянии WaitingForFood: progressNormalized = patienceTimer / (maxPatience * множитель)
    // - В состоянии Queueing:       progressNormalized = queuePatienceTimer / maxQueuePatience
    // - В остальных состояниях:     progressNormalized = 0f (бар скрыт)
    //
    // ProgressBarUI подписывается на OnProgressChanged и обновляет fillAmount.
    public event EventHandler<IHasProgress.OnProgressChangedEventArgs> OnProgressChanged;

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

        // КАРТА «Спешащие гости» (HurriedGuests): делим на ЭФФЕКТИВНОЕ терпение
        // (maxPatience * множитель) — при множителе < 1 бар опустошается быстрее.
        float effectiveMaxPatience = maxPatience * GetEffectivePatienceMultiplier();

        // Уведомляем прогресс-бар: 1.0 = полное терпение, 0.0 = кончилось.
        // Защита от деления на 0, если maxPatience = 0 (нечаянно в инспекторе).
        float normalized = effectiveMaxPatience > 0f ? Mathf.Clamp01(patienceTimer / effectiveMaxPatience) : 0f;
        OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs
        {
            progressNormalized = normalized
        });

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

    // ======================================================================
    // === КАРТЫ АПГРЕЙДОВ ===
    // ======================================================================

    /// <summary>
    /// КАРТА «Спешащие гости» (HurriedGuests): эффективное терпение = maxPatience * множитель.
    /// Множитель &lt; 1 — терпение тает быстрее (компенсация — +secondaryValue к выплате,
    /// её начисляет DeliveryManager.CalculatePayout). Множитель читается из UpgradeManager
    /// при каждом запросе: карту могут взять посреди дня, эффект подхватится без перезапуска.
    /// </summary>
    private float GetEffectivePatienceMultiplier()
    {
        if (UpgradeManager.Instance != null)
        {
            return Mathf.Max(0.1f, UpgradeManager.Instance.GetPatienceMultiplier());
        }
        return 1f;
    }

    /// <summary>
    /// КАРТА «Общий заказ» (SameDishChance): вызывается из DiningTable.CustomerSeated.
    /// Сосед (первый севший за стол) уже выбрал блюдо, а пузырь заказа (ShowOrder) и
    /// регистрация в списке заказов (AddOrderFromTable) произойдут ПОЗЖЕ — см. порядок
    /// вызовов в SitDown: SelectRecipe → CustomerSeated → ShowOrder → AddOrderFromTable.
    /// Поэтому и пузырь, и список покажут уже скопированный рецепт.
    /// </summary>
    public void MirrorOrderFrom(CustomerAI source)
    {
        if (source == null) return;

        RecipeSO sourceRecipe = source.GetOrderedRecipe();
        if (sourceRecipe == null) return;

        orderedRecipe = sourceRecipe;
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

            // Сразу показываем бар полным — клиент только встал в очередь.
            // Без этого бара не будет до первого кадра UpdateQueueing, что выглядит как "пустота".
            float normalized = maxQueuePatience > 0f ? 1f : 0f;
            OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs
            {
                progressNormalized = normalized
            });
        }
    }

    private void UpdateQueueing()
    {
        queuePatienceTimer -= Time.deltaTime;

        // Уведомляем прогресс-бар: 1.0 = полное терпение в очереди, 0.0 = кончилось.
        // Тот же прогресс-бар, что и для WaitingForFood — игрок видит одинаковую индикацию.
        // ВАЖНО: терпение в очереди от «Спешащих гостей» НЕ зависит — карта про сидящих гостей.
        float normalized = maxQueuePatience > 0f ? Mathf.Clamp01(queuePatienceTimer / maxQueuePatience) : 0f;
        OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs
        {
            progressNormalized = normalized
        });

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

        // ДИАГНОСТИКА: рецепт не выбрался — почти всегда пустой recipeListSO в инспекторе
        // префаба клиента. Заказ не зарегистрируется и клиент не сможет поесть.
        if (orderedRecipe == null)
        {
            Debug.LogWarning($"[CustomerAI] '{name}': SelectRecipe не выбрал рецепт — проверь recipeListSO у префаба клиента. Заказ не будет зарегистрирован!");
        }

        // КАРТА «Спешащие гости»: садимся с ЭФФЕКТИВНЫМ терпением (maxPatience * множитель)
        patienceTimer = maxPatience * GetEffectivePatienceMultiplier();

        diningTable.CustomerSeated(this);

        ShowOrder();

        // Регистрируем заказ в менеджере доставки
        if (DeliveryManager.Instance != null && orderedRecipe != null)
        {
            DeliveryManager.Instance.AddOrderFromTable(orderedRecipe, diningTable);
        }

        SetState(CustomerState.WaitingForFood);

        // Показываем прогресс-бар терпения сразу полным (1.0) — клиент сел, бар появился.
        // Без этого бара не будет до первого кадра UpdateWaiting, что выглядит как "пустота".
        OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs
        {
            progressNormalized = 1f
        });
    }

    private void SelectRecipe()
    {
        if (recipeListSO == null || recipeListSO.recipeSOList.Count == 0)
            return;

        // КАРТА «Алхимический хаос» (AlchemyChaos): пополняем пул рецептов/ингредиентов
        // для ежедневной мутации. Вызов при каждом заказе безопасен — дубли внутри
        // отсекаются, а отложенный бросок дня (если пул был пуст) сработает здесь.
        RecipeChaos.EnsurePool(recipeListSO.recipeSOList);

        RecipeSO baseRecipe = recipeListSO.recipeSOList[UnityEngine.Random.Range(0, recipeListSO.recipeSOList.Count)];

        // КАРТА «Алхимический хаос»: заказ прогоняется через мутацию дня.
        // Если мутации сегодня нет (или этот рецепт не пострадал) — вернётся исходник.
        // Ассет-оригинал никогда не меняется: пострадавший рецепт — это рантайм-клон.
        orderedRecipe = RecipeChaos.ApplyDailyMutation(baseRecipe);
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
        if (orderVisualPrefab == null)
        {
            // ДИАГНОСТИКА: префаб пузыря заказа не назначен — заказ «невидимый» для игрока,
            // при этом механика подачи работает. Проверь поле orderVisualPrefab у префаба.
            Debug.LogWarning($"[CustomerAI] '{name}': orderVisualPrefab не назначен — пузырь заказа не появится!");
            return;
        }

        if (orderedRecipe == null)
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

        // Если уходим сердитыми (или по ошибке с недоеденной едой), удаляем заказ из системы.
        // Для зеркальных заказов («Общий заказ») и мутаций («Алхимический хаос») снимается
        // ровно та же ссылка, что была добавлена в AddOrderFromTable — по ссылкам сходится.
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

        // Скрываем прогресс-бар терпения во всех состояниях, где он не нужен:
        // Walking / Sitting / Eating / FinishedEating / Leaving / WalkingToQueue.
        // Бар виден только в WaitingForFood и Queueing (там события стреляют из Update).
        // ProgressBarUI скрывает себя при progressNormalized == 0f.
        if (newState != CustomerState.WaitingForFood && newState != CustomerState.Queueing)
        {
            OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs
            {
                progressNormalized = 0f
            });
        }
    }

    public CustomerState GetState() => state;

    /// <summary>
    /// КАРТА «Спешащие гости»: нормализация к ЭФФЕКТИВНОМУ терпению — любые внешние
    /// читатели (ProgressBarUI и будущие UI) видят ту же скорость, что и UpdateWaiting.
    /// </summary>
    public float GetPatienceNormalized()
    {
        float effectiveMaxPatience = maxPatience * GetEffectivePatienceMultiplier();
        return effectiveMaxPatience > 0f ? Mathf.Clamp01(patienceTimer / effectiveMaxPatience) : 0f;
    }

    public float GetQueuePatienceNormalized() => maxQueuePatience > 0f ? Mathf.Clamp01(queuePatienceTimer / maxQueuePatience) : 0f;
    public RecipeSO GetOrderedRecipe() => orderedRecipe;
}