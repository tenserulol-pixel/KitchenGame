using UnityEngine;

/// <summary>
/// Позволяет игроку поднимать и переставлять любые BaseCounter
/// во время фазы подготовки дня.
///
//// Управление:
/// T — поднять выбранный объект / подтвердить новое место.
/// Escape — отменить перенос и вернуть объект обратно.
/// Delete — убрать ближайший (свободный) стул у выбранного стола.
/// Insert — вернуть последний убранный стул у выбранного стола.
/// </summary>
public class FurnitureMovingController : MonoBehaviour
{
    // Синглтон по тому же образцу, что и Player/GameLoopManager/GridPositioningSystem —
    // нужен, чтобы Player.cs мог проверить, не несёт ли игрок сейчас мебель, и на время
    // отключить притяжение к другим станциям.
    public static FurnitureMovingController Instance { get; private set; }

    [Header("Управление")]
    [SerializeField] private KeyCode moveFurnitureKey = KeyCode.T;
    [SerializeField] private KeyCode cancelKey = KeyCode.Escape;
    [SerializeField] private KeyCode removeChairKey = KeyCode.Delete;
    [SerializeField] private KeyCode returnChairKey = KeyCode.Insert;

    [Header("Настройки переноса")]
    [SerializeField] private float placementDistance = 2f;

    private BaseCounter movingCounter;
    private Collider movingCounterCollider;
    private Vector3 originalWorldPosition;

    private bool IsMoving => movingCounter != null;

    // Публичный флаг для других систем (сейчас — для Player.HandleCounterSnap)
    public bool IsCarryingFurniture => IsMoving;

    private void Awake()
    {
        Instance = this;
    }

    private void Update()
    {
        if (GameLoopManager.Instance == null ||
            !GameLoopManager.Instance.IsPreparationActive())
        {
            if (IsMoving)
            {
                CancelMove();
            }

            return;
        }

        if (!IsMoving)
        {
            if (Input.GetKeyDown(moveFurnitureKey))
            {
                TryStartMoving();
            }
            else if (Input.GetKeyDown(removeChairKey))
            {
                TryRemoveNearestChair();
            }
            else if (Input.GetKeyDown(returnChairKey))
            {
                TryReturnChair();
            }
        }
        else
        {
            UpdateGhostPosition();

            if (Input.GetKeyDown(moveFurnitureKey))
            {
                TryConfirmPlacement();
            }
            else if (Input.GetKeyDown(cancelKey))
            {
                CancelMove();
            }
        }
    }

    private void TryStartMoving()
    {
        if (Player.Instance == null)
        {
            return;
        }

        if (Player.Instance.HasKitchenObject())
        {
            return;
        }

        BaseCounter selectedCounter = Player.Instance.GetSelectedCounter();

        if (selectedCounter == null)
        {
            return;
        }

        movingCounter = selectedCounter;
        originalWorldPosition = selectedCounter.transform.position;

        movingCounterCollider = selectedCounter.GetComponent<Collider>();

        if (movingCounterCollider != null)
        {
            movingCounterCollider.enabled = false;
        }

        Debug.Log(
            $"[FurnitureMoving] '{movingCounter.name}' поднят. " +
            $"{moveFurnitureKey} — поставить, {cancelKey} — отменить.");
    }

    private void TryRemoveNearestChair()
    {
        if (Player.Instance == null)
        {
            return;
        }

        // Стул убирается у того стола, на который сейчас смотрит игрок — не нужен
        // отдельный рейкаст по стульям, раз выбор стола уже решён Player.selectedCounter.
        if (Player.Instance.GetSelectedCounter() is DiningTable table)
        {
            table.RemoveNearestChair(Player.Instance.transform.position);
        }
    }

    private void TryReturnChair()
    {
        if (Player.Instance == null)
        {
            return;
        }

        if (Player.Instance.GetSelectedCounter() is DiningTable table)
        {
            table.ReturnStoredChair();
        }
    }

    private void UpdateGhostPosition()
    {
        if (Player.Instance == null ||
            GridPositioningSystem.Instance == null)
        {
            return;
        }

        Vector3 aheadPoint =
            Player.Instance.transform.position +
            Player.Instance.transform.forward * placementDistance;

        Vector2Int candidateCell =
            GridPositioningSystem.Instance.GetGridPosition(aheadPoint);

        if (GridPositioningSystem.Instance.IsCellFreeFor(candidateCell, movingCounter))
        {
            Vector3 snapped =
                GridPositioningSystem.Instance.GetWorldPosition(candidateCell);

            snapped.y = originalWorldPosition.y;

            movingCounter.transform.position = snapped;
        }
    }

    private void TryConfirmPlacement()
    {
        if (movingCounter == null)
        {
            return;
        }

        Vector3 candidatePos = movingCounter.transform.position;

        if (GridPositioningSystem.Instance.TryPlaceCounter(movingCounter, candidatePos))
        {
            Debug.Log(
                $"[FurnitureMoving] '{movingCounter.name}' размещён на новом месте.");

            FinishMoving();
        }
        else
        {
            Debug.Log(
                "[FurnitureMoving] Это место занято — выбери другую ячейку.");
        }
    }

    private void CancelMove()
    {
        if (movingCounter != null)
        {
            movingCounter.transform.position = originalWorldPosition;

            Debug.Log(
                $"[FurnitureMoving] Перенос отменён, '{movingCounter.name}' вернулся на место.");
        }

        FinishMoving();
    }

    private void FinishMoving()
    {
        if (movingCounterCollider != null)
        {
            movingCounterCollider.enabled = true;
        }

        movingCounter = null;
        movingCounterCollider = null;
    }
}