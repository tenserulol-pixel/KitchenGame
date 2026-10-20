using UnityEngine;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

// ExecuteAlways: Awake/OnEnable/OnDisable вызываются и в edit mode, и в play mode.
// Это позволяет Editor-скриптам дёргать Instance и методы GetGridPosition/GetWorldPosition
// прямо из редактора (например, для снапа стола к сетке при перетаскивании мышью).
// Словарь occupiedCells в редакторе НЕ заполняется — это нужно только для play mode,
// чтобы не было ложных конфликтов между объектами в редакторе и при запуске игры.
[ExecuteAlways]
public class GridPositioningSystem : MonoBehaviour
{
    // Одиночка (Singleton) для легкого доступа из других скриптов (например, из менеджера строительства)
    public static GridPositioningSystem Instance { get; private set; }

    /// <summary>
    /// ★ v1.3: true у сетки, созданной аварийным автокрафтом в ResolveInstance
    /// (когда в сцене не оказалось ни одной настоящей сетки).
    /// </summary>
    public bool WasAutoCreated { get; private set; }

    /// <summary>
    /// ★ v1.3: аварийное автосоздание запасной сетки, если в сцене её нет совсем.
    /// true по умолчанию — игра никогда не остаётся без сетки. Поставь false,
    /// если хочешь старое поведение: только warning и отключённая постановка в клетку.
    /// </summary>
    public static bool EnableAutoCreateFallback = true;

    [Header("Настройки сетки")]
    [SerializeField] private float cellSize = 2f; // Размер одной ячейки (например, 2x2 метра)
    [SerializeField] private Vector3 gridOrigin = Vector3.zero; // Начало координат сетки в мировом пространстве
    [SerializeField] private Vector2Int gridBounds = new Vector2Int(10, 10); // Размеры сетки для визуализации в редакторе

    [Header("Видимая сетка в игре (не только в редакторе)")]
    [Tooltip("OnDrawGizmos ниже виден только в Scene view редактора — во время игры/в билде нужна отдельная отрисовка")]
    [SerializeField] private bool showGridOnlyDuringPreparation = true;
    [SerializeField] private Material gridLineMaterial;
    [SerializeField] private Color gridLineColor = new Color(0f, 1f, 1f, 0.5f);
    [SerializeField] private float gridLineWidth = 0.03f;

    private GameObject gridLinesContainer;

    // Словарь занятых ячеек. Хранит координаты и ссылку на объект, который её занимает.
    // Заполняется ТОЛЬКО в play mode (через RegisterCounterAtCurrentPosition).
    // В редакторе пуст — Editor-скрипт снапа не проверяет занятость, чтобы не блокировать
    // расстановку объектов дизайнером (например, временное перекрытие при перемещении).
    private Dictionary<Vector2Int, BaseCounter> occupiedCells = new Dictionary<Vector2Int, BaseCounter>();

    /// <summary>Размер ячейки сетки в метрах. Нужен Editor-скрипту для отрисовки превью.</summary>
    public float GetCellSize() => cellSize;

    /// <summary>Origin сетки в мировых координатах. Нужен Editor-скрипту.</summary>
    public Vector3 GetGridOrigin() => gridOrigin;

    /// <summary>Размеры сетки (X, Z). Нужны Editor-скрипту для ограничения зоны расстановки.</summary>
    public Vector2Int GetGridBounds() => gridBounds;

