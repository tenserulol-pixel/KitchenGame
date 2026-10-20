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

    [Header("Достижимость места (анти-«посадка за стеной»)")]
    [Tooltip("Расстояние по пути, при котором гость считается пришедшим к стулу")]
    [SerializeField] private float seatArriveDistance = 0.3f;
    [Tooltip("Максимальная ПРЯМАЯ дистанция до стула при посадке. Если гость дошёл до конца пути, но стул дальше (за стеной, в дыре NavMesh) — садиться НЕЛЬЗЯ")]
    [SerializeField] private float seatMaxStraightDistance = 0.9f;
    [Tooltip("Пауза между повторными попытками построить путь к стулу (секунды)")]
    [SerializeField] private float unreachableRetryInterval = 1.5f;
    [Tooltip("Сколько попыток построить путь, прежде чем гость откажется от места и уйдёт")]
    [SerializeField] private int maxUnreachableRetries = 3;
    [Tooltip("Сколько секунд ждать построения пути к точке очереди, прежде чем встать в очередь там, где гость стоит")]
    [SerializeField] private float queuePathWatchdogTime = 3f;

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

    // Гибрид «агент рулит — физика исполняет»: синхронизация динамического
    // Rigidbody с NavMeshAgent, телепорты через Warp, кинематика сидящего
    // гостя. Компонент CustomerPhysicsAgent заменяет CustomerCollision
    // (старый компонент с префаба снять).
    private CustomerPhysicsAgent physicsAgent;

    // === Анти-«посадка за стеной»: счётчики недостижимости ===
    // Пока путь к стулу неполный/невалидный, гость ЖДЁТ у стены (не садится!),
    // каждые unreachableRetryInterval секунд пробует перестроить путь,
    // а после maxUnreachableRetries попыток отказывается от места и уходит.
    private float unreachableTimer;
    private int unreachableRetries;

    // Watchdog очереди: путь к точке очереди так и не построился — встаём в очередь
    // там, где стоим (терпение очереди всё равно корректно завершит состояние).
    private float queuePathWatchdog;

    // === FIX: Выход и деспавн ===
    // Раньше: agent.SetDestination(Vector3.zero) («выход» = центр мира) и
    // Destroy(gameObject, 5f) — гость удалялся через 5 секунд ГДЕ БЫ НИ БЫЛ,
    // в том числе на глазах у игрока посреди зала («клиенты исчезают»).
    // Теперь: гость идёт к exitPoint (дверь) и удаляется по факту прихода;
    // leaveFailsafeTime — страховка от вечного гостя (не дошёл — удаляем).
    [Header("Выход")]
    [Tooltip("Точка выхода (дверь/порог). Если не назначена — используется мировая точка (0,0,0), как раньше.")]
    [SerializeField] private Transform exitPoint;
    [Tooltip("Расстояние до точки выхода, при котором гость считается ушедшим и уничтожается")]
    [SerializeField] private float exitArriveDistance = 0.6f;
    [Tooltip("Страховка: максимальное время пути к выходу, после которого гость удаляется принудительно")]
    [SerializeField] private float leaveFailsafeTime = 25f;
    private float leaveFailsafe;

    // Кэш-путь для мгновенной проверки достижимости при назначении места
    // (один экземпляр на всех гостей — CalculatePath перезаписывает содержимое).
    // ВНИМАНИЕ: new NavMeshPath() НЕЛЬЗЯ вызывать в инициализаторе поля — Unity
    // запрещает Unity-API из конструктора/статического инициализатора MonoBehaviour
    // (UnityException: InitializeNavMeshPath is not allowed...). Поэтому создаём
    // ЛЕНИВО при первом SetTargetSeat — см. GetPrecheckPath().
    private static NavMeshPath precheckPath;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();
        physicsAgent = GetComponent<CustomerPhysicsAgent>();
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
                UpdateLeaving();
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

    // ======================================================================
    // === ИДЁТ К СТУЛУ: посадка ТОЛЬКО по фактическому прибытию ===
    // ======================================================================
    // Старая версия садила гостя по условию remainingDistance <= 0.2f. У этого
    // условия было две дыры, обе выглядели как «гость сел дистанционно за стеной»:
    //
    // 1) ЧАСТИЧНЫЙ путь (стена между гостем и залом, NavMesh разрезан, стол
    //    передвинули переносом мебели): агент доходит до ближайшей достижимой
    //    точки — конца обрезанного пути — и remainingDistance падает до ~0,
    //    хотя до стула ещё метры. Срабатывал SitDown() прямо за стеной.
    //
    // 2) НЕВАЛИДНЫЙ путь (destination не проецируется на NavMesh): hasPath == false,
    //    remainingDistance == 0 → SitDown() срабатывал МГНОВЕННО на спавне.
    //
    // Теперь посадка требует ВСЕХ трёх условий:
    //   а) путь построен до конца (pathStatus == PathComplete);
    //   б) агент дошёл до конца пути (remainingDistance <= seatArriveDistance);
    //   в) гость РЕАЛЬНО рядом со стулом по прямой (<= seatMaxStraightDistance) —
    //      защита от проекции destination «через стену» на ближайшую точку сетки.
    // Если путь неполный — гость ждёт у стены и периодически пробует снова
    // (HandleUnreachableSeat), а не делает вид, что добрался.
    private void UpdateWalking()
    {
        if (targetChair == null)
            return;

        if (!agent.enabled || agent.pathPending)
            return;

        // (а) Путь обязан быть ПОЛНЫМ. Частичный/невалидный = до стула не добраться.
        if (agent.pathStatus != NavMeshPathStatus.PathComplete || !agent.hasPath)
        {
            HandleUnreachableSeat();
            return;
        }

        // (в) Прямая дистанция до стула — «я у точки назначения» != «я у стула».
        float straightDistance = Vector3.Distance(transform.position, targetChair.GetPosition());
        if (straightDistance > seatMaxStraightDistance)
        {
            // Дошли до конца пути, но стул далеко (проекция уехала за стену /
            // стул в дыре carve). Не садимся — пробуем перестроить путь.
            HandleUnreachableSeat();
            return;
        }

        // (б) Фактическое прибытие: конец пути достигнут.
        // Mathf.Max со stoppingDistance — если в инспекторе задан тормозной путь,
        // remainingDistance никогда не опустится ниже него, и порог должен это учесть.
        float arriveThreshold = Mathf.Max(seatArriveDistance, agent.stoppingDistance + 0.05f);
        if (agent.remainingDistance <= arriveThreshold)
        {
            SitDown();
        }
    }

    /// <summary>
    /// Ленивое создание общего пробного пути. new NavMeshPath() разрешён только
    /// в рантайме (не в cctor) — поэтому метод, а не инициализатор поля.
    /// </summary>
    private static NavMeshPath GetPrecheckPath()
    {
        if (precheckPath == null)
        {
            precheckPath = new NavMeshPath();
        }
        return precheckPath;
    }

    /// <summary>
    /// Путь к назначенному стулу неполный или стул вне прямой досягаемости.
    /// Гость стоит на месте и каждые unreachableRetryInterval секунд пробует
    /// перестроить путь (дверь могла открыться, препятствие — сдвинуться).
    /// После maxUnreachableRetries неудач гость отказывается от места и уходит:
    /// LeaveTable безопасен для никогда не садившегося гостя — заказа нет,
    /// RemoveOrder пропустится, DiningTable.OnCustomerLeft вернёт стол в Free.
    /// </summary>
    private void HandleUnreachableSeat()
    {
        unreachableTimer += Time.deltaTime;

        if (unreachableTimer < unreachableRetryInterval)
            return;

        unreachableTimer = 0f;
        unreachableRetries++;

        if (targetChair == null)
            return;

        if (unreachableRetries > Mathf.Max(1, maxUnreachableRetries))
        {
            Debug.LogWarning(
                $"[CustomerAI] '{name}': место за '{diningTable.name}' недостижимо после {maxUnreachableRetries} попыток — гость уходит. " +
                "Проверь: 1) NavMesh соединяет спавн и зал (запеки сквозь дверной проём); " +
                "2) стул не заперт за стеной (особенно после переноса мебели через T); " +
                "3) carve-бокс NavMeshObstacle стола не накрывает стулья; " +
                "4) стулья стоят НА запечённой сетке.");
            AbandonUnreachableSeat();
            return;
        }

        if (agent.isOnNavMesh)
        {
            agent.SetDestination(targetChair.GetPosition());
            Debug.Log($"[CustomerAI] '{name}': путь к стулу неполный ({agent.pathStatus}), " +
                      $"прямая дистанция {Vector3.Distance(transform.position, targetChair.GetPosition()):F1} м. " +
                      $"Повторная попытка {unreachableRetries}/{maxUnreachableRetries}.");
        }
    }

    /// <summary>
    /// Гость так и не дошёл до стула: освобождает место и уходит через выход.
    /// Заказ не создавался (SelectRecipe вызывается только в SitDown), поэтому
    /// DeliveryManager и очередь заказов не затрагиваются.
    ///
    /// FIX: раньше здесь вызывался LeaveTable() без флага, и стол по правилу
    /// «один уходит — все уходят» утягивал СИДЯЩИХ гостей с уже сделанными
    /// заказами — это выглядело как «клиенты сразу уходят после заказа, если их 2».
    /// Теперь гость, который так и не сел, уходит ОДИН: остальные члены группы
    /// продолжают сидеть, ждать еду и есть. Стол освободится, когда уйдут они.
    /// </summary>
    public void AbandonUnreachableSeat()
    {
        LeaveTable(pullWholeGroup: false);
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

        // Новое назначение — сбрасываем счётчики недостижимости от предыдущей цели.
        unreachableTimer = 0f;
        unreachableRetries = 0;

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

                // МГНОВЕННАЯ диагностика: строим пробный путь и смотрим его статус.
                // Частичный путь виден сразу при назначении — не ждём 4.5 секунды
                // ретраев, чтобы понять, что стул за стеной.
                if (agent.CalculatePath(chair.GetPosition(), GetPrecheckPath())
                    && precheckPath.status != NavMeshPathStatus.PathComplete)
                {
                    Debug.LogWarning(
                        $"[CustomerAI] '{name}': место за '{table.name}' вероятно НЕДОСТИЖИМО " +
                        $"(статус пробного пути: {precheckPath.status}). Гость попробует дойти; " +
                        "если не выйдет — откажется от места. Причина обычно: стена между спавном и залом, " +
                        "стул вне NavMesh или carve-бокс стола накрывает стулья.");
                }
            }
            else
            {
                Debug.LogWarning($"[CustomerAI] Объект {name} заспавнился вне сетки NavMesh! Пожалуйста, запеките навигацию (Navigation window).");
                // Тест-телепортация к стулу, чтобы игра не ломалась, если сетка не запечена.
                // Через WarpTo: при updatePosition=false агент и Rigidbody должны
                // узнать о переносе одновременно, иначе гость «поедет» со старого места.
                if (physicsAgent != null)
                {
                    physicsAgent.WarpTo(chair.GetPosition());
                }
                else
                {
                    transform.position = chair.GetPosition();
                }
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
                // Телепорт к точке очереди — тоже через WarpTo (см. SetTargetSeat).
                if (physicsAgent != null)
                {
                    physicsAgent.WarpTo(queuePosition);
                }
                else
                {
                    transform.position = queuePosition;
                }
            }
        }

        // Новый маршрут — сбрасываем watchdog пути очереди.
        queuePathWatchdog = 0f;

        SetState(CustomerState.WalkingToQueue);
    }

    private void UpdateWalkingToQueue()
    {
        if (!agent.enabled || agent.pathPending)
            return;

        if (agent.hasPath && agent.remainingDistance <= 0.2f)
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
            return;
        }

        // Watchdog: раньше при НЕВАЛИДНОМ пути к очереди (hasPath == false,
        // remainingDistance == 0) гость «вставал в очередь» мгновенно на спавне,
        // даже не начав идти. Теперь даём queuePathWatchdogTime секунд на построение
        // пути; если он так и не построился — встаём в очередь там, где стоим:
        // стоять у стены в очереди приемлемо, а терпение очереди всё равно
        // корректно завершит состояние уходом.
        if (!agent.hasPath)
        {
            queuePathWatchdog += Time.deltaTime;

            if (queuePathWatchdog >= queuePathWatchdogTime)
            {
                queuePatienceTimer = maxQueuePatience;
                SetState(CustomerState.Queueing);

                float normalized = maxQueuePatience > 0f ? 1f : 0f;
                OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs
                {
                    progressNormalized = normalized
                });
            }
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
    /// тем же принципом, что и у рассаженных групп в DiningTable.
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
        // FIX: защита от повторного вызова (каскад DisbandQueuedGroup и тайминги
        // терпения могли вызвать LeaveQueue дважды для одного гостя).
        if (state == CustomerState.Leaving) return;

        SetState(CustomerState.Leaving);

        if (CustomerManager.Instance != null)
        {
            CustomerManager.Instance.RemoveCustomer(this);
        }

        leaveFailsafe = leaveFailsafeTime;

        if (agent != null)
        {
            agent.enabled = true;
            if (agent.isOnNavMesh)
            {
                // FIX: выход = exitPoint из инспектора, а не мировая точка (0,0,0)
                Vector3 exitPos = exitPoint != null ? exitPoint.position : Vector3.zero;
                agent.SetDestination(exitPos);
            }
        }

        // FIX: убран Destroy(gameObject, 5f) — гость удаляется в UpdateLeaving,
        // когда реально дошёл до выхода (или по страховочному leaveFailsafeTime).
    }

    /// <summary>
    /// FIX: деспавн по факту прихода к выходу вместо таймера Destroy(gameObject, 5f).
    /// Гость удаляется, когда реально дошёл до exitPoint; leaveFailsafeTime —
    /// страховка на случай, если путь так и не построился (навмеш разрезан,
    /// гость застрял) — тогда удаляем по таймауту.
    /// </summary>
    private void UpdateLeaving()
    {
        leaveFailsafe -= Time.deltaTime;
        if (leaveFailsafe <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        if (!agent.enabled || agent.pathPending)
            return;

        if (agent.hasPath && agent.remainingDistance <= exitArriveDistance)
        {
            Destroy(gameObject);
        }
    }

    private void SitDown()
    {
        SetState(CustomerState.Sitting);

        if (agent != null)
        {
            agent.enabled = false;
        }

        // Сидящий гость — кинематическое тело: стоит как вкопанный, PhysX его
        // не сдвигает, толкучка не стаскивает со стула (SetSeated гасит скорость
        // и отключает динамику). LeaveTable вернёт динамику.
        if (physicsAgent != null)
        {
            physicsAgent.SetSeated(true);
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

    /// <summary>
    /// Уход гостя (после еды, сердитым или отказом от недостижимого места).
    ///
    /// FIX 1: параметр pullWholeGroup. Сидящий гость, потерявший терпение, по-прежнему
    /// утягивает всю группу («один уходит — все уходят»), но гость, который так и
    /// не дошёл до стула (AbandonUnreachableSeat), уходит один — раньше из-за него
    /// срывались группы: второй клиент не доставал до стула, и стол уводил первого,
    /// уже сделавшего заказ.
    ///
    /// FIX 2: защита от повторного входа. При каскаде LeaveTable вызывался дважды
    /// для одного гостя, и заказ удалялся из DeliveryManager дважды — снимался
    /// ЧУЖОЙ идентичный заказ (например, зеркальный заказ соседа по столу).
    ///
    /// FIX 3: деспавн по прибытию к exitPoint вместо Destroy(gameObject, 5f) —
    /// гость больше не растворяется посреди зала на глазах у игрока.
    /// </summary>
    public void LeaveTable(bool pullWholeGroup = true)
    {
        if (state == CustomerState.Leaving) return; // FIX: уже уходим — повторный вызов игнорируем

        SetState(CustomerState.Leaving);

        // Если уходим сердитыми (или по ошибке с недоеденной едой), удаляем заказ из системы.
        // Для зеркальных заказов («Общий заказ») и мутаций («Алхимический хаос») снимается
        // ровно та же ссылка, что была добавлена в AddOrderFromTable — по ссылкам сходится.
        // Для гостя, который так и не сел (AbandonUnreachableSeat), orderedRecipe == null —
        // блок пропускается, очередь заказов не затрагивается.
        if (DeliveryManager.Instance != null && orderedRecipe != null)
        {
            DeliveryManager.Instance.RemoveOrder(orderedRecipe, diningTable);
            orderedRecipe = null; // FIX: исключаем повторное снятие заказа
        }

        if (targetChair != null)
        {
            targetChair.ClearCustomer();
        }

        if (diningTable != null)
        {
            diningTable.OnCustomerLeft(this, pullWholeGroup);
        }

        // Удаляем из глобального списка CustomerManager
        if (CustomerManager.Instance != null)
        {
            CustomerManager.Instance.RemoveCustomer(this);
        }

        // Встаём из-за стола: возвращаем телу динамику ДО включения агента
        // (SetSeated(false) гасит скорость и включает PhysX), затем синхронизируем
        // агента с фактической позицией — иначе SetDestination построит путь
        // от места, где агент «думал», что гость стоит.
        if (physicsAgent != null)
        {
            physicsAgent.SetSeated(false);
        }

        leaveFailsafe = leaveFailsafeTime;

        if (agent != null)
        {
            agent.enabled = true;

            if (physicsAgent != null)
            {
                physicsAgent.SyncAgentToTransform();
            }

            if (agent.isOnNavMesh)
            {
                // FIX: выход = exitPoint из инспектора, а не мировая точка (0,0,0)
                Vector3 exitPos = exitPoint != null ? exitPoint.position : Vector3.zero;
                agent.SetDestination(exitPos);
            }
        }

        // FIX: убран Destroy(gameObject, 5f) — удаление по факту прихода к выходу
        // (UpdateLeaving) или по страховочному таймеру leaveFailsafeTime.
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
