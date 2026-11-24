using UnityEngine;
using System;

public class Player : MonoBehaviour, IKitchenObjectParent
{
    // Одиночка (Singleton) для глобального доступа к игроку
    public static Player Instance { get; private set; }

    // События для изменения выбранного стола (Selected Counter) и переноски объектов
    public event EventHandler<OnSelectedCounterChangedEventArgs> OnSelectedCounterChanged;
    public class OnSelectedCounterChangedEventArgs : EventArgs
    {
        public BaseCounter selectedCounter;
    }

    public event EventHandler OnPickedSomething;

    [Header("Настройки перемещения")]
    [SerializeField] private float moveSpeed = 7f;
    [SerializeField] private float rotateSpeed = 10f;
    [SerializeField] private LayerMask countersLayerMask;

    [Header("Настройки взаимодействия")]
    [SerializeField] private float interactDistance = 2f;
    [SerializeField] private float playerRadius = 0.7f;
    [SerializeField] private float playerHeight = 2f;

    [Header("Мягкая толпа (проход сквозь гостей)")]
    [Tooltip("Во сколько раз замедляется игрок в самой густой толпе (0.6 = минус 40% скорости)")]
    [SerializeField, Range(0.3f, 1f)] private float crowdMinSpeedMultiplier = 0.6f;
    [Tooltip("Суммарная глубина проникновения в капсулы гостей (м), при которой замедление максимально")]
    [SerializeField] private float crowdFullPenetration = 0.8f;
    [Tooltip("Скорость набора/спада замедления (1/сек) — защита от рывков при контакте")]
    [SerializeField] private float crowdResponseSpeed = 8f;

    [Header("Мягкий пуш от гостей (толпа пихает героя)")]
    [Tooltip("Максимальная скорость, с которой толпа способна сдвигать игрока (м/с). " +
             "Запросы приходят из CustomerPhysicsAgent: доля проникновения гостя за физ-шаг")]
    [SerializeField] private float crowdPushMaxSpeed = 2.5f;
    [Tooltip("Скорость нарастания/затухания толчка (1/сек). Больше — отзывчивее, " +
             "меньше — инертнее и мягче")]
    [SerializeField] private float crowdPushResponse = 10f;

    [Header("Притяжение к выбранной станции")]
    [Tooltip("На каком расстоянии от станции игрок 'встаёт' при взаимодействии")]
    [SerializeField] private float snapStandDistance = 1.2f;
    [Tooltip("Скорость притягивания позиции к точке у станции")]
    [SerializeField] private float snapMoveSpeed = 4f;
    [Tooltip("Скорость довора лицом к станции")]
    [SerializeField] private float snapRotateSpeed = 8f;

    [Header("Буфер взаимодействия")]
    [Tooltip("Сколько секунд после нажатия кнопка 'ждёт' появления валидной станции")]
    [SerializeField] private float interactBufferDuration = 0.2f;

    [Header("Точка удержания предметов")]
    [SerializeField] private Transform kitchenObjectHoldPoint;

    private bool isWalking;
    private Vector3 lastInteractDir;
    private BaseCounter selectedCounter;
    private KitchenObject kitchenObject;

    // -1 значит "буфер неактивен"; иначе — момент времени (Time.time), до которого
    // ожидающее нажатие ещё считается актуальным
    private float interactBufferExpireTime = -1f;
    private float interactAlternateBufferExpireTime = -1f;

    // ★ ФИКС (залипание hold-действий): последняя станция, которой мы передали
    // "кнопка зажата" (резка/мойка/уборка/перемешивание). Раньше, когда игрок
    // отходил от станции, selectedCounter обнулялся, и ветка передачи hold-состояния
    // просто переставала выполняться — SetCuttingState(false) НИКОГДА не вызывался,
    // и станция продолжала резать/мыть вечно. Теперь мы помним, кому последний раз
    // передали hold-состояние, и при смене/потере цели гасим СТАРУЮ станцию.
    private BaseCounter activeHoldCounter;

    // ★ Мягкая толпа: сглаженный множитель скорости в толпе и переиспользуемые
    // буферы — ноль аллокаций в кадре.
    private float smoothedCrowdMultiplier = 1f;
    // ★ Буферы 32: NonAlloc-запросы возвращают ОБЩЕЕ число найденных попаданий,
    // которое может ПРЕВЫШАТЬ длину буфера (лишние просто не записываются). В
    // обставленной кухне с гостями 16 переполнялось мгновенно, а «отброшенный
    // хвост» может содержать твёрдую стену — поэтому с запасом.
    private static readonly Collider[] crowdOverlapBuffer = new Collider[32];
    // ★ ФИКС CS1503: CapsuleCastNonAlloc требует буфер RaycastHit[], а не Collider[]
    // (OverlapSphereNonAlloc — Collider[], cast-функции — RaycastHit[]). Ошибка была
    // ещё в прошлой версии файла и не ловилась скобочным синтакс-чекером.
    private static readonly RaycastHit[] capsuleCastBuffer = new RaycastHit[32];

    // ★ Мягкий пуш от гостей: CustomerPhysicsAgent.FixedUpdate копит сюда запрос
    // смещения (метры за физ-шаг) через AddCrowdPush, ApplyCrowdPush в Update
    // плавно отрабатывает накопленное со сглаживанием и проверкой стен.
    private Vector3 pendingCrowdPush;
    private Vector3 crowdPushVelocity;

    // ★ Диагностика «игрок не двигается»: какой коллайдер блокирует движение.
    // Пишется в Console не чаще раза в секунду и только пока игрок жмёт движение.
    // После поиска причины выключить в инспекторе (Player → Debug Move Logs).
    [SerializeField] private bool debugMoveLogs = true;
    private float lastBlockLogTime;
    private Collider lastBlockingCollider;

