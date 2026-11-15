#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// GridSnapEditor v2.5 «Editor Mover» — перемещение мебели и стен ПРЯМО В РЕДАКТОРЕ Unity
/// (edit mode, Scene view), с тем же снапом, что и в игре:
///
/// - Обычная мебель (BaseCounter без WallPiece) прилипает к ЦЕНТРУ КЛЕТКИ сетки.
/// - Стены (объекты с маркером WallPiece) прилипают к РЕБРАМ — границам МЕЖДУ
///   клетками — и друг к другу: ближайший узел сетки → рёбра вокруг → дистанция
///   ДО СЕГМЕНТА (не узла), магнит ×0.5 к ребру с занятым коллинеарным соседом
///   (стена продолжает линию), рёбра, занятые другими стенами, пропускаются.
///   ★ BaseCounter на стене БОЛЬШЕ НЕ НУЖЕН: маркер WallPiece самодостаточен.
///   ★ Поворот стены при переносе НЕ МЕНЯЕТСЯ (см. v2.5 ниже).
///
/// ЧТО ИСПРАВИЛО v2.5 (перестановка не меняет поворот + стене не нужен BaseCounter):
/// Раньше ребро ставило стене позицию И ПОВОРОТ ЦЕЛИКОМ (PlaceWallOnEdgeGeometry
/// вызывал SetPositionAndRotation с каноничным yaw ребра, а клавиша R циклила
/// ориентацию) — стену «переворачивало» при переносе через поперечные рёбра.
/// Теперь: 1) ориентацию цели определяет САМА СТЕНА — кандидатами считаются только
/// рёбра, параллельные её текущему длинному плечу (WallRunsAlongX), поэтому снап
/// физически не может повернуть стену; 2) PlaceWallOnEdgeGeometry пишет ТОЛЬКО
/// position — rotation остаётся ровно таким, каким был. Повернуть стену можно
/// обычным инструментом Rotate (E) — снап сам подхватит новую ориентацию.
/// Плюс стены больше НЕ требуют BaseCounter: хватание в Scene view, снимок
/// занятости, «выровнять выделенное/всю сцену» и подсказка выделения работают
/// по маркеру WallPiece (стене он был нужен только старому игровому пайплайну —
/// выбор лучом, T-перенос, покупка и сейв расстановки — всё это для стен закрыто).
///
/// ПОЧЕМУ v2.1 ОТЛИЧАЕТСЯ МЕХАНИЗМОМ (важно):
/// v2.0 пыталась ПЕРЕХВАТИТЬ мышь через HandleUtility.AddDefaultControl и проверку
/// nearestControl == свой controlID. Это проигрывает встроенному Move-инструменту Unity:
/// он сам регистрирует контрол на «теле» объекта и обрабатывает перетаскивание телом
/// РАНЬШЕ кастомных duringSceneGui-колбэков. Итог у пользователя: объект «берётся,
/// но движется свободно, без прилипания» — двигал его не наш инструмент, а Unity.
///
/// v2.1 НЕ захватывает мышь ВООБЩЕ. Схема «пост-коррекции»:
/// 1. Встроенный инструмент двигает объект как обычно (тело, стрелки, Rect-инструмент —
///    любой способ; кастомные duringSceneGui-колбэки вызываются ПОСЛЕ встроенной
///    обработки событий, поэтому мы видим результат каждого кадра перетаскивания).
/// 2. В MouseDrag мы читаем, ЧТО сдвинул встроенный инструмент с начала переноса:
///    - сдвиг по X и Z (перетаскивание телом/плоскостью) → снапим обе горизонтальные оси;
///    - сдвиг только по X или только по Z (тянем стрелку) → снапим ТОЛЬКО эту ось,
///      ограничение оси стрелки сохраняется;
///    - сдвиг только по Y (стрелка вверх) → не мешаем поднимать/опускать объект.
///    Если встроенный перенос по какой-то причине не сработал, объект двигаем сами
///    от луча курсора — снап работает в обоих мирах.
/// 3. MouseUp: финальное положение уже на сетке; undo-группы схлопываются в один Ctrl+Z.
///
/// ЧТО ИСПРАВИЛО v2.2 (баг «не работает со стенами»):
/// В v2.1 стены снапились только когда встроенный инструмент сдвинул объект по ОБЕИМ
/// горизонтальным осям сразу (guard dragIsWall && !bothAxes && !freeMode). При камере
/// сбоку тело стены едет в плоскости экрана (X+Y, movedZ == false), при горизонтальном
/// протаскивании — только по X — и стены молча пропускали снап, а мебель в тех же
/// условиях работала. Теперь стены снапятся при ЛЮБОЙ горизонтальной составляющей
/// переноса: цель всё равно считается от луча курсора, а ось-ограничение стрелки
/// к стенам неприменимо (ребро ставит позицию И поворот целиком). Плюс защита от
/// «скользящего» луча: при виде сбоку пересечение луча с горизонтальной плоскостью
/// уходит на километры — в этом случае цель берётся из текущей позиции объекта,
/// иначе стену уносило к границе сетки.
///
/// ЧТО ИСПРАВИЛО v2.3 (регрессия «станции не двигаются по сетке»):
/// movedX/movedZ вычислялись по сдвигу ВСТРОЕННОГО инструмента. Но перетаскивание
/// ТЕЛОМ объекта Unity делает в плоскости ЭКРАНА: на наклонной камере это мировой
/// X+Y, Z почти не меняется → movedZ == false → ось-кламп прибивал Z станции к
/// стартовому значению, и станция замирала (при неизменной клетке X — стояла вовсе;
/// в v2.0 этого не было: мышь перехватывалась, и снап всегда шёл по обеим осям).
/// Отличаем настоящую СТРЕЛКУ от телесного переноса: стрелка сдвигает объект строго
/// по одной мировой оси БЕЗ вертикальной составляющей (наши поправки Y не трогают,
/// поэтому delta.y — чистый сигнал встроенного инструмента). Любой сдвиг по Y или
/// сразу по обеим горизонтальным осям = телесный перенос → снапим обе оси по лучу
/// курсора. Стены не тронуты: кламп им неприменим, guard пропускает любую
/// горизонтальную составляющую.
///
/// ЧТО ИСПРАВИЛО v2.4 (настоящая первопричина «не работает»):
/// Пост-коррекция v2.1+ целиком зависела от того, что встроенный Move-инструмент
/// двигает объект. Но телом он тащит только ЗАРАНЕЕ выделенный объект: мы выделяем
/// его в MouseDown — позже, чем инструмент решает, за что держится. Поэтому при
/// прямом хвате и перетаскивании встроенный инструмент НЕ ДВИГАЕТ ничего: delta == 0
/// каждый кадр. В v2.1 мебель всё равно двигала наша логика от луча курсора
/// (fallback «двигаем сами, если встроенный не сработал»). В v2.2 guard
/// «if (!useX && !useZ) return» стал выходить ДО fallback-а при нулевом сдвиге —
/// и мебель замерла (фикс v2.3 про кламп до этого места не доходил). Стены не
/// двигались по той же причине ещё с v1-го guard-а v2.1. Теперь: если к первому
/// кадру перетаскивания встроенный инструмент ничего не сдвинул — включается
/// ЛИПКИЙ самостоятельный перенос «за курсором» до конца перетаскивания
/// (обе оси, как в работавшем v2.0). Если встроенный тащит — работает
/// эвристика v2.3 (стрелка/тело).
///
/// КАК ПОЛЬЗОВАТЬСЯ:
/// - Схвати объект мышью в Scene view и тащи — позиция снапится в реальном времени
///   (зелёная подсветка — свободно, красная — клетка/ребро занято ДРУГИМ объектом).
/// - Стрелки Move-инструмента тоже снапятся вживую, с сохранением ограничения оси.
/// - Поворот стены при переносе НЕ меняется никогда: снап предлагает только рёбра,
///   параллельные текущей ориентации стены. Повернуть стену — инструментом Rotate
///   (E) до переноса; снап подхватит новую ориентацию сам.
/// - Esc во время переноса — отмена (после отпускания мыши объект вернётся на место).
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
    private const float AxisDeltaEpsilon = 0.001f; // порог «встроенный инструмент сдвинул ось»
    // Луч почти параллелен плоскости переноса (вид сбоку) — пересечение ненадёжно.
    private const float ParallelRayEpsilon = 0.08f;
    // Пересечение дальше этого расстояния — «луч в небо» у горизонта, цель бракуем.
    private const float MaxDragPlaneDistance = 1000f;

    // ── Состояние живого переноса ──
    // ★ v2.5: Component, а не BaseCounter — стены могут БЕЗ BaseCounter
    // (хватаем их по маркеру WallPiece). Мебель по-прежнему несёт BaseCounter.
    private static Component dragCounter;
    private static bool dragActive;
    private static bool dragIsWall;
    private static Plane dragPlane;
    private static Vector3 grabOffset;
    private static Vector3 preDragPosition;
    private static Quaternion preDragRotation;
    private static float dragDistancePx;
    private static bool dragChangedObject;
    private static bool dragCancelled;
    // v2.4: встроенный инструмент не тащит объект (не был выделен до клика) —
    // до конца перетаскивания двигаем его сами от луча курсора. Липкий, потому что
    // собственные поправки следующего кадра иначе выглядели бы как «стрелка».
    private static bool selfMoveMode;
    private static int undoGroup;
    private static Vector2 lastMousePos;

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

        // rawType, а не type: если встроенный инструмент «съел» событие через Use(),
        // мы всё равно видим, что это был за жест. Пост-коррекция не требует
        // захватывать мышь — поэтому мы не конкурируем с Move-инструментом вообще.
        switch (e.rawType)
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
        if (dragActive)
        {
            EndDrag();
        }

        if (e.button != 0 || e.alt || e.command || e.control) return;

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        RaycastHit hit;
        if (!Physics.Raycast(ray, out hit, GrabRayDistance)) return;

        // ★ v2.5: стены хватаем по маркеру WallPiece — BaseCounter на стене больше
        // не нужен (он был нужен только старому игровому пайплайну: выбор лучом,
        // T-перенос, покупка — всё это для стен запрещено). Мебель — по BaseCounter.
        Component grabbed = hit.collider != null ? hit.collider.GetComponentInParent<WallPiece>() : null;
        bool grabbedIsWall = grabbed != null;

        if (grabbed == null)
        {
            grabbed = hit.collider != null ? hit.collider.GetComponentInParent<BaseCounter>() : null;
        }

        if (grabbed == null) return;

        GridPositioningSystem grid = FindActiveGrid();
        if (grid == null) return;

        dragCounter = grabbed;
        dragActive = true;
        dragIsWall = grabbedIsWall;
        preDragPosition = dragCounter.transform.position;
        preDragRotation = dragCounter.transform.rotation;
        dragPlane = new Plane(Vector3.up, new Vector3(0f, hit.point.y, 0f));
        grabOffset = dragCounter.transform.position - hit.point;
        dragDistancePx = 0f;
        dragChangedObject = false;
        dragCancelled = false;
        hasPreview = false;
        selfMoveMode = false;
        lastMousePos = e.mousePosition;

        BuildOccupancySnapshot(grid, dragCounter);

        undoGroup = Undo.GetCurrentGroup();
        Undo.RecordObject(dragCounter.transform, "Перенос по сетке (Editor)");

        Selection.activeGameObject = dragCounter.gameObject;
        // ВАЖНО: событие НЕ используем, инструменты НЕ прячем — встроенный перенос
        // должен отработать. Наша коррекция случится после него в MouseDrag.
    }

    private static void OnMouseDrag(Event e)
    {
        if (!dragActive || dragCounter == null) return;
        if (e.button != 0) return; // другие кнопки — камера, не мешаем

        if (dragCancelled)
        {
            e.Use(); // перенос отменён — держим объект на месте до отпускания мыши
            return;
        }

        dragDistancePx += Mathf.Abs(e.delta.x) + Mathf.Abs(e.delta.y);
        lastMousePos = e.mousePosition;

        // Пока это ещё «клик», а не перенос — ничего не двигаем.
        if (dragDistancePx <= ClickEpsilonPx)
        {
            e.Use();
            return;
        }

        // Что сдвинул встроенный инструмент с начала переноса? Наши поправки Y не
        // меняют (высоту держим как до переноса), поэтому delta.y — чистый сигнал
        // встроенного инструмента, по нему отличаем стрелку от телесного переноса.
        Vector3 delta = dragCounter.transform.position - preDragPosition;
        bool movedX = Mathf.Abs(delta.x) > AxisDeltaEpsilon;
        bool movedZ = Mathf.Abs(delta.z) > AxisDeltaEpsilon;
        bool movedY = Mathf.Abs(delta.y) > AxisDeltaEpsilon;

        // Стрелка вверх (только Y) — не мешаем поднимать/опускать объект.
        if (!movedX && !movedZ && movedY)
        {
            hasPreview = false;
            e.Use();
            return;
        }

        // v2.4: встроенный инструмент не тащит — двигаем сами «за курсором».
        if (selfMoveMode)
        {
            ApplySnappedPosition(e.mousePosition, true, true);
            e.Use();
            return;
        }

        // Ничего не сдвинуто к первому кадру перетаскивания → встроенный инструмент
        // НЕ тащит объект (он тащит телом только заранее выделенный). Включаем
        // липкий самостоятельный перенос — обе оси, как в работавшем v2.0.
        if (!movedX && !movedZ && !movedY)
        {
            selfMoveMode = true;
            ApplySnappedPosition(e.mousePosition, true, true);
            e.Use();
            return;
        }

        // v2.3: настоящая СТРЕЛКА сдвигает объект строго по одной мировой оси БЕЗ
        // вертикальной составляющей. Перетаскивание ТЕЛОМ на наклонной камере едет
        // в плоскости ЭКРАНА (X+Y или Z+Y) — наличие movedY выдаёт телесный перенос.
        // Всё, что не чистая стрелка — телесный перенос: снапим обе горизонтальные
        // оси по лучу курсора.
        bool freeMove = movedY || (movedX && movedZ);
        bool useX = freeMove || movedX;
        bool useZ = freeMove || movedZ;

        ApplySnappedPosition(e.mousePosition, useX, useZ);
        e.Use();
    }

    private static void OnMouseUp(Event e)
    {
        if (!dragActive || dragCounter == null) return;

        bool changed = dragChangedObject && !dragCancelled;
        string movedName = dragCounter.gameObject.name;

        if (dragCancelled)
        {
            dragCounter.transform.position = preDragPosition;
            dragCounter.transform.rotation = preDragRotation;
        }

        EndDrag();

        // Схлопываем undo-группы встроенного переноса и нашей коррекции в один шаг.
        Undo.CollapseUndoOperations(undoGroup);
        e.Use();

        if (changed)
        {
            Debug.Log($"[GridSnap] '{movedName}' размещён по сетке (мебель — центр клетки, стены — ребро).");
        }
    }

    private static void OnKeyDown(Event e)
    {
        if (!dragActive || dragCounter == null) return;

        // ★ v2.5: клавиша R убрана — перестановка не крутит стену (и случайно
        // нажатая R больше не переворачивает её). Повернуть стену можно
        // инструментом Rotate (E) вне переноса.
        if (e.keyCode == KeyCode.Escape)
        {
            dragCancelled = true;
            dragCounter.transform.position = preDragPosition;
            dragCounter.transform.rotation = preDragRotation;
            hasPreview = false;
            Debug.Log("[GridSnap] Перенос отменён (Esc) — объект вернётся на место после отпускания мыши.");
            SceneView.RepaintAll();
            e.Use();
        }
    }

    private static void ApplySnappedPosition(Vector2 guiPoint, bool useX, bool useZ)
    {
        GridPositioningSystem grid = FindActiveGrid();
        if (grid == null || dragCounter == null) return;

        // v2.3: единственный пропуск — чистый подъём по Y. Стены снапим при ЛЮБОЙ
        // горизонтальной составляющей: в v2.1 требовался сдвиг обеих осей, и при камере
        // сбоку (тело стены едет по X+Y, movedZ == false) стены молча пропускали снап.
        if (!useX && !useZ) return;

        Ray ray = HandleUtility.GUIPointToWorldRay(guiPoint);
        Vector3 target;
        if (TryGetDragTarget(ray, out Vector3 planePoint))
        {
            target = planePoint + grabOffset;
        }
        else
        {
            // Камера почти параллельна плоскости переноса (вид сбоку) или луч уходит
            // к горизонту — grazing-луч даёт цель за сотни метров и стену уносит
            // к границе сетки. Снапим от текущей позиции объекта.
            target = dragCounter.transform.position;
        }

        Vector3 before = dragCounter.transform.position;

        bool placed;
        bool blocked;
        if (dragIsWall)
        {
            GridPositioningSystem.WallEdgeKey edge;
            placed = SnapWallGeometry(grid, dragCounter, target, dragOccupiedEdges, out edge, out blocked);
        }
        else
        {
            Vector2Int cell;
            placed = SnapFurnitureGeometry(grid, dragCounter, target, dragOccupiedCells, out cell, out blocked);
        }

        if (!placed) return;

        // Ось стрелки сохраняем (только мебель): тянуть X-стрелку — снапим только X.
        // Для стен НЕ применяем: посадка на ребро целиком управляет позицией XZ
        // (центр геометрии обязан сесть на середину ребра), частичное сохранение
        // координат сломало бы посадку. Поворот стена при этом НЕ меняет (v2.5).
        Vector3 after = dragCounter.transform.position;
        Vector3 final = after;
        if (!dragIsWall)
        {
            if (useX && !useZ) final = new Vector3(after.x, after.y, before.z);
            else if (!useX && useZ) final = new Vector3(before.x, after.y, after.z);
        }

        // Высоту держим как до переноса: менять её может только стрелка вверх,
        // а этот случай отфильтрован выше.
        final.y = preDragPosition.y;
        dragCounter.transform.position = final;

        dragChangedObject = true;
        previewBlocked = blocked;
        hasPreview = true;
        SceneView.RepaintAll();
    }

    /// <summary>
    /// Пересечение луча курсора с горизонтальной плоскостью переноса.
    /// false — луч почти параллелен плоскости (вид сбоку) или пересечение подозрительно
    /// далеко (луч уходит к горизонту): по такому лучу нельзя надёжно получить цель.
    /// </summary>
    private static bool TryGetDragTarget(Ray ray, out Vector3 point)
    {
        float enter;
        if (Mathf.Abs(ray.direction.y) < ParallelRayEpsilon ||
            !dragPlane.Raycast(ray, out enter) ||
            enter > MaxDragPlaneDistance)
        {
            point = default;
            return false;
        }

        point = ray.GetPoint(enter);
        return true;
    }

    private static void EndDrag()
    {
        dragCounter = null;
        dragActive = false;
        dragIsWall = false;
        dragOccupiedEdges = null;
        dragOccupiedCells = null;
        hasPreview = false;
        dragChangedObject = false;
        dragCancelled = false;
        selfMoveMode = false;
        SceneView.RepaintAll();
    }

    private static void OnHierarchyChanged()
    {
        // Объект переноса уничтожили (undo, удаление) — тихо завершаем перенос.
        // dragCounter == null ловит и «уничтожен» (fake-null), не только «не назначен».
        if (dragActive && dragCounter == null)
        {
            EndDrag();
        }
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        // Страховка: никакой активный перенос не должен пережить вход в play mode.
        if (state == PlayModeStateChange.EnteredPlayMode && dragActive)
        {
            EndDrag();
        }
    }

    // ============================================================
    //  ГЕОМЕТРИЯ СНАПА (зеркало GridPositioningSystem v1.6, но БЕЗ реестров:
    //  занятость — по текущим позициям объектов сцены)
    // ============================================================

    private static bool SnapFurnitureGeometry(
        GridPositioningSystem grid,
        Component counter,
        Vector3 target,
        Dictionary<Vector2Int, string> occupiedCells,
        out Vector2Int cell,
        out bool blocked)
    {
        cell = default;
        blocked = false;

        float size = grid.GetCellSize();
        Vector3 origin = grid.GetGridOrigin();
        Vector2Int bounds = grid.GetGridBounds();

        // Защита от кривых настроек сетки: иначе деление на ноль/негативные границы
        // телепортируют объект в угол сцены.
        if (size <= 0.0001f || bounds.x <= 0 || bounds.y <= 0) return false;

        // Зеркало GetGridPosition + зажим в границы сетки (дизайнеру не даём утащить мебель вникуда).
        int x = Mathf.Clamp(Mathf.FloorToInt((target.x - origin.x) / size), 0, bounds.x - 1);
        int z = Mathf.Clamp(Mathf.FloorToInt((target.z - origin.z) / size), 0, bounds.y - 1);
        cell = new Vector2Int(x, z);

        Vector3 snapped = grid.GetWorldPosition(cell);
        snapped.y = counter.transform.position.y;
        counter.transform.position = snapped;

        previewIsWall = false;
        previewCell = cell;
        previewCellCenter = snapped;
        previewCellSize = size;

        blocked = occupiedCells != null && occupiedCells.ContainsKey(cell);
        return true;
    }

    private static bool SnapWallGeometry(
        GridPositioningSystem grid,
        Component wall,
        Vector3 target,
        HashSet<GridPositioningSystem.WallEdgeKey> occupiedEdges,
        out GridPositioningSystem.WallEdgeKey edge,
        out bool blocked)
    {
        edge = default;
        blocked = false;

        float size = grid.GetCellSize();
        Vector3 origin = grid.GetGridOrigin();
        Vector2Int bounds = grid.GetGridBounds();
        if (size <= 0.0001f || bounds.x <= 0 || bounds.y <= 0) return false;

        // ★ v2.5: перестановка НЕ меняет поворот стены. Ориентацию цели определяет
        // САМА СТЕНА: кандидатами считаются только рёбра, параллельные её текущему
        // длинному плечу. Перпендикулярные рёбра не предлагаются вовсе — снап
        // физически не может повернуть стену (в v2.4 ребро ставило стене и позицию,
        // и каноничный поворот, а клавиша R циклила ориентацию — стена «переворачивалась»).
        bool wallRunsAlongX = WallRunsAlongX(wall);

        int nodeX = Mathf.Clamp(Mathf.RoundToInt((target.x - origin.x) / size), 0, bounds.x);
        int nodeZ = Mathf.Clamp(Mathf.RoundToInt((target.z - origin.z) / size), 0, bounds.y);

        GridPositioningSystem.WallEdgeKey? bestFree = null;
        float bestFreeDist = float.MaxValue;
        GridPositioningSystem.WallEdgeKey? bestAny = null;
        float bestAnyDist = float.MaxValue;

        for (int pass = 0; pass < 2; pass++)
        {
            bool alongX = pass == 0;

            // ★ v2.5: было фильтром по wallOrientationOverride (клавиша R) —
            // стало фильтром по собственной ориентации стены.
            if (wallRunsAlongX != alongX) continue;

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

    /// <summary>
    /// ★ v2.5: ставит геометрический центр стены на середину ребра БЕЗ смены поворота.
    /// Прошлые версии выравнивали yaw вдоль ребра (SetPositionAndRotation с каноничным
    /// поворотом) — стену «переворачивало» при переносе. Теперь пишется ТОЛЬКО
    /// position: rotation стены остаётся ровно таким, каким был до переноса.
    /// </summary>
    private static void PlaceWallOnEdgeGeometry(GridPositioningSystem grid, Component wall, GridPositioningSystem.WallEdgeKey edge)
    {
        Vector3 midpoint = grid.GetWallEdgeMidpoint(edge);
        Transform t = wall.transform;

        Vector3 center;
        if (WallPiece.TryGetGeometryCenter(wall, out center))
        {
            // Поворот не трогаем, поэтому оффсет «пивот → центр геометрии» в мировых
            // координатах постоянен — на середину ребра садится именно геометрия.
            Vector3 offset = center - t.position;

            t.position = new Vector3(midpoint.x - offset.x, t.position.y, midpoint.z - offset.z);
        }
        else
        {
            t.position = new Vector3(midpoint.x, t.position.y, midpoint.z);
        }
    }

    /// <summary>
    /// ★ v2.5: текущая ориентация стены — true, если длинная ось геометрии смотрит
    /// вдоль мирового X (ребро AlongX), false — вдоль Z (ребро AlongZ). Зеркало
    /// GridPositioningSystem: размеры берутся из геометрии (BoxCollider даёт их в
    /// локальных величинах), четверть текущего поворота показывает, куда плечо
    /// смотрит сейчас. САМ ПОВОРОТ СТЕНЫ ЭТА ФУНКЦИЯ НЕ МЕНЯЕТ.
    /// </summary>
    private static bool WallRunsAlongX(Component wall)
    {
        Bounds bounds;
        if (!WallPiece.TryGetGeometryBounds(wall, out bounds))
        {
            return true;
        }

        float currentYaw = NormalizeAngleTo360(wall.transform.rotation.eulerAngles.y);
        int quarter = Mathf.RoundToInt(currentYaw / 90f) % 2;

        return (quarter == 0)
            ? bounds.size.x >= bounds.size.z
            : bounds.size.z >= bounds.size.x;
    }

    /// <summary>
    /// ★ v2.5: локальное зеркало GridPositioningSystem.DeriveWallEdgeFromCurrentPosition,
    /// работающее с Component: стены в редакторе могут быть БЕЗ BaseCounter, а версия
    /// GPS принимает только BaseCounter. Логика идентична v1.6: геометрический центр
    /// (падает на середину ребра) + ориентация длинной оси по размерам геометрии.
    /// </summary>
    private static GridPositioningSystem.WallEdgeKey DeriveWallEdgeFromCurrentPosition(
        GridPositioningSystem grid, Component wall)
    {
        Vector3 probe = wall.transform.position;

        if (WallPiece.TryGetGeometryCenter(wall, out Vector3 center))
        {
            probe = center;
        }

        bool longAxisIsWorldX = WallRunsAlongX(wall);

        float size = grid.GetCellSize();
        Vector3 origin = grid.GetGridOrigin();
        Vector2Int bounds = grid.GetGridBounds();

        if (longAxisIsWorldX)
        {
            int lineZ = Mathf.Clamp(Mathf.RoundToInt((probe.z - origin.z) / size), 0, bounds.y);
            int cellX = Mathf.Clamp(Mathf.FloorToInt((probe.x - origin.x) / size), 0, bounds.x - 1);
            return new GridPositioningSystem.WallEdgeKey(cellX, lineZ, GridPositioningSystem.WallEdgeAxis.AlongX);
        }

        int lineX = Mathf.Clamp(Mathf.RoundToInt((probe.x - origin.x) / size), 0, bounds.x);
        int cellZ = Mathf.Clamp(Mathf.FloorToInt((probe.z - origin.z) / size), 0, bounds.y - 1);
        return new GridPositioningSystem.WallEdgeKey(lineX, cellZ, GridPositioningSystem.WallEdgeAxis.AlongZ);
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
    private static void BuildOccupancySnapshot(GridPositioningSystem grid, Component exceptObject)
    {
        dragOccupiedEdges = new HashSet<GridPositioningSystem.WallEdgeKey>();

        WallPiece[] walls = Object.FindObjectsByType<WallPiece>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (WallPiece wp in walls)
        {
            // ★ v2.5: рёбра собираем по маркеру WallPiece — BaseCounter на стене
            // не нужен (стены бывают и без него). Ребро выводим локальным зеркалом.
            if (wp == null || wp == exceptObject) continue;

            dragOccupiedEdges.Add(DeriveWallEdgeFromCurrentPosition(grid, wp));
        }

        dragOccupiedCells = new Dictionary<Vector2Int, string>();

        BaseCounter[] counters = Object.FindObjectsByType<BaseCounter>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (BaseCounter bc in counters)
        {
            if (bc == null || bc == exceptObject || WallPiece.IsWall(bc)) continue;

            Vector2Int cell = grid.GetGridPosition(bc.transform.position);
            dragOccupiedCells[cell] = bc.gameObject.name;
        }
    }

    // ============================================================
    //  СТАТИЧЕСКИЙ СНАП (меню) — совместимый публичный API v1.0
    // ============================================================

    /// <summary>Прилипает один счётчик: мебель — в клетку, стену — на ребро. Возвращает true, если позиция изменилась.</summary>
    public static bool SnapCounter(BaseCounter counter, bool registerUndo = true)
    {
        return SnapCounterStatic(counter, registerUndo);
    }

    private static bool SnapCounterStatic(Component counter, bool registerUndo = true)
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
            SnapWallGeometry(grid, counter, probe, occupied, out edge, out blocked);

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

    /// <summary>
    /// Прилипает всю мебель и все стены в активной сцене. Возвращает количество перемещённых.
    /// ★ v2.5: стены собираются по маркеру WallPiece (BaseCounter на стене не нужен),
    /// мебель — по BaseCounter; объект с обоими компонентами снапится один раз.
    /// </summary>
    public static int SnapAllCountersInScene()
    {
        HashSet<Object> processed = new HashSet<Object>();
        int snappedCount = 0;

        WallPiece[] walls = Object.FindObjectsByType<WallPiece>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (WallPiece wp in walls)
        {
            if (wp == null || !processed.Add(wp)) continue;

            if (SnapCounterStatic(wp, registerUndo: true))
            {
                snappedCount++;
            }
        }

        BaseCounter[] counters = Object.FindObjectsByType<BaseCounter>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (BaseCounter counter in counters)
        {
            if (counter == null || WallPiece.IsWall(counter) || !processed.Add(counter)) continue;

            if (SnapCounterStatic(counter, registerUndo: true))
            {
                snappedCount++;
            }
        }

        return snappedCount;
    }

    private static HashSet<GridPositioningSystem.WallEdgeKey> CollectWallEdges(GridPositioningSystem grid, Component exceptObject)
    {
        HashSet<GridPositioningSystem.WallEdgeKey> edges = new HashSet<GridPositioningSystem.WallEdgeKey>();

        WallPiece[] walls = Object.FindObjectsByType<WallPiece>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (WallPiece wp in walls)
        {
            // ★ v2.5: по маркеру WallPiece, без требования BaseCounter.
            if (wp == null || wp == exceptObject) continue;

            edges.Add(DeriveWallEdgeFromCurrentPosition(grid, wp));
        }

        return edges;
    }

    private static Dictionary<Vector2Int, string> CollectOccupiedCells(Component exceptCounter)
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
        GridPositioningSystem inst = GridPositioningSystem.Instance;
        if (inst != null && inst.isActiveAndEnabled) return inst;

        // Отключенная/умирающая сетка не годится для снапа — ищем живую в сцене.
        GridPositioningSystem[] grids = Object.FindObjectsByType<GridPositioningSystem>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
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

        // ★ v2.5: стены — по маркеру WallPiece (BaseCounter на стене не нужен).
        Component counter = selected.GetComponentInParent<WallPiece>();

        if (counter == null)
        {
            counter = selected.GetComponentInParent<BaseCounter>();
        }

        if (counter == null) return;

        GridPositioningSystem grid = FindActiveGrid();
        if (grid == null) return;

        Handles.color = new Color(0.3f, 0.9f, 1f, 0.65f);

        if (WallPiece.IsWall(counter))
        {
            GridPositioningSystem.WallEdgeKey edge = DeriveWallEdgeFromCurrentPosition(grid, counter);
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

            // ★ v2.5: стены — по маркеру WallPiece (BaseCounter на стене не нужен).
            Component counter = go.GetComponentInParent<WallPiece>();

            if (counter == null)
            {
                counter = go.GetComponentInParent<BaseCounter>();
            }

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
               (Selection.activeGameObject.GetComponentInParent<BaseCounter>() != null ||
                Selection.activeGameObject.GetComponentInParent<WallPiece>() != null);
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
