using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Физические столкновения гостя с другими гостями и с игроком.
///
/// ПРОБЛЕМА, КОТОРУЮ РЕШАЕТ:
/// Клиент — это NavMeshAgent: он двигается по запечённой сетке NavMesh
/// напрямую и физические коллайдеры ИГНОРИРУЕТ. Без этого скрипта гости
/// проходят насквозь друг через друга и сквозь игрока.
///
/// ПОВЕДЕНИЕ С ИГРОКОМ (важно!):
/// Гость НЕ обходит героя и НЕ уступает дорогу. Он идёт к своей цели
/// напрямую, а упёршись в игрока — ТОЛКАЕТ его перед собой: игрок
/// плавно оттесняется (со страховкой от вдавливания в стены, мебель
/// и других гостей). Если игроку отступать некуда — гость упирается
/// и давит на месте, но сквозь героя не проходит.
///
/// Чтобы это работало, у ИГРОКА НЕ ДОЛЖНО быть NavMeshObstacle с
/// включённым Carve: он вырезает дыру в NavMesh, и гости обходят
/// героя по маршруту ещё до всякой физики. Скрипт предупредит в
/// консоли, если найдёт такой компонент на игроке.
///
/// ПОВЕДЕНИЕ ГОСТЬ-ГОСТЬ:
/// Двойное разведение — рулевое (obstacleAvoidanceType агента) плюс
/// мягкое расталкивание на подлёте. Гости огибают друг друга и не
/// слипаются в толпе. Игрок — не агент, RVO-разведение на него
/// не действует: никто из гостей «по маршруту» от героя не шарахается.
///
/// КТО УСТУПАЕТ КОМУ:
/// гость → игрок (в контакте гость оттесняет ИГРОКА),
/// гость → гость (оба расходятся симметрично).
/// Стены и мебель не толкаются — их обходит NavMesh.
///
/// ВАЖНО (настройка сцены, без этого сломаются стулья!):
/// Проверка DiningTable.HasRoomForChair ловит ЛЮБОЙ посторонний коллайдер
/// рядом со стулом — капсула гостя считалась бы «мебелью в занятом месте»,
/// и стулья начали бы исчезать, когда гость просто проходит мимо. Поэтому:
/// создай слой Customer, назначь его корню префаба клиента и УБЕРИ слой
/// Customer из Chair Fit Check Mask у всех DiningTable (см. чеклист).
///
/// СОВМЕСТИМОСТЬ:
/// Player.cs, CustomerAI.cs, DiningTable.cs, PhysicalDoor.cs,
/// CustomerDoorOpener.cs — не изменяются. Луч взаимодействия E игрока бьёт
/// только по слою Counters, поэтому коллайдер гостя никогда не перехватит E.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(CapsuleCollider))]
[RequireComponent(typeof(Rigidbody))]
public class CustomerCollision : MonoBehaviour
{
    [Header("Расталкивание гостей между собой")]
    [Tooltip("Скорость разведения гостей (м/с) при полном перекрытии. На границе " +
             "дистанции — ноль (плавный спад). Больше значение — толпа плотнее " +
             "расталкивается, меньше — гости мягче «протискиваются».")]
    [SerializeField] private float pushStrength = 2.5f;
    [Tooltip("Максимальное смещение гостя за один физический шаг (м). Защита от рывков " +
             "и от вдавливания гостя в стену при толкучке.")]
    [SerializeField] private float maxPushPerStep = 0.03f;
    [Tooltip("Дистанция разведения гость-гость. 0 = agent.radius * 2 + 0.05 — " +
             "гости расходятся, едва соприкоснувшись капсулами.")]
    [SerializeField] private float customerPushDistance = 0f;

    [Header("Толкание игрока")]
    [Tooltip("Доля разводящего сдвига, которая уходит ИГРОКУ (0..1). Остаток гость " +
             "гасит собственным отшатом. 1 — гость выталкивает только игрока, " +
             "0.7 — игрок отступает, гость слегка пружинит.")]
    [SerializeField, Range(0f, 1f)] private float playerPushShare = 0.7f;
    [Tooltip("Максимальный сдвиг ИГРОКА за один физический шаг (м). Ограничивает " +
             "скорость, с которой гость способен оттеснить героя.")]
    [SerializeField] private float maxPlayerPushPerStep = 0.05f;
    [Tooltip("Запас к сумме радиусов при поиске контакта с игроком. На силу толкания " +
             "не влияет — только на стабильность детекта контакта.")]
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

