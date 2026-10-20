using System.Collections.Generic;
using UnityEngine;


[RequireComponent(typeof(Rigidbody))]
public class CustomerDoorOpener : MonoBehaviour
{
    [Header("Физический толчок гостей")]
    [Tooltip("Радиус контактного ассиста: гость ближе этого расстояния от центра " +
             "проёма — створку дожимают «плечом» (мягкий момент от гостя). " +
             "Должен быть примерно равен сумме половины ширины полотна и радиуса " +
             "капсулы гостя (~1 м), т.е. момент включается В МОМЕНТ касания, " +
             "а не заранее.")]
    [SerializeField] private float pushAssistRadius = 1f;
    [Tooltip("Гистерезис: пока ближайший гость ближе этого радиуса, ассист держится " +
             "включённым и дверь не хлопает по пятам. Должен быть БОЛЬШЕ " +
             "pushAssistRadius. 0 = pushAssistRadius + 0.6.")]
    [SerializeField] private float closeRadius = 0f;
    [Tooltip("Плавность физического выталкивания (м/с): максимум скорости, с которой " +
             "перекрытие капсулы гостя и полотна разгоняет створку. Убирает «выстрел» " +
             "двери от касания. 0 = не трогать настройку Rigidbody.")]
    [SerializeField] private float leafMaxDepenetration = 2.5f;

    [Header("Ассист-момент (дожим створки)")]
    [Tooltip("Мягкий крутящий момент дожима (режим Acceleration — не зависит от массы " +
             "створки). Основную работу делает физический толчок тела, ассист только " +
             "гарантирует скорость открытия выше скорости гостя.")]
    [SerializeField] private float assistTorque = 12f;
    [Tooltip("Максимальная скорость раскрытия в градусах/сек — чтобы дверь " +
             "не выстреливала как ловушка.")]
    [SerializeField] private float maxOpenSpeed = 180f;
    [Tooltip("Анти-клип буст: если ближайший гость ближе этого радиуса, а створка " +
             "открыта меньше boostAngle градусов — момент умножается на boostMultiplier.")]
    [SerializeField] private float boostRadius = 0.7f;
    [Tooltip("Порог «дверь ещё почти закрыта» для буста, в градусах.")]
    [SerializeField] private float boostAngle = 25f;
    [Tooltip("Во сколько раз усиливается момент при анти-клип бусте.")]
    [SerializeField] private float boostMultiplier = 2f;

    [Header("Аварийный пропуск (последний рубеж)")]
    [Tooltip("Включить аварийный пропуск: если физика не успела открыть дверь, " +
             "коллайдер полотна на время выключается, чтобы гость не прошёл " +
             "визуально сквозь доску. При работающей физике почти не срабатывает.")]
    [SerializeField] private bool emergencyPassEnabled = true;
    [Tooltip("Гость ближе этого расстояния от центра проёма + дверь почти закрыта " +
             "= авария. Намеренно мал (в v1 основной режим был 1.1).")]
    [SerializeField] private float emergencyPassRadius = 0.5f;
    [Tooltip("Створка открыта меньше этого угла (градусы) — считается «почти закрыта» " +
             "для аварийного пропуска.")]
    [SerializeField] private float emergencyMaxDoorAngle = 20f;

