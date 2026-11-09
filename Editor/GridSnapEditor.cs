#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// GridSnapEditor v2.0 «Editor Mover» — перемещение мебели и стен ПРЯМО В РЕДАКТОРЕ Unity
/// (edit mode, Scene view), с тем же снапом, что и в игре:
///
/// - Обычная мебель (BaseCounter без WallPiece) прилипает к ЦЕНТРУ КЛЕТКИ сетки.
/// - Стены (BaseCounter с WallPiece, маркер из фичи «Стены по рёбрам сетки» v1.6)
///   прилипают к РЕБРАМ — границам МЕЖДУ клетками — и друг к другу:
///   ближайший узел сетки → 4 ребра вокруг → дистанция ДО СЕГМЕНТА (не узла),
///   магнит ×0.5 к ребру с занятым коллинеарным соседом (стена продолжает линию),
///   рёбра, занятые другими стенами, пропускаются. Поворот выравнивается вдоль
///   ребра (0/90) по длинной оси геометрии — пивот префаба может быть где угодно.
///
/// КАК ПОЛЬЗОВАТЬСЯ:
/// - Схвати объект мышью в Scene view и тащи — позиция снапится в реальном времени
///   (зелёная подсветка — свободно, красная — клетка/ребро занято ДРУГИМ объектом).
/// - Стрелки Move-инструмента тоже работают: после отпускания объект доехает до сетки.
/// - R во время переноса стены — цикл ориентации: авто → вдоль X → вдоль Z (как в игре).
/// - Esc во время переноса — отмена (объект вернётся на место до переноса).
/// - Меню Tools → Kitchen Mover: выключатель снапа, «выровнять выделенное», «выровнять всё».
///
/// ВАЖНО:
/// - В play mode инструмент СПИТ — там работает игровой перенос (FurnitureMovingController).
/// - Занятость в редакторе определяется ГЕОМЕТРИЧЕСКИ (по текущим позициям остальных
///   объектов), а не по реестрам GridPositioningSystem — реестры в edit mode пусты по
///   дизайну v1.5+. При старте игры BaseCounter.Awake зарегистрирует всё с текущих позиций.
/// - Скрипт лежит в папке Editor/ — в билд игры не попадает.
/// </summary>
[InitializeOnLoad]
public static class GridSnapEditor
{
    private const string EditorPrefsKey = "kg_grid_snap_editor_enabled";
    private const string ToggleMenuPath = "Tools/Kitchen Mover/Снап-перетаскивание в редакторе";
    private const float ClickEpsilonPx = 2f;   // меньше этого движения — это клик, а не перенос
    private const float GrabRayDistance = 5000f;

    // ── Состояние живого переноса ──
    private static BaseCounter dragCounter;
    private static bool dragIsWall;
    private static Plane dragPlane;
    private static Vector3 grabOffset;
    private static Vector3 preDragPosition;
    private static Quaternion preDragRotation;
    private static float dragDistancePx;
    private static int wallOrientationOverride; // 0 — авто, 1 — только AlongX, 2 — только AlongZ
    private static bool undoRecorded;
    private static bool dragChangedObject;

    // Геометрическая занятость на момент старта переноса (остальные объекты).
    private static HashSet<GridPositioningSystem.WallEdgeKey> dragOccupiedEdges;
    private static Dictionary<Vector2Int, string> dragOccupiedCells;

    // ── Превью для отрисовки (заполняется при переносе, читается при Repaint) ──
    private static bool hasPreview;
    private static bool previewIsWall;
    private static bool previewBlocked;
    private static Vector2Int previewCell;
    private static Vector3 previewCellCenter;
    private static float previewCellSize;
    private static Vector3 previewEdgeA;
    private static Vector3 previewEdgeB;
    private static Vector3 previewEdgeMid;
    private static float previewNodeSize;

    // ── Safety net: позиции выделенных счётчиков на MouseDown (для снапа после
    // перетаскивания стрелками Move-инструмента, когда мы не перехватили мышь) ──
    private static List<BaseCounter> passiveWatched = new List<BaseCounter>();
    private static List<Vector3> passiveWatchedPos = new List<Vector3>();
    private static List<Quaternion> passiveWatchedRot = new List<Quaternion>();

    private static int defaultControlId;