    private void Awake()
    {
        // ★ v1.3 КРИТИЧЕСКОЕ ИСПРАВЛЕНИЕ БАГА САМОУНИЧТОЖЕНИЯ.
        //
        // ResolveInstance() мог уже проставить Instance = ЭТОТ ЖЕ САМЫЙ объект:
        // так происходит, когда чужой Awake (например, BaseCounter — а их в сцене
        // десятки) срабатывает РАНЬШЕ Awake самой сетки. Порядок вызова Awake
        // в Unity НЕ гарантирован. Чужой Awake вызывает ResolveInstance(), тот
        // находит сетку поиском по сцене и лениво заполняет Instance.
        //
        // Старая проверка «Instance == null» в этом случае проваливалась,
        // срабатывала ветка «дубликат» — и сетка вызывала Destroy(gameObject)
        // НА САМОЙ СЕБЕ через мгновение после загрузки сцены. В консоли это
        // выглядело как warning «ВТОРОЙ GridPositioningSystem: 'GridManager'
        // (дубликат)... остаётся 'GridManager'» (объект назван дважды!),
        // а все последующие попытки взять мебель получали «НЕ НАЙДЕН».
        //
        // Уничтожаем себя теперь ТОЛЬКО если Instance указывает на живой ЧУЖОЙ
        // объект сетки. Ссылка на УЖЕ УНИЧТОЖЕННЫЙ объект сравнивается с null
        // как true (Unity fake-null), поэтому ветка ниже её корректно обработает.
        if (Instance == null || Instance == this)
        {
            Instance = this;
        }
        else if (Application.isPlaying)
        {
            // Настоящий дубликат: в сцене уже есть ЖИВАЯ сетка другого объекта.
            // ★ v1.1: дубликат не уничтожается МОЛЧА — явный Warning называет оба объекта.
            Debug.LogWarning(
                $"[GridSystem] В сцене ВТОРОЙ GridPositioningSystem: '{gameObject.name}' " +
                $"(дубликат) будет удалён, остаётся '{Instance.gameObject.name}'. " +
                "Удали лишний объект из сцены вручную — дубликаты сбивают снап и занятость ячеек.");

            Destroy(gameObject);
            return;
        }
        else
        {
            // Edit mode: перезаписываем ссылку — так же ведёт себя OnEnable ниже,
            // это поддерживает undo/redo, копирование сцен и перезагрузку домена.
            Instance = this;
        }

        // BuildGridVisualization создаёт дочерние GameObject с LineRenderer.
        // В редакторе это даст лишний мусор в сцене при каждом Awake, поэтому
        // визуализацию строим ТОЛЬКО в play mode. В edit mode сетку рисует OnDrawGizmos.
        if (Application.isPlaying)
        {
            BuildGridVisualization();
        }
    }

    private void OnEnable()
    {
        // Поддержка undo/redo и перезагрузки домена: при включении объекта в редакторе
        // тоже обновляем Instance, если он пуст или указывает на уничтоженный объект.
        // В play mode чужую живую сетку НЕ перебиваем (иначе дубликаты начали бы
        // воевать друг с другом через каждое включение объекта).
        if (Instance == null || (Instance != this && !Application.isPlaying))
        {
            Instance = this;
        }
    }