    // ★ ФИКС «Движение заблокировано: 'Player'»: капсульные касты стартуют
    // внутри собственной капсулы игрока, и в такой конфигурации PhysX отдаёт
    // её же как попадание с нулевой дистанцией. Свой коллайдер (как и
    // коллайдеры дочерних визуальных объектов) блокировать движение не может
    // в принципе — фильтруем его в каждом касте через IsOwnCollider.
    private Collider[] selfColliders;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Debug.LogError("[Player] Обнаружено более одного экземпляра игрока!");
            Destroy(gameObject);
            return;
        }

        // ★ Кэш собственных коллайдеров для фильтра «свой — не препятствие».
        selfColliders = GetComponentsInChildren<Collider>();
    }

    private void Start()
    {
        // Подписываемся на события ввода
        if (GameInput.Instance != null)
        {
            GameInput.Instance.OnInteractAction += GameInput_OnInteractAction;
            GameInput.Instance.OnInteractAlternateAction += GameInput_OnInteractAlternateAction;
        }
    }

    // ★ ФИКС: при выгрузке сцены / уничтожении игрока станция не должна остаться
    // в состоянии "держат F" до конца своего существования.
    private void OnDisable()
    {
        ForceReleaseHold();
    }

    private void Update()
    {
        // Если в данный момент не идет активная игра (например, идет отсчет или подведение итогов),
        // полностью блокируем передвижение персонажа и выбор столов.
        if (GameLoopManager.Instance != null && !GameLoopManager.Instance.IsGamePlaying() && !GameLoopManager.Instance.IsPreparationActive())
        {
            isWalking = false;
            ForceReleaseHold(); // ★ ФИКС: день закончился/начался отсчёт — гасим удержание
            pendingCrowdPush = Vector3.zero; // и накопленные толчки толпы тоже гасим
            SetSelectedCounter(null);
            return;
        }

        // Черновик карт (гримуар открыт): полная заморозка игрока. ЭТО ГЛАВНЫЙ
        // БЛОКИРОВЩИК — Action Map "Player" остаётся включённой, потому что A/D и E
        // (действия Move и Interact) нужны самому черновику для навигации по гримуару.
        // Гейт глушит: ходьбу, луч выбора станции, автопритяжение (HandleCounterSnap),
        // буферизованные нажатия E/F и передачу зажатия F станциям.
        if (UpgradeManager.Instance != null && UpgradeManager.Instance.IsDraftActive)
        {
            isWalking = false;
            ForceReleaseHold(); // ★ ФИКС: гримуар открылся посреди резки — гасим удержание
            pendingCrowdPush = Vector3.zero; // и накопленные толчки толпы тоже гасим
            SetSelectedCounter(null);
            return;
        }

        HandleMovement();
        HandleInteractions();
        HandleCounterSnap();
        HandleBufferedInteractions();

        // ---------- Передача состояния удержания для длительных действий ----------
        // ★ ФИКС: вместо блока "if (selectedCounter != null) ..." — полный цикл
        // обслуживания hold-состояния, включающий СБРОС при потере/смене цели.
        UpdateHoldStates();
    }

    /// <summary>
    /// ★ ФИКС (залипание hold-действий): каждый кадр определяет, какая станция сейчас
    /// является целью удержания (selectedCounter, если она поддерживает hold-действия),
    /// передаёт ей актуальное состояние кнопки F и гасит удержание на ПРЕЖНЕЙ станции,
    /// если цель сменилась или исчезла (игрок отошёл, станция унесена/уничтожена).
    /// </summary>
    private void UpdateHoldStates()
    {
        bool isHeld = GameInput.Instance != null && GameInput.Instance.IsInteractAlternatePressed();

        BaseCounter holdTarget = GetHoldTarget(selectedCounter);

        // Цель сменилась (отошёл от станции, повернулся к другой, станция унесена
        // FurnitureMovingController'ом) — старая станция обязана получить false,
        // иначе её внутренний isCutting/isWashing/isBeingCleaned/isPlayerStirring
        // останется true, и прогресс продолжит расти без игрока.
        ReleaseStaleHold(holdTarget);

        if (holdTarget != null)
        {
            ApplyHoldState(holdTarget, isHeld);
            activeHoldCounter = holdTarget;
        }
        else
        {
            activeHoldCounter = null;
        }
    }

    /// <summary>Поддерживает ли эта станция hold-действия (зажатие F).</summary>
    private static BaseCounter GetHoldTarget(BaseCounter counter)
    {
        if (counter is CuttingCounter || counter is SinkCounter ||
            counter is Puddle || counter is StoveCounter)
        {
            return counter;
        }
        return null;
    }

    /// <summary>Передаёт станции актуальное hold-состояние, зная её тип.</summary>
    private static void ApplyHoldState(BaseCounter target, bool isHeld)
    {
        switch (target)
        {
            case CuttingCounter cuttingCounter:
                cuttingCounter.SetCuttingState(isHeld);
                break;
            case SinkCounter sinkCounter:
                sinkCounter.SetWashingState(isHeld);
                break;
            case Puddle puddle:
                puddle.SetCleaningState(isHeld);
                break;
            case StoveCounter stoveCounter:
                stoveCounter.SetStirringState(isHeld);
                break;
        }
    }

    /// <summary>
    /// Гасит удержание на прежней станции, если цель сменилась. Мёртвая ссылка
    /// (объект уничтожен — Unity == null) обрабатывается безопасно: вызов метода
    /// по уничтоженному объекту не выполняется.
    /// </summary>
    private void ReleaseStaleHold(BaseCounter currentTarget)
    {
        if (activeHoldCounter == null) return;          // ничего не держим (или уже уничтожено)
        if (activeHoldCounter == currentTarget) return; // та же станция — состояние актуально

        ApplyHoldState(activeHoldCounter, false);
        activeHoldCounter = null;
    }

    /// <summary>
    /// Принудительно гасит активное удержание — при смене фазы игры, открытии гримуара,
    /// выключении/уничтожении игрока. Вызывается из ранних return'ов Update() и OnDisable.
    /// </summary>
    private void ForceReleaseHold()
    {
        if (activeHoldCounter == null)
        {
            activeHoldCounter = null; // уничтоженная ссылка — просто забываем её
            return;
        }

        ApplyHoldState(activeHoldCounter, false);
        activeHoldCounter = null;
    }

    /// <summary>
    /// Множитель скорости от близости к лужам (Puddle.ActivePuddles) — 1, если рядом
    /// ни одной нет. Если игрок оказался в радиусе НЕСКОЛЬКИХ луж сразу, замедления не
    /// перемножаются — берётся только самое сильное, иначе можно было бы застрять почти
    /// неподвижно на пересечении пары луж, что ощущалось бы как баг, а не как риск.
    /// </summary>
    private float GetPuddleSpeedMultiplier()
    {
        float strongestMultiplier = 1f;

        foreach (Puddle puddle in Puddle.ActivePuddles)
        {
            if (puddle == null) continue;

            float distance = Vector3.Distance(transform.position, puddle.transform.position);
            if (distance <= puddle.GetSlowRadius())
            {
                strongestMultiplier = Mathf.Min(strongestMultiplier, puddle.GetSlowMultiplier());
            }
        }

        return strongestMultiplier;
    }

    /// <summary>
    /// Логика передвижения персонажа со скольжением вдоль поверхностей препятствий.
    /// </summary>
    private void HandleMovement()
    {
        // ★ Мягкий пуш от гостей: толпа мягко сдвигает героя (в том числе когда
        // он стоит на месте), затем идёт обычное движение по вводу.
        ApplyCrowdPush();

        // Получаем нормализованный вектор движения из системы ввода
        Vector2 inputVector = GameInput.Instance != null ? GameInput.Instance.GetMovementVectorNormalized() : Vector2.zero;
        Vector3 moveDir = new Vector3(inputVector.x, 0f, inputVector.y);

        // ★ Мягкая толпа: идущие гости (не сидящие) НЕ блокируют движение — герой
        // проходит сквозь толпу, а «лёгкое физическое сопротивление» — это плавное
        // замедление по глубине проникновения в капсулы гостей (см. ниже) плюс
        // мягкие толчки: гости пихают героя (AddCrowdPush / ApplyCrowdPush).
        float crowdMultiplier = GetCrowdSpeedMultiplier();
        float moveDistance = moveSpeed * GetPuddleSpeedMultiplier() * crowdMultiplier * Time.deltaTime;

        // ★ Скольжение по поверхностям (вместо осевого слайда):
        // 1) каст прямо по направлению движения — путь свободен, идём;
        // 2) упёрлись — проекция движения на плоскость препятствия по нормали
        //    ближайшего попадания: скольжение вдоль поверхности любой
        //    ориентации (в т.ч. скошенной мебели), а не только по осям X/Z;
        // 3) скольжение тоже упёрлось (угол, встречное препятствие) — повторная
        //    проекция вдоль второй поверхности: плавный обход угла.
        // Лоб в станцию: касательная составляющая обнуляется — просто стоим.
        // Инерции нет ни на одном уровне: отпустили кнопку — мгновенный стоп.
        Vector3 effectiveMoveDir = Vector3.zero;

        if (moveDir != Vector3.zero && moveDistance > 0.0001f)
        {
            Vector3 firstNormal;

            if (!TryGetBlockingNormal(moveDir, moveDistance, out firstNormal))
            {
                effectiveMoveDir = moveDir;
            }
            else
            {
                Vector3 slideDir = Vector3.ProjectOnPlane(moveDir, firstNormal);
                slideDir.y = 0f;

                // Порог: остаточная касательная < 2% шага — считаем, что идём
                // строго в поверхность, скользить нечего (лоб в стену = стоп).
                if (slideDir.sqrMagnitude > 0.0004f)
                {
                    slideDir.Normalize();

                    Vector3 slideNormal;

                    if (!TryGetBlockingNormal(slideDir, moveDistance, out slideNormal))
                    {
                        effectiveMoveDir = slideDir;
                    }
                    else
                    {
                        Vector3 edgeDir = Vector3.ProjectOnPlane(slideDir, slideNormal);
                        edgeDir.y = 0f;

                        if (edgeDir.sqrMagnitude > 0.0004f)
                        {
                            edgeDir.Normalize();

                            if (!TryGetBlockingNormal(edgeDir, moveDistance, out _))
                            {
                                effectiveMoveDir = edgeDir;
                            }
                        }
                    }
                }
            }
        }

        // ★ Диагностика «игрок не двигается»: игрок жмёт движение, но движение
        // полностью заблокировано — раз в секунду пишем, ЧТО именно блокирует.
        if (effectiveMoveDir == Vector3.zero && debugMoveLogs && inputVector != Vector2.zero &&
            Time.time - lastBlockLogTime > 1f &&
            lastBlockingCollider != null)
        {
            lastBlockLogTime = Time.time;

            Debug.Log($"[Player] Движение заблокировано: '{lastBlockingCollider.name}' " +
                      $"(layer '{LayerMask.LayerToName(lastBlockingCollider.gameObject.layer)}', " +
                      $"родитель: '{lastBlockingCollider.transform.parent?.name ?? "—"}')", lastBlockingCollider);
        }

        if (effectiveMoveDir != Vector3.zero)
        {
            transform.position += effectiveMoveDir * moveDistance;
        }

        // Анимация ходьбы честная: движемся — идём; упёрлись или отпустили
        // кнопку — стоим (раньше персонаж «бежал на месте» в стену).
        isWalking = effectiveMoveDir != Vector3.zero;

        // Плавный поворот персонажа в сторону движения
        if (moveDir != Vector3.zero)
        {
            transform.forward = Vector3.Slerp(transform.forward, moveDir, rotateSpeed * Time.deltaTime);
        }
    }

    /// <summary>
    /// ★ Мягкая толпа: капсульный каст «путь перекрыт?» — попадания ИДУЩИХ
    /// гостей исключаются, стены/мебель/двери/сидящие гости блокируют.
    /// Тонкая обёртка над TryGetBlockingNormal для проверок без скольжения
    /// (толчки толпы). Параметры каста те же, что были в прямых
    /// Physics.CapsuleCast вызовах.
    /// </summary>
    private bool CapsuleBlocked(Vector3 direction, float distance)
    {
        return TryGetBlockingNormal(direction, distance, out _);
    }

    /// <summary>
    /// ★ Капсульный каст «путь перекрыт?» с выдачей нормали БЛИЖАЙШЕГО твёрдого
    /// препятствия — основа скольжения по поверхностям. QueryTriggerInteraction.Ignore:
    /// триггеры (зоны доставки, подсветки, декор) не останавливают движение;
    /// маска ~0 = все слои, как было в прямом CapsuleCast.
    /// ★ ФИКС «невидимых стен» и фриза движения:
    /// 1) point1/point2 — это ЦЕНТРЫ ПОЛУСФЕР капсулы (конвенция Unity), а
    ///    не её нижняя/верхняя границы. Пивот игрока у ног, тело занимает
    ///    [position .. position + playerHeight], поэтому центры — на высоте
    ///    playerRadius и playerHeight - playerRadius. Раньше точки ставились
    ///    в feet и feet + playerHeight: капсула реально простиралась от
    ///    feet - playerRadius до feet + playerHeight + playerRadius и цепляла
    ///    нависающие объекты (полки, вытяжки, перекладины дверных проёмов).
    /// 2) CapsuleCastGroundSkin у нижней полусферы — иначе она ровно касается
    ///    пола (нулевое касание) и горизонтальный каст может «ловить» пол.
    /// 3) Кламп результата: NonAlloc возвращает ОБЩЕЕ число найденных
    ///    попаданий, которое может ПРЕВЫСИТЬ длину буфера. Без клампа цикл
    ///    падает IndexOutOfRangeException прямо из HandleMovement — каждый
    ///    кадр, и игрок полностью замирает («не двигается с места»).
    /// Свои коллайдеры и идущие гости препятствиями не считаются. Нормаль
    /// отдаётся в горизонтальной проекции: почти вертикальная нормаль
    /// (задели верх/низ препятствия краем капсулы) скольжения не задаёт.
    /// </summary>
    private bool TryGetBlockingNormal(Vector3 direction, float distance, out Vector3 blockNormal)
    {
        blockNormal = Vector3.zero;

        if (distance <= 0.0001f || direction.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        int hitCount = Physics.CapsuleCastNonAlloc(
            GetCapsulePoint1(),
            GetCapsulePoint2(),
            playerRadius,
            direction.normalized,
            capsuleCastBuffer,
            distance,
            ~0,
            QueryTriggerInteraction.Ignore);

        // ★ Кламп результата — детали в комментарии выше (пункт 3).
        int resultCount = Mathf.Min(hitCount, capsuleCastBuffer.Length);

        float nearestDistance = float.MaxValue;

        for (int i = 0; i < resultCount; i++)
        {
            Collider hitCollider = capsuleCastBuffer[i].collider;

            if (hitCollider == null)
            {
                continue;
            }

            // Свой коллайдер — не препятствие (каст стартует внутри собственной
            // капсулы, PhysX возвращает её как попадание: см. selfColliders).
            if (IsOwnCollider(hitCollider))
            {
                continue;
            }

            // Идущий гость — «мягкий»: сквозь него проходим (лёгкое замедление
            // даёт GetCrowdSpeedMultiplier), стеной он не считается.
            if (IsPassableGuestCollider(hitCollider))
            {
                continue;
            }

            // Решает БЛИЖАЙШЕЕ твёрдое попадание — именно оно определяет
            // поверхность, по которой будет скользить персонаж.
            if (capsuleCastBuffer[i].distance < nearestDistance)
            {
                nearestDistance = capsuleCastBuffer[i].distance;
                lastBlockingCollider = hitCollider; // для диагностики «не двигается»

                Vector3 horizontalNormal = capsuleCastBuffer[i].normal;
                horizontalNormal.y = 0f;

                blockNormal = horizontalNormal.sqrMagnitude > 0.0001f
                    ? horizontalNormal.normalized
                    : Vector3.zero;
            }
        }

        return nearestDistance < float.MaxValue;
    }

    /// <summary>
    /// True, если коллайдер принадлежит идущему (не сидящему) гостю с гибридным
    /// агентом. Гости без агента (legacy-настройка) остаются твёрдыми — старое поведение.
    /// </summary>
    private bool IsPassableGuestCollider(Collider collider)
    {
        CustomerAI guest = collider.GetComponentInParent<CustomerAI>();

        if (guest == null)
        {
            return false;
        }

        CustomerPhysicsAgent guestAgent = guest.GetComponent<CustomerPhysicsAgent>();

        return guestAgent != null && !guestAgent.IsSeated;
    }

    /// <summary>
    /// Зазор между нижней полусферой каст-капсулы и полом: при нулевом касании
    /// (капсула ровно стоит на полу) горизонтальный каст может выдать пол как
    /// препятствие. 2 см достаточно — вертикального движения у игрока нет.
    /// </summary>
    private const float CapsuleCastGroundSkin = 0.02f;

    /// <summary>
    /// ★ Центр НИЖНЕЙ полусферы капсулы игрока для всех кастов (конвенция
    /// Unity: point1/point2 — центры полусфер, а не нижняя/верхняя границы).
    /// Пивот у ног: тело занимает [position .. position + playerHeight].
    /// </summary>
    private Vector3 GetCapsulePoint1()
    {
        return transform.position +
               Vector3.up * (playerRadius + CapsuleCastGroundSkin);
    }

    /// <summary>
    /// ★ Центр ВЕРХНЕЙ полусферы капсулы игрока для всех кастов. Верх капсулы
    /// ровно на playerHeight над пивотом: нависающие объекты выше роста героя
    /// больше не ловятся как «невидимые стены».
    /// </summary>
    private Vector3 GetCapsulePoint2()
    {
        // Защита от кривых настроек: верхний центр не ниже нижнего.
        float point2Height = Mathf.Max(playerHeight - playerRadius,
                                       playerRadius + CapsuleCastGroundSkin);
        return transform.position + Vector3.up * point2Height;
    }

    /// <summary>
    /// True, если коллайдер принадлежит самому игроку или его дочерним объектам.
    /// </summary>
    private bool IsOwnCollider(Collider collider)
    {
        if (selfColliders == null)
        {
            selfColliders = GetComponentsInChildren<Collider>();
        }

        for (int i = 0; i < selfColliders.Length; i++)
        {
            if (selfColliders[i] == collider)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// ★ Лёгкое «физическое сопротивление» толпы: чем глубже игрок протиснулся
    /// в капсулы идущих гостей, тем медленнее он движется (плавно, без рывков —
    /// значение сглаживается MoveTowards). Сидящие гости не считаются — они
    /// твёрдые препятствия, «протиснуться» сквозь них нельзя.
    /// </summary>
    private float GetCrowdSpeedMultiplier()
    {
        if (crowdFullPenetration <= 0.001f)
        {
            return smoothedCrowdMultiplier;
        }

        // Сфера на высоте груди, чуть шире капсулы игрока: ловит гостей рядом.
        float scanRadius = Mathf.Max(playerRadius + 0.7f, 1.4f);
        Vector3 center = transform.position + Vector3.up * 1f;

        int hits = Physics.OverlapSphereNonAlloc(
            center,
            scanRadius,
            crowdOverlapBuffer,
            ~0,
            QueryTriggerInteraction.Ignore);

        float penetration = 0f;

        // ★ Кламп: OverlapSphereNonAlloc тоже может вернуть больше, чем влезло
        // в буфер (детали в комментарии в CapsuleBlocked) — без него цикл падает
        // IndexOutOfRangeException каждый кадр, и «игрок не двигается с места».
        int resultCount = Mathf.Min(hits, crowdOverlapBuffer.Length);

        for (int i = 0; i < resultCount; i++)
        {
            Collider col = crowdOverlapBuffer[i];

            if (col == null)
            {
                break;
            }

            CustomerAI guest = col.GetComponentInParent<CustomerAI>();

            if (guest == null)
            {
                continue;
            }

            CustomerPhysicsAgent guestAgent = guest.GetComponent<CustomerPhysicsAgent>();

            if (guestAgent == null || guestAgent.IsSeated)
            {
                continue; // legacy-гости и сидящие в «толпу» не считаются
            }

            Vector3 toGuest = guest.transform.position - transform.position;
            toGuest.y = 0f;

            float overlap = (playerRadius + EstimateGuestRadius(col)) - toGuest.magnitude;

            if (overlap > 0f)
            {
                // Потолок на одного гостя: один гость «в упор» не должен
                // замедлять героя как густая давка.
                penetration += Mathf.Min(overlap, 0.5f);
            }
        }

        float targetMultiplier = Mathf.Lerp(
            1f,
            crowdMinSpeedMultiplier,
            Mathf.Clamp01(penetration / crowdFullPenetration));

        // Сглаживание: замедление набирается и спадает постепенно — без рывков.
        smoothedCrowdMultiplier = Mathf.MoveTowards(
            smoothedCrowdMultiplier,
            targetMultiplier,
            crowdResponseSpeed * Time.deltaTime);

        return smoothedCrowdMultiplier;
    }

    /// <summary>
    /// Горизонтальный радиус коллайдера гостя (по AABB-габаритам коллайдера).
    /// </summary>
    private float EstimateGuestRadius(Collider guestCollider)
    {
        Bounds bounds = guestCollider.bounds;
        float radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
        return Mathf.Max(radius, 0.2f);
    }

    /// <summary>
    /// ★ Мягкий пуш от толпы: гость (CustomerPhysicsAgent) передаёт герою долю
    /// глубины проникновения, накопленную за физический шаг. direction —
    /// горизонталь от гостя к игроку, distance — сколько метров отодвинуть.
    /// Гости НЕ блокируют героя, но теперь ощущаются телом: толпа мягко
    /// сдвигает его (запросы суммируются, ApplyCrowdPush отрабатывает плавно).
    /// </summary>
    public void AddCrowdPush(Vector3 direction, float distance)
    {
        if (distance <= 0f)
        {
            return;
        }

        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        pendingCrowdPush += direction.normalized * distance;
    }

    /// <summary>
    /// ★ Отработка накопленных толчков от гостей. Скорость выталкивания
    /// сглаживается экспоненциально (без рывков и телепортов), ограничена
    /// crowdPushMaxSpeed и проверяется капсульным кастом: толпа способна
    /// сдвинуть героя, но НЕ способна втолкнуть его в стену, мебель или
    /// сидящего гостя. Вызывается из HandleMovement до движения по вводу.
    /// </summary>
    private void ApplyCrowdPush()
    {
        Vector3 requested = pendingCrowdPush;
        pendingCrowdPush = Vector3.zero;

        float dt = Time.deltaTime;

        if (dt <= 0.0001f)
        {
            return;
        }

        // Желаемая скорость выталкивания на этот кадр (метры за физ-шаг → м/с),
        // с потолком — даже давка не способна швырнуть героя.
        Vector3 desiredVelocity = Vector3.zero;

        if (requested.sqrMagnitude > 0.000001f)
        {
            desiredVelocity = requested / dt;
            desiredVelocity.y = 0f;

            if (desiredVelocity.sqrMagnitude > crowdPushMaxSpeed * crowdPushMaxSpeed)
            {
                desiredVelocity = desiredVelocity.normalized * crowdPushMaxSpeed;
            }
        }

        // Экспоненциальное сглаживание: толчок набирается и гаснет плавно,
        // поведение не зависит от частоты кадров. Когда контакта нет
        // (desiredVelocity = 0), скорость мягко угасает сама.
        float blend = 1f - Mathf.Exp(-crowdPushResponse * dt);
        crowdPushVelocity = Vector3.Lerp(crowdPushVelocity, desiredVelocity, blend);

        Vector3 pushMove = crowdPushVelocity * dt;

        if (pushMove.sqrMagnitude < 0.000004f)
        {
            return; // меньше 2 мм за кадр — героя не дёргаем
        }

        Vector3 pushDir = pushMove.normalized;
        float pushDistance = pushMove.magnitude;

        // Та же проверка, что у ходьбы: стены/мебель/сидящие гости твёрдые,
        // идущие гости — мягкие (сдвигаться в них можно).
        if (!CapsuleBlocked(pushDir, pushDistance))
        {
            transform.position += pushDir * pushDistance;
            return;
        }

        // Основное направление упёрлось в твёрдое — пробуем скользнуть по одной
        // оси, чтобы давление толпы не «пропадало» вплотную к препятствию.
        Vector3 pushDirX = new Vector3(pushDir.x, 0f, 0f);

        if (pushDirX.sqrMagnitude > 0.000001f &&
            !CapsuleBlocked(pushDirX.normalized, pushDistance))
        {
            transform.position += pushDirX.normalized * pushDistance;
            return;
        }

        Vector3 pushDirZ = new Vector3(0f, 0f, pushDir.z);

        if (pushDirZ.sqrMagnitude > 0.000001f &&
            !CapsuleBlocked(pushDirZ.normalized, pushDistance))
        {
            transform.position += pushDirZ.normalized * pushDistance;
        }
    }

    /// <summary>
    /// Высота луча взаимодействия над пивотом: луч ровно на уровне пола может
    /// численно «скользить» по нижней грани коллайдеров стен и не замечать их.
    /// 5 см над полом — всё ещё внутри габаритов и станций, и стен.
    /// </summary>
    private const float InteractionRayHeight = 0.05f;

    /// <summary>
    /// Определение стола перед игроком и подсвечивание его.
    /// ★ ФИКС «использование станций через стену»: раньше луч маскировался
    /// ТОЛЬКО слоем Counters, поэтому стены/двери/мебель для него были
    /// невидимы — станция подсвечивалась и использовалась сквозь стену.
    /// Теперь луч пускается по всем твёрдым слоям, и решает БЛИЖАЙШЕЕ
    /// препятствие: станция — выбираем её, что-то иное — станции перед
    /// игроком нет. Свои коллайдеры и идущие гости (мягкая толпа) препятствием
    /// не считаются.
    /// </summary>
    private void HandleInteractions()
    {
        Vector2 inputVector = GameInput.Instance != null ? GameInput.Instance.GetMovementVectorNormalized() : Vector2.zero;
        Vector3 moveDir = new Vector3(inputVector.x, 0f, inputVector.y);

        if (moveDir != Vector3.zero)
        {
            lastInteractDir = moveDir;
        }

        // ★ Луч по ВСЕМ слоям (триггеры игнорируем). Буфер общий с капсульными
        // кастами — запросы последовательные, конфликтов нет.
        Vector3 rayOrigin = transform.position + Vector3.up * InteractionRayHeight;

        int hitCount = Physics.RaycastNonAlloc(
            rayOrigin,
            lastInteractDir,
            capsuleCastBuffer,
            interactDistance,
            ~0,
            QueryTriggerInteraction.Ignore);

        // ★ Кламп: NonAlloc возвращает общее число попаданий, которое может
        // превысить длину буфера (см. CapsuleBlocked).
        int resultCount = Mathf.Min(hitCount, capsuleCastBuffer.Length);

        BaseCounter nearestCounter = null;
        float nearestCounterDistance = float.MaxValue;
        float nearestBlockerDistance = float.MaxValue;

        for (int i = 0; i < resultCount; i++)
        {
            Collider hitCollider = capsuleCastBuffer[i].collider;

            if (hitCollider == null || IsOwnCollider(hitCollider))
            {
                continue;
            }

            // Идущий гость «мягкий» и во взаимодействии: он не перекрывает
            // станцию (сидящие гости и твёрдые объекты — перекрывают).
            if (IsPassableGuestCollider(hitCollider))
            {
                continue;
            }

            float hitDistance = capsuleCastBuffer[i].distance;

            // Станцией считаем BaseCounter на слое из countersLayerMask;
            // компонент ищем и на родителе — у части станций коллайдер висит
            // на дочернем визуальном объекте.
            BaseCounter baseCounter = hitCollider.GetComponentInParent<BaseCounter>();

            if (baseCounter != null &&
                (countersLayerMask.value & (1 << hitCollider.gameObject.layer)) != 0)
            {
                if (hitDistance < nearestCounterDistance)
                {
                    nearestCounter = baseCounter;
                    nearestCounterDistance = hitDistance;
                }
            }
            else
            {
                // Стена, дверь, мебель — станция за этим объектом недостижима.
                if (hitDistance < nearestBlockerDistance)
                {
                    nearestBlockerDistance = hitDistance;
                }
            }
        }

        // Ближайшее твёрдое препятствие выигрывает: станция — выбираем,
        // посторонний объект — ничего (это и убирает взаимодействие «через
        // стену»; подсветка и буфер E/F подключены к selectedCounter, поэтому
        // исправляются автоматически).
        if (nearestCounter != null && nearestCounterDistance < nearestBlockerDistance)
        {
            if (nearestCounter != selectedCounter)
            {
                SetSelectedCounter(nearestCounter);
            }
        }
        else
        {
            SetSelectedCounter(null);
        }
    }

    /// <summary>
    /// Плавно притягивает игрока к удобной точке перед выбранной станцией и доворачивает
    /// его лицом к ней. Точка считается от РЕАЛЬНОЙ поверхности коллайдера станции
    /// (Collider.ClosestPoint), а не от фиксированного расстояния до её pivot — иначе
    /// для больших объектов вроде стола фиксированная дистанция могла оказаться внутри
    /// самого объекта, и притяжение утаскивало игрока прямо в него (или сквозь него).
    /// snapStandDistance теперь — это запас ПОВЕРХ фактического края объекта, а не
    /// расстояние от центра, так что одно и то же значение корректно работает и для
    /// маленького прилавка, и для стола.
    /// Пока игрок несёт мебель через FurnitureMovingController, притяжение отключается,
    /// иначе оно будет мешать свободно ходить со станцией в руках.
    /// </summary>
    private void HandleCounterSnap()
    {
        if (selectedCounter == null) return;

        if (FurnitureMovingController.Instance != null && FurnitureMovingController.Instance.IsCarryingFurniture)
        {
            return;
        }

        Vector3 counterCenter = selectedCounter.transform.position;
        Collider counterCollider = selectedCounter.GetComponent<Collider>();

        Vector3 idealPosition;

        if (counterCollider != null)
        {
            Vector3 surfacePoint = counterCollider.ClosestPoint(transform.position);
            Vector3 outward = transform.position - surfacePoint;
            outward.y = 0f;

            if (outward.sqrMagnitude < 0.0001f)
            {
                // Игрок уже на поверхности/внутри коллайдера — направление "наружу" через
                // ближайшую точку не определить, отталкиваемся от центра станции вместо этого.
                outward = transform.position - counterCenter;
                outward.y = 0f;
                if (outward.sqrMagnitude < 0.0001f) return;
            }

            outward.Normalize();
            idealPosition = surfacePoint + outward * (playerRadius + snapStandDistance);
        }
        else
        {
            // На счётчике почему-то нет коллайдера — старое поведение как запасной вариант.
            Vector3 towardPlayer = transform.position - counterCenter;
            towardPlayer.y = 0f;
            if (towardPlayer.sqrMagnitude < 0.0001f) return;
            towardPlayer.Normalize();
            idealPosition = counterCenter + towardPlayer * snapStandDistance;
        }

        idealPosition.y = transform.position.y; // высоту не трогаем

        // ★ Страховка (восстановлена из рабочей версии): притяжение не имеет права
        // продавливать геометрию — тот же капсульный каст, что и в движении.
        // Уперлись — останавливаемся вплотную, сквозь стену игрока не тащит никогда.
        Vector3 targetPosition = Vector3.MoveTowards(transform.position, idealPosition, snapMoveSpeed * Time.deltaTime);
        Vector3 snapStep = targetPosition - transform.position;

        if (snapStep.sqrMagnitude > 0.000001f)
        {
            Vector3 snapDir = snapStep.normalized;

            // ★ Тот же фикс геометрии, что в движении: центры полусфер, а не
            // границы капсулы. NonAlloc вместо одиночного каста: свой коллайдер
            // (каст стартует внутри собственной капсулы) отфильтровываем явно,
            // иначе притяжение считает стеной самого игрока и замирает на месте.
            int snapHitCount = Physics.CapsuleCastNonAlloc(
                GetCapsulePoint1(),
                GetCapsulePoint2(),
                playerRadius,
                snapDir,
                capsuleCastBuffer,
                snapStep.magnitude,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            int snapResultCount = Mathf.Min(snapHitCount, capsuleCastBuffer.Length);

            float allowedDistance = snapStep.magnitude;

            for (int i = 0; i < snapResultCount; i++)
            {
                Collider snapCollider = capsuleCastBuffer[i].collider;

                if (snapCollider == null || IsOwnCollider(snapCollider))
                {
                    continue;
                }

                // Притяжение не имеет права продавливать геометрию: до самого
                // ближнего твёрдого препятствия идём с тем же запасом 2 см.
                float hitDistance = Mathf.Max(0f, capsuleCastBuffer[i].distance - 0.02f);
                allowedDistance = Mathf.Min(allowedDistance, hitDistance);
            }

            targetPosition = transform.position + snapDir * allowedDistance;
        }

        transform.position = targetPosition;

        Vector3 lookDir = counterCenter - transform.position;
        lookDir.y = 0f;
        if (lookDir.sqrMagnitude > 0.0001f)
        {
            Quaternion idealRotation = Quaternion.LookRotation(lookDir.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, idealRotation, snapRotateSpeed * Time.deltaTime);
        }
    }

    /// <summary>
    /// Проверяет отложенные (буферизованные) нажатия E/F — если игрок нажал кнопку
    /// чуть раньше, чем selectedCounter успел обновиться (например, ещё доворачивается
    /// через автопритяжение), нажатие не теряется, а срабатывает, как только появится
    /// валидная станция — в пределах interactBufferDuration после нажатия.
    /// </summary>
    private void HandleBufferedInteractions()
    {
        if (interactBufferExpireTime > 0f)
        {
            if (selectedCounter != null)
            {
                selectedCounter.Interact(this);
                interactBufferExpireTime = -1f;
            }
            else if (Time.time > interactBufferExpireTime)
            {
                interactBufferExpireTime = -1f; // окно истекло, станция так и не появилась
            }
        }

        if (interactAlternateBufferExpireTime > 0f)
        {
            if (selectedCounter != null)
            {
                selectedCounter.InteractAlternate(this);
                interactAlternateBufferExpireTime = -1f;
            }
            else if (Time.time > interactAlternateBufferExpireTime)
            {
                interactAlternateBufferExpireTime = -1f;
            }
        }
    }

    /// <summary>
    /// Обычное взаимодействие (Клавиша E).
    /// </summary>
    private void GameInput_OnInteractAction(object sender, EventArgs e)
    {
        // Блокируем взаимодействие, если игра не активна и мы не в фазе подготовки
        if (GameLoopManager.Instance != null && !GameLoopManager.Instance.IsGamePlaying() && !GameLoopManager.Instance.IsPreparationActive())
            return;

        // Черновик карт: во время гримуара E занята подтверждением выбора карты —
        // это нажатие забирает UpgradeManager (подписан на то же событие), а здесь
        // взаимодействие со столами не срабатывает и в буфер не попадает.
        if (UpgradeManager.Instance != null && UpgradeManager.Instance.IsDraftActive)
            return;

        if (selectedCounter != null)
        {
            selectedCounter.Interact(this);
        }
        else
        {
            // Станция ещё не выбрана прямо сейчас — запоминаем нажатие на короткое время,
            // а не теряем его. HandleBufferedInteractions() досрочно выполнит его, как
            // только (и если) выбор станции довалидируется в пределах этого окна.
            interactBufferExpireTime = Time.time + interactBufferDuration;
        }
    }

    /// <summary>
    /// Альтернативное взаимодействие (Клавиша F или Пробел для одноразовых действий).
    /// </summary>
    private void GameInput_OnInteractAlternateAction(object sender, EventArgs e)
    {
        if (GameLoopManager.Instance != null && !GameLoopManager.Instance.IsGamePlaying() && !GameLoopManager.Instance.IsPreparationActive())
            return;

        // Черновик карт: F не работает, пока открыт гримуар (см. комментарий выше).
        if (UpgradeManager.Instance != null && UpgradeManager.Instance.IsDraftActive)
            return;

        if (selectedCounter != null)
        {
            // Вызываем альтернативное взаимодействие (например, для открытия контейнера)
            selectedCounter.InteractAlternate(this);

            // ВАЖНО: состояние удержания теперь передаётся в Update() каждый кадр,
            // поэтому здесь НЕ вызываем SetCuttingState или SetWashingState.
        }
        else
        {
            interactAlternateBufferExpireTime = Time.time + interactBufferDuration;
        }
    }

    private void SetSelectedCounter(BaseCounter baseCounter)
    {
        this.selectedCounter = baseCounter;
        OnSelectedCounterChanged?.Invoke(this, new OnSelectedCounterChangedEventArgs
        {
            selectedCounter = selectedCounter
        });
    }

    public bool IsWalking() => isWalking;

    public BaseCounter GetSelectedCounter() => selectedCounter;

    // Для UpgradeManager — карты улучшений увеличивают эти значения, не задают напрямую,
    // поэтому геттер+сеттер тут был бы менее удобен, чем явный "увеличить на".
    public void IncreaseMoveSpeed(float amount) => moveSpeed += amount;
    public void IncreaseInteractDistance(float amount) => interactDistance += amount;

    [Header("Модификаторы резки от карт улучшений")]
    [Tooltip("1 = обычная скорость резки. Нестабильная магия и подобные карты прибавляют сюда.")]
    [SerializeField] private float cuttingSpeedMultiplier = 1f;
    [Tooltip("Шанс (0..1) испортить ингредиент при завершении резки — по умолчанию 0, без риска.")]
    [SerializeField] private float cuttingRuinChance = 0f;

    public void IncreaseCuttingSpeedMultiplier(float amount) => cuttingSpeedMultiplier += amount;

    // Зажимаем в [0..1]: иначе при многократном взятии карты "Нестабильная магия"
    // шанс уходит >1, и Random.value < ruinChance срабатывает ВСЕГДА — карта начинает
    // безусловно портить ингредиент, что явно не задумывалось.
    public void IncreaseCuttingRuinChance(float amount) =>
        cuttingRuinChance = Mathf.Clamp01(cuttingRuinChance + amount);

    public float GetCuttingSpeedMultiplier() => cuttingSpeedMultiplier;
    public float GetCuttingRuinChance() => cuttingRuinChance;

    [Header("Модификатор цены рецептов от карт улучшений")]
    [Tooltip("1 = обычная цена. Применяется в момент выплаты в DeliveryManager, не мутирует сам RecipeSO.")]
    [SerializeField] private float recipeCostMultiplier = 1f;

    public void IncreaseRecipeCostMultiplier(float amount) => recipeCostMultiplier += amount;
    public float GetRecipeCostMultiplier() => recipeCostMultiplier;

    // === РЕАЛИЗАЦИЯ ИНТЕРФЕЙСА IKITCHENOBJECTPARENT (Для переноски предметов) ===

    public Transform GetKitchenObjectFollowTransform()
    {
        return kitchenObjectHoldPoint;
    }

    public void SetKitchenObject(KitchenObject kitchenObject)
    {
        this.kitchenObject = kitchenObject;
        if (kitchenObject != null)
        {
            OnPickedSomething?.Invoke(this, EventArgs.Empty);
        }
    }

    public KitchenObject GetKitchenObject()
    {
        return this.kitchenObject;
    }

    public void ClearKitchenObject()
    {
        this.kitchenObject = null;
    }

    public bool HasKitchenObject()
    {
        return kitchenObject != null;
    }
}
