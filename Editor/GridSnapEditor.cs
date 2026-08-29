#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor-скрипт автоматического прилипания BaseCounter к сетке GridPositioningSystem.
///
/// Подписывается на SceneView.duringSceneGui через [InitializeOnLoad] статический конструктор,
/// поэтому не нужно вешать его на конкретный объект — он работает всегда, когда открыта Unity.
///
/// Логика:
/// - При EventType.MouseUp с button==0 (отпустили левую кнопку мыши в Scene view) проверяем,
///   является ли выделенный объект BaseCounter. Если да — прилипаем к ближайшей ячейке сетки.
/// - При перетаскивании через transform handle в Scene view это срабатывает автоматически
///   после отпускания мыши.
/// - При изменении через Inspector — НЕ срабатывает (там нет MouseUp в Scene view).
///   Для этого случая есть контекстное меню "Snap to Grid" в самом BaseCounter.
///
/// Плюс пункт в верхнем меню Tools → Snap All Counters to Grid — для разового выравнивания
/// всех объектов на сцене (например, после импорта готовой расстановки).
///
/// ВАЖНО: скрипт лежит в папке Editor/, и Unity автоматически исключит его из билда.
/// </summary>
[InitializeOnLoad]
public static class GridSnapEditor
{
    static GridSnapEditor()
    {
        // Подписываемся при компиляции скрипта — срабатывает и при открытии проекта.
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private static void OnSceneGUI(SceneView sceneView)
    {
        // Не работаем в play mode — там snap делает BaseCounter.Awake при загрузке сцены.
        if (Application.isPlaying) return;

        Event e = Event.current;

        // Реагируем только на отпускание ЛКМ. MouseUp стреляет после перетаскивания
        // объекта через стандартный transform handle в Scene view.
        if (e == null || e.type != EventType.MouseUp || e.button != 0) return;

        // Что выделено? Если несколько объектов — обрабатываем все.
        GameObject[] selected = Selection.gameObjects;
        if (selected == null || selected.Length == 0) return;

        bool snappedAny = false;

        foreach (GameObject go in selected)
        {
            if (go == null) continue;

            // GetComponentInParent: если выделен дочерний объект стола (например, Chair),
            // всё равно snapаем корневой BaseCounter.
            BaseCounter counter = go.GetComponentInParent<BaseCounter>();
            if (counter == null) continue;

            if (SnapCounter(counter, registerUndo: true))
            {
                snappedAny = true;
            }
        }

        if (snappedAny)
        {
            // Перерисовать Scene view, чтобы было видно новое положение.
            SceneView.RepaintAll();
        }
    }

    /// <summary>
    /// Прилипает к сетке один BaseCounter. Возвращает true, если позиция реально изменилась
    /// (то есть объект был не на сетке до вызова).
    ///
    /// registerUndo: если true — добавляет операцию в Undo-стек, чтобы можно было
    /// отменить через Ctrl+Z. При массовом snap всех объектов сразу удобнее одно Undo,
    /// а не N операций, поэтому параметр опциональный.
    /// </summary>
    public static bool SnapCounter(BaseCounter counter, bool registerUndo = true)
    {
        if (counter == null) return false;

        GridPositioningSystem grid = FindActiveGrid();
        if (grid == null) return false;

        Vector3 currentPos = counter.transform.position;
        Vector2Int cell = grid.GetGridPosition(currentPos);
        Vector3 snappedPos = grid.GetWorldPosition(cell);

        // Сохраняем высоту — сетка работает в плоскости XZ, не трогаем Y.
        snappedPos.y = currentPos.y;

        // Если уже на сетке — ничего не делаем (избегаем ложных Undo-записей).
        if (Vector3.SqrMagnitude(currentPos - snappedPos) < 0.0001f)
        {
            return false;
        }

        if (registerUndo)
        {
            Undo.RecordObject(counter.transform, $"Snap {counter.gameObject.name} to grid");
        }

        counter.transform.position = snappedPos;
        EditorUtility.SetDirty(counter);
        return true;
    }

    /// <summary>
    /// Прилипает все BaseCounter в активной сцене. Возвращает количество перемещённых.
    /// </summary>
    public static int SnapAllCountersInScene()
    {
        GridPositioningSystem grid = FindActiveGrid();
        if (grid == null) return 0;

        // FindObjectsByType быстрее FindObjectsOfType и доступен с Unity 2023.
        // Если у тебя старая версия Unity и метода нет — замени на FindObjectsOfType<BaseCounter>.
        BaseCounter[] counters = Object.FindObjectsByType<BaseCounter>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        int snappedCount = 0;
        foreach (BaseCounter counter in counters)
        {
            if (SnapCounter(counter, registerUndo: true))
            {
                snappedCount++;
            }
        }

        return snappedCount;
    }

    /// <summary>
    /// Находит активный GridPositioningSystem в сцене. Сначала пробует Instance (установлен
    /// через Awake при ExecuteAlways), затем — через FindObjectsByType как fallback.
    /// </summary>
    private static GridPositioningSystem FindActiveGrid()
    {
        if (GridPositioningSystem.Instance != null)
        {
            return GridPositioningSystem.Instance;
        }

        // Если Instance ещё не установлен (например, сцена только что открыта и Awake
        // ещё не отработал) — ищем вручную. FindFirstObjectByType быстрее FindObjectsByType.
#if UNITY_2023_1_OR_NEWER
        return Object.FindFirstObjectByType<GridPositioningSystem>();
#else
        var grids = Object.FindObjectsOfType<GridPositioningSystem>();
        return grids != null && grids.Length > 0 ? grids[0] : null;
#endif
    }

    // ===== Пункты меню =====

    [MenuItem("Tools/Snap Selected Counter to Grid")]
    private static void SnapSelectedCounterMenuItem()
    {
        if (Selection.activeGameObject == null)
        {
            Debug.LogWarning("[GridSnap] Ничего не выделено. Выберите BaseCounter в Scene view.");
            return;
        }

        BaseCounter counter = Selection.activeGameObject.GetComponentInParent<BaseCounter>();
        if (counter == null)
        {
            Debug.LogWarning($"[GridSnap] На '{Selection.activeGameObject.name}' нет компонента BaseCounter.");
            return;
        }

        bool snapped = SnapCounter(counter, registerUndo: true);
        if (snapped)
        {
            Debug.Log($"[GridSnap] '{counter.gameObject.name}' прилип к сетке.");
        }
        else
        {
            Debug.Log($"[GridSnap] '{counter.gameObject.name}' уже на сетке — без изменений.");
        }
    }

    [MenuItem("Tools/Snap All Counters to Grid")]
    private static void SnapAllCountersMenuItem()
    {
        int count = SnapAllCountersInScene();
        if (count > 0)
        {
            Debug.Log($"[GridSnap] Прилипнули {count} объектов к сетке.");
            SceneView.RepaintAll();
        }
        else
        {
            Debug.Log("[GridSnap] Все BaseCounter уже на сетке — без изменений.");
        }
    }

    // ===== Валидация пунктов меню (серое, если не применимо) =====

    [MenuItem("Tools/Snap Selected Counter to Grid", true)]
    private static bool ValidateSnapSelectedCounter()
    {
        if (Application.isPlaying) return false;
        if (Selection.activeGameObject == null) return false;
        return Selection.activeGameObject.GetComponentInParent<BaseCounter>() != null;
    }

    [MenuItem("Tools/Snap All Counters to Grid", true)]
    private static bool ValidateSnapAllCounters()
    {
        return !Application.isPlaying;
    }
}
#endif