    private void OnDestroy()
    {
        // В редакторе НЕ очищаем Instance при OnDestroy — иначе undo/redo сломают
        // доступ к сетке из Editor-скрипта. В play mode — очищаем как обычно.
        if (Application.isPlaying && Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (gridLinesContainer == null || !showGridOnlyDuringPreparation) return;

        bool shouldShow = GameLoopManager.Instance != null && GameLoopManager.Instance.IsPreparationActive();

        if (gridLinesContainer.activeSelf != shouldShow)
        {
            gridLinesContainer.SetActive(shouldShow);
        }
    }

    /// <summary>
    /// ★ v1.3: безопасное получение сетки с самовосстановлением и аварийным автокрафтом.
    /// 1) Instance жив — возвращаем его.
    /// 2) Ищем по всем загруженным сценам, включая НЕактивные объекты, и восстанавливаем.
    /// 3) Диагностика: если скрипт найден на префаб-ассете — объясняем, что ассеты не работают.
    /// 4) Ничего не нашли в play mode — создаём запасную сетку со стандартными настройками,
    ///    бронируем ячейки существующей мебели (без телепортации) и предупреждаем, как
    ///    подстроить cellSize/gridOrigin под комнату.
    /// </summary>
    public static GridPositioningSystem ResolveInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        // Поиск по всем загруженным сценам, включая отключённые объекты — самый частый
        // сценарий потери Instance: объект случайно деактивировали при правке сцены.
        GridPositioningSystem[] found = FindObjectsByType<GridPositioningSystem>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        if (found != null && found.Length > 0)
        {
            // ★ v1.3: из найденных предпочитаем ВКЛЮЧЁННЫЙ объект — раньше found[0]
            // мог оказаться отключённым, даже если в сцене был активный кандидат.
            GridPositioningSystem best = null;
            foreach (GridPositioningSystem candidate in found)
            {
                if (candidate != null && candidate.gameObject.activeInHierarchy)
                {
                    best = candidate;
                    break;
                }
            }

            if (best == null)
            {
                best = found[0];
            }

            Instance = best;

            string extra = found.Length > 1
                ? $" ВНИМАНИЕ: в сцене {found.Length} штук — оставь одну!"
                : string.Empty;

            Debug.LogWarning(
                $"[GridSystem] Instance был потерян — восстановлен через поиск сцены " +
                $"('{Instance.name}', сцена '{Instance.gameObject.scene.name}', " +
                $"активен: {Instance.gameObject.activeInHierarchy}).{extra} " +
                "Если это сообщение появилось сразу после загрузки сцены — это штатная ленивая " +
                "инициализация (кто-то обратился к сетке раньше её Awake), не ошибка. " +
                "Если объект отключён — включи его, иначе сетка не работает.",
                Instance);

            return Instance;
        }

        // ★ v1.2 ДИАГНОСТИКА: компонента нет ни в одной загруженной сцене.

        // Причина №1: скрипт висит на ПРЕФАБЕ (ассете в окне Project), а не на объекте сцены —
        // классическая ошибка «перетащил скрипт на префаб». Resources.FindObjectsOfTypeAll
        // видит и ассеты: если найден экземпляр, не привязанный ни к какой сцене — это префаб.
        GridPositioningSystem[] everywhere = Resources.FindObjectsOfTypeAll<GridPositioningSystem>();
        foreach (GridPositioningSystem candidate in everywhere)
        {
            if (candidate != null && !candidate.gameObject.scene.IsValid())
            {
                Debug.LogWarning(
                    $"[GridSystem] Скрипт GridPositioningSystem найден на ПРЕФАБЕ «{candidate.name}», " +
                    "а НЕ на объекте сцены — ассеты не работают в игре! Перетащи этот префаб в " +
                    "игровую сцену (или создай пустой объект и добавь компонент вручную).",
                    candidate);
                return null;
            }
        }

        // ★ v1.3 АВАРИЙНАЯ ЗАЩИТА: в play mode не остаёмся без сетки совсем —
        // создаём запасную со стандартными настройками, чтобы постановка в клетку
        // продолжала работать, а мебель не ломала геймплей.
        if (Application.isPlaying && EnableAutoCreateFallback)
        {
            GameObject autoObject = new GameObject("GridPositioningSystem (AUTO)");
            GridPositioningSystem autoGrid = autoObject.AddComponent<GridPositioningSystem>();
            autoGrid.WasAutoCreated = true;

            // Бронируем ячейки уже существующей мебели ПО ТЕКУЩИМ ПОЗИЦИЯМ —
            // без телепортации на центры ячеек (игра уже идёт, дёргать мебель нельзя).
            autoGrid.RegisterAllExistingCountersWithoutAlignment();

            Debug.LogWarning(
                "[GridSystem] GridPositioningSystem НЕ НАЙДЕН в сцене — создан ЗАПАСНОЙ " +
                "'GridPositioningSystem (AUTO)' со стандартными настройками " +
                "(cellSize = 2, origin = (0, 0, 0), bounds 10x10). " +
                "Сейчас загружены: " + BuildLoadedSceneList() + ". " +
                "Если мебель встаёт мимо клеток — удали AUTO-объект, добавь свой " +
                "(Tools → Grid System → 3) Создать сетку в активной сцене) и настрой " +
                "cellSize/gridOrigin под комнату.", autoObject);

            return autoGrid;
        }

        // Сюда доходим только в edit mode или если автокрафт отключён флагом.
        // Причина №2: объект лежит в другой сцене. Печатаем список реально загруженных
        // сцен — если объект добавлен в BootScene/MainMenuScene, они ВЫГРУЖАЮТСЯ при
        // переходе (LoadSceneAsync работает в режиме Single), и объект умирает вместе с ними.
        Debug.LogWarning(
            "[GridSystem] GridPositioningSystem НЕ НАЙДЕН ни в одной загруженной сцене! " +
            "Сейчас загружены: " + BuildLoadedSceneList() + ". " +
            "Объект с компонентом должен лежать в ИГРОВОЙ сцене (та, что грузится через " +
            "SceneLoader), а НЕ в BootScene/MainMenuScene — они выгружаются при переходе. " +
            "Также проверь: объект создавался в Edit Mode (создание в Play Mode откатывается " +
            "при выходе) и сцена сохранена (Ctrl+S).");

        return null;
    }