    [Header("Призрачная дверь по времени контакта (v2.1)")]
    [Tooltip("Если клиент непрерывно упирается в полотно дольше этого времени (сек) и не может пройти — полотно становится призрачным: коллайдер выключается, клиент проходит, потом коллайдер возвращается. 0 = выключить призрака по времени, останется только аварийный радиус.")]
    [SerializeField] private float pushTimeToGhost = 2.5f;
    [Tooltip("Тело, движущееся быстрее этой скорости (м/с), не считается «упирающимся» — отсекает гостей, проходящих мимо на бегу.")]
    [SerializeField] private float stuckSpeedThreshold = 1f;
    [Tooltip("Минимальное время призрачного состояния (сек) — защита от мерцания коллайдера.")]
    [SerializeField] private float ghostMinTime = 0.4f;
    [Tooltip("Призрак снимается, когда никто из гостей не подходит к «линии полотна» ближе этого радиуса (м). Примерно радиус капсулы гостя + запас.")]
    [SerializeField] private float leafClearRadius = 0.45f;
    [Tooltip("Пауза после возврата коллайдера (сек), прежде чем дверь снова сможет стать призрачной.")]
    [SerializeField] private float ghostCooldown = 0.5f;
    [Tooltip("Логировать включение/выключение призрачного режима — полезно при отладке застреваний.")]
    [SerializeField] private bool ghostDebugLogs = true;

    [Header("Служебное")]
    [Tooltip("Как часто искать гостей (сек). Клиентов мало, но и сканировать " +
             "каждый кадр незачем.")]
    [SerializeField] private float scanInterval = 0.12f;
    [Tooltip("Запас в градусах: если в выбранную сторону створке осталось " +
             "меньше — открываем в другую сторону (на случай односторонних " +
             "углов в PhysicalDoor).")]
    [SerializeField] private float limitMargin = 3f;
    [SerializeField] private bool debugLogs = false;

    private Rigidbody leafBody;
    private HingeJoint hinge;
    private Collider[] leafColliders;

    // Центр проёма: снимается один раз при старте, пока дверь закрыта и
    // коллайдеры включены. Все радиусы меряются от этой точки.
    private Vector3 doorCenter;

    // Гостевой контур с гистерезисом: true, пока хотя бы один гость
    // внутри closeRadius (вход в контур — по pushAssistRadius).
    private bool guestNear;
    private float scanTimer;

    // Текущее состояние аварийного пропуска — коллайдеры переключаются
    // только на смене состояния, а не каждый кадр. С v2.1 этот флаг общий
    // для обеих причин призрака: и аварийный радиус, и долгий контакт.
    private bool emergencyPass;

    // Призрак сработал из-за долгого контакта, а не аварийного радиуса (для логов).
    private bool ghostByStuck;
    private float ghostedAt;
    private float ghostCooldownUntil;

    // Накопленное время НЕПРЕРЫВНОГО контакта гостей с полотном.
    // Ключ — Rigidbody гостя; контакт прервался — таймер гостя обнуляется.
    private readonly Dictionary<Rigidbody, float> pushTimers = new Dictionary<Rigidbody, float>();

    // Кто контактировал на последнем физическом шаге (для сброса ушедших).
    private readonly HashSet<Rigidbody> stepTouches = new HashSet<Rigidbody>();

    // Длина полотна: расстояние петля → центр масс × 2. Снимается в Start,
    // пока дверь закрыта. Нужна геометрической проверке «линии полотна»,
    // потому что при выключенных коллайдерах физика контактов молчит.
    private float leafLength;

    private void Start()
    {
        leafBody = GetComponent<Rigidbody>();
        hinge = GetComponent<HingeJoint>();
        leafColliders = GetComponentsInChildren<Collider>();

        // PhysicalDoor создаёт HingeJoint в своём Awake — все Awake на сцене
        // отрабатывают до любого Start, поэтому здесь джоинт уже существует.
        if (hinge == null)
        {
            Debug.LogWarning(
                "[CustomerDoorOpener] HingeJoint не найден на створке. " +
                "Скрипт должен висеть на том же объекте, что PhysicalDoor.");
        }

        if (leafBody != null && !leafBody.isKinematic && leafMaxDepenetration > 0f)
        {
            // Плавный физический толчок: капсула гостя выдавливает полотно
            // с ограниченной скоростью, без «выстрела» двери от касания.
            leafBody.maxDepenetrationVelocity = leafMaxDepenetration;
        }

        // Длина полотна для геометрической проверки «линии полотна» в
        // призрачном режиме: центр масс лежит посередине створки.
        if (leafBody != null && hinge != null)
        {
            Vector3 pivot = transform.TransformPoint(hinge.anchor);
            leafLength = Vector3.Distance(leafBody.worldCenterOfMass, pivot) * 2f;
        }

        doorCenter = ComputeDoorCenter();

        if (closeRadius <= 0f)
        {
            closeRadius = pushAssistRadius + 0.6f;
        }
    }

