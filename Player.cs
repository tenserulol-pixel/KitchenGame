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

    [Header("Точка удержания предметов")]
    [SerializeField] private Transform kitchenObjectHoldPoint;

    private bool isWalking;
    private Vector3 lastInteractDir;
    private BaseCounter selectedCounter;
    private KitchenObject kitchenObject;

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

        // ---------- Передача состояния удержания для длительных действий ----------
        if (selectedCounter != null)
        {
            bool isHeld = GameInput.Instance != null && GameInput.Instance.IsInteractAlternatePressed();

            // Для разделочного стола
            if (selectedCounter is CuttingCounter cuttingCounter)
            {
                cuttingCounter.SetCuttingState(isHeld);
            }
            // Для раковины (если добавите метод SetWashingState)
            // else if (selectedCounter is SinkCounter sinkCounter)
            // {
            //     sinkCounter.SetWashingState(isHeld);
            // }
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