    static GridSnapEditor()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    // ============================================================
    //  ГЛАВНЫЙ КОЛБЭК SCENE VIEW
    // ============================================================

    private static void OnSceneGUI(SceneView sceneView)
    {
        // В play mode работает игровой перенос — здесь спим.
        if (Application.isPlaying || !ToolEnabled) return;

        Event e = Event.current;
        if (e == null) return;

        // Layout: заявляем «пустой» контрол по умолчанию. Благодаря этому клик по ТЕЛУ
        // объекта достаётся нам, а клик по стрелкам/плоскостям Move-инструмента — им
        // (у хэндлов свой ближайший контрол, AddDefaultControl имеет низший приоритет).
        if (e.type == EventType.Layout)
        {
            defaultControlId = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(defaultControlId);
            return;
        }

        switch (e.type)
        {
            case EventType.MouseDown:
                OnMouseDown(e);
                break;
            case EventType.MouseDrag:
                OnMouseDrag(e);
                break;
            case EventType.MouseUp:
                OnMouseUp(e);
                break;
            case EventType.KeyDown:
                OnKeyDown(e);
                break;
        }

        if (dragCounter != null && hasPreview)
        {
            DrawPreview();
        }
        else if (dragCounter == null)
        {
            DrawSelectionHint();
        }
    }

    // ============================================================
    //  ЖИВОЙ ПЕРЕНОС: MOUSE-DOWN / DRAG / UP / KEYS
    // ============================================================

    private static void OnMouseDown(Event e)
    {
        // Хвостовой страховочный случай: прошлый перенос не завершился (MouseUp ушёл
        // мимо окна) — тихо завершаем его, прежде чем начинать новый.
        if (dragCounter != null)
        {
            EndDrag();
        }

        if (e.button != 0 || e.alt || e.command || e.control) return;
        if (HandleUtility.nearestControl != defaultControlId) return; // клик по хэндлам — не наш

        RememberPassiveSelection();

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        RaycastHit hit;
        if (!Physics.Raycast(ray, out hit, GrabRayDistance)) return;

        BaseCounter counter = hit.collider != null ? hit.collider.GetComponentInParent<BaseCounter>() : null;
        if (counter == null) return;

        GridPositioningSystem grid = FindActiveGrid();
        if (grid == null) return;

        dragCounter = counter;
        dragIsWall = WallPiece.IsWall(counter);
        preDragPosition = counter.transform.position;
        preDragRotation = counter.transform.rotation;
        dragPlane = new Plane(Vector3.up, new Vector3(0f, hit.point.y, 0f));
        grabOffset = counter.transform.position - hit.point;
        dragDistancePx = 0f;
        undoRecorded = false;
        dragChangedObject = false;
        wallOrientationOverride = 0;
        hasPreview = false;

        BuildOccupancySnapshot(grid, counter);

        Selection.activeGameObject = counter.gameObject;
        Tools.hidden = true; // прячем стрелки Move-инструмента, пока тащим сами
        e.Use();
    }

    private static void OnMouseDrag(Event e)
    {
        if (dragCounter == null) return;
        if (e.button != 0)
        {
            e.Use();
            return;
        }

        dragDistancePx += Mathf.Abs(e.delta.x) + Mathf.Abs(e.delta.y);

        // Пока это ещё «клик», а не перенос — ничего не двигаем (и не пишем Undo).
        if (dragDistancePx <= ClickEpsilonPx)
        {
            e.Use();
            return;
        }

        if (!undoRecorded)
        {
            Undo.RecordObject(dragCounter.transform, "Перенос по сетке (Editor)");
            undoRecorded = true;
        }

        ApplySnappedPosition(e.mousePosition);
        e.Use();
    }

    private static void OnMouseUp(Event e)
    {
        if (dragCounter != null)
        {
            bool changed = dragChangedObject;
            string movedName = dragCounter != null ? dragCounter.gameObject.name : "<объект>";
            EndDrag();
            e.Use();

            if (changed)
            {
                Debug.Log($"[GridSnap] '{movedName}' размещён по сетке (мебель — центр клетки, стены — ребро).");
            }
            return;
        }

        // SAFETY NET: если объект тащили СТРЕЛКАМИ Move-инструмента (мы не перехватывали
        // мышь), на отпускании доезжаем до сетки — как в v1.0. Чистый клик без движения
        // ничего не снапит.
        SnapPassiveSelectionIfMoved();
    }

