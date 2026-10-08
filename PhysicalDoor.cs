using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Физическая распашная дверь: игрок толкает её телом (на игроке должен быть BodyPusher),
/// пружина сама закрывает дверь обратно. HingeJoint настраивается скриптом автоматически
/// в Awake — вручную в инспекторе ничего добавлять не нужно.
/// Вешается на СТВОРКУ двери. Пивот створки должен стоять на ЛИНИИ ПЕТЕЛЬ!
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PhysicalDoor : MonoBehaviour
{
    [Header("Петля")]
    [Tooltip("Точка петли в ЛОКАЛЬНЫХ координатах створки. Если пивот на краю двери со стороны петель — оставь (0,0,0).")]
    [SerializeField] private Vector3 hingeAnchorLocal = Vector3.zero;

    [Tooltip("Ось петли в локальных координатах. Обычная дверь — строго вверх (0,1,0).")]
    [SerializeField] private Vector3 hingeAxisLocal = Vector3.up;

    [Header("Открытие")]
    [Tooltip("На сколько градусов открывается дверь в каждую сторону")]
    [SerializeField] private float openAngle = 110f;

    [Tooltip("Распашная дверь: качается в ОБЕ стороны (как в кафе/кухне). Выключи — будет открываться только от себя")]
    [SerializeField] private bool swingsBothWays = true;

    [Header("Автозакрытие пружиной")]
    [Tooltip("Пружина тянет дверь к закрытому положению. Выключи — дверь останется там, где её толкнули")]
    [SerializeField] private bool autoClose = true;

    [Tooltip("Сила пружины автозакрытия")]
    [SerializeField] private float closeStrength = 10f;

    [Tooltip("Демпфер — гасит раскачку и дребезг двери у закрытого положения")]
    [SerializeField] private float closeDamping = 3f;

    [Header("Физика")]
    [Tooltip("Масса створки (кг). Тяжелее — открывается медленнее и величественнее")]
    [SerializeField] private float doorMass = 20f;

    [Tooltip("Замок: заблокированную дверь нельзя толкнуть (створка замирает)")]
    [SerializeField] private bool startLocked = false;

    [Header("Звук (опционально)")]
    [Tooltip("Скрип/стук — воспроизводится, когда дверь пришла в движение. Оставь пустым, если звука нет")]
    [SerializeField] private AudioSource creakSound;

    [Header("События")]
    public UnityEvent onOpened;
    public UnityEvent onClosed;

    public bool IsLocked => locked;

    private Rigidbody rb;
    private HingeJoint joint;
    private bool locked;
    private bool wasOpen;

    // Угол, начиная с которого дверь считается открытой (для событий)
    private const float OpenThreshold = 5f;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.mass = Mathf.Max(1f, doorMass);
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // Петля: берём существующий HingeJoint или добавляем свой
        joint = GetComponent<HingeJoint>();
        if (joint == null) joint = gameObject.AddComponent<HingeJoint>();

        joint.connectedBody = null; // петля закреплена в мире (в раме doorway)

        // Ось петли: защита от нулевого вектора
        Vector3 axis = hingeAxisLocal;
        if (axis.sqrMagnitude < 0.001f) axis = Vector3.up;
        joint.axis = axis.normalized;

        // Точка петли в локальных координатах створки
        joint.anchor = hingeAnchorLocal;

        // Лимиты угла: распашная качается от -openAngle до +openAngle,
        // обычная — только от 0 до openAngle
        joint.useLimits = true;
        float a = Mathf.Abs(openAngle);
        joint.limits = new JointLimits
        {
            min = swingsBothWays ? -a : 0f,
            max = a,
            bounciness = 0f
        };

        // Пружина: автозакрытие (тянется к 0) либо «трение»
        // (spring=0, демпфер гасит колебания — дверь остаётся там, где остановили)
        joint.useSpring = true;
        joint.spring = new JointSpring
        {
            targetPosition = 0f,
            spring = autoClose ? closeStrength : 0f,
            damper = closeDamping
        };

        locked = startLocked;
        ApplyLock();
    }

    private void FixedUpdate()
    {
        if (joint == null) return;

        // События «открылась» / «закрылась»
        bool isOpen = Mathf.Abs(joint.angle) > OpenThreshold;
        if (isOpen != wasOpen)
        {
            if (isOpen) onOpened?.Invoke();
            else onClosed?.Invoke();
            wasOpen = isOpen;
        }

        // Скрип при движении (если звук назначен)
        if (creakSound != null && !creakSound.isPlaying &&
            rb.angularVelocity.magnitude > 0.8f)
        {
            creakSound.Play();
        }
    }

    /// <summary>
    /// Запрограммированный толчок двери (для кнопок, заклинаний, ИИ).
    /// worldDirection — направление толчка в мировых координатах.
    /// </summary>
    public void Push(Vector3 worldDirection, float power)
    {
        if (locked || joint == null) return;

        Vector3 axisWorld = transform.TransformDirection(joint.axis).normalized;
        float dot = Vector3.Dot(worldDirection, transform.right);
        float side = Mathf.Abs(dot) < 0.01f ? 1f : Mathf.Sign(dot);

        rb.AddTorque(axisWorld * side * power, ForceMode.Impulse);
    }

    /// <summary>Заблокировать/разблокировать дверь (замок).</summary>
    public void SetLocked(bool value)
    {
        locked = value;
        ApplyLock();
    }

    private void ApplyLock()
    {
        if (rb != null) rb.isKinematic = locked;
    }

    // Жёлтая линия в редакторе показывает, где встанет ось петли.
    // Выдели створку — линия должна проходить ровно через край двери у петель.
    private void OnDrawGizmosSelected()
    {
        Vector3 worldAnchor = transform.TransformPoint(hingeAnchorLocal);
        Vector3 axis = hingeAxisLocal;
        if (axis.sqrMagnitude < 0.001f) axis = Vector3.up;
        Vector3 worldAxis = transform.TransformDirection(axis.normalized);

        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(worldAnchor, 0.05f);
        Gizmos.DrawLine(worldAnchor - worldAxis, worldAnchor + worldAxis);
    }
}