    /// <summary>
    /// Центр проёма: центр коллайдера полотна (створка закрыта) либо позиция
    /// объекта, если коллайдера нет. Снимается один раз в Start.
    /// </summary>
    private Vector3 ComputeDoorCenter()
    {
        if (leafColliders != null)
        {
            foreach (Collider col in leafColliders)
            {
                if (col != null && col.enabled && !col.isTrigger)
                {
                    return col.bounds.center;
                }
            }
        }

        return transform.position;
    }

    private void Update()
    {
        scanTimer -= Time.deltaTime;

        if (scanTimer > 0f)
        {
            return;
        }

        scanTimer = scanInterval;

        float nearestGuestDistance = FindNearestGuestDistance();

        // Гистерезис: ассист включается, когда гость УЖЕ КАСАЕТСЯ полотна
        // (pushAssistRadius), а не за два метра до двери, как в v1.
        if (!guestNear && nearestGuestDistance <= pushAssistRadius)
        {
            guestNear = true;

            if (debugLogs)
            {
                Debug.Log("[CustomerDoorOpener] Гость упёрся в дверь — физический толчок + ассист.");
            }
        }
        else if (guestNear && nearestGuestDistance > closeRadius)
        {
            guestNear = false;

            if (debugLogs)
            {
                Debug.Log("[CustomerDoorOpener] Гость прошёл — пружина закрывает дверь.");
            }
        }

        // Призрачный режим: аварийный радиус (гость в проёме при закрытой
        // створке) либо долгий контакт (клиент упирается дольше нормы).
        UpdateGhostPass(nearestGuestDistance);
    }

    private void FixedUpdate()
    {
        DecayStuckTimers();

        if (!guestNear || leafBody == null || leafBody.isKinematic)
        {
            return;
        }

        ApplyOpeningTorque();
    }

    /// <summary>
    /// Сброс таймеров контакта: кто не прижимался к полотну на последнем
    /// физическом шаге — тот ушёл, его накопленное время обнуляется.
    /// «Долго упирается» значит именно НЕПРЕРЫВНО, поэтому никаких
    /// накапливающихся «суммарных касаний» тут нет.
    /// </summary>
    private void DecayStuckTimers()
    {
        if (emergencyPass)
        {
            // Пока полотно призрачное, контактов нет — таймеры уже обнулены.
            stepTouches.Clear();
            return;
        }

        foreach (KeyValuePair<Rigidbody, float> timer in pushTimers)
        {
            if (!stepTouches.Contains(timer.Key))
            {
                pushTimers[timer.Key] = 0f;
            }
        }

        stepTouches.Clear();
    }

    /// <summary>
    /// Учёт контакта полотна с гостями: капсула гостя давит на створку,
    /// но пройти не может — копим время. Работает и с кинематическими,
    /// и с динамическими капсулами гостей (колбэки идут по Rigidbody полотна).
    /// </summary>
    private void OnCollisionStay(Collision collision)
    {
        if (emergencyPass || pushTimeToGhost <= 0f)
        {
            return;
        }

        Rigidbody body = collision.rigidbody;

        if (body == null || body == leafBody)
        {
            return;
        }

        if (!IsCustomerCollider(collision.collider))
        {
            return;
        }

#if UNITY_6000_0_OR_NEWER
        float speed = body.linearVelocity.magnitude;
#else
        float speed = body.velocity.magnitude;
#endif
        // Гость, который реально идёт своим путём, контакта почти не имеет.
        // Застрявший стоит на месте. Быстрые тела в копилку не идут.
        if (speed > stuckSpeedThreshold)
        {
            return;
        }

        // Полотно может состоять из нескольких коллайдеров — за один
        // физический шаг колбэк придёт по паре на каждый коллайдер.
        // Время контакта копим один раз на гостя.
        if (!stepTouches.Add(body))
        {
            return;
        }

        float current;
        pushTimers.TryGetValue(body, out current);
        pushTimers[body] = current + Time.fixedDeltaTime;
    }