    private static void OnKeyDown(Event e)
    {
        if (dragCounter == null) return;

        if (e.keyCode == KeyCode.R && dragIsWall)
        {
            wallOrientationOverride = (wallOrientationOverride + 1) % 3;
            string mode = wallOrientationOverride == 0 ? "АВТО" : wallOrientationOverride == 1 ? "вдоль X" : "вдоль Z";
            Debug.Log($"[GridSnap] Ориентация стены: {mode}.");
            ApplySnappedPosition(e.mousePosition);
            e.Use();
        }
        else if (e.keyCode == KeyCode.Escape)
        {
            CancelDrag();
            e.Use();
        }
    }

    private static void ApplySnappedPosition(Vector2 guiPoint)
    {
        GridPositioningSystem grid = FindActiveGrid();
        if (grid == null || dragCounter == null) return;

        Ray ray = HandleUtility.GUIPointToWorldRay(guiPoint);
        float enter;
        if (!dragPlane.Raycast(ray, out enter)) return;

        Vector3 target = ray.GetPoint(enter) + grabOffset;

        bool blocked;
        if (dragIsWall)
        {
            GridPositioningSystem.WallEdgeKey edge;
            SnapWallGeometry(grid, dragCounter, target, wallOrientationOverride, dragOccupiedEdges, out edge, out blocked);
        }
        else
        {
            Vector2Int cell;
            SnapFurnitureGeometry(grid, dragCounter, target, dragOccupiedCells, out cell, out blocked);
        }

        previewBlocked = blocked;
        hasPreview = true;
        SceneView.RepaintAll();
    }

    private static void CancelDrag()
    {
        if (dragCounter == null) return;

        dragCounter.transform.position = preDragPosition;
        dragCounter.transform.rotation = preDragRotation;
        Debug.Log("[GridSnap] Перенос отменён (Esc) — объект возвращён на место.");
        EndDrag();
    }

    private static void EndDrag()
    {
        dragCounter = null;
        dragIsWall = false;
        dragOccupiedEdges = null;
        dragOccupiedCells = null;
        hasPreview = false;
        undoRecorded = false;
        dragChangedObject = false;
        Tools.hidden = false;
        passiveWatched.Clear();
        SceneView.RepaintAll();
    }

    private static void OnHierarchyChanged()
    {
        // Объект переноса уничтожили (undo, удаление) — тихо завершаем перенос.
        if (dragCounter == null && (Tools.hidden || passiveWatched.Count > 0))
        {
            Tools.hidden = false;
            passiveWatched.Clear();
            dragOccupiedEdges = null;
            dragOccupiedCells = null;
            hasPreview = false;
        }
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        // Страховка: никакой скрытый инструмент не должен пережить вход в play mode.
        if (state == PlayModeStateChange.EnteredPlayMode && Tools.hidden)
        {
            Tools.hidden = false;
        }
    }

    // ============================================================
    //  ГЕОМЕТРИЯ СНАПА (зеркало GridPositioningSystem v1.6, но БЕЗ реестров:
    //  занятость — по текущим позициям объектов сцены)
    // ============================================================

    private static void SnapFurnitureGeometry(
        GridPositioningSystem grid,
        BaseCounter counter,
        Vector3 target,
        Dictionary<Vector2Int, string> occupiedCells,
        out Vector2Int cell,
        out bool blocked)
    {
        float size = grid.GetCellSize();
        Vector3 origin = grid.GetGridOrigin();
        Vector2Int bounds = grid.GetGridBounds();

        // Зеркало GetGridPosition + зажим в границы сетки (дизайнеру не даём утащить мебель вникуда).
        int x = Mathf.Clamp(Mathf.FloorToInt((target.x - origin.x) / size), 0, bounds.x - 1);
        int z = Mathf.Clamp(Mathf.FloorToInt((target.z - origin.z) / size), 0, bounds.y - 1);
        cell = new Vector2Int(x, z);

        Vector3 snapped = grid.GetWorldPosition(cell);
        snapped.y = counter.transform.position.y;
        counter.transform.position = snapped;
        dragChangedObject = true;

        previewIsWall = false;
        previewCell = cell;
        previewCellCenter = snapped;
        previewCellSize = size;

        blocked = occupiedCells != null && occupiedCells.ContainsKey(cell);
    }

