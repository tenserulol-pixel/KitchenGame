using UnityEngine;

/// <summary>
/// Позволяет игроку поднимать и переставлять любые BaseCounter
/// во время фазы подготовки дня.
///
//// Управление:
/// T — поднять выбранный объект / подтвердить новое место.
/// Escape — отменить перенос и вернуть объект обратно.
/// </summary>
public class FurnitureMovingController : MonoBehaviour
{
    [Header("Управление")]
    [SerializeField] private KeyCode moveFurnitureKey = KeyCode.T;
    [SerializeField] private KeyCode cancelKey = KeyCode.Escape;

    [Header("Настройки переноса")]
    [SerializeField] private float placementDistance = 2f;

    private BaseCounter movingCounter;
    private Collider movingCounterCollider;
    private Vector3 originalWorldPosition;

    private bool IsMoving => movingCounter != null;

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