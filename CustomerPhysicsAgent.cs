using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// <summary>
/// Гибрид «агент рулит — физика исполняет» (вариант 1 миграции на обычную физику).
///
/// ЗАМЕНЯЕТ CustomerCollision (его компонент с префаба гостя снять).
///
/// Раньше: гость — NavMeshAgent, который телепортирует transform каждый кадр,
/// плюс КИНЕМАТИЧЕСКИЙ Rigidbody и ручное разведение (CustomerCollision).
/// PhysX в движении гостя не участвовал вообще.
///
/// Теперь: Rigidbody гостя ДИНАМИЧЕСКИЙ. NavMeshAgent остаётся мозгом
/// (путь, скорость, RVO-руление), но не двигает transform (updatePosition=false).
/// Его желаемая скорость (desiredVelocity) тело плавно отрабатывает как
/// настоящую физическую скорость, а контакты разруливает PhysX:
/// - гость-гость: честные столкновения капсул + RVO обход по маршруту;
/// - гость-стены/мебель: тело физически не пролезает сквозь геометрию;
/// - гость-дверь: динамическое полотно и гость толкаются по массам;
/// - игрок (двигается через transform) проходит СКВОЗЬ толпу: контакт гостя
///   с героем всегда МЯГКИЙ — гость ПИХАЕТ героя ограниченным толчком
///   (доля проникновения за физ-шаг уходит в Player.AddCrowdPush), а сам
///   слегка уступает плечом. Герой ощущает толкучку: его сдвигает и чуть
///   замедляет (Player.cs), но жёсткой коллизии и рывков нет;
/// - BodyPusher (на игроке) больше не швыряет гостей импульсом — гости
///   исключены из толкаемых целей на уровне кода (двери/ящики работают как раньше);
/// - гость → гость: PhysX + RVO, как раньше, плюс двухуровневое анти-залипание:
///   манёвр обхода дугой с эскалацией (сторона/угол/длительность растут от попытки
///   к попытке), а после нескольких неудач — АВАРИЙНЫЙ ПРОРЫВ: на rescueSeconds
///   коллизии с другими гостями отключаются и гость проходит «сквозь» толкучку.
///
/// СОСТОЯНИЯ:
/// - Активные (Walking / WalkingToQueue / Queueing / Leaving): тело
///   динамическое, скорость следует за агентом.
/// - Посаженные (SetSeated(true) из CustomerAI.SitDown): тело
///   кинематическое — сидящий гость неподвижен и твёрд для игрока,
///   не дрожит и не съезжает со стула от толкучки. LeaveTable возвращает
///   динамику и синхронизирует агента через Warp.
///
/// ТЕЛЕПОРТЫ: все перемещения «рывком» (спавн вне NavMesh, аварийная
/// посадка) в CustomerAI идут через WarpTo() — при updatePosition=false
/// без Warp агент повёл бы гостя со старого места.
///
/// ★ АНТИ-ЗАЛИПАНИЕ (обход толкучки) — три уровня эскалации:
/// 1) RVO-руление и PhysX сами стараются развести гостей;
/// 2) толкучка (контакт с другим гостем ИЛИ с игроком дольше guestJamSeconds
///    при живом курсе и без горизонтального хода) → манёвр обхода дугой:
///    на sidestepSeconds скорость ведётся в сторону ОТ блокера (лобовые
///    пары — по правилу «уходи влево»: встречные гости гарантированно берут
///    разные стороны и расходятся, а не грызутся на одной дуге). Попытка не
///    удалась — повтор с эскалацией: сторона меняется (при неопознанном
///    блокере — поочерёдно), угол растёт на sidestepAngleStepPerAttempt,
///    длительность — на sidestepExtraSecondsPerAttempt;
/// 3) после maxSidestepAttemptsBeforeRescue неудачных манёвров — аварийный
///    ПРОРЫВ (StartRescue): на rescueSeconds PhysX-коллизии этого гостя с
///    другими гостями отключаются (пара восстанавливается по таймеру после
///    выхода из толкучки), а тело идёт по пути НАПРЯМУЮ «сквозь» толкучку.
///    Игрок тоже не «прилипает» к гостю: он учитывается как блокер в том же
///    манёвре, а во время прорыва гость проходит мимо без толчков (пара
///    гость × игрок всегда мягкая — матрица коллизий выключена, пихание
///    делает скрипт, а не PhysX).
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(CapsuleCollider))]
[RequireComponent(typeof(Rigidbody))]
public class CustomerPhysicsAgent : MonoBehaviour
{
    [Header("Следование за агентом")]
    [Tooltip("Скорость подстройки скорости тела под желаемую агентом (1/с). " +
             "Больше — жёстче следование пути, меньше — инертнее и заметнее внешние толчки")]
    [SerializeField] private float velocityFollow = 10f;

    [Header("Физическое тело")]
    [Tooltip("Масса гостя (кг). Влияет на толкание с дверью и другими гостями")]
    [SerializeField] private float bodyMass = 80f;
    [Tooltip("Замораживать высоту (Y). Карта плоская — заморозка не даёт гостю " +
             "провалиться в carve-дыру стола или упасть с кромки NavMesh")]
    [SerializeField] private bool freezeYPosition = true;
    [Tooltip("Максимальная скорость выдавливания тела из геометрии (м/с). " +
             "Защита от «выстреливания» гостя при застревании")]
    [SerializeField] private float maxDepenetrationVelocity = 2f;