    private static bool SnapWallGeometry(
        GridPositioningSystem grid,
        BaseCounter wall,
        Vector3 target,
        int orientationOverride,
        HashSet<GridPositioningSystem.WallEdgeKey> occupiedEdges,
        out GridPositioningSystem.WallEdgeKey edge,
        out bool blocked)
    {
        edge = default;
        blocked = false;

        float size = grid.GetCellSize();
        Vector3 origin = grid.GetGridOrigin();
        Vector2Int bounds = grid.GetGridBounds();

        int nodeX = Mathf.Clamp(Mathf.RoundToInt((target.x - origin.x) / size), 0, bounds.x);
        int nodeZ = Mathf.Clamp(Mathf.RoundToInt((target.z - origin.z) / size), 0, bounds.y);

        GridPositioningSystem.WallEdgeKey? bestFree = null;
        float bestFreeDist = float.MaxValue;
        GridPositioningSystem.WallEdgeKey? bestAny = null;
        float bestAnyDist = float.MaxValue;

        for (int pass = 0; pass < 2; pass++)
        {
            bool alongX = pass == 0;

            if (orientationOverride == 1 && !alongX) continue;
            if (orientationOverride == 2 && alongX) continue;

            for (int side = -1; side <= 1; side += 2)
            {
                GridPositioningSystem.WallEdgeKey candidate = alongX
                    ? new GridPositioningSystem.WallEdgeKey(
                        nodeX + (side < 0 ? -1 : 0), nodeZ, GridPositioningSystem.WallEdgeAxis.AlongX)
                    : new GridPositioningSystem.WallEdgeKey(
                        nodeX, nodeZ + (side < 0 ? -1 : 0), GridPositioningSystem.WallEdgeAxis.AlongZ);

                if (!grid.IsEdgeInsideGrid(candidate)) continue;

                Vector3 start = grid.GetWallEdgeMidpoint(candidate);
                Vector3 end = start;

                if (alongX)
                {
                    start.x -= size * 0.5f;
                    end.x += size * 0.5f;
                }
                else
                {
                    start.z -= size * 0.5f;
                    end.z += size * 0.5f;
                }

                float dist = SqrDistancePointToSegmentXZ(target, start, end);

                if (dist < bestAnyDist)
                {
                    bestAnyDist = dist;
                    bestAny = candidate;
                }

                bool occupied = occupiedEdges != null && occupiedEdges.Contains(candidate);
                if (occupied) continue; // занятые чужими стенами рёбра — во вторую очередь

                // Магнит «к друг другу»: продолжение линии существующих стен.
                if (HasOccupiedCollinearNeighbor(candidate, occupiedEdges))
                {
                    dist *= 0.5f;
                }

                if (dist < bestFreeDist)
                {
                    bestFreeDist = dist;
                    bestFree = candidate;
                }
            }
        }

        if (!bestFree.HasValue && !bestAny.HasValue)
        {
            return false; // теоретически недостижимо (вокруг зажатого узла всегда есть валидные рёбра)
        }

        // Все ближайшие рёбра заняты — ставим на ближайшее вообще, но показываем красным.
        edge = bestFree.HasValue ? bestFree.Value : bestAny.Value;
        blocked = !bestFree.HasValue;

        PlaceWallOnEdgeGeometry(grid, wall, edge);
        dragChangedObject = true;

        // Геометрия превью.
        Vector3 a = grid.GetWallEdgeMidpoint(edge);
        Vector3 b = a;
        if (edge.axis == GridPositioningSystem.WallEdgeAxis.AlongX)
        {
            a.x -= size * 0.5f;
            b.x += size * 0.5f;
        }
        else
        {
            a.z -= size * 0.5f;
            b.z += size * 0.5f;
        }

        previewIsWall = true;
        previewEdgeA = a;
        previewEdgeB = b;
        previewEdgeMid = grid.GetWallEdgeMidpoint(edge);
        previewNodeSize = Mathf.Max(0.1f, size * 0.08f);

        return true;
    }

