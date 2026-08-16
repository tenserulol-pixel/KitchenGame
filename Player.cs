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

    private void Update()
    {
        // Если в данный момент не идет активная игра (например, идет отсчет или подведение итогов),
        // полностью блокируем передвижение персонажа и выбор столов.
        if (GameLoopManager.Instance != null && !GameLoopManager.Instance.IsGamePlaying() && !GameLoopManager.Instance.IsPreparationActive())
        {
            isWalking = false;
            SetSelectedCounter(null);
            return;
        }

        HandleMovement();
        HandleInteractions();
        HandleCounterSnap();
        HandleBufferedInteractions();

        // ---------- Передача состояния удержания для длительных действий ----------
        if (selectedCounter != null)
        {
            bool isHeld = GameInput.Instance != null && GameInput.Instance.IsInteractAlternatePressed();

            // Для разделочного стола
            if (selectedCounter is CuttingCounter cuttingCounter)
            {
                cuttingCounter.SetCuttingState(isHeld);
            }
            // Для раковины
            else if (selectedCounter is SinkCounter sinkCounter)
            {
                sinkCounter.SetWashingState(isHeld);
            }
            // Для других столов, поддерживающих удержание, добавляйте аналогично
        }
    }

    /// <summary>
    /// Логика передвижения персонажа со скольжением вдоль препятствий.
    /// </summary>
    private void HandleMovement()
    {
        // Получаем нормализованный вектор движения из системы ввода
        Vector2 inputVector = GameInput.Instance != null ? GameInput.Instance.GetMovementVectorNormalized() : Vector2.zero;
        Vector3 moveDir = new Vector3(inputVector.x, 0f, inputVector.y);

        float moveDistance = moveSpeed * Time.deltaTime;

        // Проверяем, может ли игрок двигаться в заданном направлении (капсульный каст)
        bool canMove = !Physics.CapsuleCast(
            transform.position,
            transform.position + Vector3.up * playerHeight,
            playerRadius,
            moveDir,
            moveDistance
        );

        if (!canMove)
        {
            // Пытаемся двигаться только по оси X
            Vector3 moveDirX = new Vector3(moveDir.x, 0f, 0f).normalized;
            canMove = moveDir.x != 0 && !Physics.CapsuleCast(
                transform.position,
                transform.position + Vector3.up * playerHeight,
                playerRadius,
                moveDirX,
                moveDistance
            );

            if (canMove)
            {
                moveDir = moveDirX;
            }
            else
            {
                // Пытаемся двигаться только по оси Z
                Vector3 moveDirZ = new Vector3(0f, 0f, moveDir.z).normalized;
                canMove = moveDir.z != 0 && !Physics.CapsuleCast(
                    transform.position,
                    transform.position + Vector3.up * playerHeight,
                    playerRadius,
                    moveDirZ,
                    moveDistance
                );

                if (canMove)
                {
                    moveDir = moveDirZ;
                }
            }
        }

        if (canMove && moveDir != Vector3.zero)
        {
            transform.position += moveDir * moveDistance;
        }

        isWalking = moveDir != Vector3.zero;

        // Плавный поворот персонажа в сторону движения
        if (moveDir != Vector3.zero)
        {
            transform.forward = Vector3.Slerp(transform.forward, moveDir, rotateSpeed * Time.deltaTime);
        }
    }

    /// <summary>
    /// Определение стола перед игроком и подсвечивание его.
    /// </summary>
    private void HandleInteractions()
    {
        Vector2 inputVector = GameInput.Instance != null ? GameInput.Instance.GetMovementVectorNormalized() : Vector2.zero;
        Vector3 moveDir = new Vector3(inputVector.x, 0f, inputVector.y);

        if (moveDir != Vector3.zero)
        {
            lastInteractDir = moveDir;
        }

        // Пускаем луч (Raycast) вперед, чтобы найти интерактивный стол на слое Counters
        if (Physics.Raycast(transform.position, lastInteractDir, out RaycastHit raycastHit, interactDistance, countersLayerMask))
        {
            if (raycastHit.transform.TryGetComponent<BaseCounter>(out BaseCounter baseCounter))
            {
                if (baseCounter != selectedCounter)
                {
                    SetSelectedCounter(baseCounter);
                }
            }
            else
            {
                SetSelectedCounter(null);
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

        transform.position = Vector3.MoveTowards(transform.position, idealPosition, snapMoveSpeed * Time.deltaTime);

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