    [Header("Страховка NavMesh")]
    [Tooltip("Если гостя вытолкнули с NavMesh (в carve-дыру стола/двери, за кромку), " +
             "через это время он ставится на ближайшую точку сетки (сек). Лечит " +
             "«пропавших» гостей: путь восстанавливается раньше, чем гость откажется от места")]
    [SerializeField] private float navMeshRecoveryInterval = 0.5f;
    [Tooltip("Радиус поиска ближайшей точки NavMesh при возврате (м)")]
    [SerializeField] private float navMeshRecoveryRadius = 2.5f;

    [Header("Обход толкучки (анти-залипание гостей)")]
    [Tooltip("Сколько секунд гость может толкаться с другим гостем без прогресса, " +
             "прежде чем включится манёвр обхода (сек)")]
    [SerializeField] private float guestJamSeconds = 1.5f;
    [Tooltip("Длительность манёвра: столько времени скорость ведётся дугой вокруг " +
             "мешающего гостя (сек)")]
    [SerializeField] private float sidestepSeconds = 1f;
    [Tooltip("Угол дуги манёвра (градусы). 75 — заметный заход сбоку, 90 — почти боком")]
    [SerializeField, Range(15f, 120f)] private float sidestepAngle = 75f;
    [Tooltip("Пауза перед повторным манёвром, если толкучка не разрешилась (сек)")]
    [SerializeField] private float sidestepRepeatPause = 0.6f;
    [Tooltip("Сдвиг за физ-кадр (м), ниже которого гость считается не движущимся")]
    [SerializeField] private float sidestepProgressThreshold = 0.004f;
    [Tooltip("Сколько попыток дуги разрешено до аварийного прорыва")]
    [SerializeField, Range(1, 5)] private int maxSidestepAttemptsBeforeRescue = 2;
    [Tooltip("На сколько градусов растёт дуга обхода с каждой неудачной попыткой (градусы)")]
    [SerializeField] private float sidestepAngleStepPerAttempt = 15f;
    [Tooltip("На сколько секунд удлиняется дуга с каждой неудачной попыткой (сек)")]
    [SerializeField] private float sidestepExtraSecondsPerAttempt = 0.25f;

    [Header("Аварийный прорыв (последний уровень анти-залипания)")]
    [Tooltip("Сколько секунд гость проходит «сквозь» толкучку с отключёнными " +
             "коллизиями с другими гостями (сек)")]
    [SerializeField] private float rescueSeconds = 1.2f;
    [Tooltip("Пауза после прорыва, прежде чем снова возможен манёвр/прорыв (сек)")]
    [SerializeField] private float rescueCooldownSeconds = 3f;

    [Header("Мягкая толпа: гость пихает игрока")]
    [Tooltip("Какую долю глубины проникновения гость передаёт ИГРОКУ мягким толчком (0..1). " +
             "Игрок ощущает толкучку (его сдвигает), но жёсткой коллизии нет — герой " +
             "по-прежнему проходит сквозь толпу свободно")]
    [SerializeField, Range(0f, 1f)] private float playerPushShare = 0.5f;
    [Tooltip("Максимальный сдвиг ИГРОКА за один физический шаг (м) — потолок скорости, " +
             "с которой гость способен отодвигать героя. 0.035 ≈ 1.75 м/с при 50 Гц физики")]
    [SerializeField] private float maxPlayerPushPerStep = 0.035f;
    [Tooltip("Какую долю глубины проникновения гость гасит собственным сдвигом в сторону (0..1). " +
             "«Поворачивается плечом» — вместе с толчком делает контакт мягким для обеих сторон")]
    [SerializeField, Range(0f, 1f)] private float guestYieldShare = 0.45f;
    [Tooltip("Максимальный сдвиг СОБСТВЕННОГО тела гостя за один физический шаг (м). " +
             "Ограничивает скорость, с которой гость способен отступить в сторону от героя")]
    [SerializeField] private float maxGuestYieldPerStep = 0.04f;
    [Tooltip("Запас к сумме радиусов при поиске контакта с игроком. На силу уступки " +
             "не влияет — только на стабильность детекта контакта")]
    [SerializeField] private float playerScanMargin = 0.25f;

    [Header("Служебное")]
    [SerializeField] private bool debugLogs = false;

    private NavMeshAgent agent;
    private Rigidbody body;
    private CustomerAI self;
    private Collider selfCollider;

    // Радиус капсулы игрока — читается один раз при первом контакте.
    private float playerRadiusCache = -1f;

    // Одноразовое предупреждение про NavMeshObstacle на игроке.
    private bool playerObstacleWarned;

    // Гость посажен за стол (тело кинематическое).
    private bool seated;

    // Сколько времени тело гостя находится вне NavMesh подряд.
    private float offMeshTimer;

    // ★ Анти-залипание: контакт толпы (другой гость ИЛИ игрок) из скана
    // ScanCrowdContacts (перечитывается каждый физ-кадр) и состояние манёвра.
    private bool hadCrowdContact;
    private Vector3 lastBlockerPosition;
    private float guestContactDistance;
    private float guestJamTimer;
    private float sidestepTimer;
    private float sidestepCooldownTimer;
    private float sidestepSide;
    private Vector3 sidestepBaseDirection;
    private float sidestepSpeed;