    /// <summary>Ставит геометрический центр стены на середину ребра, поворот — вдоль ребра.</summary>
    private static void PlaceWallOnEdgeGeometry(GridPositioningSystem grid, BaseCounter wall, GridPositioningSystem.WallEdgeKey edge)
    {
        bool edgeAlongX = edge.axis == GridPositioningSystem.WallEdgeAxis.AlongX;
        Vector3 midpoint = grid.GetWallEdgeMidpoint(edge);
        Quaternion alignedRotation = Quaternion.Euler(0f, ComputeEdgeAlignedYaw(wall, edgeAlongX), 0f);
        Transform t = wall.transform;

        Vector3 center;
        if (WallPiece.TryGetGeometryCenter(wall, out center))
        {
            // Локальный оффсет «пивот → центр геометрии» в ТЕКУЩЕЙ ориентации,
            // перенесённый в выровненную — на ребро садится именно геометрия.
            Vector3 localOffset = Quaternion.Inverse(t.rotation) * (center - t.position);
            Vector3 worldOffset = alignedRotation * localOffset;

            t.SetPositionAndRotation(
                new Vector3(midpoint.x - worldOffset.x, t.position.y, midpoint.z - worldOffset.z),
                alignedRotation);
        }
        else
        {
            t.SetPositionAndRotation(
                new Vector3(midpoint.x, t.position.y, midpoint.z),
                alignedRotation);
        }
    }

    /// <summary>
    /// Поворот (yaw), при котором длинная ось геометрии ложится вдоль ребра:
    /// AlongX → 0, AlongZ → 90. Зеркало GridPositioningSystem.GetEdgeAlignedYaw.
    /// </summary>
    private static float ComputeEdgeAlignedYaw(BaseCounter wall, bool edgeAlongX)
    {
        Bounds bounds;
        if (!WallPiece.TryGetGeometryBounds(wall, out bounds))
        {
            return 0f;
        }

        float currentYaw = NormalizeAngleTo360(wall.transform.rotation.eulerAngles.y);
        int quarter = Mathf.RoundToInt(currentYaw / 90f) % 2;
        bool longAxisIsWorldX = (quarter == 0)
            ? bounds.size.x >= bounds.size.z
            : bounds.size.z >= bounds.size.x;

        return (longAxisIsWorldX == edgeAlongX) ? 0f : 90f;
    }

    private static bool HasOccupiedCollinearNeighbor(GridPositioningSystem.WallEdgeKey edge, HashSet<GridPositioningSystem.WallEdgeKey> occupiedEdges)
    {
        if (occupiedEdges == null || occupiedEdges.Count == 0) return false;

        if (edge.axis == GridPositioningSystem.WallEdgeAxis.AlongX)
        {
            return occupiedEdges.Contains(new GridPositioningSystem.WallEdgeKey(edge.x - 1, edge.z, GridPositioningSystem.WallEdgeAxis.AlongX)) ||
                   occupiedEdges.Contains(new GridPositioningSystem.WallEdgeKey(edge.x + 1, edge.z, GridPositioningSystem.WallEdgeAxis.AlongX));
        }

        return occupiedEdges.Contains(new GridPositioningSystem.WallEdgeKey(edge.x, edge.z - 1, GridPositioningSystem.WallEdgeAxis.AlongZ)) ||
               occupiedEdges.Contains(new GridPositioningSystem.WallEdgeKey(edge.x, edge.z + 1, GridPositioningSystem.WallEdgeAxis.AlongZ));
    }

    private static float SqrDistancePointToSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
    {
        float abx = b.x - a.x;
        float abz = b.z - a.z;
        float apx = p.x - a.x;
        float apz = p.z - a.z;

        float abLenSqr = abx * abx + abz * abz;

        float t = abLenSqr > 0.0001f
            ? Mathf.Clamp01((apx * abx + apz * abz) / abLenSqr)
            : 0f;

        float cx = a.x + abx * t - p.x;
        float cz = a.z + abz * t - p.z;

        return cx * cx + cz * cz;
    }

    private static float NormalizeAngleTo360(float yaw)
    {
        yaw %= 360f;
        if (yaw < 0f) yaw += 360f;
        return yaw;
    }