    /// <summary>
    /// Принадлежит ли коллайдер одному из живых гостей. Гости берутся из
    /// CustomerManager — как и во всех остальных проверках этого скрипта.
    /// </summary>
    private bool IsCustomerCollider(Collider other)
    {
        if (other == null)
        {
            return false;
        }

        Transform otherTransform = other.transform;

        if (CustomerManager.Instance != null)
        {
            foreach (CustomerAI customer in CustomerManager.Instance.GetCustomers())
            {
                if (customer != null && otherTransform.IsChildOf(customer.transform))
                {
                    return true;
                }
            }

            return false;
        }

        // Тестовый стенд без CustomerManager: ищем CustomerAI в родителях.
        return otherTransform.GetComponentInParent<CustomerAI>() != null;
    }

    /// <summary>
    /// Призрачный режим полотна: коллайдер выключен, пока через проём
    /// кто-то проходит; возвращается, когда «линию полотна» никто не касается.
    ///
    /// Две причины включить:
    /// 1) АВАРИЯ (как в v2): гость в emergencyPassRadius от центра проёма,
    ///    а створка всё ещё почти закрыта.
    /// 2) ДОЛГИЙ КОНТАКТ (v2.1): клиент непрерывно упирается в полотно
    ///    дольше pushTimeToGhost секунд — створка наполовину открыта поперёк
    ///    прохода, гостя подпирает очередь, полотно зажато с двух сторон и т.п.
    ///
    /// Возврат коллайдера — только когда минимум ghostMinTime прошло И никто
    /// из гостей не подходит к линии полотна ближе leafClearRadius. После
    /// возврата держится пауза ghostCooldown против мерцания.
    /// </summary>
    private void UpdateGhostPass(float nearestGuestDistance)
    {
        if (emergencyPass)
        {
            if (Time.time - ghostedAt < ghostMinTime || AnyGuestTouchesLeafLine())
            {
                return;
            }

            emergencyPass = false;
            ghostCooldownUntil = Time.time + ghostCooldown;
            SetLeafCollidersEnabled(true);

            if (debugLogs || ghostDebugLogs)
            {
                Debug.Log(ghostByStuck
                    ? "[CustomerDoorOpener] Проём свободен — коллайдер полотна включён обратно."
                    : "[CustomerDoorOpener] Аварийный пропуск завершён — коллайдер полотна включён обратно.");
            }

            ghostByStuck = false;
            return;
        }

        float maxPushTime = 0f;

        foreach (KeyValuePair<Rigidbody, float> timer in pushTimers)
        {
            if (timer.Value > maxPushTime)
            {
                maxPushTime = timer.Value;
            }
        }

        bool emergencyCondition = emergencyPassEnabled &&
                                  nearestGuestDistance <= emergencyPassRadius &&
                                  IsDoorAlmostClosed();
        bool stuckCondition = pushTimeToGhost > 0f && maxPushTime >= pushTimeToGhost;

        if ((!emergencyCondition && !stuckCondition) || Time.time < ghostCooldownUntil)
        {
            return;
        }

        emergencyPass = true;
        ghostByStuck = stuckCondition;
        ghostedAt = Time.time;
        pushTimers.Clear();
        stepTouches.Clear();
        SetLeafCollidersEnabled(false);

        if (debugLogs || ghostDebugLogs)
        {
            Debug.Log(ghostByStuck
                ? $"[CustomerDoorOpener] Клиент {maxPushTime:F1} сек упирался в дверь — дверь стала призрачной."
                : "[CustomerDoorOpener] Гость в проёме при закрытой створке — аварийный пропуск.");
        }
    }

