using UnityEngine;
using UnityEngine.AI;

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
/// - игрок (двигается через transform) для PhysX — «бесконечно тяжёлый»:
///   проходя сквозь толпу, он РАЗДВИГАЕТ гостей; BodyPusher дополнительно
///   может швырять гостей импульсом (слой Customer в Pushable Layers);
/// - гость → игрок: как и раньше, кастомный код ОТТЕСНЯЕТ игрока
///   (перенесён из CustomerCollision без изменения логики). Поэтому в
///   матрице коллизий пара Customer × Player должна быть ВЫКЛЮЧЕНА —
///   иначе PhysX и этот код будут драться за одну пару капсул.
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

    [Header("Толкание игрока (перенесено из CustomerCollision)")]
    [Tooltip("Доля разводящего сдвига, которая уходит ИГРОКУ (0..1). Остаток гость " +
             "гасит собственным отшатом. 1 — гость выталкивает только игрока, " +
             "0.7 — игрок отступает, гость слегка пружинит")]
    [SerializeField, Range(0f, 1f)] private float playerPushShare = 0.7f;
    [Tooltip("Максимальный сдвиг ИГРОКА за один физический шаг (м). Ограничивает " +
             "скорость, с которой гость способен оттеснить героя")]
    [SerializeField] private float maxPlayerPushPerStep = 0.05f;
    [Tooltip("Запас к сумме радиусов при поиске контакта с игроком. На силу толкания " +
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

    // Переиспользуемые буферы — ноль аллокаций в физ-кадре.
    private static readonly Collider[] overlapBuffer = new Collider[32];
    private static readonly RaycastHit[] castBuffer = new RaycastHit[8];

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

        if (debugLogs)
        {
            Debug.Log($"[CustomerPhysicsAgent] '{name}': гибрид активен " +
                      $"(масса {bodyMass} кг, follow {velocityFollow}, " +
                      $"Y {(freezeYPosition ? "заморожен" : "свободен")}).");
        }
    }

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

        // === Гость оттесняет игрока (CustomerCollision + фикс знаков) ===
        if (agentDrives && IsGuestActiveState() && IsInteractionWindow())
        {
            ApplyPlayerPush();
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
    /// Один OverlapSphere на физ-кадр. Гостя-гостя больше не разводим вручную —
    /// их разводит PhysX. Здесь остаётся только контакт с игроком.
    /// </summary>
    private void ApplyPlayerPush()
    {
        float scanRadius = GetPlayerScanRadius();

        // Сфера на высоте груди: ловит капсулу игрока (0..2 м), но не пол.
        Vector3 center = transform.position + Vector3.up * 1f;

        int hits = Physics.OverlapSphereNonAlloc(
            center,
            scanRadius,
            overlapBuffer,
            ~0,
            QueryTriggerInteraction.Ignore);

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
            }

            // Всё прочее (стены, мебель, другие гости) — зона ответственности
            // NavMesh-руления и PhysX.
        }
    }

    /// <summary>
    /// Контакт гостя с игроком: гость оттесняет героя на свою долю
    /// (playerPushShare), неразрешённый остаток гасит отшатом.
    /// Итог: свободного игрока гость толкает перед собой, зажатого
    /// стеной — давит на месте, сквозь не проходит.
    /// Логика перенесена из CustomerCollision.ResolvePlayerContact без изменений.
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
        // заблаговременной уступки нет.
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
        // ФИКС ЗНАКОВ (наследован из CustomerCollision): раньше сдвиги были
        // направлены ДРУГ К ДРУГУ — пара «схлопывалась», динамическое тело гостя
        // выстреливало из геометрии, и клиент выглядел «пропавшим». Правильно:
        // игрока толкаем ОТ гостя (-away), гостя слегка пружинит ОТ игрока (+away).
        float requested = Mathf.Min(overlap * playerPushShare, maxPlayerPushPerStep);
        float applied = MovePlayerSafely(player, playerCol, -away, requested);

        // Всё, что игрок «не взял» (упёрся в стену/мебель/другого гостя),
        // гость гасит отшатом — без этого он проседал бы в капсулу игрока.
        float residual = overlap - applied;

        if (residual > 0.0005f)
        {
            ShiftBody(away * residual);
        }
    }

    /// <summary>
    /// Сдвиг физического тела гостя (отшат при контакте с игроком). Пишем в
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
    /// Сдвиг игрока со страховкой: капсульный каст по направлению толкания.
    /// Стены, мебель, дверь и другие гости гасят сдвиг — игрок не будет
    /// вдавлен в геометрию и не окажется внутри чужой капсулы.
    /// Возвращает фактически применённый сдвиг.
    /// Логика перенесена из CustomerCollision.MovePlayerSafely без изменений.
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

        // Красный — радиус контакта с игроком (ближе него гость начинает толкать).
        Gizmos.color = new Color(1f, 0.25f, 0.15f, 0.8f);
        Gizmos.DrawWireSphere(center, (playerRadiusCache > 0f ? playerRadiusCache : 0.7f) + guestRadius);
    }
}
