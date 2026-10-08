using UnityEngine;

/// <summary>
/// Позволяет игроку поднимать и переставлять любые BaseCounter
/// во время фазы подготовки дня.
///
//// Управление:
/// T — поднять выбранный объект / подтвердить новое место.
/// Escape — отменить перенос и вернуть объект обратно.
///
/// Переключение стульев (убрать/вернуть) сюда больше не входит — оно теперь
/// висит на обычной кнопке E через DiningTable.Interact(), см. этот файл.
///
/// После ЛЮБОЙ постановки мебели ВСЕ столы сцены перепроверяют своих стульев
/// (RecheckAllTableChairs): поставленная мебель могла перекрыть чужой стул.
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
                // Черновик карт (или другой UI с блокировкой ввода) открыт — клавиши переноса глушим.
        // Если мебель уже в руках — перенос просто замирает: игрок тоже замирает.
        if (GameLoopManager.Instance.IsUiInputLocked())
        {
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

            // ★ ИЗМЕНЕНО: сначала FinishMoving, потом проверка стульев.
            // FinishMoving возвращает коллайдер переносимой мебели — без него мебель
            // невидима для физики, и стулья соседних столов не поняли бы, что им тесно.
            FinishMoving();

            // Проверяем стулья ВСЕХ столов, а не только переставленного:
            // новая мебель (стол, печь, прилавок) могла перекрыть чужие стулья.
            // Сами столы решают, убирать ли стул, через DiningTable.RemoveChairsWithoutRoom.
            RecheckAllTableChairs();
        }
        else
        {
            Debug.Log(
                "[FurnitureMoving] Это место занято — выбери другую ячейку.");
        }
    }

    /// <summary>
    /// Повторная проверка места для стульев всех столов сцены. Столов немного,
    /// проверка дешёвая (OverlapSphere + до 4 лучей на стул), а вызывается только
    /// в момент постановки мебели — поэтому обходим все столы без оптимизаций.
    /// </summary>
    private void RecheckAllTableChairs()
    {
        DiningTable[] allTables = FindObjectsByType<DiningTable>(FindObjectsSortMode.None);

        foreach (DiningTable table in allTables)
        {
            if (table == null) continue;

            table.RemoveChairsWithoutRoom();
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