    // Переиспользуемые буферы — ноль аллокаций в физ-кадре.
    private static readonly Collider[] overlapBuffer = new Collider[32];
    private static readonly RaycastHit[] castBuffer = new RaycastHit[8];

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        body = GetComponent<Rigidbody>();
        self = GetComponent<CustomerAI>();
        selfCollider = GetComponent<Collider>();

        // Гость — кинематическое тело: физика его видит, но силы на него
        // не действуют (дверь не швыряет гостей, BodyPusher их не толкает).
        body.isKinematic = true;

        // Рулевое разведение агентов силами самой NavMesh-системы.
        // Действует ТОЛЬКО между гостями: игрок — не агент, RVO его не видит,
        // поэтому от героя по маршруту гости не шарахаются.
        // Если в инспекторе уже выбрано качество выше — не трогаем.
        if (agent.obstacleAvoidanceType == ObstacleAvoidanceType.NoObstacleAvoidance)
        {
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
        }

        if (customerPushDistance <= 0f && agent != null)
        {
            customerPushDistance = agent.radius * 2f + 0.05f;
        }

        if (debugLogs)
        {
            Debug.Log($"[CustomerCollision] '{name}': расталкивание активно " +
                      $"(гость-гость {customerPushDistance:F2} м, игрока не обходит — толкание в контакте).");
        }
    }

    private void FixedUpdate()
    {
        if (!IsCollisionActive())
        {
            return;
        }

        ApplySeparation();
    }

    /// <summary>
    /// Столкновения работают только у «стоящих или идущих» гостей.
    /// Сидящих (Sitting / WaitingForFood / Eating / FinishedEating) не
    /// трогаем: гость, сидящий за столом, не должен съезжать со стула,
    /// когда мимо проходит игрок или другой гость.
    /// </summary>
    private bool IsCollisionActive()
    {
        if (GameLoopManager.Instance != null &&
            !GameLoopManager.Instance.IsGamePlaying() &&
            !GameLoopManager.Instance.IsPreparationActive())
        {
            return false;
        }

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

    /// <summary>
    /// Один OverlapSphere на физ-кадр. Гость-гость — мягкое расталкивание
    /// на подлёте, игрок — толкание в физическом контакте.
    /// </summary>
    private void ApplySeparation()
    {
        float scanRadius = Mathf.Max(customerPushDistance, GetPlayerScanRadius());

        // Сфера на высоте груди: ловит капсулы гостей (центр ~1 м) и
        // капсулу игрока (0..2 м), но не пол.
        Vector3 center = transform.position + Vector3.up * 1f;

        int hits = Physics.OverlapSphereNonAlloc(
            center,
            scanRadius,
            overlapBuffer,
            ~0,
            QueryTriggerInteraction.Ignore);

        Vector3 totalPush = Vector3.zero;
        bool hasPush = false;

        for (int i = 0; i < hits; i++)
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
                // Гость игрока НЕ уступает: в контакте он ОТТЕСНЯЕТ героя.
                ResolvePlayerContact(player, col);
                continue;
            }

            CustomerAI otherGuest = col.GetComponentInParent<CustomerAI>();

            // Не гость (стена, мебель, дверь, реквизит) — не расталкиваем:
            // обходом стен занимается NavMesh, а капсулами мебели — путь агента.
            if (otherGuest == null || otherGuest == self)
            {
                continue;
            }

            Vector3 away = transform.position - otherGuest.transform.position;
            away.y = 0f;

            float distance = away.magnitude;

            if (distance >= customerPushDistance)
            {
                continue;
            }

            // Полное совпадение центров (заспавнились в одной точке) —
            // разводим в случайную сторону, иначе направление не определить.
            if (distance < 0.0001f)
            {
                away = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));

                if (away.sqrMagnitude < 0.0001f)
                {
                    away = Vector3.forward;
                }

                distance = 0.0001f;
            }

            away.Normalize();

            // Спад силы: на границе дистанции — ноль, при полном перекрытии — 1.
            float falloff = 1f - Mathf.Clamp01(distance / customerPushDistance);

            totalPush += away * (pushStrength * falloff * Time.fixedDeltaTime);
            hasPush = true;
        }

        if (!hasPush)
        {
            return;
        }

        totalPush = Vector3.ClampMagnitude(totalPush, maxPushPerStep);

        // Небольшой сдвиг transform безопасен для NavMeshAgent — агент
        // продолжает вести гостя с нового места (так же работает RVO).
        transform.position += totalPush;
    }

    /// <summary>
    /// Контакт гостя с игроком: гость оттесняет героя на свою долю
    /// (playerPushShare), неразрешённый остаток гасит отшатом.
    /// Итог: свободного игрока гость толкает перед собой, зажатого
    /// стеной — давит на месте, сквозь не проходит.
    /// </summary>
    private void ResolvePlayerContact(Player player, Collider playerCol)
    {
        WarnIfPlayerHasObstacle(player);

        float playerRadius = GetPlayerRadius(player, playerCol);
        float contactDistance = playerRadius + agent.radius;

        Vector3 away = transform.position - player.transform.position;
        away.y = 0f;

        float distance = away.magnitude;
        float overlap = contactDistance - distance;

        // Контакта нет — гость свободно подходит вплотную, никакой
        // заблаговременной уступки больше нет.
        if (overlap <= 0f)
        {
            return;
        }

        // Полное совпадение центров — толкаем в случайную сторону.
        if (distance < 0.0001f)
        {
            away = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));

            if (away.sqrMagnitude < 0.0001f)
            {
                away = Vector3.forward;
            }

            away.Normalize();
        }
        else
        {
            away.Normalize();
        }

        // Доля игрока: часть перекрытия гость передаёт герою.
        float requested = Mathf.Min(overlap * playerPushShare, maxPlayerPushPerStep);
        float applied = MovePlayerSafely(player, playerCol, away, requested);

        // Всё, что игрок «не взял» (упёрся в стену/мебель/другого гостя),
        // гость гасит отшатом — без этого он проседал бы в капсулу игрока.
        float residual = overlap - applied;

        if (residual > 0.0005f)
        {
            transform.position -= away * residual;
        }
    }

    /// <summary>
    /// Сдвиг игрока со страховкой: капсульный каст по направлению толкания.
    /// Стены, мебель, дверь и другие гости гасят сдвиг — игрок не будет
    /// вдавлен в геометрию и не окажется внутри чужой капсулы.
    /// Возвращает фактически применённый сдвиг.
    /// </summary>
    private float MovePlayerSafely(Player player, Collider playerCol, Vector3 direction, float shift)
    {
        if (shift <= 0.0001f)
        {
            return 0f;
        }

        CapsuleCollider capsule = playerCol as CapsuleCollider;

        if (capsule == null)
        {
            capsule = player.GetComponentInChildren<CapsuleCollider>();
        }

        if (capsule == null)
        {
            // Капсулы на игроке нет — толкать нечем, контакт погасит отшат гостя.
            return 0f;
        }

        // Капсула игрока вертикальная (ось Y), как в проекте: центр (0,1,0), рост 2.
        float scale = Mathf.Max(
            Mathf.Abs(capsule.transform.lossyScale.x),
            Mathf.Abs(capsule.transform.lossyScale.z));
        float radius = capsule.radius * scale;
        float height = Mathf.Max(capsule.height * Mathf.Abs(capsule.transform.lossyScale.y), radius * 2f);
        Vector3 center = capsule.transform.TransformPoint(capsule.center);
        float half = Mathf.Max(height * 0.5f - radius, 0f);

        Vector3 point1 = center + Vector3.up * half;
        Vector3 point2 = center - Vector3.up * half;

        int castHits = Physics.CapsuleCastNonAlloc(
            point1,
            point2,
            radius,
            direction,
            castBuffer,
            shift,
            ~0,
            QueryTriggerInteraction.Ignore);

        float allowed = shift;

        for (int i = 0; i < castHits; i++)
        {
            RaycastHit hit = castBuffer[i];

            // NonAlloc заполняет буфер подряд; страховка от пустых записей.
            if (hit.collider == null)
            {
                continue;
            }

            // Сам игрок и толкающий гость путь не перекрывают.
            if (hit.transform.IsChildOf(player.transform))
            {
                continue;
            }

            if (hit.transform.IsChildOf(transform))
            {
                continue;
            }

            // Стена / мебель / дверь / другой гость — режем сдвиг до препятствия.
            allowed = Mathf.Min(allowed, Mathf.Max(hit.distance - 0.005f, 0f));
        }

        if (allowed <= 0.0001f)
        {
            return 0f;
        }

        player.transform.position += direction * allowed;
        return allowed;
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
            Debug.LogWarning("[CustomerCollision] У игрока найден NavMeshObstacle с Carve — " +
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

        // Красный — радиус контакта с игроком (ближе него гость начинает толкать).
        Gizmos.color = new Color(1f, 0.25f, 0.15f, 0.8f);
        Gizmos.DrawWireSphere(center, (playerRadiusCache > 0f ? playerRadiusCache : 0.7f) + guestRadius);

        // Жёлтый — дистанция разведения гостей.
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(center, customerPushDistance > 0f
            ? customerPushDistance
            : guestRadius * 2f + 0.05f);
    }
}