    // ★ Эскалация и аварийный прорыв.
    // Сколько попыток дуги подряд не удались (сбрасывается при успехе).
    private int sidestepAttemptCount;
    // Остаток времени прорыва (>0 — тело идёт по пути «сквозь» толкучку).
    private float rescueTimer;
    // Пауза после прорыва, прежде чем снова возможен манёвр/прорыв.
    private float rescueCooldownTimer;
    private Vector3 rescueDirection;
    private float rescueSpeed;
    // Коллайдеры гостей с отключёнными на время прорыва коллизиями и моменты
    // (Time.time), когда пару можно восстановить (с запасом после выхода из толкучки).
    private readonly List<Collider> rescuedIgnoreColliders = new List<Collider>();
    private readonly List<float> rescuedIgnoreRestoreTimes = new List<float>();
    private Vector3 lastBodyPosition;
    private bool lastBodyPositionValid;

    // Переиспользуемые буферы — ноль аллокаций в физ-кадре.
    private static readonly Collider[] overlapBuffer = new Collider[32];

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        body = GetComponent<Rigidbody>();
        self = GetComponent<CustomerAI>();
        selfCollider = GetComponent<Collider>();

        // Динамическое тело: физика исполняет движение, заданное агентом.
        // Используется body.velocity (а не linearVelocity из Unity 6) — старое
        // имя компилируется во всех версиях, в Unity 6 это просто warning.
        body.isKinematic = false;
        body.mass = bodyMass;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        body.maxDepenetrationVelocity = maxDepenetrationVelocity;
        body.constraints = RigidbodyConstraints.FreezeRotation |
                           (freezeYPosition ? RigidbodyConstraints.FreezePositionY : 0);

        // Агент больше не двигает transform — путь и руление остаются за ним.
        agent.updatePosition = false;

        // Рулевое разведение агентов силами NavMesh-системы: гости огибают
        // друг друга по маршруту ещё до физических контактов.
        // Если в инспекторе уже выбрано качество выше — не трогаем.
        if (agent.obstacleAvoidanceType == ObstacleAvoidanceType.NoObstacleAvoidance)
        {
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
        }

        // ★ Радиус «настоящего касания» двух гостей: сумма агентовых радиусов
        // с запасом, но не меньше 0.6 м — капсулы префабов бывают шире
        // agent.radius, и RVO тогда недооценивает зазор (типичная причина толкучки).
        guestContactDistance = Mathf.Max(
            (agent != null ? agent.radius : 0.35f) * 2f + 0.1f,
            0.6f);