    /// <summary>
    /// ★ v1.3: список загруженных сцен одной строкой — для диагностического warning'а.
    /// Сразу видно, что играет, например, 'GameScene' (Assets/Scenes/GameScene.unity),
    /// а объект добавили в другую сцену.
    /// </summary>
    private static string BuildLoadedSceneList()
    {
        if (SceneManager.sceneCount == 0)
        {
            return "(сцены не загружены?)";
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (i > 0) sb.Append(", ");
            sb.Append("'").Append(scene.name).Append("' (").Append(scene.path).Append(")");
            if (!scene.isLoaded) sb.Append(" (выгружена)");
        }
        return sb.ToString();
    }

    /// <summary>
    /// ★ v1.3: бронирует ячейки под ВСЮ существующую мебель по её текущим позициям —
    /// БЕЗ выравнивания (телепортации) на центры ячеек. Нужен аварийной AUTO-сетке:
    /// она создаётся, когда игра уже идёт, и дёргать мебель нельзя. Конфликты
    /// (два объекта на одной ячейке) молча пропускаются — первый занявший побеждает.
    /// </summary>
    private void RegisterAllExistingCountersWithoutAlignment()
    {
        BaseCounter[] counters = FindObjectsByType<BaseCounter>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        foreach (BaseCounter counter in counters)
        {
            if (counter == null) continue;

            Vector2Int cell = GetGridPosition(counter.transform.position);

            if (occupiedCells.ContainsKey(cell))
            {
                continue; // Конфликт — не шумим, первый занявший ячейку побеждает.
            }

            occupiedCells[cell] = counter;
        }

        Debug.Log($"[GridSystem] AUTO-сетка: забронировано {occupiedCells.Count} ячеек " +
                  "под существующую мебель (по текущим позициям, без выравнивания).");
    }

    /// <summary>
    /// Строит сетку из LineRenderer'ов один раз при старте — сами линии не меняются
    /// (cellSize/gridBounds фиксированы для сцены), меняется только видимость контейнера.
    /// LineRenderer выбран не просто так: в отличие от GL.Lines, он одинаково работает
    /// в любом render pipeline без написания под него отдельного шейдера.
    /// </summary>
    private void BuildGridVisualization()
    {
        gridLinesContainer = new GameObject("GridLinesVisual");
        gridLinesContainer.transform.SetParent(transform);

        // Линии вдоль оси Z — по одной на каждое значение X от 0 до gridBounds.x включительно
        for (int x = 0; x <= gridBounds.x; x++)
        {
            float worldX = gridOrigin.x + x * cellSize;
            Vector3 start = new Vector3(worldX, gridOrigin.y + 0.02f, gridOrigin.z);
            Vector3 end = new Vector3(worldX, gridOrigin.y + 0.02f, gridOrigin.z + gridBounds.y * cellSize);
            CreateGridLine(start, end);
        }

        // Линии вдоль оси X — по одной на каждое значение Z от 0 до gridBounds.y включительно
        for (int z = 0; z <= gridBounds.y; z++)
        {
            float worldZ = gridOrigin.z + z * cellSize;
            Vector3 start = new Vector3(gridOrigin.x, gridOrigin.y + 0.02f, worldZ);
            Vector3 end = new Vector3(gridOrigin.x + gridBounds.x * cellSize, gridOrigin.y + 0.02f, worldZ);
            CreateGridLine(start, end);
        }
    }