    /// <summary>
    /// Снимок занятости ПО ТЕКУЩИМ ПОЗИЦИЯМ объектов (реестры сетки в edit mode пусты).
    /// Рёбра — со всех WallPiece сцены, кроме переносимого; клетки — со всей мебели.
    /// </summary>
    private static void BuildOccupancySnapshot(GridPositioningSystem grid, BaseCounter exceptCounter)
    {
        dragOccupiedEdges = new HashSet<GridPositioningSystem.WallEdgeKey>();

        WallPiece[] walls = Object.FindObjectsByType<WallPiece>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (WallPiece wp in walls)
        {
            if (wp == null) continue;

            BaseCounter bc = wp.GetComponent<BaseCounter>();
            if (bc == null || bc == exceptCounter) continue;

            dragOccupiedEdges.Add(grid.DeriveWallEdgeFromCurrentPosition(bc));
        }

        dragOccupiedCells = new Dictionary<Vector2Int, string>();

        BaseCounter[] counters = Object.FindObjectsByType<BaseCounter>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (BaseCounter bc in counters)
        {
            if (bc == null || bc == exceptCounter || WallPiece.IsWall(bc)) continue;

            Vector2Int cell = grid.GetGridPosition(bc.transform.position);
            dragOccupiedCells[cell] = bc.gameObject.name;
        }
    }

    // ============================================================
    //  SAFETY NET: снап после перетаскивания стрелками Move-инструмента
    // ============================================================

    private static void RememberPassiveSelection()
    {
        passiveWatched.Clear();
        passiveWatchedPos.Clear();
        passiveWatchedRot.Clear();

        GameObject[] selected = Selection.gameObjects;
        if (selected == null) return;

        foreach (GameObject go in selected)
        {
            if (go == null) continue;

            BaseCounter counter = go.GetComponentInParent<BaseCounter>();
            if (counter == null || passiveWatched.Contains(counter)) continue;

            passiveWatched.Add(counter);
            passiveWatchedPos.Add(counter.transform.position);
            passiveWatchedRot.Add(counter.transform.rotation);
        }
    }

    private static void SnapPassiveSelectionIfMoved()
    {
        for (int i = 0; i < passiveWatched.Count; i++)
        {
            BaseCounter counter = passiveWatched[i];
            if (counter == null) continue;

            bool moved = Vector3.SqrMagnitude(counter.transform.position - passiveWatchedPos[i]) > 0.0001f ||
                         Quaternion.Angle(counter.transform.rotation, passiveWatchedRot[i]) > 0.5f;
            if (!moved) continue;

            SnapCounterStatic(counter);
        }

        passiveWatched.Clear();
        passiveWatchedPos.Clear();
        passiveWatchedRot.Clear();
    }

    // ============================================================
    //  СТАТИЧЕСКИЙ СНАП (меню / safety net) — совместимый публичный API v1.0
    // ============================================================

    /// <summary>Прилипает один счётчик: мебель — в клетку, стену — на ребро. Возвращает true, если позиция изменилась.</summary>
    public static bool SnapCounter(BaseCounter counter, bool registerUndo = true)
    {
        return SnapCounterStatic(counter, registerUndo);
    }

    private static bool SnapCounterStatic(BaseCounter counter, bool registerUndo = true)
    {
        if (counter == null) return false;

        GridPositioningSystem grid = FindActiveGrid();
        if (grid == null) return false;

        Vector3 posBefore = counter.transform.position;
        Quaternion rotBefore = counter.transform.rotation;

        if (registerUndo)
        {
            Undo.RecordObject(counter.transform, "Снап по сетке (Editor)");
        }

        if (WallPiece.IsWall(counter))
        {
            Vector3 probe = posBefore;
            Vector3 center;
            if (WallPiece.TryGetGeometryCenter(counter, out center))
            {
                probe = center;
            }

            GridPositioningSystem.WallEdgeKey edge;
            bool blocked;

            HashSet<GridPositioningSystem.WallEdgeKey> occupied = CollectWallEdges(grid, counter);
            SnapWallGeometry(grid, counter, probe, 0, occupied, out edge, out blocked);

            if (blocked)
            {
                Debug.LogWarning($"[GridSnap] '{counter.gameObject.name}': ближайшее ребро {edge} занято другой стеной — стена поставлена туда всё равно. Разведи стены вручную.", counter);
            }
        }
        else
        {
            Vector2Int cell;
            bool blocked;
            SnapFurnitureGeometry(grid, counter, posBefore, CollectOccupiedCells(counter), out cell, out blocked);
        }

        EditorUtility.SetDirty(counter);
        return Vector3.SqrMagnitude(counter.transform.position - posBefore) > 0.0001f ||
               Quaternion.Angle(counter.transform.rotation, rotBefore) > 0.5f;
    }

