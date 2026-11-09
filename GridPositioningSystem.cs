using UnityEngine;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

// ExecuteAlways: Awake/OnEnable/OnDisable вызываются и в edit mode, и в play mode.
// Это позволяет Editor-скриптам дёргать Instance и методы GetGridPosition/GetWorldPosition
// прямо из редактора (например, для снапа стола к сетке при перетаскивании мышью).
// Словарь occupiedCells в редакторе НЕ заполняется — это нужно только для play mode,
// чтобы не было ложных конфликтов между объектами в редакторе и при запуске игры.
//
// ★ v1.6 (фича «Стены по рёбрам сетки»): предметы с маркером WallPiece живут
// НЕ в ячейках, а на РЁБРАХ — границах МЕЖДУ клетками (WallEdgeKey = линия сетки
// + номер сегмента на ней). Регистрация (BaseCounter.Awake → RegisterCounterAtCurrentPosition),
// постановка (TryPlaceCounter), удаление (RemoveCounter) и AUTO-сетка маршрутизируют
// стены в отдельный реестр occupiedWallEdges автоматически. Прилипание «к друг другу»:
// точная геометрия рёбер (торцы соседних сегментов совпадают, перпендикулярные
// встречаются в узле) + магнит к коллинеарным соседям в TrySnapWorldToWallEdge.
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

    // ★ v1.6: реестр занятых РЁБЕР стенами. Ключ — ребро (линия + сегмент),
    // значение — стена (BaseCounter с WallPiece). Стены не занимают ячейки:
    // мебель свободно ставится в клетки по обе стороны от стены.
    private Dictionary<WallEdgeKey, BaseCounter> occupiedWallEdges = new Dictionary<WallEdgeKey, BaseCounter>();

    /// <summary>
    /// ★ v1.6: ориентация ребра. AlongX — ребро вдоль мировой оси X (на линии z),
    /// AlongZ — вдоль мировой оси Z (на линии x).
    /// </summary>
    public enum WallEdgeAxis
    {
        AlongX = 0,
        AlongZ = 1
    }

    /// <summary>
    /// ★ v1.6: ребро сетки — место, где стоит стена.
    /// AlongX(x, z): сегмент по X на линии z — от worldX = origin + x*S до origin + (x+1)*S,
    ///               сам лежит на worldZ = origin.z + z*S. Валидно: 0≤x&lt;bounds.x, 0≤z≤bounds.y.
    /// AlongZ(x, z): сегмент по Z на линии x, лежит на worldX = origin.x + x*S.
    ///               Валидно: 0≤x≤bounds.x, 0≤z&lt;bounds.y.
    /// </summary>
    public struct WallEdgeKey : System.IEquatable<WallEdgeKey>
    {
        public int x;
        public int z;
        public WallEdgeAxis axis;

        public WallEdgeKey(int x, int z, WallEdgeAxis axis)
        {
            this.x = x;
            this.z = z;
            this.axis = axis;
        }

        public bool Equals(WallEdgeKey other)
        {
            return x == other.x && z == other.z && axis == other.axis;
        }

        public override bool Equals(object obj)
        {
            return obj is WallEdgeKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((x * 397) ^ (z * 31)) ^ (int)axis;
            }
        }

        public override string ToString()
        {
            return (axis == WallEdgeAxis.AlongX ? "H" : "V") + "(" + x + "," + z + ")";
        }
    }

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

            // ★ v1.6: стены бронируются на рёбрах — по геометрическому центру,
            // без выравнивания (AUTO-сетка создаётся посреди игры).
            if (counter.GetComponent<WallPiece>() != null)
            {
                WallEdgeKey wallEdge = DeriveWallEdgeFromCurrentPosition(counter);

                if (IsEdgeInsideGrid(wallEdge) && !occupiedWallEdges.ContainsKey(wallEdge))
                {
                    occupiedWallEdges[wallEdge] = counter;
                }

                continue;
            }

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
    ///
    /// ★ v1.5: перед регистрацией СНИМАЕМ все прежние регистрации этого же счётчика.
    /// Раньше метод проверял только занятость ЦЕЛЕВОЙ ячейки; если счётчик уже числился
    /// в другой ячейке (двойной компонент BaseCounter на префабе, телепорт без снятия
    /// регистрации и т.п.), старая запись оставалась навсегда — «фантомная» занятая
    /// клетка, которую нельзя освободить никаким переносом. Теперь таких фантомов
    /// не бывает: перерегистрация всегда атомарна (снял всё → зарегистрировал).
    /// </summary>
    public void RegisterCounterAtCurrentPosition(BaseCounter counter)
    {
        Vector2Int gridPos = GetGridPosition(counter.transform.position);

        // ★ v1.5: защита от конфигурационной ошибки — несколько BaseCounter на одном
        // GameObject. Каждый компонент регистрируется сам и создаёт вторую запись с
        // потенциально другой ячейкой (дочерний Transform != корневого). Один объект =
        // один компонент = одна ячейка.
        BaseCounter[] siblings = counter.GetComponents<BaseCounter>();

        if (siblings.Length > 1)
        {
            Debug.LogError($"[GridSystem] '{counter.gameObject.name}': найдено {siblings.Length} " +
                           "компонентов BaseCounter на одном объекте — регистрация клеток дублируется. " +
                           "Оставь ОДИН компонент (проверь и дочерние объекты префаба), иначе " +
                           "часть ячеек останется фантомно занятой.");
        }

        // ★ v1.6: стены живут на РЁБРАХ, а не в ячейках — отдельный реестр,
        // позиция — середина ребра, поворот — вдоль ребра.
        if (WallPiece.IsWall(counter))
        {
            RegisterWallAtCurrentPosition(counter);
            return;
        }

        // ★ v1.5: запоминаем прежнюю регистрацию (если была), но НЕ снимаем её до
        // успешного прохождения конфликта — иначе при отказе счётчик остался бы
        // вообще без ячейки.
        bool hadRegistration = TryGetCellOf(counter, out Vector2Int previousCell);

        if (IsCellOccupied(gridPos))
        {
            // Проверяем: если ячейку занял тот же самый объект, игнорируем ошибку
            if (occupiedCells[gridPos] == counter) return;

            Debug.LogWarning($"[GridSystem] Конфликт! Ячейка {gridPos} уже занята объектом '{occupiedCells[gridPos].gameObject.name}'. Не удалось зарегистрировать '{counter.gameObject.name}'");
            return;
        }

        // ★ v1.5: конфликтов нет — снимаем ВСЕ прежние записи этого счётчика
        // (где бы они ни были) и регистрируем заново. Перерегистрация атомарна.
        if (hadRegistration)
        {
            RemoveAllRegistrations(counter);

            if (previousCell != gridPos)
            {
                Debug.Log($"[GridSystem] '{counter.gameObject.name}' перерегистрирован: " +
                          $"{previousCell} → {gridPos} (старая ячейка освобождена).");
            }
        }

        // Выравниваем объект строго по центру ячейки
        Vector3 alignedPosition = GetWorldPosition(gridPos);
        alignedPosition.y = counter.transform.position.y; // Сохраняем исходную высоту стола
        counter.transform.position = alignedPosition;

        // Бронируем ячейку за этим столом
        occupiedCells[gridPos] = counter;
        Debug.Log($"[GridSystem] '{counter.gameObject.name}' автоматически выровнен и зарегистрирован в ячейке {gridPos}");
    }

    // ============================================================
    //  ★ v1.6: СТЕНЫ ПО РЁБРАМ СЕТКИ
    // ============================================================

    /// <summary>Середина ребра (мировая точка на границе между клетками). Y = gridOrigin.y.</summary>
    public Vector3 GetWallEdgeMidpoint(WallEdgeKey edge)
    {
        if (edge.axis == WallEdgeAxis.AlongX)
        {
            return new Vector3(
                gridOrigin.x + (edge.x + 0.5f) * cellSize,
                gridOrigin.y,
                gridOrigin.z + edge.z * cellSize);
        }

        return new Vector3(
            gridOrigin.x + edge.x * cellSize,
            gridOrigin.y,
            gridOrigin.z + (edge.z + 0.5f) * cellSize);
    }

    /// <summary>Лежит ли ребро в пределах сетки (учитывая допустимые диапазоны каждого типа).</summary>
    public bool IsEdgeInsideGrid(WallEdgeKey edge)
    {
        if (edge.axis == WallEdgeAxis.AlongX)
        {
            return edge.x >= 0 && edge.x < gridBounds.x &&
                   edge.z >= 0 && edge.z <= gridBounds.y;
        }

        return edge.x >= 0 && edge.x <= gridBounds.x &&
               edge.z >= 0 && edge.z < gridBounds.y;
    }

    /// <summary>Занято ли ребро какой-либо стеной.</summary>
    public bool IsEdgeOccupied(WallEdgeKey edge)
    {
        return occupiedWallEdges.ContainsKey(edge);
    }

    /// <summary>Кто занимает ребро (для диагностики). Если пусто — false.</summary>
    public bool TryGetWallEdgeOwner(WallEdgeKey edge, out BaseCounter owner)
    {
        if (occupiedWallEdges.TryGetValue(edge, out BaseCounter found) && found != null)
        {
            owner = found;
            return true;
        }

        owner = null;
        return false;
    }

    /// <summary>
    /// Свободно ли ребро для данной стены: пусто, либо занято ею же самой
    /// (её собственное ребро при переносе не считается препятствием).
    /// </summary>
    public bool IsEdgeFreeFor(WallEdgeKey edge, BaseCounter wall)
    {
        return !occupiedWallEdges.TryGetValue(edge, out BaseCounter owner) || owner == wall;
    }

    /// <summary>Есть ли у ребра занятый СОСЕДЬ ПО ТОЙ ЖЕ ЛИНИИ — для магнита «продолжить стену».</summary>
    private bool HasOccupiedCollinearNeighbor(WallEdgeKey edge)
    {
        if (edge.axis == WallEdgeAxis.AlongX)
        {
            return IsEdgeOccupied(new WallEdgeKey(edge.x - 1, edge.z, WallEdgeAxis.AlongX)) ||
                   IsEdgeOccupied(new WallEdgeKey(edge.x + 1, edge.z, WallEdgeAxis.AlongX));
        }

        return IsEdgeOccupied(new WallEdgeKey(edge.x, edge.z - 1, WallEdgeAxis.AlongZ)) ||
               IsEdgeOccupied(new WallEdgeKey(edge.x, edge.z + 1, WallEdgeAxis.AlongZ));
    }

    /// <summary>
    /// Выводит ребро из текущего положения стены: берём геометрический центр
    /// (падает на середину ребра при снапе) + ориентацию длинной оси по bounds.
    /// Нужен регистрации на Awake-пути, сейву и диагностике.
    /// </summary>
    public WallEdgeKey DeriveWallEdgeFromCurrentPosition(BaseCounter wall)
    {
        Vector3 probe = wall.transform.position;

        if (WallPiece.TryGetGeometryCenter(wall, out Vector3 center))
        {
            probe = center;
        }

        bool longAxisIsWorldX = true;

        if (WallPiece.TryGetGeometryBounds(wall, out Bounds bounds))
        {
            float currentYaw = NormalizeAngleTo360(wall.transform.rotation.eulerAngles.y);
            int quarter = Mathf.RoundToInt(currentYaw / 90f) % 2;
            longAxisIsWorldX = (quarter == 0)
                ? bounds.size.x >= bounds.size.z
                : bounds.size.z >= bounds.size.x;
        }

        if (longAxisIsWorldX)
        {
            int lineZ = Mathf.Clamp(Mathf.RoundToInt((probe.z - gridOrigin.z) / cellSize), 0, gridBounds.y);
            int cellX = Mathf.Clamp(Mathf.FloorToInt((probe.x - gridOrigin.x) / cellSize), 0, gridBounds.x - 1);
            return new WallEdgeKey(cellX, lineZ, WallEdgeAxis.AlongX);
        }

        int lineX = Mathf.Clamp(Mathf.RoundToInt((probe.x - gridOrigin.x) / cellSize), 0, gridBounds.x);
        int cellZ = Mathf.Clamp(Mathf.FloorToInt((probe.z - gridOrigin.z) / cellSize), 0, gridBounds.y - 1);
        return new WallEdgeKey(lineX, cellZ, WallEdgeAxis.AlongZ);
    }

    /// <summary>
    /// Ближайшее к точке world СВОБОДНОЕ ребро — ядро прилипания стен.
    /// Берём ближайший узел сетки и четыре ребра вокруг него (два AlongX,
    /// два AlongZ); расстояние считается ДО СЕГМЕНТА, а не до узла — иначе
    /// стена «гуляла» бы по линии. orientationOverride: 0 — авто,
    /// 1 — только AlongX, 2 — только AlongZ (переключается клавишей R).
    /// Занятые чужими стенами рёбра не предлагаются; ребро с занятым
    /// коллинеарным соседом получает магнит (дистанция ×0.5) — стены
    /// естественно продолжают линию друг друга.
    /// </summary>
    public bool TrySnapWorldToWallEdge(Vector3 world, BaseCounter wall, int orientationOverride, out WallEdgeKey edge)
    {
        edge = default;

        int nodeX = Mathf.Clamp(Mathf.RoundToInt((world.x - gridOrigin.x) / cellSize), 0, gridBounds.x);
        int nodeZ = Mathf.Clamp(Mathf.RoundToInt((world.z - gridOrigin.z) / cellSize), 0, gridBounds.y);

        WallEdgeKey? best = null;
        float bestDist = float.MaxValue;

        for (int pass = 0; pass < 2; pass++)
        {
            bool alongX = pass == 0;

            if (orientationOverride == 1 && !alongX) continue;
            if (orientationOverride == 2 && alongX) continue;

            for (int side = -1; side <= 1; side += 2)
            {
                WallEdgeKey candidate = alongX
                    ? new WallEdgeKey(nodeX + (side < 0 ? -1 : 0), nodeZ, WallEdgeAxis.AlongX)
                    : new WallEdgeKey(nodeX, nodeZ + (side < 0 ? -1 : 0), WallEdgeAxis.AlongZ);

                if (!IsEdgeInsideGrid(candidate)) continue;
                if (!IsEdgeFreeFor(candidate, wall)) continue;

                Vector3 start = GetWallEdgeMidpoint(candidate);
                Vector3 end = start;

                if (alongX)
                {
                    start.x -= cellSize * 0.5f;
                    end.x += cellSize * 0.5f;
                }
                else
                {
                    start.z -= cellSize * 0.5f;
                    end.z += cellSize * 0.5f;
                }

                float dist = SqrDistancePointToSegmentXZ(world, start, end);

                // ★ МАГНИТ «к друг другу»: продолжение существующей линии стен.
                if (HasOccupiedCollinearNeighbor(candidate))
                {
                    dist *= 0.5f;
                }

                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = candidate;
                }
            }
        }

        if (best == null)
        {
            return false;
        }

        edge = best.Value;
        return true;
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

    /// <summary>
    /// Ставит стену на ребро: проверяет границы/занятость, снимает прежние
    /// регистрации (клетки и рёбра), ставит геометрический центр на середину
    /// ребра и поворачивает вдоль него. Пивот может быть где угодно — центрируется
    /// именно геометрия (WallPiece.TryGetGeometryCenter).
    /// </summary>
    public bool TryPlaceWall(BaseCounter wall, WallEdgeKey edge)
    {
        if (wall == null) return false;

        if (!IsEdgeInsideGrid(edge))
        {
            Debug.LogWarning($"[GridSystem] Ребро {edge} вне сетки (bounds {gridBounds}) — стена не размещена.");
            return false;
        }

        if (!IsEdgeFreeFor(edge, wall))
        {
            string ownerName = TryGetWallEdgeOwner(edge, out BaseCounter owner) && owner != null
                ? owner.gameObject.name
                : "?";

            Debug.LogWarning($"[GridSystem] Ребро {edge} уже занято стеной '{ownerName}' — выбери другое место.");
            return false;
        }

        SnapWallToEdgeInternal(wall, edge);
        Debug.Log($"[GridSystem] Стена '{wall.gameObject.name}' размещена на ребре {edge}.");
        return true;
    }

    /// <summary>
    /// Ищет свободное ребро рядом с текущим положением стены (кольца радиусом 1..3,
    /// только коллинеарные рёбра — ориентация стены сохраняется) и ставит на него.
    /// Нужен восстановлению сейва, когда сохранённое ребро чем-то занято.
    /// </summary>
    public bool TryRelocateWallToNearestFreeEdge(BaseCounter wall)
    {
        if (wall == null) return false;

        WallEdgeKey home = DeriveWallEdgeFromCurrentPosition(wall);

        for (int radius = 1; radius <= 3; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    if (Mathf.Abs(dx) != radius && Mathf.Abs(dz) != radius) continue;

                    WallEdgeKey candidate = new WallEdgeKey(home.x + dx, home.z + dz, home.axis);

                    if (IsEdgeInsideGrid(candidate) && IsEdgeFreeFor(candidate, wall))
                    {
                        return TryPlaceWall(wall, candidate);
                    }
                }
            }
        }

        return false;
    }

    /// <summary>Регистрация стены на её текущем месте (путь BaseCounter.Awake и восстановления сейва).</summary>
    private void RegisterWallAtCurrentPosition(BaseCounter wall)
    {
        if (!WallPiece.TryGetGeometryCenter(wall, out Vector3 center))
        {
            Debug.LogError($"[GridSystem] '{wall.gameObject.name}': у стены нет геометрии (BoxCollider/Renderer) — ребро не определить, регистрация пропущена.");
            return;
        }

        WallEdgeKey edge = DeriveWallEdgeFromCurrentPosition(wall);

        if (!IsEdgeInsideGrid(edge))
        {
            Debug.LogWarning($"[GridSystem] '{wall.gameObject.name}': ребро {edge} вне сетки (bounds {gridBounds}) — стена НЕ зарегистрирована. Сдвинь её внутрь зоны сетки.");
            return;
        }

        if (IsEdgeOccupied(edge) && occupiedWallEdges[edge] != wall)
        {
            Debug.LogWarning($"[GridSystem] Конфликт! Ребро {edge} уже занято стеной '{occupiedWallEdges[edge].gameObject.name}'. '{wall.gameObject.name}' НЕ зарегистрирована — сдвинь одну из стен.");
            return;
        }

        SnapWallToEdgeInternal(wall, edge);
        Debug.Log($"[GridSystem] Стена '{wall.gameObject.name}' зарегистрирована на ребре {edge} (позиция {wall.transform.position}).");
    }

    /// <summary>
    /// Атомарная постановка стены на ребро: снять все старые регистрации →
    /// выровнять поворот вдоль ребра → посадить геометрический центр на середину →
    /// забронировать ребро.
    /// </summary>
    private void SnapWallToEdgeInternal(BaseCounter wall, WallEdgeKey edge)
    {
        RemoveAllRegistrations(wall);

        bool edgeAlongX = edge.axis == WallEdgeAxis.AlongX;
        Vector3 midpoint = GetWallEdgeMidpoint(edge);
        Quaternion alignedRotation = Quaternion.Euler(0f, GetEdgeAlignedYaw(wall, edgeAlongX), 0f);

        if (WallPiece.TryGetGeometryCenter(wall, out Vector3 center))
        {
            // Локальный оффсет «пивот → центр геометрии» в ТЕКУЩЕЙ ориентации,
            // затем переносим его в новую (выровненную). На ребро садится именно
            // геометрический центр стены — пивот префаба может быть где угодно.
            Vector3 localOffset = Quaternion.Inverse(wall.transform.rotation) * (center - wall.transform.position);
            Vector3 worldOffset = alignedRotation * localOffset;

            Vector3 pos = new Vector3(
                midpoint.x - worldOffset.x,
                wall.transform.position.y,
                midpoint.z - worldOffset.z);

            wall.transform.rotation = alignedRotation;
            wall.transform.position = pos;
        }
        else
        {
            wall.transform.rotation = alignedRotation;
            wall.transform.position = new Vector3(midpoint.x, wall.transform.position.y, midpoint.z);
        }

        occupiedWallEdges[edge] = wall;
    }

    /// <summary>
    /// Поворот стены (yaw), при котором её длинная ось ложится вдоль ребра:
    /// AlongX → 0/180, AlongZ → 90/270. Длинная ось определяется по bounds
    /// геометрии с поправкой на текущую четверть поворота.
    /// </summary>
    private float GetEdgeAlignedYaw(BaseCounter wall, bool edgeAlongX)
    {
        if (!WallPiece.TryGetGeometryBounds(wall, out Bounds bounds))
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

    private static float NormalizeAngleTo360(float yaw)
    {
        yaw %= 360f;
        if (yaw < 0f) yaw += 360f;
        return yaw;
    }

    /// <summary>
    /// Пытается разместить счетчик/стол на сетке (например, при покупке стола).
    /// </summary>
    /// <returns>True, если размещение успешно. False, если ячейка занята.</returns>
    public bool TryPlaceCounter(BaseCounter counter, Vector3 targetWorldPosition)
    {
        // ★ v1.6: стены не живут в ячейках — ближайшее ребро + прилипание.
        if (counter != null && counter.GetComponent<WallPiece>() != null)
        {
            if (TrySnapWorldToWallEdge(targetWorldPosition, counter, 0, out WallEdgeKey wallEdge))
            {
                return TryPlaceWall(counter, wallEdge);
            }

            Debug.LogWarning($"[GridSystem] '{counter.gameObject.name}': рядом с {targetWorldPosition} нет свободного ребра — стена не размещена.");
            return false;
        }

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
    /// ★ v1.6: снимок рёберного реестра стен — нужен FurnitureSaveUtility
    /// при сохранении расстановки. Возвращается КОПИЯ словаря.
    /// </summary>
    public IReadOnlyDictionary<WallEdgeKey, BaseCounter> GetWallEdgesSnapshot()
    {
        return new Dictionary<WallEdgeKey, BaseCounter>(occupiedWallEdges);
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
    ///
    /// ★ v1.5: удаляет ВСЕ записи счётчика, а не только первую. Если по какой-то
    /// причине счётчик успел зарегистрироваться дважды (двойной компонент,
    /// телепорт мимо API), старая версия оставляла фантомную ячейку навсегда —
    /// типичный симптом: «перенёс CuttingCounter, а старая клетка всё равно занята».
    /// Теперь лишние регистрации снимаются автоматически, с явным Warning в Console.
    /// </summary>
    public void RemoveCounter(BaseCounter counter)
    {
        int removed = RemoveAllRegistrations(counter);

        if (removed > 1)
        {
            Debug.LogWarning($"[GridSystem] '{counter.gameObject.name}': в реестре было {removed} " +
                             "регистраций одновременно (фантомные ячейки) — все сняты. " +
                             "Причина обычно в повторном вызове RegisterCounterAtCurrentPosition " +
                             "или во втором компоненте BaseCounter на объекте.");
        }
    }

    /// <summary>
    /// ★ v1.5: снимает ВСЕ регистрации данного счётчика. Возвращает, сколько записей снято.
    /// </summary>
    public int RemoveAllRegistrations(BaseCounter counter)
    {
        int removed = 0;

        // Словарь нельзя менять во время foreach — собираем ключи, потом удаляем.
        List<Vector2Int> keysToRemove = new List<Vector2Int>();

        foreach (KeyValuePair<Vector2Int, BaseCounter> kvp in occupiedCells)
        {
            if (kvp.Value == counter)
            {
                keysToRemove.Add(kvp.Key);
            }
        }

        foreach (Vector2Int key in keysToRemove)
        {
            occupiedCells.Remove(key);
            removed++;
            Debug.Log($"[GridSystem] Объект {counter.gameObject.name} удален. Ячейка {key} теперь свободна.");
        }

        // ★ v1.6: рёберный реестр стен чистим тем же заходом — иначе после
        // удаления стены её ребро осталось бы занятым навсегда.
        List<WallEdgeKey> edgeKeysToRemove = new List<WallEdgeKey>();

        foreach (KeyValuePair<WallEdgeKey, BaseCounter> wallKvp in occupiedWallEdges)
        {
            if (wallKvp.Value == counter)
            {
                edgeKeysToRemove.Add(wallKvp.Key);
            }
        }

        foreach (WallEdgeKey key in edgeKeysToRemove)
        {
            occupiedWallEdges.Remove(key);
            removed++;
            Debug.Log($"[GridSystem] Объект {counter.gameObject.name} удален. Ребро {key} теперь свободно.");
        }

        return removed;
    }

    /// <summary>
    /// ★ v1.5: сколько ячеек реестра сейчас держит данный счётчик.
    /// Норма — 0 (не зарегистрирован) или 1. Больше 1 = фантом (см. RemoveCounter).
    /// </summary>
    public int CountRegistrations(BaseCounter counter)
    {
        int count = 0;

        foreach (KeyValuePair<Vector2Int, BaseCounter> kvp in occupiedCells)
        {
            if (kvp.Value == counter) count++;
        }

        return count;
    }

    /// <summary>
    /// ★ v1.5: полный дамп реестра занятых ячеек в Console.
    /// Вызывается через контекстное меню компонента (правый клик по заголовку
    /// GridPositioningSystem в Inspector → «Dump Occupied Cells») при живой игре.
    /// Мгновенно видно: какая ячейка кем занята, есть ли фантомные регистрации.
    /// </summary>
    [ContextMenu("Dump Occupied Cells")]
    public void DebugDumpOccupiedCells()
    {
        Debug.Log($"[GridSystem] === ДАМП РЕЕСТРА: {occupiedCells.Count} занятых ячеек ===");

        foreach (KeyValuePair<Vector2Int, BaseCounter> kvp in occupiedCells)
        {
            if (kvp.Value == null)
            {
                Debug.Log($"  {kvp.Key} → <null> (уничтоженный объект — регистрация протухла)");
                continue;
            }

            int regs = CountRegistrations(kvp.Value);
            string marker = regs > 1 ? "  ← ФАНТОМ: у объекта " + regs + " регистраций!" : "";

            Debug.Log($"  {kvp.Key} → '{kvp.Value.gameObject.name}' (позиция {kvp.Value.transform.position}){marker}");
        }
    }

    /// <summary>
    /// ★ v1.5: чистка протухших записей — ячейки, ссылающиеся на уничтоженные объекты.
    /// Unity делает «== null» для Destroy-нутых объектов, но ключ в словаре остаётся.
    /// Вызывается через контекстное меню, на каждый кадр не вешаем.
    /// </summary>
    [ContextMenu("Purge Destroyed Entries")]
    public void PurgeDestroyedEntries()
    {
        List<Vector2Int> deadKeys = new List<Vector2Int>();

        foreach (KeyValuePair<Vector2Int, BaseCounter> kvp in occupiedCells)
        {
            if (kvp.Value == null) deadKeys.Add(kvp.Key);
        }

        foreach (Vector2Int key in deadKeys)
        {
            occupiedCells.Remove(key);
            Debug.LogWarning($"[GridSystem] Протухшая запись удалена: ячейка {key} ссылалась на уничтоженный объект.");
        }

        if (deadKeys.Count == 0)
        {
            Debug.Log("[GridSystem] Протухших записей нет.");
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

        // ★ v1.6: занятые рёбра стен — оранжевые планки по границам клеток.
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);

        foreach (KeyValuePair<WallEdgeKey, BaseCounter> wallKvp in occupiedWallEdges)
        {
            Vector3 edgeMid = GetWallEdgeMidpoint(wallKvp.Key);
            Vector3 edgeSize = wallKvp.Key.axis == WallEdgeAxis.AlongX
                ? new Vector3(cellSize, 0.06f, 0.12f)
                : new Vector3(0.12f, 0.06f, cellSize);

            Gizmos.DrawCube(edgeMid, edgeSize);
        }
    }
}