    private void CreateGridLine(Vector3 start, Vector3 end)
    {
        GameObject lineObj = new GameObject("GridLine");
        lineObj.transform.SetParent(gridLinesContainer.transform);

        LineRenderer lr = lineObj.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        lr.startWidth = gridLineWidth;
        lr.endWidth = gridLineWidth;
        lr.useWorldSpace = true;

        // Запасной материал, чтобы линии были видны сразу, без ручной настройки —
        // но для билда надёжнее назначить gridLineMaterial самим, под свой render pipeline.
        lr.material = gridLineMaterial != null ? gridLineMaterial : new Material(Shader.Find("Sprites/Default"));
        lr.startColor = gridLineColor;
        lr.endColor = gridLineColor;
    }

    /// <summary>
    /// Конвертирует мировую позицию в координаты ячейки сетки (X, Z).
    /// </summary>
    public Vector2Int GetGridPosition(Vector3 worldPosition)
    {
        int x = Mathf.FloorToInt((worldPosition.x - gridOrigin.x) / cellSize);
        int z = Mathf.FloorToInt((worldPosition.z - gridOrigin.z) / cellSize);
        return new Vector2Int(x, z);
    }

    /// <summary>
    /// Получает центральную мировую позицию для заданной ячейки сетки.
    /// </summary>
    public Vector3 GetWorldPosition(Vector2Int gridPosition)
    {
        float x = gridPosition.x * cellSize + (cellSize / 2f) + gridOrigin.x;
        float z = gridPosition.y * cellSize + (cellSize / 2f) + gridOrigin.z;
        return new Vector3(x, gridOrigin.y, z); // Используем базовую высоту сетки
    }

    /// <summary>
    /// Проверяет, свободна ли выбранная ячейка.
    /// </summary>
    public bool IsCellOccupied(Vector2Int gridPosition)
    {
        return occupiedCells.ContainsKey(gridPosition);
    }

    /// <summary>
    /// ★ v1.1: кто занимает ячейку — нужно диагностике FurnitureMovingController,
    /// чтобы лог говорил «ячейка занята 'StoveCounter'», а не просто «занята».
    /// </summary>
    public bool TryGetCellOwner(Vector2Int gridPosition, out BaseCounter owner)
    {
        if (occupiedCells.TryGetValue(gridPosition, out BaseCounter found) && found != null)
        {
            owner = found;
            return true;
        }

        owner = null;
        return false;
    }

    /// <summary>
    /// Регистрирует объект на его текущей позиции и выравнивает по сетке.
    /// Полезно для объектов, которые изначально расставлены на сцене в редакторе Unity.
    /// </summary>
    public void RegisterCounterAtCurrentPosition(BaseCounter counter)
    {
        Vector2Int gridPos = GetGridPosition(counter.transform.position);

        if (IsCellOccupied(gridPos))
        {
            // Проверяем: если ячейку занял тот же самый объект, игнорируем ошибку
            if (occupiedCells[gridPos] == counter) return;

            Debug.LogWarning($"[GridSystem] Конфликт! Ячейка {gridPos} уже занята объектом '{occupiedCells[gridPos].gameObject.name}'. Не удалось зарегистрировать '{counter.gameObject.name}'");
            return;
        }

        // Выравниваем объект строго по центру ячейки
        Vector3 alignedPosition = GetWorldPosition(gridPos);
        alignedPosition.y = counter.transform.position.y; // Сохраняем исходную высоту стола
        counter.transform.position = alignedPosition;

        // Бронируем ячейку за этим столом
        occupiedCells[gridPos] = counter;
        Debug.Log($"[GridSystem] '{counter.gameObject.name}' автоматически выровнен и зарегистрирован в ячейке {gridPos}");
    }