    /// <summary>
    /// Касается ли кто-то из гостей «линии полотна» (отрезок от петли длиной
    /// leafLength, с запасом leafClearRadius). Геометрическая проверка —
    /// работает и при выключенных коллайдерах, когда физика контактов молчит.
    /// </summary>
    private bool AnyGuestTouchesLeafLine()
    {
        if (hinge == null || leafLength <= 0f)
        {
            return false;
        }

        Vector3 pivot = transform.TransformPoint(hinge.anchor);
        Vector3 axisWorld = transform.TransformDirection(hinge.axis).normalized;

        // Направление полотна: проекция вектора «петля → центр масс»
        // на плоскость, перпендикулярную оси петли.
        Vector3 leafDirection = leafBody != null
            ? leafBody.worldCenterOfMass - pivot
            : transform.right;

        leafDirection -= Vector3.Dot(leafDirection, axisWorld) * axisWorld;

        if (leafDirection.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        leafDirection.Normalize();

        float clearSqr = leafClearRadius * leafClearRadius;

        if (CustomerManager.Instance != null)
        {
            foreach (CustomerAI customer in CustomerManager.Instance.GetCustomers())
            {
                if (customer != null &&
                    GuestCrossesLeafLine(customer.transform.position, pivot, leafDirection, clearSqr))
                {
                    return true;
                }
            }

            return false;
        }

        CustomerAI[] sceneGuests = FindObjectsByType<CustomerAI>(FindObjectsSortMode.None);

        foreach (CustomerAI customer in sceneGuests)
        {
            if (customer != null &&
                GuestCrossesLeafLine(customer.transform.position, pivot, leafDirection, clearSqr))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Точка гостя в пределах leafClearRadius от отрезка полотна?
    /// Проверка горизонтальная (высота игнорируется) — капсула гостя и
    /// створка пересекаются по высоте всегда.
    /// </summary>
    private bool GuestCrossesLeafLine(Vector3 guestPosition, Vector3 pivot, Vector3 leafDirection, float clearSqr)
    {
        Vector3 toGuest = guestPosition - pivot;
        float alongLeaf = Vector3.Dot(toGuest, leafDirection);

        // Гость заведомо за краем полотна (с запасом) — проходу не мешает.
        if (alongLeaf < -leafClearRadius || alongLeaf > leafLength + leafClearRadius)
        {
            return false;
        }

        Vector3 closestPoint = pivot + leafDirection * Mathf.Clamp(alongLeaf, 0f, leafLength);

        return HorizontalSqrDistance(closestPoint, guestPosition) <= clearSqr;
    }

    /// <summary>
    /// Створка «почти закрыта»: модуль угла петли меньше порога.
    /// Позиция покоя PhysicalDoor = закрытая дверь, поэтому |angle| —
    /// это и есть текущая раскрытость.
    /// </summary>
    private bool IsDoorAlmostClosed()
    {
        if (hinge == null)
        {
            return true;
        }

        return Mathf.Abs(hinge.angle) < emergencyMaxDoorAngle;
    }

    /// <summary>
    /// Расстояние по горизонтали от центра проёма до ближайшего гостя.
    /// Гости берутся из CustomerManager (живой список) — коллайдеры им
    /// для этого не нужны. Если менеджера нет (тестовый стенд) — ищем
    /// по сцене.
    /// </summary>
    private float FindNearestGuestDistance()
    {
        bool hasGuest = false;
        float bestSqrDistance = float.MaxValue;

        if (CustomerManager.Instance != null)
        {
            foreach (CustomerAI customer in CustomerManager.Instance.GetCustomers())
            {
                if (customer == null)
                {
                    continue;
                }

                float sqrDistance = HorizontalSqrDistance(doorCenter, customer.transform.position);

                if (sqrDistance < bestSqrDistance)
                {
                    bestSqrDistance = sqrDistance;
                    hasGuest = true;
                }
            }
        }
        else
        {
            // Тестовый стенд без CustomerManager: ищем по всей сцене.
            CustomerAI[] sceneGuests = FindObjectsByType<CustomerAI>(FindObjectsSortMode.None);

            foreach (CustomerAI customer in sceneGuests)
            {
                if (customer == null)
                {
                    continue;
                }

                float sqrDistance = HorizontalSqrDistance(doorCenter, customer.transform.position);

                if (sqrDistance < bestSqrDistance)
                {
                    bestSqrDistance = sqrDistance;
                    hasGuest = true;
                }
            }
        }

        return hasGuest ? Mathf.Sqrt(bestSqrDistance) : float.MaxValue;
    }

    private static float HorizontalSqrDistance(Vector3 from, Vector3 to)
    {
        float dx = to.x - from.x;
        float dz = to.z - from.z;

        return dx * dx + dz * dz;
    }

    /// <summary>
    /// Контактный ассист: мягкий момент ОТ ближайшего гостя, пока он
    /// прижимается к полотну. Направление — как у распашной двери,
    /// которую толкнули «от себя»: знак поворота = знак тройного
    /// произведения оси петли, вектора на полотно и вектора на гостя.
    /// Если в эту сторону упирается лимит петли — открываем в свободную.
    /// </summary>
    private void ApplyOpeningTorque()
    {
        if (hinge == null)
        {
            return;
        }

        // Ось петли в мировых координатах (у PhysicalDoor ось локальная оси Y створки).
        Vector3 axisWorld = transform.TransformDirection(hinge.axis).normalized;

        // Точка петли в мире.
        Vector3 pivot = transform.TransformPoint(hinge.anchor);

        // Ближайший гость.
        Transform nearestGuest = FindNearestGuestTransform();

        if (nearestGuest == null)
        {
            return;
        }

        // Направление от петли на полотно и от петли на гостя (горизонтально).
        Vector3 leafDirection = leafBody.worldCenterOfMass - pivot;
        leafDirection.y = 0f;

        Vector3 toGuest = nearestGuest.position - pivot;
        toGuest.y = 0f;

        if (leafDirection.sqrMagnitude < 0.0001f || toGuest.sqrMagnitude < 0.0001f)
        {
            return;
        }

        // Знак поворота: крутим полотно в сторону гостя (открывается «от него»).
        float crossSign = Mathf.Sign(Vector3.Dot(axisWorld, Vector3.Cross(leafDirection, toGuest)));

        if (crossSign == 0f)
        {
            crossSign = 1f;
        }

        // Лимит петли в выбранную сторону исчерпан — пробуем другую сторону.
        if (hinge.useLimits && !HasSwingRoom(crossSign))
        {
            float oppositeSign = -crossSign;

            if (HasSwingRoom(oppositeSign))
            {
                crossSign = oppositeSign;
            }
            else
            {
                // Обе стороны закрыты — открывать некуда, аварийный пропуск
                // всё равно выпустит гостя.
                return;
            }
        }

        // Базовый момент ассиста.
        float torque = assistTorque;

        // Анти-клип буст: гость почти вплотную, а створка ещё почти закрыта —
        // дожимаем в два раза сильнее, чтобы гость не догнал полотно.
        float nearestGuestDistance = Mathf.Sqrt(HorizontalSqrDistance(doorCenter, nearestGuest.position));

        if (nearestGuestDistance <= boostRadius && Mathf.Abs(hinge.angle) < boostAngle)
        {
            torque *= boostMultiplier;
        }

        // Ограничение скорости: не разгоняем створку выше maxOpenSpeed.
        float currentSpin = Vector3.Dot(leafBody.angularVelocity, axisWorld);

        if (crossSign * currentSpin * Mathf.Rad2Deg >= maxOpenSpeed)
        {
            return;
        }

        leafBody.AddTorque(
            axisWorld * (crossSign * torque),
            ForceMode.Acceleration);
    }

    /// <summary>
    /// Есть ли у петли запас хода в направлении swingSign (в градусах
    /// больше limitMargin). Без лимитов запас считается бесконечным.
    /// </summary>
    private bool HasSwingRoom(float swingSign)
    {
        if (!hinge.useLimits)
        {
            return true;
        }

        JointLimits limits = hinge.limits;
        float currentAngle = hinge.angle;

        // Створка может на мгновение выскочить за лимит на скорости —
        // на этот случай считаем запас от зажатого в диапазон угла.
        currentAngle = Mathf.Clamp(currentAngle, limits.min, limits.max);

        float room = swingSign > 0f
            ? limits.max - currentAngle
            : currentAngle - limits.min;

        return room > limitMargin;
    }

    private Transform FindNearestGuestTransform()
    {
        Transform best = null;
        float bestSqrDistance = float.MaxValue;

        if (CustomerManager.Instance != null)
        {
            foreach (CustomerAI customer in CustomerManager.Instance.GetCustomers())
            {
                if (customer == null)
                {
                    continue;
                }

                float sqrDistance = HorizontalSqrDistance(doorCenter, customer.transform.position);

                if (sqrDistance < bestSqrDistance)
                {
                    bestSqrDistance = sqrDistance;
                    best = customer.transform;
                }
            }
        }
        else
        {
            CustomerAI[] sceneGuests = FindObjectsByType<CustomerAI>(FindObjectsSortMode.None);

            foreach (CustomerAI customer in sceneGuests)
            {
                if (customer == null)
                {
                    continue;
                }

                float sqrDistance = HorizontalSqrDistance(doorCenter, customer.transform.position);

                if (sqrDistance < bestSqrDistance)
                {
                    bestSqrDistance = sqrDistance;
                    best = customer.transform;
                }
            }
        }

        return best;
    }

    private void SetLeafCollidersEnabled(bool enabledState)
    {
        if (leafColliders == null)
        {
            return;
        }

        foreach (Collider col in leafColliders)
        {
            if (col != null)
            {
                col.enabled = enabledState;
            }
        }

        if (debugLogs)
        {
            Debug.Log($"[CustomerDoorOpener] Коллайдер полотна: " +
                      $"{(enabledState ? "включён" : "выключен (аварийный пропуск)")}");
        }
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 center = Application.isPlaying ? doorCenter : transform.position;

        // Жёлтый — радиус контактного ассиста (гость касается полотна).
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(center, pushAssistRadius);

        // Оранжевый — радиус анти-клип буста.
        Gizmos.color = new Color(1f, 0.55f, 0.15f, 0.8f);
        Gizmos.DrawWireSphere(center, boostRadius);

        // Красный — радиус аварийного пропуска.
        Gizmos.color = new Color(1f, 0.25f, 0.15f, 0.8f);
        Gizmos.DrawWireSphere(center, Mathf.Max(emergencyPassRadius, 0.1f));

        // Голубая линия — «линия полотна»: пока её касается кто-то из гостей,
        // призрачный режим не выключается.
        if (Application.isPlaying && hinge != null)
        {
            Vector3 pivot = transform.TransformPoint(hinge.anchor);
            Vector3 axisWorld = transform.TransformDirection(hinge.axis).normalized;
            Vector3 leafDirection = leafBody != null
                ? leafBody.worldCenterOfMass - pivot
                : transform.right;
            leafDirection -= Vector3.Dot(leafDirection, axisWorld) * axisWorld;

            if (leafDirection.sqrMagnitude > 0.0001f)
            {
                leafDirection.Normalize();
                Vector3 tip = pivot + leafDirection * Mathf.Max(leafLength, pushAssistRadius);

                Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.8f);
                Gizmos.DrawLine(pivot, tip);
            }
        }
    }
}