    /// <summary>Прилипает всю мебель и все стены в активной сцене. Возвращает количество перемещённых.</summary>
    public static int SnapAllCountersInScene()
    {
        BaseCounter[] counters = Object.FindObjectsByType<BaseCounter>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        int snappedCount = 0;
        foreach (BaseCounter counter in counters)
        {
            if (SnapCounterStatic(counter, registerUndo: true))
            {
                snappedCount++;
            }
        }

        return snappedCount;
    }

    private static HashSet<GridPositioningSystem.WallEdgeKey> CollectWallEdges(GridPositioningSystem grid, BaseCounter exceptCounter)
    {
        HashSet<GridPositioningSystem.WallEdgeKey> edges = new HashSet<GridPositioningSystem.WallEdgeKey>();

        WallPiece[] walls = Object.FindObjectsByType<WallPiece>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (WallPiece wp in walls)
        {
            if (wp == null) continue;

            BaseCounter bc = wp.GetComponent<BaseCounter>();
            if (bc == null || bc == exceptCounter) continue;

            edges.Add(grid.DeriveWallEdgeFromCurrentPosition(bc));
        }

        return edges;
    }

    private static Dictionary<Vector2Int, string> CollectOccupiedCells(BaseCounter exceptCounter)
    {
        Dictionary<Vector2Int, string> cells = new Dictionary<Vector2Int, string>();

        GridPositioningSystem grid = FindActiveGrid();
        if (grid == null) return cells;

        BaseCounter[] counters = Object.FindObjectsByType<BaseCounter>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (BaseCounter bc in counters)
        {
            if (bc == null || bc == exceptCounter || WallPiece.IsWall(bc)) continue;

            cells[grid.GetGridPosition(bc.transform.position)] = bc.gameObject.name;
        }

        return cells;
    }

    // ============================================================
    //  СЛУЖЕБНОЕ
    // ============================================================

    private static GridPositioningSystem FindActiveGrid()
    {
        if (GridPositioningSystem.Instance != null)
        {
            return GridPositioningSystem.Instance;
        }

        GridPositioningSystem[] grids = Object.FindObjectsByType<GridPositioningSystem>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        return grids != null && grids.Length > 0 ? grids[0] : null;
    }

    private static bool ToolEnabled
    {
        get { return EditorPrefs.GetBool(EditorPrefsKey, true); }
    }

    // ============================================================
    //  ОТРИСОВКА
    // ============================================================

    private static void DrawPreview()
    {
        if (previewIsWall)
        {
            Handles.color = previewBlocked
                ? new Color(1f, 0.25f, 0.2f, 0.95f)
                : new Color(0.25f, 1f, 0.35f, 0.95f);

            Handles.DrawAAPolyLine(7f, previewEdgeA, previewEdgeB);
            Handles.DotHandleCap(0, previewEdgeA, Quaternion.identity, previewNodeSize, EventType.Repaint);
            Handles.DotHandleCap(0, previewEdgeB, Quaternion.identity, previewNodeSize, EventType.Repaint);
            Handles.DotHandleCap(0, previewEdgeMid, Quaternion.identity, previewNodeSize * 0.8f, EventType.Repaint);
        }
        else
        {
            Handles.color = previewBlocked
                ? new Color(1f, 0.25f, 0.2f, 0.95f)
                : new Color(0.25f, 1f, 0.35f, 0.95f);

            Handles.DrawWireCube(
                previewCellCenter + new Vector3(0f, 0.04f, 0f),
                new Vector3(previewCellSize, 0.06f, previewCellSize));
        }
    }