    /// <summary>
    /// Пытается разместить счетчик/стол на сетке (например, при покупке стола).
    /// </summary>
    /// <returns>True, если размещение успешно. False, если ячейка занята.</returns>
    public bool TryPlaceCounter(BaseCounter counter, Vector3 targetWorldPosition)
    {
        Vector2Int gridPos = GetGridPosition(targetWorldPosition);

        if (IsCellOccupied(gridPos) && occupiedCells[gridPos] != counter)
        {
            Debug.LogWarning($"[GridSystem] Не удалось разместить! Ячейка {gridPos} уже занята объектом: {occupiedCells[gridPos].gameObject.name}");
            return false;
        }

        // Если этот же счётчик уже числится в какой-то другой ячейке — например, Awake()
        // уже успел зарегистрировать его по позиции спавна до того, как вызвали этот метод —
        // освобождаем старую ячейку, иначе она навсегда останется фантомно "занятой".
        RemoveCounter(counter);

        // Выравниваем объект по центру сетки
        Vector3 alignedPosition = GetWorldPosition(gridPos);

        // Корректируем высоту под объект
        alignedPosition.y = counter.transform.position.y;
        counter.transform.position = alignedPosition;

        // Регистрируем объект в словаре
        occupiedCells[gridPos] = counter;
        Debug.Log($"[GridSystem] Объект {counter.gameObject.name} успешно размещен в ячейке {gridPos}");
        return true;
    }

    /// <summary>
    /// ★ v1.4: снимок реестра занятых ячеек — нужен FurnitureSaveUtility при
    /// сохранении расстановки мебели. Возвращается КОПИЯ словаря: вызывающий
    /// не может случайно испортить реестр сетки.
    /// </summary>
    public IReadOnlyDictionary<Vector2Int, BaseCounter> GetOccupiedCellsSnapshot()
    {
        return new Dictionary<Vector2Int, BaseCounter>(occupiedCells);
    }

    /// <summary>
    /// ★ v1.4: в какой ячейке сейчас зарегистрирован данный счётчик.
    /// Нужен диагностике и сохранению. Если объект не зарегистрирован — false.
    /// </summary>
    public bool TryGetCellOf(BaseCounter counter, out Vector2Int cell)
    {
        foreach (KeyValuePair<Vector2Int, BaseCounter> kvp in occupiedCells)
        {
            if (kvp.Value == counter)
            {
                cell = kvp.Key;
                return true;
            }
        }

        cell = default;
        return false;
    }

    /// <summary>
    /// Проверяет, свободна ли ячейка проверки — свободна ли она вообще, либо занята
    /// тем же самым counter'ом (актуально при переносе мебели: собственная текущая
    /// ячейка объекта не должна считаться "занятой" для него самого).
    /// </summary>
    public bool IsCellFreeFor(Vector2Int gridPosition, BaseCounter counter)
    {
        return !occupiedCells.ContainsKey(gridPosition) || occupiedCells[gridPosition] == counter;
    }

    /// <summary>
    /// Удаляет стол с сетки по его координатам.
    /// </summary>
    public void RemoveCounter(Vector2Int gridPosition)
    {
        if (occupiedCells.ContainsKey(gridPosition))
        {
            occupiedCells.Remove(gridPosition);
            Debug.Log($"[GridSystem] Ячейка {gridPosition} теперь свободна.");
        }
    }

    /// <summary>
    /// Находит и удаляет переданный стол из реестра занятых ячеек сетки.
    /// </summary>
    public void RemoveCounter(BaseCounter counter)
    {
        Vector2Int keyToRemove = Vector2Int.zero;
        bool found = false;

        foreach (var kvp in occupiedCells)
        {
            if (kvp.Value == counter)
            {
                keyToRemove = kvp.Key;
                found = true;
                break;
            }
        }

        if (found)
        {
            occupiedCells.Remove(keyToRemove);
            Debug.Log($"[GridSystem] Объект {counter.gameObject.name} удален. Ячейка {keyToRemove} теперь свободна.");
        }
    }

    /// <summary>
    /// Отрисовка сетки в редакторе Unity для удобного проектирования уровней.
    /// </summary>
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0f, 1f, 1f, 0.3f); // Полупрозрачный голубой цвет

        for (int x = 0; x < gridBounds.x; x++)
        {
            for (int z = 0; z < gridBounds.y; z++)
            {
                Vector2Int gridPos = new Vector2Int(x, z);
                Vector3 cellCenter = GetWorldPosition(gridPos);

                // Рисуем границы ячейки плоским кубом на полу
                Gizmos.DrawWireCube(cellCenter, new Vector3(cellSize, 0.05f, cellSize));
            }
        }
    }
}
