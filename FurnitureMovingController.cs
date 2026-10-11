using UnityEngine;

/// <summary>
/// Позволяет игроку поднимать и переставлять любые BaseCounter
/// во время фазы подготовки дня.
///
/// Управление:
/// T — поднять выбранный объект / подтвердить новое место.
/// Escape — отменить перенос и вернуть объект обратно.
///
/// Автокоррекция относительно стен (v2):
/// Призрак мебели НЕ заходит в стены — он остаётся на последнем корректном
/// месте, а подтверждение «в стену» блокируется. Работает в двух режимах:
///
/// 1) АВТО (wallMask = Nothing, по умолчанию): стены определяются сами,
///    настройка сцены НЕ нужна. Стеной считается любой коллайдер, который
///    НЕ является: мебелью (BaseCounter), стулом (Chair), игроком (Player),
///    клиентом (CustomerAI), динамическим физическим телом (дверное полотно)
///    или полом (плоский коллайдер под мебелью).
///
/// 2) Слои (wallMask != Nothing): стенами считаются ТОЛЬКО коллайдеры
///    выбранных слоёв. Ручной режим для тех, кто хочет жёстко задать,
///    что считать стеной (например, слой Wall).
///
/// Почему v1 молча не работала (призрак продолжал заходить в стены):
/// БАГ 1 — геометрия предмета снималась с collider.bounds ПОСЛЕ выключения
///         коллайдера. У выключенного коллайдера bounds нулевые, проверочный
///         бокс получался размером (0,0,0) и IsOverlappingWall всегда
///         возвращала «стены нет».
/// БАГ 2 — коллайдер искали только на корне (GetComponent<Collider>), а у
///         части префабов он висит на дочернем объекте — бокс снова нулевой.
/// БАГ 3 — работа требовала ручной настройки сцены (слой Wall + wallMask),
///         без неё проверка была выключена по дизайну.
/// Теперь геометрия снимается ДО выключения коллайдеров и читается напрямую
/// из BoxCollider.size/center (не зависит от enabled вообще), ищется по всем
/// дочерним объектам, а по умолчанию включён АВТО-режим.
///
/// Переключение стульев (убрать/вернуть) сюда больше не входит — оно висит
/// на обычной кнопке E через DiningTable.Interact(), см. этот файл.
///
/// Пока открыт модальный экран (гримуар карт, магазин), клавиши переноса
/// глушатся, а сам перенос замирает: гейт GameLoopManager.IsUiInputLocked.
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

    [Header("Стены (автокоррекция призрака)")]
    [Tooltip("Nothing (0) = АВТО-режим: стены определяются сами (любой коллайдер, " +
             "кроме мебели/стульев/игрока/клиентов/дверного полотна/пола). " +
             "Настройка сцены не требуется. Если выбрать слои — стенами " +
             "считаются ТОЛЬКО они (ручной режим).")]
    [SerializeField] private LayerMask wallMask = 0;
    [Tooltip("Допустимый зазор между мебелью и стеной в метрах. Проверочный бокс " +
             "сжимается на эту величину с каждой стороны, чтобы мебель можно было " +
             "ставить вплотную к стене — касание не считается пересечением.")]
    [SerializeField] private float wallClearance = 0.05f;

    private BaseCounter movingCounter;
    private Vector3 originalWorldPosition;

    // ★ v2: ВСЕ коллайдеры переносимого предмета, включая дочерние объекты.
    // В v1 брали только GetComponent<Collider>() с корня — если коллайдер префаба
    // висит на дочернем объекте, проверка стен молча отключалась.
    private Collider[] movingColliders = new Collider[0];

    // Геометрия проверочного бокса: смещение центра относительно pivot предмета
    // и полуразмеры. Снимается ОДИН раз в момент подъёма — ДО выключения
    // коллайдеров (у выключенного коллайдера bounds нулевые — баг v1), а в
    // приоритетном источнике (BoxCollider.size/center) от состояния enabled
    // вообще ничего не зависит.
    private Vector3 wallBoxOffset;
    private Vector3 wallBoxHalfExtents;
    private bool wallBoxValid;

    // Переиспользуемый буфер для OverlapBoxNonAlloc — ноль аллокаций в кадр.
    private static readonly Collider[] wallOverlapBuffer = new Collider[16];

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

        // Модальный экран (гримуар/магазин) открыт: клавиши переноса глушатся,
        // перенос замирает на месте — до закрытия экрана.
        if (GameLoopManager.Instance.IsUiInputLocked)
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

        // ★ v2: собираем коллайдеры со всего предмета, включая дочерние объекты.
        movingColliders = selectedCounter.GetComponentsInChildren<Collider>();

        // ★ v2: геометрия снимается ДО выключения коллайдеров. В v1 это
        // делалось после — bounds выключенного коллайдера нулевые, проверочный
        // бокс становился (0,0,0), и проверка стен всегда давала «стены нет».
        CaptureWallBox();

        foreach (Collider col in movingColliders)
        {
            col.enabled = false;
        }

        LogWallCheckDiagnostics();

        Debug.Log(
            $"[FurnitureMoving] '{movingCounter.name}' поднят. " +
            $"{moveFurnitureKey} — поставить, {cancelKey} — отменить.");
    }

    /// <summary>
    /// ★ v2: запоминает форму переносимого предмета — смещение центра его
    /// коллайдера относительно pivot и полуразмеры. Источники геометрии
    /// (по приоритету):
    /// 1) самый крупный BoxCollider предмета — size/center не зависят от enabled;
    /// 2) суммарные bounds всех его коллайдеров (они ещё включены — валидны);
    /// 3) bounds рендерера (у предмета совсем нет коллайдеров).
    /// Дальше этот бокс переставляется к призраку и проверяется физикой на стены.
    /// </summary>
    private void CaptureWallBox()
    {
        wallBoxOffset = Vector3.zero;
        wallBoxHalfExtents = Vector3.zero;
        wallBoxValid = false;

        if (movingCounter == null)
        {
            return;
        }

        // 1) Прямой источник: BoxCollider на корне или в детях, самый объёмный.
        BoxCollider[] boxes = movingCounter.GetComponentsInChildren<BoxCollider>();

        BoxCollider bestBox = null;
        float bestVolume = 0f;

        foreach (BoxCollider box in boxes)
        {
            Vector3 worldSize = Vector3.Scale(box.size, box.transform.lossyScale);
            float volume = worldSize.x * worldSize.y * worldSize.z;

            if (box.size != Vector3.zero && volume > bestVolume)
            {
                bestVolume = volume;
                bestBox = box;
            }
        }

        if (bestBox != null)
        {
            Vector3 worldCenter = bestBox.transform.TransformPoint(bestBox.center);
            Vector3 worldSize = Vector3.Scale(bestBox.size, bestBox.transform.lossyScale);

            wallBoxOffset = worldCenter - movingCounter.transform.position;
            wallBoxHalfExtents = worldSize * 0.5f;
            wallBoxValid = wallBoxHalfExtents.magnitude > 0.01f;

            return;
        }

        // 2) Fallback: суммарные bounds всех коллайдеров. Валидны, потому что
        //    CaptureWallBox вызывается ДО их выключения.
        if (movingColliders != null && movingColliders.Length > 0)
        {
            Bounds bounds = movingColliders[0].bounds;

            for (int i = 1; i < movingColliders.Length; i++)
            {
                bounds.Encapsulate(movingColliders[i].bounds);
            }

            wallBoxOffset = bounds.center - movingCounter.transform.position;
            wallBoxHalfExtents = bounds.extents;
            wallBoxValid = wallBoxHalfExtents.magnitude > 0.01f;

            return;
        }

        // 3) Fallback: рендереры (у предмета вообще нет коллайдеров).
        Renderer itemRenderer = movingCounter.GetComponentInChildren<Renderer>();

        if (itemRenderer != null)
        {
            Bounds bounds = itemRenderer.bounds;

            wallBoxOffset = bounds.center - movingCounter.transform.position;
            wallBoxHalfExtents = bounds.extents;
            wallBoxValid = wallBoxHalfExtents.magnitude > 0.01f;
        }
    }

    /// <summary>
    /// ★ v2: диагностика в Console в момент подъёма — сразу видно, каким
    /// режимом работает проверка стен и какого размера проверочный бокс.
    /// Если бокс нулевой — будет Warning с точной причиной.
    /// </summary>
    private void LogWallCheckDiagnostics()
    {
        if (!wallBoxValid)
        {
            Debug.LogWarning(
                $"[FurnitureMoving] У '{movingCounter.name}' не найдено геометрии " +
                "(ни BoxCollider, ни Collider, ни Renderer) — проверка стен для " +
                "этого предмета работать не будет.");

            return;
        }

        if (wallMask.value != 0)
        {
            Debug.Log(
                $"[FurnitureMoving] Проверка стен: режим «слои» (mask={wallMask.value}), " +
                $"бокс предмета {wallBoxHalfExtents * 2f} м.");
        }
        else
        {
            Debug.Log(
                "[FurnitureMoving] Проверка стен: АВТО-режим (стены определяются сами), " +
                $"бокс предмета {wallBoxHalfExtents * 2f} м.");
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

        if (!GridPositioningSystem.Instance.IsCellFreeFor(candidateCell, movingCounter))
        {
            return;
        }

        Vector3 snapped =
            GridPositioningSystem.Instance.GetWorldPosition(candidateCell);

        snapped.y = originalWorldPosition.y;

        // ★ Автокоррекция стены: в ячейку, пересекающую стену, призрак НЕ
        // заходит — он остаётся на текущем (уже корректном) месте. Со стороны
        // это выглядит как «мебель упёрлась в стену», позиция всегда валидна
        // и её можно сразу подтвердить.
        if (IsOverlappingWall(snapped))
        {
            return;
        }

        movingCounter.transform.position = snapped;
    }

    private void TryConfirmPlacement()
    {
        if (movingCounter == null)
        {
            return;
        }

        Vector3 candidatePos = movingCounter.transform.position;

        // ★ Страховка на подтверждении. Штатно призрак не заходит в стену,
        // но проверка защищает от любых будущих путей постановки (смена пивота,
        // ресайз коллайдера, правки UpdateGhostPosition).
        if (IsOverlappingWall(candidatePos))
        {
            Debug.Log("[FurnitureMoving] Здесь стена — мебель сюда поставить нельзя.");

            return;
        }

        if (GridPositioningSystem.Instance.TryPlaceCounter(movingCounter, candidatePos))
        {
            Debug.Log(
                $"[FurnitureMoving] '{movingCounter.name}' размещён на новом месте.");

            // Сначала FinishMoving, потом проверка стульев: FinishMoving
            // возвращает коллайдеры переносимой мебели — без них мебель невидима
            // для физики, и стулья соседних столов не поняли бы, что им тесно.
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
    /// ★ v2: пересекает ли проверочный бокс предмета в позиции testPosition
    /// стену. Триггеры игнорируются (зоны двери/луж — не стены), собственные
    /// выключенные коллайдеры предмета физика не видит — ложного
    /// самопересечения нет. wallClearance сжимает бокс: касание стены
    /// вплотную не блокирует постановку.
    ///
    /// wallMask != 0 — ручной режим: стенами считаются только выбранные слои.
    /// wallMask == 0 — АВТО-режим: опрашиваем все слои и отсеиваем всё,
    /// что НЕ стена (мебель, стулья, игрок, клиенты, дверное полотно, пол).
    /// </summary>
    private bool IsOverlappingWall(Vector3 testPosition)
    {
        if (!wallBoxValid)
        {
            return false;
        }

        Vector3 half = wallBoxHalfExtents;
        half.x = Mathf.Max(0.01f, half.x - wallClearance);
        half.y = Mathf.Max(0.01f, half.y - wallClearance);
        half.z = Mathf.Max(0.01f, half.z - wallClearance);

        Vector3 center = testPosition + wallBoxOffset;

        // ── Ручной режим: только выбранные слои ──
        if (wallMask.value != 0)
        {
            int layerHits = Physics.OverlapBoxNonAlloc(
                center,
                half,
                wallOverlapBuffer,
                Quaternion.identity, // мебель в проекте не вращается; при добавлении поворота — передать rotation предмета
                wallMask,
                QueryTriggerInteraction.Ignore);

            return layerHits > 0;
        }

        // ── АВТО-режим: стены определяются сами ──
        int hits = Physics.OverlapBoxNonAlloc(
            center,
            half,
            wallOverlapBuffer,
            Quaternion.identity,
            ~0, // все слои
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits; i++)
        {
            Collider hit = wallOverlapBuffer[i];

            if (hit == null)
            {
                break;
            }

            // Собственный предмет выключен, но страховка лишней не бывает.
            if (IsOwnCollider(hit))
            {
                continue;
            }

            // Динамические физические тела (дверное полотно на HingeJoint,
            // физические реквизиты) — не стены: они двигаются сами.
            Rigidbody hitBody = hit.attachedRigidbody;

            if (hitBody != null && !hitBody.isKinematic)
            {
                continue;
            }

            // Мебель (все счётчики, столы, печи) — не стена: за пересечение
            // с ней отвечает сетка GridPositioningSystem (занятые ячейки).
            if (hit.GetComponentInParent<BaseCounter>() != null)
            {
                continue;
            }

            // Стул — не стена.
            if (hit.GetComponentInParent<Chair>() != null)
            {
                continue;
            }

            // Игрок — не стена (иначе призрак замирал бы рядом с самим собой).
            if (hit.GetComponentInParent<Player>() != null)
            {
                continue;
            }

            // Клиент — не стена: пробегающий мимо покупатель не должен
            // замораживать перестановку мебели.
            if (hit.GetComponentInParent<CustomerAI>() != null)
            {
                continue;
            }

            // Пол: плоский и крупный коллайдер под мебелью — не стена.
            // Страховка на случай, если мебель чуть утоплена в пол.
            if (IsFloorLike(hit, half * 2f))
            {
                continue;
            }

            // Всё остальное (стены, дверная рама, колонны, статичный декор) —
            // считается стеной: призрак туда не идёт.
            return true;
        }

        return false;
    }

    /// <summary>
    /// ★ v2: принадлежит ли коллайдер переносимому предмету.
    /// </summary>
    private bool IsOwnCollider(Collider hit)
    {
        for (int i = 0; i < movingColliders.Length; i++)
        {
            if (movingColliders[i] == hit)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// ★ v2: похож ли коллайдер на пол — тонкий по высоте и крупнее
    /// проверочного бокса по обеим горизонтальным осям. Стены вертикальные
    /// (высокие), пол горизонтальный и тонкий — так их и различаем.
    /// </summary>
    private bool IsFloorLike(Collider hit, Vector3 boxSize)
    {
        Bounds hitBounds = hit.bounds;

        return hitBounds.size.y <= 0.5f &&
               hitBounds.size.x >= boxSize.x * 1.5f &&
               hitBounds.size.z >= boxSize.z * 1.5f;
    }

    /// <summary>
    /// Визуализация проверочного бокса в Scene view во время переноса —
    /// зелёный = место валидно, красный = пересекает стену.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!IsMoving || !wallBoxValid)
        {
            return;
        }

        Vector3 center = movingCounter.transform.position + wallBoxOffset;

        Gizmos.color = IsOverlappingWall(movingCounter.transform.position)
            ? Color.red
            : Color.green;

        Gizmos.DrawWireCube(center, wallBoxHalfExtents * 2f);
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
        // ★ v2: возвращаем ВСЕ коллайдеры предмета (включая дочерние).
        if (movingColliders != null)
        {
            foreach (Collider col in movingColliders)
            {
                if (col != null)
                {
                    col.enabled = true;
                }
            }
        }

        movingColliders = new Collider[0];
        movingCounter = null;
    }
}