        if (debugLogs)
        {
            Debug.Log($"[CustomerPhysicsAgent] '{name}': гибрид активен " +
                      $"(масса {bodyMass} кг, follow {velocityFollow}, " +
                      $"Y {(freezeYPosition ? "заморожен" : "свободен")}).");
        }
    }

    /// <summary>
    /// True, пока гость сидит за столом (тело кинематическое — твёрдое для игрока).
    /// Player.cs использует это: сидящие гости остаются твёрдыми препятствиями,
    /// идущие — «мягкие», герой проходит сквозь них с лёгким замедлением.
    /// </summary>
    public bool IsSeated => seated;

    /// <summary>
    /// Переключает тело гостя «сидит/встал». Вызывается из CustomerAI:
    /// SitDown → SetSeated(true), LeaveTable → SetSeated(false).
    /// Сидящий гость кинематический: неподвижен, твёрд для игрока,
    /// PhysX его не сдвигает и не дрожит рядом со стулом/столом.
    /// </summary>
    public void SetSeated(bool value)
    {
        if (seated == value)
        {
            return;
        }

        seated = value;

        if (body == null)
        {
            return;
        }

        if (value)
        {
            body.linearVelocity = Vector3.zero;
            body.isKinematic = true;
        }
        else
        {
            body.isKinematic = false;
            body.linearVelocity = Vector3.zero;
        }
    }

    /// <summary>
    /// Телепорт гостя (спавн вне NavMesh, аварийная посадка на стул).
    /// Синхронизирует transform, физическое тело и внутреннюю позицию агента
    /// одним вызовом — при updatePosition=false без Warp агент считал бы,
    /// что гость всё ещё на старом месте, и повёл бы его оттуда.
    /// </summary>
    public void WarpTo(Vector3 position)
    {
        transform.position = position;

        if (body != null)
        {
            body.position = position;
            body.linearVelocity = Vector3.zero;
        }

        if (agent != null && agent.enabled)
        {
            if (agent.isOnNavMesh)
            {
                agent.Warp(position);
            }
            else
            {
                // Агент вне сетки — Warp бесполезен: переинициализация.
                // Transform уже перенесён выше, при включении агент
                // привяжется к ближайшей точке сетки.
                agent.enabled = false;
                agent.enabled = true;
            }
        }
    }

    /// <summary>
    /// Синхронизирует внутреннюю позицию агента с фактической. Вызывать сразу
    /// после agent.enabled = true у гостя, который постоял выключенным
    /// (LeaveTable) — ДО SetDestination, иначе путь построится от старого места.
    /// Если гость оказался вне сетки (мебель передвинули) — подтягиваем к
    /// ближайшей точке NavMesh, чтобы SetDestination вообще смог построить путь.
    /// </summary>
    public void SyncAgentToTransform()
    {
        if (agent == null || !agent.enabled)
        {
            return;
        }

        if (agent.isOnNavMesh)
        {
            agent.Warp(transform.position);
            return;
        }

        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
        {
            transform.position = hit.position;

            if (body != null)
            {
                body.position = hit.position;
            }

            agent.Warp(hit.position);
        }
    }

    private void FixedUpdate()
    {
        if (body == null)
        {
            return;
        }

        bool agentDrives = !seated &&
                           agent != null &&
                           agent.enabled &&
                           agent.isOnNavMesh &&
                           !body.isKinematic;

        // === Скан контактов: игрок (мягкая уступка) и другие гости (анти-залипание) ===
        // Выполняется ДО расчёта скорости: пометка контакта толпы нужна
        // манёвру обхода в этом же физ-кадре.
        if (agentDrives && IsGuestActiveState() && IsInteractionWindow())
        {
            ScanCrowdContacts();
        }
        else
        {
            hadCrowdContact = false;
        }

        // === Скорость: плавно тянем тело к желаемой скорости агента ===
        // Экспоненциальный Lerp — поведение не зависит от частоты физики.
        // Внешние толчки (игрок, дверь, другие гости) не затираются мгновенно:
        // их след гаснет за ~0.1-0.3 с, импульс успевает сдвинуть тело.
        Vector3 current = body.linearVelocity;
        Vector3 target;

        if (agentDrives)
        {
            Vector3 desired = agent.desiredVelocity;
            target = new Vector3(desired.x, current.y, desired.z);

            // ★ Аварийный прорыв: на rescueSeconds тело идёт по пути НАПРЯМУЮ
            // «сквозь» толкучку — PhysX его не удерживает (пара гость-гость отключена).
            if (rescueTimer > 0f && rescueDirection.sqrMagnitude > 0.001f)
            {
                Vector3 rescue = rescueDirection * rescueSpeed;
                target = new Vector3(rescue.x, current.y, rescue.z);
            }
            // ★ Манёвр обхода: пока он активен, скорость тела ведётся по дуге
            // вокруг мешающего гостя (в сторону от него) — даже если RVO-руление
            // в затыке заглушило desiredVelocity: гость физически огибает препятствие.
            else if (sidestepTimer > 0f && sidestepBaseDirection.sqrMagnitude > 0.001f)
            {
                Vector3 arced = Quaternion.Euler(0f, sidestepAngle * sidestepSide, 0f) *
                                (sidestepBaseDirection * sidestepSpeed);

                target = new Vector3(arced.x, current.y, arced.z);
            }
        }
        else
        {
            // Сидим или агент выключен — плавно останавливаемся на месте.
            target = new Vector3(0f, current.y, 0f);
        }

        float blend = 1f - Mathf.Exp(-velocityFollow * Time.fixedDeltaTime);
        body.linearVelocity = Vector3.Lerp(current, target, blend);

        // Обязательный ритуал гибрида: агент должен знать фактическую позицию
        // тела, иначе remainingDistance/pathStatus считаются от старого места.
        if (agent != null && agent.enabled)
        {
            agent.nextPosition = body.position;
        }

        // === Анти-залипание: толкучка → манёвр обхода, неудачные попытки → прорыв ===
        UpdateSidestepManeuver(agentDrives);
        UpdateRescueTimers();

        // === Страховка NavMesh: вытолкнутого с сетки гостя возвращаем назад ===
        // Толчок игрока/двери может занести тело в carve-дыру стола или двери,
        // за кромку NavMesh. Там pathStatus ломается, и через 3 ретрая гость
        // отказался бы от места и ушёл (симптом «клиент пропал»). Ставим тело
        // на ближайшую точку сетки — путь перестраивается сам.
        if (agentDrives)
        {
            if (agent.isOnNavMesh)
            {
                offMeshTimer = 0f;
            }
            else
            {
                offMeshTimer += Time.fixedDeltaTime;

                if (offMeshTimer >= navMeshRecoveryInterval &&
                    NavMesh.SamplePosition(
                        transform.position,
                        out NavMeshHit meshHit,
                        navMeshRecoveryRadius,
                        NavMesh.AllAreas))
                {
                    WarpTo(meshHit.position);
                    offMeshTimer = 0f;

                    if (debugLogs)
                    {
                        Debug.Log($"[CustomerPhysicsAgent] '{name}': тело вытолкнуто с NavMesh — возвращено на сетку.", this);
                    }
                }
            }
        }
        else
        {
            offMeshTimer = 0f;
        }
    }

    /// <summary>
    /// Гость активен, пока идёт/стоит в очереди/уходит. Вызывается только
    /// у таких гостей — сидящих игрок может беспрепятственно «обнимать»,
    /// они всё равно кинематические.
    /// </summary>
    private bool IsGuestActiveState()
    {
        if (self == null)
        {
            return false;
        }

        CustomerAI.CustomerState state = self.GetState();

        return state == CustomerAI.CustomerState.Walking ||
               state == CustomerAI.CustomerState.WalkingToQueue ||
               state == CustomerAI.CustomerState.Queueing ||
               state == CustomerAI.CustomerState.Leaving;
    }

    private bool IsInteractionWindow()
    {
        return GameLoopManager.Instance == null ||
               GameLoopManager.Instance.IsGamePlaying() ||
               GameLoopManager.Instance.IsPreparationActive();
    }

    /// <summary>
    /// Один OverlapSphere на физ-кадр. Гостя-гостя по-прежнему разводит PhysX;
    /// здесь: мягкое пихание гостем игрока (ResolvePlayerContact) и ЗАМЕТКА
    /// контакта толпы (другой гость ИЛИ игрок) для манёвра обхода (hadCrowdContact).
    /// </summary>
    private void ScanCrowdContacts()
    {
        hadCrowdContact = false;
        lastBlockerPosition = Vector3.zero;

        float scanRadius = GetPlayerScanRadius();

        // Сфера на высоте груди: ловит капсулу игрока (0..2 м), но не пол.
        Vector3 center = transform.position + Vector3.up * 1f;

        int hits = Physics.OverlapSphereNonAlloc(
            center,
            scanRadius,
            overlapBuffer,
            ~0,
            QueryTriggerInteraction.Ignore);

        // ★ Кламп: NonAlloc возвращает ОБЩЕЕ число попаданий, которое может
        // ПРЕВЫСИТЬ длину буфера — без клампа цикл падает
        // IndexOutOfRangeException каждый физ-шаг (та же причина, по которой
        // замирал игрок: см. Player.CapsuleBlocked).
        int resultCount = Mathf.Min(hits, overlapBuffer.Length);

        for (int i = 0; i < resultCount; i++)
        {
            Collider col = overlapBuffer[i];

            if (col == null)
            {
                break;
            }

            // Свою собственную капсулу не считаем помехой.
            if (col == selfCollider)
            {
                continue;
            }

            Player player = col.GetComponentInParent<Player>();

            if (player != null)
            {
                // Гость мягко пихает героя: ограниченный толчок игроку + своя
                // лёгкая уступка плечом (детали в ResolvePlayerContact).
                ResolvePlayerContact(player, col);
            }

            // ★ Анти-залипание: заодно фиксируем контакт с ДРУГИМ гостем —
            // в радиусе настоящего касания, а не всего скан-радиуса сферы.
            CustomerAI otherGuest = col.GetComponentInParent<CustomerAI>();

            if (otherGuest != null && otherGuest != self)
            {
                Vector3 toGuest = otherGuest.transform.position - transform.position;
                toGuest.y = 0f;

                if (toGuest.magnitude <= guestContactDistance)
                {
                    hadCrowdContact = true;

                    if (lastBlockerPosition.sqrMagnitude < 0.0001f)
                    {
                        lastBlockerPosition = otherGuest.transform.position;
                    }
                }
            }

            // Всё прочее (стены, мебель) — зона ответственности NavMesh-руления и PhysX.
        }
    }

    /// <summary>
    /// ★ Анти-залипание: накопление «толкучки» и запуск/гашение манёвра обхода,
    /// плюс эскалация попыток и вызов аварийного прорыва. Толкучка = контакт с
    /// другим гостем ИЛИ игроком при живом курсе (путь есть, до цели не дошёл)
    /// и без горизонтального прогресса. Манёвр: на sidestepSeconds (+ эскалация)
    /// желаемая скорость идёт дугой от блокера; контакт ушёл и ход набрался —
    /// гасим досрочно. Несколько неудачных попыток подряд — StartRescue.
    /// </summary>
    private void UpdateSidestepManeuver(bool agentDrives)
    {
        bool active = agentDrives && IsGuestActiveState() && IsInteractionWindow();

        if (!active)
        {
            guestJamTimer = 0f;
            sidestepTimer = 0f;
            sidestepAttemptCount = 0;
            lastBodyPosition = body != null ? body.position : transform.position;
            lastBodyPositionValid = body != null;
            return;
        }

        if (rescueTimer > 0f)
        {
            // Во время прорыва толкучка не копится: тело идёт по пути напрямую.
            guestJamTimer = 0f;
            sidestepTimer = 0f;
            lastBodyPosition = body.position;
            lastBodyPositionValid = true;
            return;
        }

        // Хочет ли гость идти: путь есть и до цели ещё не дошёл. Стоящий в
        // очереди и севший гость желания не выражают — толкучка «на месте»
        // манёвр не запускает.
        bool tryingToGo = agent.hasPath &&
                          agent.remainingDistance > agent.stoppingDistance + 0.1f;

        // Горизонтальный прогресс за физ-кадр.
        Vector3 frameDelta = body.position - lastBodyPosition;
        frameDelta.y = 0f;
        bool progressed = lastBodyPositionValid &&
                          frameDelta.magnitude > sidestepProgressThreshold;

        if (hadCrowdContact && tryingToGo && !progressed)
        {
            // Толкается с гостем/игроком, курс жив, а места не набирает — копим таймер.
            guestJamTimer += Time.fixedDeltaTime;
        }
        else
        {
            // Контакт или затык разрешились — таймер гаснет вдвое быстрее накопления.
            guestJamTimer = Mathf.Max(0f, guestJamTimer - Time.fixedDeltaTime * 2f);
        }

        if (sidestepCooldownTimer > 0f)
        {
            sidestepCooldownTimer -= Time.fixedDeltaTime;
        }

        if (rescueCooldownTimer > 0f)
        {
            rescueCooldownTimer -= Time.fixedDeltaTime;
        }

        if (sidestepTimer > 0f)
        {
            sidestepTimer -= Time.fixedDeltaTime;

            // Досрочный выход: контакт ушёл и гость снова набирает ход — манёвр
            // удался, счётчик эскалации обнуляется.
            if (!hadCrowdContact && progressed)
            {
                sidestepTimer = 0f;
                sidestepAttemptCount = 0;
            }
            else if (sidestepTimer <= 0f)
            {
                // Попытка истекла без успеха: если толкучка осталась — эскалация:
                // повтор дуги с большим углом/длительностью, после нескольких
                // неудач подряд — аварийный прорыв.
                sidestepTimer = 0f;

                if (tryingToGo && hadCrowdContact && rescueCooldownTimer <= 0f)
                {
                    if (sidestepAttemptCount >= maxSidestepAttemptsBeforeRescue)
                    {
                        StartRescue();
                    }
                    else
                    {
                        StartSidestep();
                    }
                }
                else
                {
                    sidestepAttemptCount = 0;
                }
            }
        }
        else if (tryingToGo &&
                 guestJamTimer >= guestJamSeconds &&
                 sidestepCooldownTimer <= 0f &&
                 rescueCooldownTimer <= 0f)
        {
            StartSidestep();
        }

        lastBodyPosition = body.position;
        lastBodyPositionValid = true;
    }

    /// <summary>
    /// ★ Запуск манёвра обхода с эскалацией: фиксируем курс на момент старта
    /// (желаемая скорость, иначе конечная точка пути, иначе взгляд), скорость
    /// и сторону дуги — от блокера (гость или игрок). Блокер не опознан —
    /// сторона чередуется от попытки к попытке; лобовой контакт (блокер почти
    /// точно по курсу) — правило «уходи влево», чтобы встречная пара гостей
    /// гарантированно взяла РАЗНЫЕ стороны и разошлась борт к борту, а не
    /// грызлась на одной дуге. Каждая неудачная попытка делает дугу круче и
    /// длиннее (sidestepAngleStepPerAttempt, sidestepExtraSecondsPerAttempt) —
    /// гость обходит толкучку всё решительнее.
    /// </summary>
    private void StartSidestep()
    {
        int attempt = sidestepAttemptCount; // 0 для первой попытки
        sidestepAttemptCount++;

        float angle = Mathf.Min(sidestepAngle + attempt * sidestepAngleStepPerAttempt, 120f);
        float duration = sidestepSeconds + attempt * sidestepExtraSecondsPerAttempt;

        sidestepTimer = duration;
        sidestepCooldownTimer = duration + sidestepRepeatPause;
        guestJamTimer = 0f;

        Vector3 course = agent != null ? agent.desiredVelocity : Vector3.zero;
        course.y = 0f;

        if (course.sqrMagnitude < 0.0001f && agent != null && agent.hasPath &&
            agent.path != null && agent.path.corners.Length >= 2)
        {
            course = agent.path.corners[agent.path.corners.Length - 1] - transform.position;
            course.y = 0f;
        }

        if (course.sqrMagnitude < 0.0001f)
        {
            course = transform.forward;
            course.y = 0f;
        }

        sidestepBaseDirection = course.normalized;
        sidestepSpeed = agent != null && agent.speed > 0.05f ? agent.speed : 1.5f;

        Vector3 toBlocker = lastBlockerPosition - transform.position;
        toBlocker.y = 0f;

        if (toBlocker.sqrMagnitude < 0.0001f)
        {
            // Блокер не опознан (контакт по буферу скана) — чередуем сторону дуги
            // от попытки к попытке: повторный неопознанный затык не бьётся
            // в ту же стену, а встречная пара с опознанными блокерами всё
            // равно разъедется по правилу «влево» ниже.
            sidestepSide = attempt % 2 == 0 ? -1f : 1f;
        }
        else
        {
            // Блокер левее курса (cross > 0) — дуга вправо (+1), и наоборот.
            float cross = sidestepBaseDirection.x * toBlocker.z -
                          sidestepBaseDirection.z * toBlocker.x;

            if (Mathf.Abs(cross) < toBlocker.magnitude * 0.15f)
            {
                // ★ ЛОБОВАЯ ВСТРЕЧА (блокер почти точно по курсу, |cross| —
                // численный шум): раньше оба гостя пары могли выбрать ОДНУ
                // сторону и продолжали грызться лбами. Правило «уходи влево»:
                // каждый сворачивает в СВОЮ левую сторону — у встречных это
                // противоположные стороны мира, пара расходится борт к борту
                // (как пешеходы на тротуаре).
                sidestepSide = -1f;
            }
            else
            {
                sidestepSide = cross > 0f ? 1f : -1f;
            }
        }

        if (debugLogs)
        {
            Debug.Log($"[CustomerPhysicsAgent] '{name}': толкучка — обход дугой " +
                      $"{(sidestepSide > 0f ? "вправо" : "влево")} ({angle:F0}°, " +
                      $"{duration:F1} с, попытка {sidestepAttemptCount}).", this);
        }
    }

    /// <summary>
    /// ★ Аварийный прорыв — последний уровень анти-залипания. После
    /// maxSidestepAttemptsBeforeRescue неудачных дуг: на rescueSeconds гость идёт
    /// по пути НАПРЯМУЮ: PhysX-коллизии с другими гостями отключаются (они сейчас
    /// касаются — иначе тела просто снова упираются друг в друга), и тело проходит
    /// толкучку «сквозь». Пары восстанавливаются по таймеру с запасом +0.3 с
    /// после конца прорыва — к этому моменту гости уже разошлись, а остаточное
    /// проникновение мягко разбирает Depenetration (maxDepenetrationVelocity).
    /// Гарантирует: никакая толкучка не длится вечно.
    /// </summary>
    private void StartRescue()
    {
        rescueTimer = rescueSeconds;
        rescueCooldownTimer = rescueSeconds + rescueCooldownSeconds;
        guestJamTimer = 0f;
        sidestepTimer = 0f;
        sidestepAttemptCount = 0;

        Vector3 course = agent != null ? agent.desiredVelocity : Vector3.zero;
        course.y = 0f;

        if (course.sqrMagnitude < 0.0001f && agent != null && agent.hasPath &&
            agent.path != null && agent.path.corners.Length >= 2)
        {
            course = agent.path.corners[agent.path.corners.Length - 1] - transform.position;
            course.y = 0f;
        }

        if (course.sqrMagnitude < 0.0001f)
        {
            course = transform.forward;
            course.y = 0f;
        }

        rescueDirection = course.normalized;
        rescueSpeed = agent != null && agent.speed > 0.05f ? agent.speed * 1.25f : 2f;

        // Отключаем пару гость-гость со всеми, кто касается прямо сейчас.
        int hits = Physics.OverlapSphereNonAlloc(
            transform.position + Vector3.up * 1f,
            guestContactDistance + 0.3f,
            overlapBuffer,
            ~0,
            QueryTriggerInteraction.Ignore);

        // ★ Кламп буфера — детали в комментарии к циклу выше.
        int resultCount = Mathf.Min(hits, overlapBuffer.Length);

        for (int i = 0; i < resultCount; i++)
        {
            Collider col = overlapBuffer[i];

            if (col == null || col == selfCollider)
            {
                continue;
            }

            CustomerAI other = col.GetComponentInParent<CustomerAI>();

            if (other == null || other == self)
            {
                continue;
            }

            if (rescuedIgnoreColliders.Contains(col))
            {
                continue;
            }

            if (selfCollider != null)
            {
                Physics.IgnoreCollision(selfCollider, col, true);
                rescuedIgnoreColliders.Add(col);
                rescuedIgnoreRestoreTimes.Add(Time.time + rescueSeconds + 0.3f);
            }
        }

        if (debugLogs)
        {
            Debug.Log($"[CustomerPhysicsAgent] '{name}': дуги не помогли — аварийный " +
                      $"прорыв сквозь толкучку ({rescueSeconds:F1} с).", this);
        }
    }

    /// <summary>
    /// Выполняется каждый физ-кадр: гашение таймера прорыва и восстановление
    /// отключённых пар гость-гость по их таймерам (даже после конца прорыва).
    /// </summary>
    private void UpdateRescueTimers()
    {
        if (rescueTimer > 0f)
        {
            rescueTimer -= Time.fixedDeltaTime;

            if (rescueTimer <= 0f)
            {
                rescueTimer = 0f;

                if (debugLogs)
                {
                    Debug.Log($"[CustomerPhysicsAgent] '{name}': прорыв завершён — " +
                              "пары коллизий будут восстановлены.", this);
                }
            }
        }

        for (int i = rescuedIgnoreColliders.Count - 1; i >= 0; i--)
        {
            Collider other = rescuedIgnoreColliders[i];

            if (other == null)
            {
                // Гость уничтожен (конец дня, выход в меню) — просто забываем пару.
                rescuedIgnoreColliders.RemoveAt(i);
                rescuedIgnoreRestoreTimes.RemoveAt(i);
                continue;
            }

            if (Time.time >= rescuedIgnoreRestoreTimes[i] && selfCollider != null)
            {
                Physics.IgnoreCollision(selfCollider, other, false);
                rescuedIgnoreColliders.RemoveAt(i);
                rescuedIgnoreRestoreTimes.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Полное восстановление отключённых пар (уничтожение гостя, выгрузка сцены).
    /// </summary>
    private void RestoreIgnoredColliders()
    {
        if (selfCollider != null)
        {
            for (int i = 0; i < rescuedIgnoreColliders.Count; i++)
            {
                Collider other = rescuedIgnoreColliders[i];

                if (other != null)
                {
                    Physics.IgnoreCollision(selfCollider, other, false);
                }
            }
        }

        rescuedIgnoreColliders.Clear();
        rescuedIgnoreRestoreTimes.Clear();
    }

    // ★ Если гость уничтожается во время прорыва (конец дня, выход в меню),
    // отключённые пары не должны остаться «проигноренными» навсегда.
    private void OnDestroy()
    {
        RestoreIgnoredColliders();
    }

    /// <summary>
    /// ★ Контакт гостя с игроком — МЯГКОЕ ПИХАНИЕ вместо жёсткой коллизии:
    /// гость передаёт герою часть глубины проникновения ограниченным толчком
    /// (playerPushShare, потолок maxPlayerPushPerStep → Player.AddCrowdPush),
    /// а сам слегка «поворачивается плечом» — гасит остаток собственным
    /// сдвигом в сторону (guestYieldShare, maxGuestYieldPerStep). Толпа
    /// ощущается: героя сдвигает и замедляет (Player.cs), но рывков нет и
    /// сквозь толпу он проходит свободно. Игрок попутно считается источником
    /// затыка: манёвр обхода срабатывает на него так же, как на другого гостя
    /// (застрявший НА игроке гость обойдёт его). Во время аварийного прорыва
    /// толчки отключены — гость проходит мимо молча.
    /// </summary>
    private void ResolvePlayerContact(Player player, Collider playerCol)
    {
        WarnIfPlayerHasObstacle(player);

        // Во время прорыва гость проходит «сквозь» всё — ни толчков, ни заметок.
        if (rescueTimer > 0f)
        {
            return;
        }

        float playerRadius = GetPlayerRadius(player, playerCol);
        float contactDistance = playerRadius + agent.radius;

        Vector3 toPlayer = player.transform.position - transform.position;
        toPlayer.y = 0f;

        float distance = toPlayer.magnitude;
        float overlap = contactDistance - distance;

        // Контакта нет — гость свободно подходит вплотную, никакого
        // заблаговременного толчка нет.
        if (overlap <= 0f)
        {
            return;
        }

        // Игрок — тоже источник затыка: манёвр обхода работает на него.
        hadCrowdContact = true;

        if (lastBlockerPosition.sqrMagnitude < 0.0001f)
        {
            lastBlockerPosition = player.transform.position;
        }

        Vector3 pushDirection;

        // Полное совпадение центров — толкаем в случайную сторону.
        if (distance < 0.0001f)
        {
            pushDirection = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));

            if (pushDirection.sqrMagnitude < 0.0001f)
            {
                pushDirection = Vector3.forward;
            }

            pushDirection.Normalize();
        }
        else
        {
            pushDirection = toPlayer / distance;
        }

        // ★ Мягкое пихание: игроку уходит доля проникновения за физ-шаг,
        // жёстко ограниченная потолком скорости. Сглаживание и проверку стен
        // выполняет Player.ApplyCrowdPush — отсюда уходит только «запрос
        // смещения», телепортов и рывков не бывает.
        float pushShift = Mathf.Min(overlap * playerPushShare, maxPlayerPushPerStep);

        if (pushShift > 0.0005f)
        {
            player.AddCrowdPush(pushDirection, pushShift);
        }

        // Гость сам чуть-чуть уступает плечом (в сторону ОТ игрока) —
        // остаток проникновения гасится мягче для обеих сторон.
        float yieldShift = Mathf.Min(overlap * guestYieldShare, maxGuestYieldPerStep);

        if (yieldShift > 0.0005f)
        {
            ShiftBody(-pushDirection * yieldShift);
        }
    }

    /// <summary>
    /// Сдвиг физического тела гостя (мягкая уступка при контакте с игроком). Пишем в
    /// body.position, а не в transform: тело динамическое, запись в transform
    /// дерётся с интерполяцией и PhysX-синком. Заодно держим агента в курсе.
    /// </summary>
    private void ShiftBody(Vector3 offset)
    {
        if (body == null)
        {
            transform.position += offset;
            return;
        }

        Vector3 target = body.position + offset;
        body.position = target;

        if (agent != null && agent.enabled)
        {
            agent.nextPosition = target;
        }
    }

    /// <summary>
    /// Одноразовая самодиагностика: NavMeshObstacle с Carve на игроке
    /// заставляет гостей обходить героя ещё на уровне маршрута.
    /// </summary>
    private void WarnIfPlayerHasObstacle(Player player)
    {
        if (playerObstacleWarned)
        {
            return;
        }

        playerObstacleWarned = true;

        NavMeshObstacle obstacle = player.GetComponent<NavMeshObstacle>();

        if (obstacle == null)
        {
            obstacle = player.GetComponentInChildren<NavMeshObstacle>();
        }

        if (obstacle != null && obstacle.enabled && obstacle.carving)
        {
            Debug.LogWarning("[CustomerPhysicsAgent] У игрока найден NavMeshObstacle с Carve — " +
                             "гости будут ОБХОДИТЬ героя по маршруту ещё до физики. Удали этот " +
                             "компонент с игрока, чтобы гости толкались, а не обходили.", obstacle);
        }
    }

    private float GetPlayerRadius(Player player, Collider playerCol)
    {
        if (playerRadiusCache > 0f)
        {
            return playerRadiusCache;
        }

        CapsuleCollider capsule = playerCol as CapsuleCollider;

        if (capsule == null)
        {
            capsule = player.GetComponentInChildren<CapsuleCollider>();
        }

        if (capsule != null)
        {
            float scale = Mathf.Max(
                Mathf.Abs(capsule.transform.lossyScale.x),
                Mathf.Abs(capsule.transform.lossyScale.z));
            playerRadiusCache = capsule.radius * scale;
        }
        else
        {
            playerRadiusCache = 0.7f; // игрок проекта: CapsuleCollider radius 0.7
        }

        return playerRadiusCache;
    }

    private float GetPlayerScanRadius()
    {
        float baseRadius = playerRadiusCache > 0f ? playerRadiusCache : 0.7f;
        return baseRadius + agent.radius + playerScanMargin;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 center = transform.position + Vector3.up * 1f;
        NavMeshAgent gizmoAgent = GetComponent<NavMeshAgent>();
        float guestRadius = gizmoAgent != null ? gizmoAgent.radius : 0.35f;

        // Красный — радиус контакта с игроком (ближе него гость начинает пихать героя).
        Gizmos.color = new Color(1f, 0.25f, 0.15f, 0.8f);
        Gizmos.DrawWireSphere(center, (playerRadiusCache > 0f ? playerRadiusCache : 0.7f) + guestRadius);
    }
}
