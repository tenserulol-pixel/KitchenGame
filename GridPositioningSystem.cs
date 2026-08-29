using UnityEngine;
using System.Collections.Generic;

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
        // В редакторе (edit mode) тоже устанавливаем Instance — это позволяет Editor-скриптам
        // обращаться к GridPositioningSystem.Instance.GetGridPosition(...) и т.п.
        // НЕ делаем Destroy(gameObject) при дублировании в редакторе — иначе при копировании
        // сцены или undo/redo будут теряться объекты.
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Application.isPlaying)
        {
            Destroy(gameObject);
            return;
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