    /// <summary>Подсказка у выделенного объекта без переноса: где он «сидит» сейчас.</summary>
    private static void DrawSelectionHint()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null) return;

        BaseCounter counter = selected.GetComponentInParent<BaseCounter>();
        if (counter == null) return;

        GridPositioningSystem grid = FindActiveGrid();
        if (grid == null) return;

        Handles.color = new Color(0.3f, 0.9f, 1f, 0.65f);

        if (WallPiece.IsWall(counter))
        {
            GridPositioningSystem.WallEdgeKey edge = grid.DeriveWallEdgeFromCurrentPosition(counter);
            Vector3 a = grid.GetWallEdgeMidpoint(edge);
            Vector3 b = a;

            if (edge.axis == GridPositioningSystem.WallEdgeAxis.AlongX)
            {
                a.x -= grid.GetCellSize() * 0.5f;
                b.x += grid.GetCellSize() * 0.5f;
            }
            else
            {
                a.z -= grid.GetCellSize() * 0.5f;
                b.z += grid.GetCellSize() * 0.5f;
            }

            Handles.DrawAAPolyLine(4f, a, b);
        }
        else
        {
            Vector2Int cell = grid.GetGridPosition(counter.transform.position);
            Vector3 center = grid.GetWorldPosition(cell);
            Handles.DrawWireCube(
                center + new Vector3(0f, 0.04f, 0f),
                new Vector3(grid.GetCellSize(), 0.06f, grid.GetCellSize()));
        }
    }

    // ============================================================
    //  МЕНЮ
    // ============================================================

    [MenuItem(ToggleMenuPath, false, 100)]
    private static void ToggleTool()
    {
        bool next = !ToolEnabled;
        EditorPrefs.SetBool(EditorPrefsKey, next);
        Debug.Log(next
            ? "[GridSnap] Снап-перетаскивание в редакторе ВКЛЮЧЕНО: тащи мебель/стены мышью прямо в Scene view."
            : "[GridSnap] Снап-перетаскивание в редакторе ВЫКЛЮЧЕНО: Scene view работает как обычно.");
    }

    [MenuItem(ToggleMenuPath, true, 100)]
    private static bool ValidateToggleTool()
    {
        return true; // заодно показывает галочку текущего состояния
    }

    [MenuItem("Tools/Kitchen Mover/Выровнять ВЫДЕЛЕННОЕ по сетке", false, 101)]
    private static void SnapSelectedMenuItem()
    {
        GameObject[] selected = Selection.gameObjects;
        if (selected == null || selected.Length == 0)
        {
            Debug.LogWarning("[GridSnap] Ничего не выделено.");
            return;
        }

        int count = 0;
        HashSet<Object> processed = new HashSet<Object>();

        foreach (GameObject go in selected)
        {
            if (go == null) continue;

            BaseCounter counter = go.GetComponentInParent<BaseCounter>();
            if (counter == null || processed.Contains(counter)) continue;

            processed.Add(counter);
            if (SnapCounterStatic(counter, registerUndo: true))
            {
                count++;
            }
        }

        Debug.Log($"[GridSnap] Выровнено объектов: {count}.");
        SceneView.RepaintAll();
    }

    [MenuItem("Tools/Kitchen Mover/Выровнять ВЫДЕЛЕННОЕ по сетке", true)]
    private static bool ValidateSnapSelected()
    {
        return !Application.isPlaying && Selection.activeGameObject != null;
    }

    [MenuItem("Tools/Kitchen Mover/Выровнять ВСЮ сцену по сетке", false, 102)]
    private static void SnapAllMenuItem()
    {
        int count = SnapAllCountersInScene();
        Debug.Log($"[GridSnap] Выровнено объектов во всей сцене: {count}.");
        SceneView.RepaintAll();
    }

    [MenuItem("Tools/Kitchen Mover/Выровнять ВСЮ сцену по сетке", true)]
    private static bool ValidateSnapAll()
    {
        return !Application.isPlaying;
    }

    // ── Устаревшие пункты меню v1.0 — работают через новую логику ──

    [MenuItem("Tools/Snap Selected Counter to Grid", false, 110)]
    private static void LegacySnapSelected()
    {
        SnapSelectedMenuItem();
    }

    [MenuItem("Tools/Snap Selected Counter to Grid", true)]
    private static bool ValidateLegacySnapSelected()
    {
        return !Application.isPlaying && Selection.activeGameObject != null &&
               Selection.activeGameObject.GetComponentInParent<BaseCounter>() != null;
    }

    [MenuItem("Tools/Snap All Counters to Grid", false, 111)]
    private static void LegacySnapAll()
    {
        SnapAllMenuItem();
    }

    [MenuItem("Tools/Snap All Counters to Grid", true)]
    private static bool ValidateLegacySnapAll()
    {
        return !Application.isPlaying;
    }
}
#endif
