using UnityEngine;

/// <summary>
/// Физическая дверь для гостей — v2 «улучшенная физика».
///
/// ЧТО ИЗМЕНИЛОСЬ ОТНОСИТЕЛЬНО v1 (вариант А «магический триггер»):
/// В Task 31 гости получили CapsuleCollider + кинематический Rigidbody
/// (CustomerCollision). Теперь гость — физическое тело: его капсула
/// при касании ВЫТАЛКИВАЕТ динамичное полотно двери силами PhysX.
/// Магия v1 (створка распахивалась сама за 2.2 м до гостя, а вплотную
/// коллайдер полотна вообще выключался) больше не нужна и вредит:
/// дверь открывалась до касания, а выключенный коллайдер позволял
/// ИГРОКУ пройти сквозь закрытую дверь, пока рядом стоял гость.
///
/// КАК РАБОТАЕТ v2 (3 уровня, от физики к страховке):
///
/// 1) ФИЗИЧЕСКИЙ ТОЛЧОК (основной). Гость идёт к двери и УПИРАЕТСЯ в
///    закрытую створку телом. Кинематическая капсула гостя продавливает
///    динамический Rigidbody полотна — дверь распахивается от толчка,
///    как настоящая. Rigidbody.maxDepenetrationVelocity ограничивает
///    скорость выталкивания, чтобы дверь не «выстреливала» от касания.
///
/// 2) КОНТАКТНЫЙ АССИСТ. Когда гость вплотную к полотну (pushAssistRadius),
///    на створку подаётся мягкий крутящий момент ОТ гостя — как будто её
///    дожимают плечом. Это гарантия, что дверь открывается быстрее, чем
///    гость идёт: гость не успевает добежать до ещё закрытого полотна.
///    Если гость почти вплотную, а створка всё ещё почти закрыта —
///    момент удваивается (анти-клип буст).
///
/// 3) АВАРИЙНЫЙ ПРОПУСК (последний рубеж). Если гость уже в 0.5 м от
///    центра проёма, а створка всё ещё почти закрыта (физика не успела,
///    гостя заспавнили в проёме) — коллайдеры полотна на время
///    выключаются, чтобы не было видимого прохода сквозь доску.
///    В v1 это был основной режим с радиусом 1.1; теперь условия
///    настолько жёсткие, что срабатывает почти никогда.
///
/// 4) Гость отошёл дальше closeRadius → ассист выключается, пружина
///    автозакрытия из PhysicalDoor доводит створку сама.
///
/// ЧТО НЕ ТРОГАЕТ:
/// PhysicalDoor.cs и BodyPusher.cs не изменяются. Толкание двери игроком,
/// пружина, лимиты петли — всё работает как раньше. CustomerCollision.cs
/// (толкание гостей и игрока) — не изменяется.
///
/// НАСТРОЙКА СЦЕНЫ:
/// Повесь компонент на дверное ПОЛОТНО — тот объект, где уже висят
/// PhysicalDoor и Rigidbody. Rigidbody полотна должен быть динамическим
/// (НЕ isKinematic), иначе физический толчок гостей не работает.
/// </summary>
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
    // только на смене состояния, а не каждый кадр.
    private bool emergencyPass;

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

        // Аварийный пропуск: гость уже в проёме, а створка всё ещё почти
        // закрыта. Нормальная физика до этого не доводит — ассист открывает
        // дверь раньше; срабатывание здесь означает сбой (спавн в проёме и т.п.).
        bool shouldEmergencyPass = emergencyPassEnabled &&
                                   nearestGuestDistance <= emergencyPassRadius &&
                                   IsDoorAlmostClosed();

        if (shouldEmergencyPass != emergencyPass)
        {
            emergencyPass = shouldEmergencyPass;
            SetLeafCollidersEnabled(!emergencyPass);
        }
    }

    private void FixedUpdate()
    {
        if (!guestNear || leafBody == null || leafBody.isKinematic)
        {
            return;
        }

        ApplyOpeningTorque();
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
    }
}
