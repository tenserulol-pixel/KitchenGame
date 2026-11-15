using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// ★ v1.7 (фича «Стены не только прямые»):
///
/// 1) КЛЕТОЧНАЯ МОДЕЛЬ: после BSP каждая комната — НАБОР КЛЕТОК (полиомино),
///    а не прямоугольник. Рёбра стен, дверные проёмы и мебель выводятся из
///    принадлежности клеток, поэтому любые ступени и вырезы согласованы
///    автоматически: ребро-стена появляется ровно между клетками разных
///    владельцев (и по контуру здания).
/// 2) ЛОМАНЫЕ ПЕРЕГОРОДКИ (toggle «Ломаные перегородки», сила — slider):
///    прямые прогоны общих стен смещаются сегментами по 1–4 клетки в ту или
///    иную комнату — появляются Г-, Т- и зигзаг-комнаты. Ступень ставится
///    только на прогоне из 5+ рёбер и так, чтобы хотя бы один кусок стены
///    остался ≥ 4 рёбер — правило дверей «2 клетки с отступом» не деградирует.
///    Каждая ступень проверяется: обе комнаты остаются связными и не меньше
///    2 клеток, иначе шаг откатывается. Клетки нижнего ряда (фасад со
///    входом) не трогаются — вход с улицы всегда целиком в одном зале.
/// 3) СРЕЗ УГЛОВ ЗДАНИЯ (toggle «Срезать углы здания»): ступенчатая
///    «диагональ» на углах внешнего контура — здание перестаёт быть
///    прямоугольным. Срез откатывается, если ломает связность комнаты.
/// 4) Двери: общие стены пары комнат могут быть НЕСКОЛЬКИМИ прогонами и
///    даже на обеих осях (следствие ступеней) — CarveDoors выбирает лучший
///    прогон: 2 клетки с отступом, затем 1 клетка с отступом, затем
///    fallback без отступа (только пары остовного дерева). Правила v1.4
///    сохранены; вход с улицы — по нижнему сегменту комнаты-владельца.
/// 5) Мебель: «станции вдоль стен» читают ФАКТИЧЕСКИЕ стены комнаты
///    (клетка + сторона, где за соседней клеткой чужая комната или улица) —
///    в ломаных комнатах станции стоят и вдоль ступеней. Столы: свободно
///    стоящие клетки проверяются по набору клеток комнаты.
/// 6) Детерминизм: rng расходуется в порядке BSP → срезы → ступени → двери →
///    мебель; тот же сид даёт то же здание. Планировки на прежних сидах
///    отличаются от v1.6 (новые этапы потребляют rng).
///
/// ★ v1.6 (фича «Двери параллельны стенам, уличная открыта»):
///
/// 1) ОРИЕНТАЦИЯ СПАВНА ПО ГЕОМЕТРИИ: раньше yaw считался по ручной настройке
///    оси, и при несовпадении с фактическим префабом полотно вставало
///    ПЕРПЕНДИКУЛЯРНО стене — это ломало отсчёт угла HingeJoint (ноль петли =
///    поза спавна: пружина и лимиты считаются от неё). Теперь ось полотна
///    снимается с геометрии префаба в identity-позе (длинная горизонтальная
///    сторона габаритов = полотно, знак центра = куда оно смотрит от пивота;
///    toggle «Ось створки автоматически», ручной выбор +X/+Z — override).
///    yaw подбирается так, чтобы ЗАКРЫТАЯ створка легла ВДОЛЬ проёма от
///    петли. Если пивот в ЦЕНТРЕ полотна — створка центрируется на проём.
/// 2) Межкомнатные двери стартуют ЗАКРЫТЫМИ (створка вдоль проёма), пружина
///    PhysicalDoor держит их, пока персонаж или клиент не откроет движением:
///    игрок толкает телом (BodyPusher на игроке), клиенты — через
///    CustomerDoorOpener, который генератор добавляет на створку, если его
///    нет в префабе. Прошли — пружина закрывает обратно.
/// 3) Уличная дверь стартует ОТКРЫТОЙ и ПАРАЛЛЕЛЬНА стене (toggle «Уличная
///    дверь всегда открыта»): полотно ставится в закрытую позу, затем
///    ПОВОРАЧИВАЕТСЯ на 180° вокруг петли — распахивается наружу, к улице,
///    и прижимается к внешней плоскости фасада (отступ: полтолщины стены +
///    габарит полотна + 2 см — ничего не пересекается). Дверь на петле,
///    НЕ «на роликах» (отвод полотна вдоль фасада отменён). Створка статична:
///    PhysicalDoor, HingeJoint, Rigidbody и CustomerDoorOpener снимаются —
///    проём свободен всегда. Toggle off — уличная закрыта, как межкомнатные.
/// 4) Интерфейс окна листается вертикальным скроллбаром (ползунок справа):
///    все настройки, кнопки и отчёт доступны прокруткой вниз при любой
///    высоте окна; минимальная высота окна снижена до 400 px.
///
/// ★ v1.4 (фича «Двери: 2 клетки, с дверью, не в упор к стене»):
///
/// 1) Проём между комнатами = 2 соседних ребра общей стены, с отступом не
///    меньше ОДНОГО ребра-стены от торцов — двери не прилегают к углам и
///    перпендикулярным стенам. Общая стена длиной 3 ребра → проём в 1 ребро
///    по центру. Короче 3 → пары вне дерева пропускаются; парам дерева
///    (нужна связность) ставится 1 ребро без отступа (fallback в отчёте).
/// 2) Вход с улицы — тоже 2 клетки в комнате, куда попадает середина здания
///    (ширина комнаты 3 → 1 клетка по центру), с отступом от углов.
/// 3) В КАЖДЫЙ проём ставится дверь: префаб створки с PhysicalDoor (поле
///    «Door prefab» в окне). Петля — в начальный угол проёма (меньшая
///    координата), створка ложится вдоль проёма; ось полотна берётся из
///    геометрии префаба (ручной выбор +X/+Z — override). Y берётся у
///    префаба. Двери живут под 'BSP Building/BSP Doors',
///    occupiedWallEdges не затрагиваются.
///
/// ★ v1.2 (фича «Кухня + станции и столы») — после стен генератор сам расставляет мебель:
///
/// 1) Одна комната становится КУХНЕЙ (самая большая / случайная / первая — выбор
///    в окне). КРОМЕ неё исключён зал у входа: комната, в которую ведёт вход
///    с улицы, всегда остаётся ЗАЛОМ — кухня выбирается из остальных. В кухню
///    ставятся СТАНЦИИ — префабы с BaseCounter из списка окна: сначала клетки
///    вдоль стен кухни лицом в комнату, затем любые свободные.
/// 2) В остальных комнатах (столовых) ставятся СТОЛЫ (префабы с DiningTable)
///    одной общей очередью на ВСЁ здание, а не по одному залу за раз:
///    предпочтение свободно стоящим клеткам (4 соседа свободны), поворот —
///    авторский поворот префаба (стулья настроены относительно стола).
/// 3) Проходы: клетки по обе стороны каждого дверного проёма не занимаются
///    (toggle «Проходы у дверей свободны»).
/// 4) Занятость учитывается: клетки уже стоящих в сцене станций/столов
///    (BaseCounter без WallPiece) блокируются; стены клетки не занимают.
/// 5) В рантайме всё работает само: BaseCounter.Awake регистрирует каждую
///    станцию/стол в occupiedCells (RegisterCounterAtCurrentPosition), а
///    FurnitureSaveUtility сохраняет/восстанавливает их как авторскую мебель.
/// 6) Детерминизм: мебель потребляет rng СТРОГО после стен и дверей — тот же
///    seed и те же настройки стен дают то же здание при любых списках мебели.
/// 7) «Лицо» префаба станции задаётся в окне (+Z/+X/-Z/-X): итоговый yaw =
///    yaw стороны (лицом в комнату) − смещение фронта. По умолчанию +Z.
///
/// ★ v1.1 (фича «Стены поворачиваются при генерации») — исправлены повороты стен:
///
/// 1) ГЛАВНОЕ: поворот больше НЕ ТРЕБУЕТ BaseCounter. v1.0 вычислял yaw только
///    при наличии BaseCounter на корне префаба («counter == null → yaw 0»),
///    а после перехода стен на чисто редакторский формат (WallPiece v1.1,
///    BaseCounter удалён со стены) все стены ложились БЕЗ ПОВОРОТА: вдоль
///    X-рёбер случайно правильно, вдоль Z-рёбер — поперёк. Теперь якорь
///    геометрии — WallPiece (Component-API v1.1), работает с префабом любого
///    состава.
/// 2) yaw сохраняет «четверть» поворота префаба: инстансу ставится текущий
///    поворот (кратный 90), а +90 добавляется ТОЛЬКО когда длинная ось смотрит
///    не вдоль ребра. v1.0 возвращал абсолютные 0/90 — у префабов с поворотом
///    90/270 оси инвертировались. Та же семантика, что во встроенном
///    FurnitureMovingController.GetWallAlignedYaw.
/// 3) Рёбра, занятые УЖЕ стоящими стенами (блокировка двойной постановки),
///    теперь ищутся по WallPiece без BaseCounter — локальное зеркало
///    GridPositioningSystem.DeriveWallEdgeFromCurrentPosition (та же математика,
///    что в GridSnapEditor v2.5).
/// 4) В отчёте и логе — разбивка поставленных стен по осям (вдоль X / вдоль Z).
///
/// ★ v1.0 (фича «Автогенерация здания из стен, BSP») — Editor-окно для быстрой постройки
/// здания с комнатами ИЗ ТВОИХ ЖЕ СТЕНОВЫХ ПРЕФАБОВ.
///
/// ЗАЧЕМ: расставлять сегменты стен по одному долго. Окно режет прямоугольник здания
/// BSP-алгоритмом на комнаты, переводит стены комнат в рёбра сетки (WallEdgeKey),
/// прорубает дверные проёмы и расставляет экземпляры префаба стены точно на рёбра —
/// тем же чисто геометрическим способом, что и GridSnapEditor.
///
/// КАК ПОЛЬЗОВАТЬСЯ:
/// 1) Открой ИГРОВУЮ сцену с GridPositioningSystem.
/// 2) Tools → Building Generator (BSP).
/// 3) Перетащи ПРЕФАБ стены (корень с WallPiece; BaseCounter не нужен) в поле Wall prefab.
/// 4) Задай габариты здания в клетках, минимальную/максимальную комнату, сид.
/// 5) Префаб СТВОРКИ с PhysicalDoor — в поле Door prefab. Ось полотна
///    определяется по геометрии префаба (или вручную +X/+Z). Межкомнатные
///    двери закрыты; уличная распахнута наружу и прижата к фасаду (toggle
///    «Уличная дверь всегда открыта»).
/// 6) «Сгенерировать здание» — стены появятся под корнем 'BSP Building'.
///    «Снести сгенерированное» — удаляет корень со всеми стенами.
///
/// КАК РАБОТАЕТ:
/// - BSP: прямоугольник здания рекурсивно режется на комнаты, пока комната больше
///   минимальной; случайный «ранний стоп» оставляет крупные залы.
/// - Рёбра: периметры всех комнат складываются в HashSet — общие стены смежных
///   комнат дедуплицируются автоматически (одно ребро = одна стена).
/// - Двери: строится граф смежности комнат (пары комнат с общей стеной),
///   BFS со случайным порядком соседей строит остовное дерево — на каждом его
///   ребре прорубается проём: 2 клетки с отступом ≥ 1 ребра от торцов стены
///   (v1.4). Ровно (комнат - 1) проёмов дают ГАРАНТИРОВАННУЮ связность всех
///   комнат (проверено симуляцией на 10 800 раскладках). Шанс доп. проёма
///   добавляет проходы между соседями вне дерева. В каждый проём ставится
///   створка PhysicalDoor. «Вход с улицы» — 2 клетки на южной стороне в
///   комнате, куда попадает середина здания (или чуть левее центра).
/// - Постановка: инстанс префаба ставится геометрией (центр WallPiece-геометрии на
///   середину ребра, длинная ось вдоль ребра) — БЕЗ TryPlaceWall и БЕЗ записи в
///   occupiedWallEdges. В edit mode реестры сетки и так пусты, а запись туда из
///   редактора оставляла бы фантомные рёбра после ручного удаления стен.
///
/// ОГРАНИЧЕНИЯ v1.0:
/// - проём: с v1.4 — 2 клетки с отступом от торцов (fallback 1 клетка);
/// - рёбра, занятые УЖЕ стоящими в сцене стенами, пропускаются (счётчик в отчёте);
/// - мебель в клетках не проверяется — стена может визуально пересечь мебель;
/// - здание ставится в сцену как обычные объекты и сохраняется вместе с ней.
/// </summary>
public class BuildingGeneratorWindow : EditorWindow
{
    private const string BuildingRootName = "BSP Building";

    // --- Настройки окна ---
    private GameObject wallPrefab;

    // v1.4: двери — префаб СТВОРКИ с PhysicalDoor (Rigidbody/HingeJoint
    // добавятся скриптом сами). [SerializeField] — переживает перекомпиляцию.
    [SerializeField] private GameObject doorPrefab = null;
    [SerializeField] private DoorLeafAxis doorLeafAxis = DoorLeafAxis.PlusZ;

    // v1.6: уличная створка распахивается на 180° вокруг петли и прижимается
    // СНАРУЖИ к фасаду — параллельна стене, проём свободен всегда. Дверь на
    // петле, не «на роликах» (отвод полотна вдоль фасада отменён).
    // Межкомнатные двери при этом закрыты, пока их не толкнут телом.
    [SerializeField] private bool streetDoorAlwaysOpen = true;

    // v1.6: ось полотна в префабе определяется по его геометрии; ручной
    // выбор (+X/+Z) — override для нестандартных префабов. Спавн по ручной
    // оси без сверки с префабом давал полотно перпендикулярно стене.
    [SerializeField] private bool autoDetectLeafAxis = true;

    private int seed = 12345;
    private Vector2Int buildingCells = new Vector2Int(10, 8);
    private Vector2Int buildingOffset = Vector2Int.zero;
    private int minRoomCells = 3;
    private int maxRoomCells = 6;
    private float earlyStopChance = 0.1f;
    private bool buildInteriorWalls = true;
    private float extraDoorChance = 0.25f;
    private bool streetEntrance = true;

    // v1.7: ломаные стены. Прямые прогоны перегородок смещаются сегментами —
    // комнаты перестают быть прямоугольными (Г-, Т- и зигзаг-формы). Нижний
    // ряд (фасад со входом) джиттером не трогается. [SerializeField] —
    // настройки переживают перекомпиляцию домена Unity.
    [SerializeField] private bool jaggedWalls = true;
    [SerializeField] private float jaggedStrength = 0.4f;

    // v1.7: ступенчатый срез углов внешнего контура — здание не прямоугольное.
    [SerializeField] private bool cutCorners = false;

    private string lastReport = "";

    // v1.6: прокрутка содержимого окна — настройки и отчёт не всегда
    // помещаются на экране целиком. [SerializeField] — позиция скролла
    // переживает перекомпиляцию домена Unity.
    [SerializeField] private Vector2 guiScroll = Vector2.zero;

    // --- v1.2: станции и столы ---
    // [SerializeField] — чтобы настройки переживали перекомпиляцию домена Unity
    // (EditorWindow сериализует приватные поля только с атрибутом).
    [SerializeField] private bool placeFurniture = true;
    [SerializeField] private KitchenPickMode kitchenMode = KitchenPickMode.LargestRoom;
    [SerializeField] private PrefabFront stationFront = PrefabFront.PlusZ;
    [SerializeField] private bool stationsAlongWalls = true;
    [SerializeField] private bool keepDoorCellsFree = true;
    [SerializeField] private List<FurnitureEntry> stationEntries = new List<FurnitureEntry>();
    [SerializeField] private List<FurnitureEntry> tableEntries = new List<FurnitureEntry>();

    private static readonly string[] KitchenModeLabels =
        { "Самая большая комната", "Случайная комната", "Первая комната" };

    private static readonly string[] PrefabFrontLabels =
        { "+Z (лицо вперед)", "+X (лицо вправо)", "-Z (лицо назад)", "-X (лицо влево)" };

    /// <summary>Строка списка «префаб + сколько поставить».</summary>
    [System.Serializable]
    private class FurnitureEntry
    {
        public GameObject prefab;
        public int count = 1;
    }

    /// <summary>Как выбирается комната-кухня.</summary>
    private enum KitchenPickMode
    {
        LargestRoom = 0,
        RandomRoom = 1,
        FirstRoom = 2
    }

    /// <summary>
    /// v1.4: вдоль какой ЛОКАЛЬНОЙ оси тянется полотно двери в префабе при
    /// yaw 0 (ручной override; по умолчанию ось определяется по геометрии —
    /// autoDetectLeafAxis). Пивот створки должен стоять на линии петель
    /// (требование PhysicalDoor) — генератор ставит пивот в начальный угол
    /// проёма, а при пивоте в центре полотна центрирует створку на проём.
    /// </summary>
    private enum DoorLeafAxis
    {
        PlusX = 0,
        PlusZ = 1
    }

    private static readonly string[] DoorLeafAxisLabels = { "Вдоль +X", "Вдоль +Z" };

    /// <summary>Где у префаба станции его «лицо» (фронт, обращённый к игроку).</summary>
    private enum PrefabFront
    {
        PlusZ = 0,
        PlusX = 1,
        MinusZ = 2,
        MinusX = 3
    }

    /// <summary>Комната BSP в клеточных координатах сетки (глобальных, с учётом смещения).</summary>
    private struct RoomRect
    {
        public int x;
        public int z;
        public int w;
        public int h;
    }

    [MenuItem("Tools/Building Generator (BSP)")]
    private static void Open()
    {
        BuildingGeneratorWindow window = GetWindow<BuildingGeneratorWindow>("BSP Building");

        // v1.6: окно можно сделать ниже — содержимое листается скроллом.
        window.minSize = new Vector2(340f, 400f);
    }

    // ============================================================
    //  GUI
    // ============================================================

    private void OnGUI()
    {
        // v1.6: всё содержимое окна — в вертикальном скролле: на невысоких
        // экранах настройки, кнопки и отчёт доступны прокруткой вниз.
        guiScroll = GUILayout.BeginScrollView(
            guiScroll, false, true, GUILayout.ExpandHeight(true));

        GridPositioningSystem grid = FindActiveGrid();

        if (grid == null)
        {
            EditorGUILayout.HelpBox(
                "GridPositioningSystem не найден в сцене — генератор ставит стены на рёбра сетки. " +
                "Открой игровую сцену с сеткой и попробуй снова.", MessageType.Error);
        }
        else
        {
            Vector2Int bounds = grid.GetGridBounds();
            float cell = grid.GetCellSize();

            EditorGUILayout.HelpBox(
                "Сетка: '" + grid.gameObject.name + "', клетка " + cell + " м, границы " +
                bounds.x + "x" + bounds.y + " клеток (" +
                (bounds.x * cell) + "x" + (bounds.y * cell) + " м). " +
                "Здание должно влезать в эти границы.", MessageType.Info);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Префаб стены", EditorStyles.boldLabel);
        wallPrefab = (GameObject)EditorGUILayout.ObjectField(
            "Wall prefab", wallPrefab, typeof(GameObject), false);

        if (wallPrefab != null && wallPrefab.GetComponent<WallPiece>() == null)
        {
            EditorGUILayout.HelpBox(
                "На корне префаба нет WallPiece — постановка на рёбра и поворот не сработают. " +
                "Повесь WallPiece на КОРЕНЬ префаба.", MessageType.Warning);
        }

        if (wallPrefab != null && wallPrefab.GetComponent<WallPiece>() != null)
        {
            EditorGUILayout.HelpBox(
                "Каждая стена автоматически поворачивается длинной осью вдоль своего ребра " +
                "(вдоль X или вдоль Z). BaseCounter на стене не требуется — стены теперь " +
                "чисто редакторские объекты (WallPiece v1.1).", MessageType.None);
        }

        if (wallPrefab != null && !EditorUtility.IsPersistent(wallPrefab))
        {
            EditorGUILayout.HelpBox(
                "В поле объект СЦЕНЫ, а не ассет. Перетащи сюда ПРЕФАБ из окна Project.",
                MessageType.Warning);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Двери", EditorStyles.boldLabel);
        doorPrefab = (GameObject)EditorGUILayout.ObjectField(
            "Door prefab (PhysicalDoor)", doorPrefab, typeof(GameObject), false);
        autoDetectLeafAxis = EditorGUILayout.Toggle(
            "Ось створки автоматически", autoDetectLeafAxis);

        if (!autoDetectLeafAxis)
        {
            doorLeafAxis = (DoorLeafAxis)EditorGUILayout.Popup(
                "Ось полотна в префабе", (int)doorLeafAxis, DoorLeafAxisLabels);
        }

        streetDoorAlwaysOpen = EditorGUILayout.Toggle(
            "Уличная дверь всегда открыта", streetDoorAlwaysOpen);

        if (doorPrefab != null && doorPrefab.GetComponent<PhysicalDoor>() == null)
        {
            EditorGUILayout.HelpBox(
                "«" + doorPrefab.name + "»: на корне нет PhysicalDoor — створка не будет " +
                "физической (игроки и клиенты её не толкнут). Поставится как декорация.",
                MessageType.Warning);
        }

        // v1.6: подсказка про ориентацию и состояния дверей.
        EditorGUILayout.HelpBox(
            "Створки спавнятся ПАРАЛЛЕЛЬНО стенам: ось полотна снимается с " +
            "геометрии префаба (или задана вручную), закрытая створка ложится " +
            "вдоль проёма. Межкомнатные двери закрыты, пока их не толкнут " +
            "движением: игрок телом (BodyPusher на игроке), клиенты через " +
            "CustomerDoorOpener — он добавится на створку сам. Уличная дверь: " +
            (streetDoorAlwaysOpen
                ? "распахнута наружу на 180° и прижата к фасаду (параллельна стене), проём свободен."
                : "закрыта, как межкомнатные."),
            MessageType.None);

        if (doorPrefab != null && !EditorUtility.IsPersistent(doorPrefab))
        {
            EditorGUILayout.HelpBox(
                "В поле объект СЦЕНЫ, а не ассет. Перетащи сюда ПРЕФАБ двери из окна Project.",
                MessageType.Warning);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Габариты здания (в клетках)", EditorStyles.boldLabel);
        buildingCells = EditorGUILayout.Vector2IntField("Размер (X x Z)", buildingCells);
        buildingOffset = EditorGUILayout.Vector2IntField("Смещение от угла сетки", buildingOffset);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("BSP-настройки", EditorStyles.boldLabel);
        minRoomCells = EditorGUILayout.IntField("Мин. комната (клеток)", minRoomCells);
        maxRoomCells = EditorGUILayout.IntField("Макс. комната (клеток)", maxRoomCells);
        earlyStopChance = EditorGUILayout.Slider("Шанс раннего стопа", earlyStopChance, 0f, 1f);

        EditorGUILayout.Space();
        buildInteriorWalls = EditorGUILayout.Toggle("Внутренние перегородки", buildInteriorWalls);

        EditorGUI.BeginDisabledGroup(!buildInteriorWalls);
        extraDoorChance = EditorGUILayout.Slider(
            "Шанс доп. проёма между соседями", extraDoorChance, 0f, 1f);
        EditorGUI.EndDisabledGroup();

        // v1.7: форма стен — ломаные перегородки и срез углов.
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Форма стен (v1.7)", EditorStyles.boldLabel);

        EditorGUI.BeginDisabledGroup(!buildInteriorWalls);
        jaggedWalls = EditorGUILayout.Toggle("Ломаные перегородки (ступени)", jaggedWalls);

        if (jaggedWalls)
        {
            jaggedStrength = EditorGUILayout.Slider("Сила неровности", jaggedStrength, 0.05f, 1f);
        }

        EditorGUI.EndDisabledGroup();

        cutCorners = EditorGUILayout.Toggle("Срезать углы здания", cutCorners);

        EditorGUILayout.HelpBox(
            "Ломаные перегородки смещают сегменты общих стен на 1–4 клетки: " +
            "комнаты становятся Г-, Т- и зигзаг-образными, а стены — ступенчатыми. " +
            "Срез углов снимает ступенчатую диагональ с внешнего контура здания. " +
            "Каждая ступень и срез проверяют связность комнат и откатываются, " +
            "если её ломают; фасад со входом остаётся цельным.", MessageType.None);

        streetEntrance = EditorGUILayout.Toggle("Вход с улицы", streetEntrance);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Сид", EditorStyles.boldLabel);
        seed = EditorGUILayout.IntField("Seed", seed);

        if (GUILayout.Button("Случайный сид"))
        {
            seed = Random.Range(0, int.MaxValue);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Станции и столы", EditorStyles.boldLabel);
        placeFurniture = EditorGUILayout.Toggle("Расставлять станции и столы", placeFurniture);

        EditorGUI.BeginDisabledGroup(!placeFurniture);

        kitchenMode = (KitchenPickMode)EditorGUILayout.Popup(
            "Комната-кухня", (int)kitchenMode, KitchenModeLabels);
        stationFront = (PrefabFront)EditorGUILayout.Popup(
            "«Лицо» префаба станции", (int)stationFront, PrefabFrontLabels);
        stationsAlongWalls = EditorGUILayout.Toggle("Станции вдоль стен кухни", stationsAlongWalls);
        keepDoorCellsFree = EditorGUILayout.Toggle("Проходы у дверей свободны", keepDoorCellsFree);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Станции (кухня)", EditorStyles.miniBoldLabel);
        DrawEntryList(stationEntries, "станцию");

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Столы (всё здание, кроме кухни)", EditorStyles.miniBoldLabel);
        DrawEntryList(tableEntries, "стол");

        // Мгновенная обратная связь по составу списков.
        foreach (FurnitureEntry entry in stationEntries)
        {
            if (entry == null || entry.prefab == null) continue;

            if (entry.prefab.GetComponent<BaseCounter>() == null)
            {
                EditorGUILayout.HelpBox(
                    "«" + entry.prefab.name + "»: на корне нет BaseCounter — станция не " +
                    "зарегистрируется в сетке при старте. Генерация отменится.", MessageType.Error);
            }
        }

        foreach (FurnitureEntry entry in tableEntries)
        {
            if (entry == null || entry.prefab == null) continue;

            if (entry.prefab.GetComponent<DiningTable>() == null)
            {
                EditorGUILayout.HelpBox(
                    "«" + entry.prefab.name + "»: на корне нет DiningTable — клиенты не смогут " +
                    "сесть за него. Генерация отменится.", MessageType.Error);
            }
        }

        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space(12f);

        EditorGUI.BeginDisabledGroup(grid == null);
        if (GUILayout.Button("Сгенерировать здание", GUILayout.Height(32f)))
        {
            Generate();
        }
        EditorGUI.EndDisabledGroup();

        if (GUILayout.Button("Снести сгенерированное"))
        {
            ClearGenerated();
        }

        if (!string.IsNullOrEmpty(lastReport))
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(lastReport, MessageType.Info);
        }

        GUILayout.EndScrollView();
    }

    // ============================================================
    //  ГЕНЕРАЦИЯ
    // ============================================================

    private void Generate()
    {
        GridPositioningSystem grid = FindActiveGrid();

        if (grid == null)
        {
            Debug.LogError("[BuildingGen] Сетка не найдена — генерация отменена.");
            return;
        }

        if (wallPrefab == null)
        {
            Debug.LogError("[BuildingGen] Не задан префаб стены — генерация отменена.");
            return;
        }

        if (!EditorUtility.IsPersistent(wallPrefab))
        {
            Debug.LogError("[BuildingGen] В поле должен лежать ПРЕФАБ из окна Project, " +
                           "а не объект сцены — генерация отменена.");
            return;
        }

        if (wallPrefab.GetComponent<WallPiece>() == null)
        {
            Debug.LogError("[BuildingGen] На корне префаба стены нет WallPiece — генерация отменена.");
            return;
        }

        // v1.1: BaseCounter не проверяется — генерации он не нужен (WallPiece v1.1).

        // v1.2: префабы станций и столов — с нужными компонентами на корне.
        foreach (FurnitureEntry entry in stationEntries)
        {
            if (entry == null || entry.prefab == null) continue;

            if (!EditorUtility.IsPersistent(entry.prefab))
            {
                Debug.LogError("[BuildingGen] Префаб станции '" + entry.prefab.name +
                               "' — объект СЦЕНЫ, а не ассет. Перетащи ПРЕФАБ из окна " +
                               "Project — генерация отменена.");
                return;
            }

            if (entry.prefab.GetComponent<BaseCounter>() == null)
            {
                Debug.LogError("[BuildingGen] Префаб станции '" + entry.prefab.name +
                               "' без BaseCounter на корне — при старте он не зарегистрируется " +
                               "в сетке. Генерация отменена.");
                return;
            }
        }

        foreach (FurnitureEntry entry in tableEntries)
        {
            if (entry == null || entry.prefab == null) continue;

            if (!EditorUtility.IsPersistent(entry.prefab))
            {
                Debug.LogError("[BuildingGen] Префаб стола '" + entry.prefab.name +
                               "' — объект СЦЕНЫ, а не ассет. Перетащи ПРЕФАБ из окна " +
                               "Project — генерация отменена.");
                return;
            }

            if (entry.prefab.GetComponent<DiningTable>() == null)
            {
                Debug.LogError("[BuildingGen] Префаб стола '" + entry.prefab.name +
                               "' без DiningTable на корне — клиенты не смогут сесть за него. " +
                               "Генерация отменена.");
                return;
            }
        }

        if (minRoomCells < 2) minRoomCells = 2;
        if (maxRoomCells < minRoomCells) maxRoomCells = minRoomCells;

        Vector2Int gridBounds = grid.GetGridBounds();

        if (buildingCells.x < minRoomCells || buildingCells.y < minRoomCells)
        {
            Debug.LogError("[BuildingGen] Здание меньше минимальной комнаты (" + minRoomCells +
                           " клеток) — увеличь габариты здания или уменьши мин. комнату.");
            return;
        }

        if (buildingOffset.x < 0 || buildingOffset.y < 0 ||
            buildingOffset.x + buildingCells.x > gridBounds.x ||
            buildingOffset.y + buildingCells.y > gridBounds.y)
        {
            Debug.LogError("[BuildingGen] Здание " + buildingCells.x + "x" + buildingCells.y +
                           " клеток со смещением " + buildingOffset + " не влезает в сетку " +
                           gridBounds.x + "x" + gridBounds.y + " клеток. Уменьши здание или смещение.");
            return;
        }

        System.Random rng = new System.Random(seed);

        // 1) BSP: режем прямоугольник здания на комнаты. Прямоугольные листы —
        //    только ОСНОВА: дальше каждая комната превращается в набор клеток.
        RoomRect building = new RoomRect();
        building.x = buildingOffset.x;
        building.z = buildingOffset.y;
        building.w = buildingCells.x;
        building.h = buildingCells.y;

        List<RoomRect> rooms = new List<RoomRect>();
        SplitRoom(building, rng, rooms);

        // 2) v1.7: клеточная модель — каждой клетке здания назначен владелец
        //    (комната). Ступени, стены, двери и мебель работают с клетками.
        List<HashSet<Vector2Int>> roomCells;
        Dictionary<Vector2Int, int> cellOwner;
        HashSet<Vector2Int> buildingCellSet;
        BuildCellModel(rooms, out roomCells, out cellOwner, out buildingCellSet);

        // 2a) v1.7: ступенчатый срез углов внешнего контура — до джиттера,
        //     чтобы срез не конфликтовал со сдвигами перегородок.
        int cornerCuts = 0;

        if (cutCorners)
        {
            cornerCuts = CutBuildingCorners(building, roomCells, cellOwner, buildingCellSet, rng);
        }

        // 2b) v1.7: ломаные перегородки — сегменты общих стен смещаются в
        //     соседние комнаты (ступени по 1–4 клетки), связность проверяется.
        int jitterSteps = 0;

        if (jaggedWalls && buildInteriorWalls)
        {
            jitterSteps = ApplyWallJitter(building, roomCells, cellOwner, buildingCellSet, rng);
        }

        // 3) Рёбра стен из клеточной модели: ребро живёт между клетками разных
        //    владельцев (перегородки) и по контуру здания. HashSet дедуплицирует
        //    рёбра, полученные с обеих сторон границы.
        HashSet<GridPositioningSystem.WallEdgeKey> wallSet = DeriveWallEdges(
            buildingCellSet, cellOwner, buildInteriorWalls);

        // 4) Дверные проёмы.
        HashSet<GridPositioningSystem.WallEdgeKey> doorSet =
            new HashSet<GridPositioningSystem.WallEdgeKey>();

        // v1.4: статистика для отчёта — в ОТКРЫТИЯХ (по 2 ребра), не в рёбрах.
        int openingsTwo = 0;
        int openingsOne = 0;
        int flushFallbacks = 0;

        if (buildInteriorWalls)
        {
            BuildRoomDoors(roomCells, cellOwner, buildingCellSet, rng, extraDoorChance,
                wallSet, doorSet, out openingsTwo, out openingsOne, out flushFallbacks);
        }

        // v1.3: вход с улицы на нижней стороне здания. Его комната всегда
        // становится ЗАЛОМ (столовой) — кухня выбирается из остальных.
        // v1.4: вход шириной 2 клетки, с отступом ≥ 1 ребра от торцов
        // нижнего СЕГМЕНТА комнаты (не «в упор» к углам и перегородкам).
        // v1.7: сегмент и владелец читаются из клеточной модели — срезы углов
        // и ступени перегородок вход не ломают (нижний ряд джиттером не трогается).
        Vector2Int? entranceCell = null;

        if (streetEntrance)
        {
            entranceCell = CarveStreetEntrance(building, cellOwner, wallSet, doorSet,
                out int streetTwo, out int streetOne, out int streetFlush);

            openingsTwo += streetTwo;
            openingsOne += streetOne;
            flushFallbacks += streetFlush;
        }

        // 4) Сносим предыдущее здание (повторный запуск = перегенерация).
        GameObject previousRoot = FindBuildingRoot();

        if (previousRoot != null)
        {
            Undo.DestroyObjectImmediate(previousRoot);
        }

        // 5) Занятые рёбра УЖЕ стоящих стен — туда ничего не ставим.
        HashSet<GridPositioningSystem.WallEdgeKey> existingEdges = CollectExistingWallEdges(grid);

        GameObject root = new GameObject(BuildingRootName);
        Undo.RegisterCreatedObjectUndo(root, "BSP Building");

        // Сортировка — стабильный порядок в иерархии при одном и том же сиде.
        List<GridPositioningSystem.WallEdgeKey> ordered = new List<GridPositioningSystem.WallEdgeKey>(wallSet);
        ordered.Sort(CompareWallEdges);

        // v1.1: разбивка по осям — в отчёте видно, что повороты расставились.
        int placed = 0;
        int skipped = 0;
        int placedAlongX = 0;
        int placedAlongZ = 0;

        try
        {
            for (int i = 0; i < ordered.Count; i++)
            {
                GridPositioningSystem.WallEdgeKey edge = ordered[i];

                if (existingEdges.Contains(edge))
                {
                    skipped++;
                    continue;
                }

                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(wallPrefab);
                Undo.RegisterCreatedObjectUndo(instance, "BSP wall");
                instance.transform.SetParent(root.transform, true);

                PlaceWallOnEdge(grid, instance, edge);
                placed++;

                if (edge.axis == GridPositioningSystem.WallEdgeAxis.AlongX) placedAlongX++;
                else placedAlongZ++;

                if ((i & 3) == 0)
                {
                    EditorUtility.DisplayProgressBar(
                        "BSP Building",
                        "Стена " + (i + 1) + " / " + ordered.Count,
                        (float)(i + 1) / ordered.Count);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        // v1.4: двери-створки в каждый проём (после стен, до мебели).
        // v1.6: все створки параллельны стенам; уличная распахнута наружу
        // и прижата к фасаду, межкомнатные — закрыты.
        int doorsPlaced = 0;
        bool streetDoorOpen = false;

        if (doorPrefab != null && doorSet.Count > 0)
        {
            doorsPlaced = PlaceDoors(grid, root, doorSet, building, entranceCell,
                out streetDoorOpen);
        }

        // v1.2: комната-кухня + станции и столы (после стен, до пометки сцены dirty).
        int kitchenIndex = -1;
        int entranceRoomIndex = -1;
        int stationsPlaced = 0;
        int stationsMissing = 0;
        int tablesPlaced = 0;
        int tablesMissing = 0;

        if (placeFurniture)
        {
            PlaceFurniture(grid, root, roomCells, buildingCellSet, cellOwner,
                doorSet, entranceCell, rng,
                out kitchenIndex, out entranceRoomIndex, out stationsPlaced,
                out stationsMissing, out tablesPlaced, out tablesMissing);
        }

        EditorSceneManager.MarkSceneDirty(root.scene);

        // v1.7: форма стен — в отчёте видно, сколько ступеней и срезов встало.
        string shapeReport = "";

        if (jitterSteps > 0)
        {
            shapeReport += " Ступеней в перегородках: " + jitterSteps + ".";
        }

        if (cornerCuts > 0)
        {
            shapeReport += " Срезано углов: " + cornerCuts + ".";
        }

        lastReport = "Комнат: " + rooms.Count + ". Стен поставлено: " + placed +
                     " (вдоль X: " + placedAlongX + ", вдоль Z: " + placedAlongZ + ")." +
                     shapeReport +
                     " Проёмов: " + (openingsTwo + openingsOne) +
                     " (" + doorSet.Count + " ребер; 2-клеточных: " + openingsTwo +
                     (openingsOne > 0 ? ", 1-клеточных: " + openingsOne : "") +
                     (flushFallbacks > 0 ? ", без отступа от стены: " + flushFallbacks : "") +
                     ")" +
                     (doorPrefab != null
                         ? ". Дверей поставлено: " + doorsPlaced +
                           (streetEntrance
                               ? (streetDoorOpen
                                   ? " (уличная — распахнута наружу, прижата к фасаду)"
                                   : " (уличная — закрыта как остальные)")
                               : "")
                         : ". Двери не ставились: префаб двери не задан") +
                     ". Пропущено занятых рёбер: " + skipped + ".";

        string furnitureReport = "";

        if (placeFurniture)
        {
            // v1.7: комнаты — полиомино, размер кухни — числом клеток.
            int kitchenCellCount = kitchenIndex >= 0 && kitchenIndex < roomCells.Count
                ? roomCells[kitchenIndex].Count
                : buildingCellSet.Count;

            furnitureReport = (entranceRoomIndex >= 0
                               ? "Зал у входа: комната №" + (entranceRoomIndex + 1) +
                                 " (кухня — не она). "
                               : "") +
                              "Кухня: " +
                              (kitchenIndex >= 0 ? "комната №" + (kitchenIndex + 1) : "всё здание") +
                              " (" + kitchenCellCount + " клеток), станций: " +
                              stationsPlaced +
                              (stationsMissing > 0 ? " (не хватило мест: " + stationsMissing + ")" : "") +
                              ", столов: " + tablesPlaced +
                              (tablesMissing > 0 ? " (не хватило мест: " + tablesMissing + ")" : "") + ".";

            lastReport += "\n" + furnitureReport;
        }

        Debug.Log("[BuildingGen] Сид " + seed + ": комнат " + rooms.Count +
                  ", стен поставлено " + placed + " (вдоль X " + placedAlongX +
                  ", вдоль Z " + placedAlongZ + "), проёмов " +
                  (openingsTwo + openingsOne) + " (дверей " + doorsPlaced + ")" +
                  ", пропущено занятых рёбер " + skipped + ". " +
                  (placeFurniture ? furnitureReport + " " : "") +
                  "Результат — под корнем '" + BuildingRootName + "'.", root);
    }

    /// <summary>Удаляет корень 'BSP Building' со всеми сгенерированными стенами.</summary>
    private void ClearGenerated()
    {
        GameObject root = FindBuildingRoot();

        if (root == null)
        {
            lastReport = "Сгенерированное здание не найдено (корень '" + BuildingRootName + "' отсутствует).";
            return;
        }

        Scene scene = root.gameObject.scene;

        // v1.2: стены и мебель считаем отдельно (мебель = BaseCounter без WallPiece).
        int wallCount = root.GetComponentsInChildren<WallPiece>(true).Length;

        int furnitureCount = 0;

        BaseCounter[] counters = root.GetComponentsInChildren<BaseCounter>(true);

        foreach (BaseCounter counter in counters)
        {
            if (!WallPiece.IsWall(counter)) furnitureCount++;
        }

        Undo.DestroyObjectImmediate(root);
        EditorSceneManager.MarkSceneDirty(scene);

        lastReport = "Снесено: стен " + wallCount + ", станций/столов " + furnitureCount +
                     " (корень '" + BuildingRootName + "').";
        Debug.Log("[BuildingGen] " + lastReport);
    }

    // ============================================================
    //  BSP: РАЗБИЕНИЕ НА КОМНАТЫ
    // ============================================================

    /// <summary>
    /// Рекурсивное BSP-разбиение: комната режется, пока её сторона позволяет
    /// отложить minRoomCells с обеих сторон разреза. Комнаты не пересекаются
    /// и вместе покрывают весь прямоугольник здания.
    /// </summary>
    private void SplitRoom(RoomRect room, System.Random rng, List<RoomRect> output)
    {
        bool canSplitV = room.w >= minRoomCells * 2;
        bool canSplitH = room.h >= minRoomCells * 2;
        bool smallEnough = room.w <= maxRoomCells && room.h <= maxRoomCells;

        if ((smallEnough && rng.NextDouble() < earlyStopChance) || (!canSplitV && !canSplitH))
        {
            output.Add(room);
            return;
        }

        bool splitV;

        if (canSplitV && canSplitH)
        {
            // Режем длинную сторону; при равенстве — случайно.
            splitV = room.w > room.h || (room.w == room.h && rng.Next(2) == 0);
        }
        else
        {
            splitV = canSplitV;
        }

        if (splitV)
        {
            int cut = rng.Next(minRoomCells, room.w - minRoomCells + 1);

            RoomRect left = room;
            left.w = cut;

            RoomRect right = room;
            right.x = room.x + cut;
            right.w = room.w - cut;

            SplitRoom(left, rng, output);
            SplitRoom(right, rng, output);
        }
        else
        {
            int cut = rng.Next(minRoomCells, room.h - minRoomCells + 1);

            RoomRect bottom = room;
            bottom.h = cut;

            RoomRect top = room;
            top.z = room.z + cut;
            top.h = room.h - cut;

            SplitRoom(bottom, rng, output);
            SplitRoom(top, rng, output);
        }
    }

    // ============================================================
    //  v1.7: КЛЕТОЧНАЯ МОДЕЛЬ (комнаты — полиомино, не прямоугольники)
    // ============================================================

    /// <summary>
    /// Раскладывает BSP-комнаты по клеткам: cellOwner[(x, z)] = индекс комнаты,
    /// roomCells[i] — набор клеток комнаты, buildingCellSet — все клетки здания.
    /// BSP-комнаты не пересекаются, поэтому владельцы назначаются без конфликтов.
    /// rng не тратится.
    /// </summary>
    private static void BuildCellModel(
        List<RoomRect> rooms,
        out List<HashSet<Vector2Int>> roomCells,
        out Dictionary<Vector2Int, int> cellOwner,
        out HashSet<Vector2Int> buildingCellSet)
    {
        roomCells = new List<HashSet<Vector2Int>>(rooms.Count);

        for (int i = 0; i < rooms.Count; i++)
        {
            roomCells.Add(new HashSet<Vector2Int>());
        }

        cellOwner = new Dictionary<Vector2Int, int>();
        buildingCellSet = new HashSet<Vector2Int>();

        for (int i = 0; i < rooms.Count; i++)
        {
            foreach (Vector2Int cell in EnumerateRectCells(rooms[i]))
            {
                roomCells[i].Add(cell);
                cellOwner[cell] = i;
                buildingCellSet.Add(cell);
            }
        }
    }

    /// <summary>Клетки прямоугольника (слева направо, снизу вверх — детерминированно).</summary>
    private static IEnumerable<Vector2Int> EnumerateRectCells(RoomRect room)
    {
        for (int x = room.x; x < room.x + room.w; x++)
        {
            for (int z = room.z; z < room.z + room.h; z++)
            {
                yield return new Vector2Int(x, z);
            }
        }
    }

    /// <summary>
    /// v1.7: ступенчатый срез углов здания. С каждого угла снимается
    /// «ступенчатая диагональ» — треугольник клеток (dx + dz &lt; k) уходит
    /// с контура: клетки удаляются из модели (становятся улицей), наружные
    /// стены обтекают вырез. Размер k = 1..3, но не больше трети меньшей
    /// стороны; здание меньше 6x6 не режется. Если срез оставил какую-то
    /// комнату пустой или несвязной — весь угол откатывается.
    /// Возвращает число фактических срезов.
    /// </summary>
    private static int CutBuildingCorners(
        RoomRect building,
        List<HashSet<Vector2Int>> roomCells,
        Dictionary<Vector2Int, int> cellOwner,
        HashSet<Vector2Int> buildingCellSet,
        System.Random rng)
    {
        if (building.w < 6 || building.h < 6) return 0;

        int maxSize = Mathf.Clamp(Mathf.Min(building.w, building.h) / 3, 1, 3);

        // Углы: лево-низ, право-низ, лево-верх, право-верх; знаки направляют
        // dx/dz внутрь здания от угловой клетки.
        Vector2Int[] cornerOrigin =
        {
            new Vector2Int(building.x, building.z),
            new Vector2Int(building.x + building.w - 1, building.z),
            new Vector2Int(building.x, building.z + building.h - 1),
            new Vector2Int(building.x + building.w - 1, building.z + building.h - 1)
        };

        int[] dxSign = { 1, -1, 1, -1 };
        int[] dzSign = { 1, 1, -1, -1 };

        int cuts = 0;

        for (int c = 0; c < cornerOrigin.Length; c++)
        {
            int size = 1 + rng.Next(maxSize);

            List<Vector2Int> removed = new List<Vector2Int>();
            Dictionary<Vector2Int, int> removedOwners = new Dictionary<Vector2Int, int>();

            for (int dx = 0; dx < size; dx++)
            {
                for (int dz = 0; dz < size - dx; dz++)
                {
                    Vector2Int cell = new Vector2Int(
                        cornerOrigin[c].x + dxSign[c] * dx,
                        cornerOrigin[c].y + dzSign[c] * dz);

                    if (cellOwner.ContainsKey(cell))
                    {
                        removed.Add(cell);
                    }
                }
            }

            if (removed.Count == 0) continue;

            RemoveCellsFromModel(removed, roomCells, cellOwner, buildingCellSet, removedOwners);

            if (AllRoomsConnected(roomCells))
            {
                cuts++;
            }
            else
            {
                // Срез сломал комнату (пустая или развалилась) — откат угла.
                RestoreCellsToModel(removed, roomCells, cellOwner, buildingCellSet, removedOwners);
            }
        }

        return cuts;
    }

    /// <summary>Убирает клетки из модели (клетка становится «улицей»), запоминая прежних владельцев.</summary>
    private static void RemoveCellsFromModel(
        List<Vector2Int> cells,
        List<HashSet<Vector2Int>> roomCells,
        Dictionary<Vector2Int, int> cellOwner,
        HashSet<Vector2Int> buildingCellSet,
        Dictionary<Vector2Int, int> removedOwners)
    {
        foreach (Vector2Int cell in cells)
        {
            int owner;
            if (!cellOwner.TryGetValue(cell, out owner)) continue;

            removedOwners[cell] = owner;
            roomCells[owner].Remove(cell);
            cellOwner.Remove(cell);
            buildingCellSet.Remove(cell);
        }
    }

    /// <summary>Возвращает клетки в модель (откат среза угла или ступени).</summary>
    private static void RestoreCellsToModel(
        List<Vector2Int> cells,
        List<HashSet<Vector2Int>> roomCells,
        Dictionary<Vector2Int, int> cellOwner,
        HashSet<Vector2Int> buildingCellSet,
        Dictionary<Vector2Int, int> removedOwners)
    {
        foreach (Vector2Int cell in cells)
        {
            int owner;
            if (!removedOwners.TryGetValue(cell, out owner)) continue;

            roomCells[owner].Add(cell);
            cellOwner[cell] = owner;
            buildingCellSet.Add(cell);
        }
    }

    /// <summary>Пара клеток по разные стороны внутреннего граничного ребра.</summary>
    private struct CellPair
    {
        public Vector2Int a;
        public Vector2Int b;
    }

    /// <summary>Прямой прогон внутренних границ: серия пар клеток на одной линии с одной парой владельцев.</summary>
    private class BoundaryRun
    {
        public bool vertical;   // граница между левыми/правыми клетками (иначе — нижними/верхними)
        public int line;        // координата линии границы (x+1 для вертикальной, z+1 для горизонтальной)
        public int ownerA;     // владелец клеток стороны A (левые/нижние)
        public int ownerB;     // владелец клеток стороны B (правые/верхние)
        public List<CellPair> cells = new List<CellPair>();
    }

    /// <summary>
    /// v1.7: ломаные перегородки. Внутренние границы собираются в прямые
    /// прогоны; каждый прогон из 2+ рёбер с шансом jaggedStrength сдвигается
    /// сегментом из 1..4 клеток в одну из сторон: клетки сегмента переходят
    /// из одной комнаты в соседнюю. После шага проверяется, что обе затронутые
    /// комнаты остались связными и не мельче 2 клеток — иначе шаг откатывается.
    /// Клетки нижнего ряда (z == building.z) не трогаются: фасад со входом
    /// остаётся цельным, вход всегда целиком в одном зале.
    /// Возвращает число фактических ступеней.
    /// </summary>
    private int ApplyWallJitter(
        RoomRect building,
        List<HashSet<Vector2Int>> roomCells,
        Dictionary<Vector2Int, int> cellOwner,
        HashSet<Vector2Int> buildingCellSet,
        System.Random rng)
    {
        // 1) Прогоны внутренних границ (по модели после среза углов; сдвиги
        //    валидируются по факту, устаревший прогон просто отбрасывается).
        List<BoundaryRun> runs = CollectBoundaryRuns(cellOwner, buildingCellSet);

        int steps = 0;

        foreach (BoundaryRun run in runs)
        {
            int runLength = run.cells.Count;

            // Ступень ставится ТОЛЬКО на длинном прогоне (≥ 5 рёбер), и так,
            // чтобы хотя бы один из оставшихся кусков этой же стены был ≥ 4
            // рёбер — тогда пара комнат по-прежнему имеет прогон под проём
            // «2 клетки с отступом» (правило дверей v1.4 не деградирует).
            // Короткие прогоны (2–4 ребра) не трогаются вовсе.
            if (runLength < 5) continue;

            int maxSegmentLength = Mathf.Min(4, runLength - 4);

            if (maxSegmentLength < 1) continue;
            if (rng.NextDouble() > jaggedStrength) continue;

            // Сегмент: длина 1..min(4, длина-4), случайное положение, при
            // котором с одной из сторон остаётся кусок ≥ 4 рёбер.
            int segmentLength = 1 + rng.Next(maxSegmentLength);

            // Зоны допустимых позиций p: [0 .. N-L-4] (длинный кусок справа)
            // и [4 .. N-L] (длинный кусок слева); размеры зон совпадают.
            int sideZoneCount = runLength - segmentLength - 3;

            if (sideZoneCount < 1) continue;

            int segmentStart = rng.Next(2) == 0
                ? rng.Next(sideZoneCount)                       // длинный кусок справа
                : 4 + rng.Next(sideZoneCount);                  // длинный кусок слева

            // Куда двигать: клетки стороны A → комнате B, или наоборот.
            bool moveSideA = rng.Next(2) == 0;

            List<Vector2Int> moved = new List<Vector2Int>();
            Dictionary<Vector2Int, int> movedFrom = new Dictionary<Vector2Int, int>();

            bool stepValid = true;

            for (int i = segmentStart; i < segmentStart + segmentLength; i++)
            {
                Vector2Int a = run.cells[i].a;
                Vector2Int b = run.cells[i].b;

                // Прогон мог устареть после соседних сдвигов: граница должна жить.
                int ownerA;
                int ownerB;

                if (!cellOwner.TryGetValue(a, out ownerA) ||
                    !cellOwner.TryGetValue(b, out ownerB) ||
                    ownerA == ownerB)
                {
                    stepValid = false;
                    break;
                }

                // Нижний ряд (фасад со входом) не трогаем.
                if (a.y == building.z || b.y == building.z)
                {
                    stepValid = false;
                    break;
                }

                Vector2Int victim = moveSideA ? a : b;
                int donor = moveSideA ? ownerA : ownerB;
                int receiver = moveSideA ? ownerB : ownerA;

                // Комната-донор не должна мельчить: остаётся минимум 2 клетки.
                if (roomCells[donor].Count <= 2)
                {
                    stepValid = false;
                    break;
                }

                movedFrom[victim] = donor;
                moved.Add(victim);

                roomCells[donor].Remove(victim);
                roomCells[receiver].Add(victim);
                cellOwner[victim] = receiver;
            }

            // Валидация: все затронутые комнаты связны и непусты.
            if (stepValid)
            {
                HashSet<int> affected = new HashSet<int>();

                foreach (KeyValuePair<Vector2Int, int> kv in movedFrom)
                {
                    affected.Add(kv.Value);               // донор
                    affected.Add(cellOwner[kv.Key]);      // получатель
                }

                foreach (int roomIndex in affected)
                {
                    if (roomCells[roomIndex].Count == 0 || !IsConnected(roomCells[roomIndex]))
                    {
                        stepValid = false;
                        break;
                    }
                }
            }

            if (stepValid)
            {
                steps++;
            }
            else
            {
                // Откат: переносим клетки обратно донорам (владельцы сохранены).
                foreach (Vector2Int victim in moved)
                {
                    int donor = movedFrom[victim];
                    int receiver = cellOwner[victim];

                    roomCells[receiver].Remove(victim);
                    roomCells[donor].Add(victim);
                    cellOwner[victim] = donor;
                }
            }
        }

        return steps;
    }

    /// <summary>
    /// Собирает прямые прогоны внутренних границ текущей модели: пара смежных
    /// клеток разных владельцев → группировка по (ориентация, линия, владельцы)
    /// → сортировка вдоль прогона. Каждое ребро учитывается один раз — со
    /// стороны клетки, смотрящей на восточного/северного соседа.
    /// </summary>
    private static List<BoundaryRun> CollectBoundaryRuns(
        Dictionary<Vector2Int, int> cellOwner,
        HashSet<Vector2Int> buildingCellSet)
    {
        Dictionary<long, BoundaryRun> byKey = new Dictionary<long, BoundaryRun>();

        foreach (Vector2Int cell in buildingCellSet)
        {
            int owner;
            if (!cellOwner.TryGetValue(cell, out owner)) continue;

            // Восточный сосед → вертикальная граница на линии x+1.
            Vector2Int east = cell + Vector2Int.right;

            if (buildingCellSet.Contains(east))
            {
                int eastOwner = cellOwner[east];

                if (eastOwner != owner)
                {
                    AddBoundaryPair(byKey, true, cell.x + 1, owner, eastOwner, cell, east);
                }
            }

            // Северный сосед → горизонтальная граница на линии z+1.
            Vector2Int north = cell + Vector2Int.up;

            if (buildingCellSet.Contains(north))
            {
                int northOwner = cellOwner[north];

                if (northOwner != owner)
                {
                    AddBoundaryPair(byKey, false, cell.y + 1, owner, northOwner, cell, north);
                }
            }
        }

        List<BoundaryRun> runs = new List<BoundaryRun>(byKey.Values);

        foreach (BoundaryRun run in runs)
        {
            // Сортировка пар вдоль прогона — детерминизм и корректные сегменты.
            run.cells.Sort((p, q) => (run.vertical ? p.a.y : p.a.x)
                .CompareTo(run.vertical ? q.a.y : q.a.x));
        }

        // Детерминированный порядок обработки прогонов.
        runs.Sort(CompareRuns);

        return runs;
    }

    private static void AddBoundaryPair(
        Dictionary<long, BoundaryRun> byKey,
        bool vertical,
        int line,
        int ownerA,
        int ownerB,
        Vector2Int a,
        Vector2Int b)
    {
        long key = ((vertical ? 1L : 0L) << 40) |
                   ((long)line << 24) |
                   ((long)(ownerA & 0xFFF) << 12) |
                   (long)(ownerB & 0xFFF);

        BoundaryRun run;

        if (!byKey.TryGetValue(key, out run))
        {
            run = new BoundaryRun
            {
                vertical = vertical,
                line = line,
                ownerA = ownerA,
                ownerB = ownerB
            };
            byKey[key] = run;
        }

        run.cells.Add(new CellPair { a = a, b = b });
    }

    /// <summary>Стабильный порядок прогонов: вертикальные раньше, затем линия, затем владельцы.</summary>
    private static int CompareRuns(BoundaryRun a, BoundaryRun b)
    {
        int va = a.vertical ? 1 : 0;
        int vb = b.vertical ? 1 : 0;

        if (va != vb) return va.CompareTo(vb);
        if (a.line != b.line) return a.line.CompareTo(b.line);
        if (a.ownerA != b.ownerA) return a.ownerA.CompareTo(b.ownerA);
        return a.ownerB.CompareTo(b.ownerB);
    }

    /// <summary>
    /// v1.7: рёбра стен из клеточной модели. Ребро появляется между клетками
    /// разных владельцев (перегородка) и между клеткой здания и «улицей»
    /// (внешний контур). includeInterior=false — только контур (перегородки
    /// выключены). Семантика рёбер: AlongX (x, z) — нижняя граница клетки
    /// (x, z); AlongZ (x, z) — левая граница клетки (x, z).
    /// </summary>
    private static HashSet<GridPositioningSystem.WallEdgeKey> DeriveWallEdges(
        HashSet<Vector2Int> buildingCellSet,
        Dictionary<Vector2Int, int> cellOwner,
        bool includeInterior)
    {
        HashSet<GridPositioningSystem.WallEdgeKey> edges =
            new HashSet<GridPositioningSystem.WallEdgeKey>();

        foreach (Vector2Int cell in buildingCellSet)
        {
            int owner = cellOwner[cell];

            // Юг: сосед (x, z-1) → ребро AlongX (x, z) — нижняя граница клетки.
            Vector2Int south = cell + Vector2Int.down;
            if (IsBoundaryNeighbor(south, buildingCellSet, cellOwner, owner, includeInterior))
            {
                edges.Add(new GridPositioningSystem.WallEdgeKey(
                    cell.x, cell.y, GridPositioningSystem.WallEdgeAxis.AlongX));
            }

            // Север: сосед (x, z+1) → ребро AlongX (x, z+1) — его нижняя граница.
            Vector2Int north = cell + Vector2Int.up;
            if (IsBoundaryNeighbor(north, buildingCellSet, cellOwner, owner, includeInterior))
            {
                edges.Add(new GridPositioningSystem.WallEdgeKey(
                    cell.x, cell.y + 1, GridPositioningSystem.WallEdgeAxis.AlongX));
            }

            // Запад: сосед (x-1, z) → ребро AlongZ (x, z) — левая граница клетки.
            Vector2Int west = cell + Vector2Int.left;
            if (IsBoundaryNeighbor(west, buildingCellSet, cellOwner, owner, includeInterior))
            {
                edges.Add(new GridPositioningSystem.WallEdgeKey(
                    cell.x, cell.y, GridPositioningSystem.WallEdgeAxis.AlongZ));
            }

            // Восток: сосед (x+1, z) → ребро AlongZ (x+1, z) — его левая граница.
            Vector2Int east = cell + Vector2Int.right;
            if (IsBoundaryNeighbor(east, buildingCellSet, cellOwner, owner, includeInterior))
            {
                edges.Add(new GridPositioningSystem.WallEdgeKey(
                    cell.x + 1, cell.y, GridPositioningSystem.WallEdgeAxis.AlongZ));
            }
        }

        return edges;
    }

    /// <summary>Сосед даёт ребро-стену: он вне здания (улица) или — при включённых перегородках — другой комнаты.</summary>
    private static bool IsBoundaryNeighbor(
        Vector2Int neighbor,
        HashSet<Vector2Int> buildingCellSet,
        Dictionary<Vector2Int, int> cellOwner,
        int owner,
        bool includeInterior)
    {
        if (!buildingCellSet.Contains(neighbor)) return true;   // улица — всегда стена
        if (!includeInterior) return false;                     // перегородки выключены
        return cellOwner[neighbor] != owner;
    }

    /// <summary>
    /// v1.3/v1.4/v1.7: вход с улицы — 2 клетки на нижней стороне здания
    /// (середина / чуть левее), с отступом ≥ 1 ребра от торцов нижнего
    /// СЕГМЕНТА комнаты-владельца. Комната, владеющая клеткой входа, всегда
    /// становится залом. Сегмент = непрерывный прогон клеток одного владельца
    /// по нижнему ряду, поэтому ступени перегородок (нижний ряд не трогается)
    /// и срезы углов (середина фасада не срезается) вход не ломают.
    /// Возвращает клетку входа или null, если нижний ряд не нашёлся.
    /// </summary>
    private static Vector2Int? CarveStreetEntrance(
        RoomRect building,
        Dictionary<Vector2Int, int> cellOwner,
        HashSet<GridPositioningSystem.WallEdgeKey> wallSet,
        HashSet<GridPositioningSystem.WallEdgeKey> doorSet,
        out int openingsTwo,
        out int openingsOne,
        out int flushFallbacks)
    {
        openingsTwo = 0;
        openingsOne = 0;
        flushFallbacks = 0;

        int midCellX = building.x + (building.w - 1) / 2;

        // Владелец клетки середины нижнего ряда. Срезы углов до середины
        // фасада не доходят (здание ≥ 6 клеток шириной) — но страховочный
        // поиск ближайшей существующей клетки всё же есть (уклон влево —
        // требование «по центру или левее»).
        int owner = -1;
        int anchorX = midCellX;

        for (int dx = 0; dx < building.w && owner < 0; dx++)
        {
            int left = midCellX - dx;

            if (left >= building.x &&
                cellOwner.TryGetValue(new Vector2Int(left, building.z), out int leftOwner))
            {
                owner = leftOwner;
                anchorX = left;
                break;
            }

            int right = midCellX + dx;

            if (right < building.x + building.w &&
                cellOwner.TryGetValue(new Vector2Int(right, building.z), out int rightOwner))
            {
                owner = rightOwner;
                anchorX = right;
            }
        }

        if (owner < 0) return null;

        // Нижний сегмент комнаты-владельца: непрерывный прогон клеток этого
        // владельца по нижнему ряду, содержащий клетку-якорь.
        int segStart = anchorX;

        while (segStart > building.x &&
               cellOwner.TryGetValue(new Vector2Int(segStart - 1, building.z), out int leftOwner) &&
               leftOwner == owner)
        {
            segStart--;
        }

        int segEnd = anchorX;

        while (segEnd < building.x + building.w - 1 &&
               cellOwner.TryGetValue(new Vector2Int(segEnd + 1, building.z), out int rightOwner) &&
               rightOwner == owner)
        {
            segEnd++;
        }

        int segLength = segEnd - segStart + 1;

        // 2-клеточное окно с отступом ≥ 1 ребра от торцов сегмента.
        int lo = segStart + 1;
        int hi = segEnd - 2;

        if (hi >= lo)
        {
            // Центрируем по сегменту; при равенстве вариантов — центр-влево.
            int s = Mathf.Clamp(segStart + (segLength - 1) / 2, lo, hi);

            MoveWallEdgeToDoor(wallSet, doorSet, s, building.z,
                GridPositioningSystem.WallEdgeAxis.AlongX);
            MoveWallEdgeToDoor(wallSet, doorSet, s + 1, building.z,
                GridPositioningSystem.WallEdgeAxis.AlongX);

            openingsTwo++;

            // Ребро AlongX (x, z) — нижняя граница клетки (x, z).
            return new Vector2Int(s, building.z);
        }

        // Сегмент короче 4 клеток: 1 клетка. Для длины 3 отступ есть с обеих
        // сторон; для 1–2 клеток проём прилегает к торцу — вынужденный
        // fallback (считается в отчёте).
        int single = segStart + (segLength - 1) / 2;
        bool flush = single == segStart || single == segEnd;

        MoveWallEdgeToDoor(wallSet, doorSet, single, building.z,
            GridPositioningSystem.WallEdgeAxis.AlongX);

        if (flush) flushFallbacks++;
        else openingsOne++;

        return new Vector2Int(single, building.z);
    }

    /// <summary>Связность ВСЕХ комнат: каждая — непустой 4-связный набор клеток.</summary>
    private static bool AllRoomsConnected(List<HashSet<Vector2Int>> roomCells)
    {
        foreach (HashSet<Vector2Int> cells in roomCells)
        {
            if (cells.Count == 0) return false;
            if (!IsConnected(cells)) return false;
        }

        return true;
    }

    /// <summary>4-связность набора клеток: flood fill от первой клетки покрывает весь набор.</summary>
    private static bool IsConnected(HashSet<Vector2Int> cells)
    {
        if (cells.Count == 0) return true;

        Vector2Int start = new Vector2Int(int.MaxValue, int.MaxValue);

        foreach (Vector2Int cell in cells)
        {
            if (cell.x < start.x || (cell.x == start.x && cell.y < start.y))
            {
                start = cell;
            }
        }

        HashSet<Vector2Int> visited = new HashSet<Vector2Int> { start };
        Stack<Vector2Int> stack = new Stack<Vector2Int>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            Vector2Int current = stack.Pop();

            Vector2Int[] neighbors =
            {
                current + Vector2Int.right,
                current + Vector2Int.left,
                current + Vector2Int.up,
                current + Vector2Int.down
            };

            foreach (Vector2Int neighbor in neighbors)
            {
                if (cells.Contains(neighbor) && visited.Add(neighbor))
                {
                    stack.Push(neighbor);
                }
            }
        }

        return visited.Count == cells.Count;
    }

    // ============================================================
    //  ДВЕРИ: ОСТОВНОЕ ДЕРЕВО ГРАФА КОМНАТ (v1.7 — по клеткам)
    // ============================================================

    /// <summary>
    /// Проёмы по остовному дереву графа смежности комнат. Пара смежных комнат
    /// собирает ОБЩИЕ рёбра — границу клеток двух владельцев (v1.7: из
    /// клеточной модели, периметров прямоугольников больше нет). Общая стена
    /// пары может состоять из НЕСКОЛЬКИХ прогонов и даже двух осей — это
    /// учитывает CarveDoors. BFS со случайным порядком соседей строит дерево
    /// из (n - 1) пар; на каждом его ребре прорубается проём (v1.4: 2 клетки
    /// с отступом от торцов прогона). Связность всех комнат гарантирована по
    /// построению. Доп. проёмы между соседями вне дерева — с шансом
    /// extraDoorChance.
    /// </summary>
    private static void BuildRoomDoors(
        List<HashSet<Vector2Int>> roomCells,
        Dictionary<Vector2Int, int> cellOwner,
        HashSet<Vector2Int> buildingCellSet,
        System.Random rng,
        float extraDoorChance,
        HashSet<GridPositioningSystem.WallEdgeKey> wallSet,
        HashSet<GridPositioningSystem.WallEdgeKey> doorSet,
        out int doorsTwo,
        out int doorsOne,
        out int flushFallbacks)
    {
        doorsTwo = 0;
        doorsOne = 0;
        flushFallbacks = 0;

        int roomCount = roomCells.Count;

        // Общие внутренние рёбра каждой смежной пары комнат: перебираем клетки
        // и смотрим восточного/северного соседа — каждое ребро учитывается
        // ровно один раз (со стороны клетки с меньшими координатами).
        Dictionary<long, List<GridPositioningSystem.WallEdgeKey>> sharedByPair =
            new Dictionary<long, List<GridPositioningSystem.WallEdgeKey>>();

        List<List<int>> adjacency = new List<List<int>>(roomCount);

        for (int i = 0; i < roomCount; i++)
        {
            adjacency.Add(new List<int>());
        }

        foreach (Vector2Int cell in buildingCellSet)
        {
            int owner;

            if (!cellOwner.TryGetValue(cell, out owner))
            {
                continue;
            }

            Vector2Int east = cell + Vector2Int.right;

            if (buildingCellSet.Contains(east))
            {
                int eastOwner = cellOwner[east];

                if (eastOwner != owner)
                {
                    // AlongZ-ребро (x, z) — граница клеток (x-1, z) и (x, z).
                    AddSharedEdge(sharedByPair, adjacency, owner, eastOwner,
                        new GridPositioningSystem.WallEdgeKey(
                            cell.x + 1, cell.y,
                            GridPositioningSystem.WallEdgeAxis.AlongZ));
                }
            }

            Vector2Int north = cell + Vector2Int.up;

            if (buildingCellSet.Contains(north))
            {
                int northOwner = cellOwner[north];

                if (northOwner != owner)
                {
                    // AlongX-ребро (x, z) — граница клеток (x, z-1) и (x, z).
                    AddSharedEdge(sharedByPair, adjacency, owner, northOwner,
                        new GridPositioningSystem.WallEdgeKey(
                            cell.x, cell.y + 1,
                            GridPositioningSystem.WallEdgeAxis.AlongX));
                }
            }
        }

        // Остовное дерево: BFS от комнаты 0, соседи в случайном порядке —
        // сид управляет и формой дерева, и положением проёмов.
        bool[] visited = new bool[roomCount];
        HashSet<long> treePairs = new HashSet<long>();
        Queue<int> queue = new Queue<int>();

        queue.Enqueue(0);
        visited[0] = true;

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();

            List<int> neighbors = new List<int>(adjacency[current]);

            for (int i = neighbors.Count - 1; i > 0; i--)
            {
                int swap = rng.Next(i + 1);
                int tmp = neighbors[i];
                neighbors[i] = neighbors[swap];
                neighbors[swap] = tmp;
            }

            foreach (int neighbor in neighbors)
            {
                if (visited[neighbor]) continue;

                visited[neighbor] = true;
                treePairs.Add(PairKey(
                    Mathf.Min(current, neighbor), Mathf.Max(current, neighbor)));
                queue.Enqueue(neighbor);
            }
        }

        // Проём на каждом ребре дерева — связность гарантирована.
        CarveDoors(treePairs, sharedByPair, rng, wallSet, doorSet,
            true, out doorsTwo, out doorsOne, out flushFallbacks);

        // Доп. проёмы между смежными комнатами вне дерева.
        if (extraDoorChance > 0f)
        {
            List<long> extraPairs = new List<long>();

            for (int i = 0; i < roomCount; i++)
            {
                for (int j = i + 1; j < roomCount; j++)
                {
                    long key = PairKey(i, j);

                    if (treePairs.Contains(key)) continue;
                    if (!sharedByPair.ContainsKey(key)) continue;
                    if (rng.NextDouble() < extraDoorChance) extraPairs.Add(key);
                }
            }

            CarveDoors(extraPairs, sharedByPair, rng, wallSet, doorSet,
                false, out int extraTwo, out int extraOne, out int extraFlush);

            doorsTwo += extraTwo;
            doorsOne += extraOne;
            flushFallbacks += extraFlush;
        }
    }

    /// <summary>
    /// Регистрирует общее ребро пары комнат: список рёбер пары + запись о
    /// смежности (один раз на пару — при создании списка).
    /// </summary>
    private static void AddSharedEdge(
        Dictionary<long, List<GridPositioningSystem.WallEdgeKey>> sharedByPair,
        List<List<int>> adjacency,
        int ownerA,
        int ownerB,
        GridPositioningSystem.WallEdgeKey edge)
    {
        int lo = Mathf.Min(ownerA, ownerB);
        int hi = Mathf.Max(ownerA, ownerB);
        long key = PairKey(lo, hi);

        List<GridPositioningSystem.WallEdgeKey> list;

        if (!sharedByPair.TryGetValue(key, out list))
        {
            list = new List<GridPositioningSystem.WallEdgeKey>();
            sharedByPair[key] = list;
            adjacency[lo].Add(hi);
            adjacency[hi].Add(lo);
        }

        list.Add(edge);
    }

    /// <summary>Пара комнат (i, j), i &lt; j, → один ключ для словарей.</summary>
    private static long PairKey(int i, int j)
    {
        return (long)i * 100000L + j;
    }

    /// <summary>
    /// v1.4/v1.7: прорубает проёмы в общих стенах пар комнат. Общие рёбра пары
    /// группируются в коллинеарные НЕПРЕРЫВНЫЕ прогоны (после ступеней общая
    /// стена может состоять из нескольких прогонов и даже двух осей). Для
    /// проёма выбирается лучший прогон: сначала любой длиной ≥ 4 (2 клетки с
    /// отступом ≥ 1 ребра с обеих сторон), затем ровно 3 (1 клетка с
    /// отступом), затем — только для пар остовного дерева — короткий прогон
    /// без отступа (вынужденный fallback, попадает в отчёт). Правила
    /// отступов v1.4 сохранены.
    /// </summary>
    private static void CarveDoors(
        IEnumerable<long> pairs,
        Dictionary<long, List<GridPositioningSystem.WallEdgeKey>> sharedByPair,
        System.Random rng,
        HashSet<GridPositioningSystem.WallEdgeKey> wallSet,
        HashSet<GridPositioningSystem.WallEdgeKey> doorSet,
        bool allowFlushFallback,
        out int doorsTwo,
        out int doorsOne,
        out int flushFallbacks)
    {
        doorsTwo = 0;
        doorsOne = 0;
        flushFallbacks = 0;

        foreach (long key in pairs)
        {
            List<GridPositioningSystem.WallEdgeKey> shared;

            if (!sharedByPair.TryGetValue(key, out shared)) continue;

            // Непрерывные прогоны общей стены пары (ось + линия + координаты подряд).
            List<List<GridPositioningSystem.WallEdgeKey>> runs = SplitIntoRuns(shared);

            List<List<GridPositioningSystem.WallEdgeKey>> twoCellRuns =
                new List<List<GridPositioningSystem.WallEdgeKey>>();
            List<List<GridPositioningSystem.WallEdgeKey>> oneCellRuns =
                new List<List<GridPositioningSystem.WallEdgeKey>>();
            List<List<GridPositioningSystem.WallEdgeKey>> flushRuns =
                new List<List<GridPositioningSystem.WallEdgeKey>>();

            foreach (List<GridPositioningSystem.WallEdgeKey> run in runs)
            {
                if (run.Count >= 4) twoCellRuns.Add(run);
                else if (run.Count == 3) oneCellRuns.Add(run);
                else flushRuns.Add(run);
            }

            List<GridPositioningSystem.WallEdgeKey> chosen;
            int start;
            int width;

            if (twoCellRuns.Count > 0)
            {
                chosen = twoCellRuns[rng.Next(twoCellRuns.Count)];
                width = 2;
                start = 1 + rng.Next(chosen.Count - 3);
            }
            else if (oneCellRuns.Count > 0)
            {
                chosen = oneCellRuns[rng.Next(oneCellRuns.Count)];
                width = 1;
                start = 1 + rng.Next(chosen.Count - 2);
            }
            else if (flushRuns.Count > 0 && allowFlushFallback)
            {
                // Короткий прогон (1–2 ребра) у пары дерева: связность
                // важнее отступа — ставим единственно возможное ребро.
                chosen = flushRuns[rng.Next(flushRuns.Count)];
                width = 1;
                start = rng.Next(chosen.Count);
                flushFallbacks++;
            }
            else
            {
                continue;
            }

            for (int i = start; i < start + width; i++)
            {
                MoveWallEdgeToDoor(wallSet, doorSet,
                    chosen[i].x, chosen[i].z, chosen[i].axis);
            }

            if (width == 2) doorsTwo++;
            else doorsOne++;
        }
    }

    /// <summary>
    /// Рёбра одной пары комнат → непрерывные коллинеарные прогоны: группировка
    /// по оси и линии, внутри линии — по подряд идущим координатам.
    /// </summary>
    private static List<List<GridPositioningSystem.WallEdgeKey>> SplitIntoRuns(
        List<GridPositioningSystem.WallEdgeKey> edges)
    {
        Dictionary<long, List<GridPositioningSystem.WallEdgeKey>> byLine =
            new Dictionary<long, List<GridPositioningSystem.WallEdgeKey>>();

        foreach (GridPositioningSystem.WallEdgeKey edge in edges)
        {
            // Ключ: ось + линия (z для AlongX, x для AlongZ).
            long lineKey = ((long)(int)edge.axis << 32) |
                           (long)(edge.axis == GridPositioningSystem.WallEdgeAxis.AlongX
                               ? edge.z
                               : edge.x);

            List<GridPositioningSystem.WallEdgeKey> list;

            if (!byLine.TryGetValue(lineKey, out list))
            {
                list = new List<GridPositioningSystem.WallEdgeKey>();
                byLine[lineKey] = list;
            }

            list.Add(edge);
        }

        List<List<GridPositioningSystem.WallEdgeKey>> runs =
            new List<List<GridPositioningSystem.WallEdgeKey>>();

        foreach (KeyValuePair<long, List<GridPositioningSystem.WallEdgeKey>> kv in byLine)
        {
            List<GridPositioningSystem.WallEdgeKey> line = kv.Value;
            bool alongX = line[0].axis == GridPositioningSystem.WallEdgeAxis.AlongX;

            line.Sort((a, b) => (alongX ? a.x : a.z).CompareTo(alongX ? b.x : b.z));

            List<GridPositioningSystem.WallEdgeKey> current =
                new List<GridPositioningSystem.WallEdgeKey> { line[0] };

            for (int i = 1; i < line.Count; i++)
            {
                int prevCoord = alongX ? line[i - 1].x : line[i - 1].z;
                int coord = alongX ? line[i].x : line[i].z;

                if (coord == prevCoord + 1)
                {
                    current.Add(line[i]);
                }
                else
                {
                    runs.Add(current);
                    current = new List<GridPositioningSystem.WallEdgeKey> { line[i] };
                }
            }

            runs.Add(current);
        }

        return runs;
    }

    /// <summary>Переносит одно ребро из wallSet в doorSet (делает проёмом).</summary>
    private static void MoveWallEdgeToDoor(
        HashSet<GridPositioningSystem.WallEdgeKey> wallSet,
        HashSet<GridPositioningSystem.WallEdgeKey> doorSet,
        int a, int b,
        GridPositioningSystem.WallEdgeAxis axis)
    {
        GridPositioningSystem.WallEdgeKey edge =
            new GridPositioningSystem.WallEdgeKey(a, b, axis);

        if (wallSet.Remove(edge))
        {
            doorSet.Add(edge);
        }
    }

    /// <summary>Проём как цельный прогон рёбер (для постановки одной двери).</summary>
    private struct DoorSpan
    {
        public int x;
        public int z;
        public GridPositioningSystem.WallEdgeAxis axis;
        public int width;
    }

    /// <summary>
    /// Группирует рёбра проёмов в прогоны: соседние коллинеарные рёбра — один
    /// проём. Дверь-объект ставится ОДНА на прогон, а не на каждое ребро.
    /// Прогоны разных пар всегда разделены хотя бы одним ребром-стеной
    /// (отступ из CarveDoors), поэтому склейка разных дверей невозможна.
    /// </summary>
    private static List<DoorSpan> GroupDoorSpans(
        HashSet<GridPositioningSystem.WallEdgeKey> doorSet)
    {
        List<DoorSpan> spans = new List<DoorSpan>();

        Dictionary<int, List<int>> alongXByLine = new Dictionary<int, List<int>>();
        Dictionary<int, List<int>> alongZByLine = new Dictionary<int, List<int>>();

        foreach (GridPositioningSystem.WallEdgeKey edge in doorSet)
        {
            if (edge.axis == GridPositioningSystem.WallEdgeAxis.AlongX)
            {
                if (!alongXByLine.TryGetValue(edge.z, out List<int> cells))
                {
                    cells = new List<int>();
                    alongXByLine[edge.z] = cells;
                }

                cells.Add(edge.x);
            }
            else
            {
                if (!alongZByLine.TryGetValue(edge.x, out List<int> cells))
                {
                    cells = new List<int>();
                    alongZByLine[edge.x] = cells;
                }

                cells.Add(edge.z);
            }
        }

        CollectSpans(alongXByLine, GridPositioningSystem.WallEdgeAxis.AlongX, spans);
        CollectSpans(alongZByLine, GridPositioningSystem.WallEdgeAxis.AlongZ, spans);

        return spans;
    }

    /// <summary>Прогоны подряд идущих координат на одной линии → DoorSpan.</summary>
    private static void CollectSpans(
        Dictionary<int, List<int>> lineCells,
        GridPositioningSystem.WallEdgeAxis axis,
        List<DoorSpan> spans)
    {
        foreach (KeyValuePair<int, List<int>> kv in lineCells)
        {
            List<int> cells = kv.Value;
            cells.Sort();

            int runStart = cells[0];
            int runLength = 1;

            for (int i = 1; i < cells.Count; i++)
            {
                if (cells[i] == cells[i - 1] + 1)
                {
                    runLength++;
                    continue;
                }

                AddDoorSpan(spans, axis, kv.Key, runStart, runLength);

                runStart = cells[i];
                runLength = 1;
            }

            AddDoorSpan(spans, axis, kv.Key, runStart, runLength);
        }
    }

    private static void AddDoorSpan(
        List<DoorSpan> spans,
        GridPositioningSystem.WallEdgeAxis axis,
        int line, int start, int length)
    {
        DoorSpan span = new DoorSpan();

        if (axis == GridPositioningSystem.WallEdgeAxis.AlongX)
        {
            span.x = start;
            span.z = line;
        }
        else
        {
            span.x = line;
            span.z = start;
        }

        span.axis = axis;
        span.width = length;
        spans.Add(span);
    }

    /// <summary>
    /// v1.4: ставит дверь-префаб в каждый проём. Петля — в НАЧАЛЬНЫЙ угол
    /// проёма (меньшая координата), створка ложится вдоль проёма. Y — от
    /// префаба. Позиции детерминированы, rng не тратится.
    /// v1.6: ориентация и состояния. Ось полотна снимается с геометрии
    /// префаба в identity-позе (DetectLeafFrame), yaw кладёт ЗАКРЫТУЮ створку
    /// ВДОЛЬ проёма (параллельно стене — перпендикулярный спавн v1.5 ломал
    /// отсчёт угла петли). Межкомнатные створки закрыты;
    /// CustomerDoorOpener добавляется на них при отсутствии (клиенты открывают
    /// движением). Уличная (toggle «Уличная дверь всегда открыта») после
    /// закрытой позы ПОВОРАЧИВАЕТСЯ на 180° вокруг петли — распахивается
    /// наружу, к улице, и прижимается к внешней плоскости фасада (параллельно
    /// стене, без пересечения со стеной), становится статикой. При
    /// выключенном toggle уличная дверь закрыта, как межкомнатные.
    /// </summary>
    private int PlaceDoors(
        GridPositioningSystem grid,
        GameObject root,
        HashSet<GridPositioningSystem.WallEdgeKey> doorSet,
        RoomRect building,
        Vector2Int? entranceCell,
        out bool streetDoorOpen)
    {
        streetDoorOpen = false;

        if (doorPrefab == null) return 0;

        GameObject doorsRoot = new GameObject("BSP Doors");
        Undo.RegisterCreatedObjectUndo(doorsRoot, "BSP Doors");
        doorsRoot.transform.SetParent(root.transform, false);

        float halfCell = grid.GetCellSize() * 0.5f;

        // v1.6: полтолщины стенового префаба — для отступа распахнутой
        // уличной створки за внешнюю плоскость фасада.
        float wallHalfThickness = ComputeWallHalfThickness(wallPrefab);

        int placed = 0;

        foreach (DoorSpan span in GroupDoorSpans(doorSet))
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(doorPrefab);
            Undo.RegisterCreatedObjectUndo(instance, "BSP door");
            instance.transform.SetParent(doorsRoot.transform, true);

            bool alongX = span.axis == GridPositioningSystem.WallEdgeAxis.AlongX;

            // Уличная — тот проём нижней стороны здания, куда попадает клетка
            // входа (entranceCell.x). Рёбра периметра между проёмами не режутся,
            // поэтому уличный прогон не склеивается с межкомнатными.
            bool isStreet = entranceCell.HasValue && alongX &&
                            span.z == building.z &&
                            entranceCell.Value.x >= span.x &&
                            entranceCell.Value.x < span.x + span.width;

            bool parkedOpen = isStreet && streetDoorAlwaysOpen;

            Vector3 mid = grid.GetWallEdgeMidpoint(
                new GridPositioningSystem.WallEdgeKey(span.x, span.z, span.axis));

            // Петля = начальный угол проёма: середина первого ребра минус полклетки.
            Vector3 hinge = alongX
                ? new Vector3(mid.x - halfCell, mid.y, mid.z)
                : new Vector3(mid.x, mid.y, mid.z - halfCell);

            // v1.6: геометрия полотна снимается в identity-позе (до любого
            // поворота): фактическая ось полотна в префабе может не совпадать
            // с ручной настройкой — тогда закрытая створка вставала
            // перпендикулярно стене и ломала угол открытия.
            instance.transform.SetPositionAndRotation(
                new Vector3(hinge.x, instance.transform.position.y, hinge.z),
                Quaternion.identity);
            LeafFrame leaf = DetectLeafFrame(instance);

            // ЗАКРЫТАЯ поза: полотно параллельно стене, вдоль проёма, от
            // петли в сторону проёма. Ноль петли PhysicalDoor = поза спавна
            // (пружина и лимиты считаются от неё) — перпендикулярный спавн
            // запрещён.
            float yaw = ComputeDoorYaw(alongX, leaf);

            // Пивот в ЦЕНТРЕ полотна (геометрия уходит в обе стороны от
            // пивота) — центрируем створку на проём, иначе половина полотна
            // торчала бы наружу проёма.
            Vector3 pivot = leaf.pivotCentered
                ? new Vector3(
                    alongX ? hinge.x + span.width * halfCell : mid.x,
                    instance.transform.position.y,
                    alongX ? mid.z : hinge.z + span.width * halfCell)
                : new Vector3(hinge.x, instance.transform.position.y, hinge.z);

            instance.transform.SetPositionAndRotation(
                pivot, Quaternion.Euler(0f, yaw, 0f));

            if (parkedOpen)
            {
                // Уличная: ПОВОРОТ на 180° вокруг петли — полотно распахивается
                // наружу, к улице, и ложится ВДОЛЬ фасада (параллельно стене),
                // прижатым к его внешней плоскости. Ребро AlongX
                // (x, building.z) — южная граница здания: комнаты в +Z, улица
                // в -Z. Отступ — по габаритам полотна, снятым в закрытой позе:
                // при развороте на 180° наружный борт полотна встаёт за
                // внешнюю плоскость фасада (полтолщины стены + зазор 2 см),
                // ничего не пересекается.
                Bounds closedBounds = ComputeLeafBounds(instance);
                float innerOffset = closedBounds.min.z - pivot.z;
                float streetFaceZ = mid.z - wallHalfThickness;

                instance.transform.SetPositionAndRotation(
                    new Vector3(pivot.x, pivot.y, streetFaceZ + innerOffset - 0.02f),
                    Quaternion.Euler(0f, yaw + 180f, 0f));

                streetDoorOpen = true;
            }

            ConfigureDoorState(instance, parkedOpen);

            placed++;
        }

        return placed;
    }

    /// <summary>
    /// v1.6: начальное состояние створки на инстансе префаба.
    /// Межкомнатная (closed): CustomerDoorOpener добавляется, если его нет —
    /// без него клиенты открывают дверь только «в лоб» телом, без ассиста и
    /// аварийного пропуска. Требуется PhysicalDoor на префабе (Rigidbody и
    /// HingeJoint появляются от него).
    /// Уличная распахнутая (parkedOpen): полотно стоит распахнутым на 180°,
    /// прижатым снаружи к фасаду — CustomerDoorOpener убирается (его центр
    /// проёма и радиусы снимаются с ЗАКРЫТОЙ створки в Start), PhysicalDoor
    /// и HingeJoint убираются (статичная открытая дверь не качается),
    /// Rigidbody убирается — иначе динамическое тело падало бы под
    /// гравитацией без пружины джоинта.
    /// </summary>
    private static void ConfigureDoorState(GameObject leaf, bool parkedOpen)
    {
        CustomerDoorOpener opener = leaf.GetComponent<CustomerDoorOpener>();

        if (parkedOpen)
        {
            if (opener != null)
            {
                Undo.DestroyObjectImmediate(opener);
            }

            PhysicalDoor physical = leaf.GetComponent<PhysicalDoor>();

            if (physical != null)
            {
                Undo.DestroyObjectImmediate(physical);
            }

            HingeJoint joint = leaf.GetComponent<HingeJoint>();

            if (joint != null)
            {
                Undo.DestroyObjectImmediate(joint);
            }

            // После снятия PhysicalDoor тело больше никому не нужно:
            // полотно становится статичной преградой у фасада.
            Rigidbody body = leaf.GetComponent<Rigidbody>();

            if (body != null)
            {
                Undo.DestroyObjectImmediate(body);
            }

            return;
        }

        if (opener == null && leaf.GetComponent<PhysicalDoor>() != null)
        {
            Undo.AddComponent<CustomerDoorOpener>(leaf);
        }
    }

    /// <summary>
    /// v1.6: полтолщины стенового префаба — по геометрии через
    /// WallPiece.TryGetGeometryBounds (самый крупный BoxCollider). Толщина —
    /// меньшая из горизонтальных сторон: префаб может быть авторизован как
    /// вдоль X, так и вдоль Z. Геометрию снять не удалось — дефолт 0.1 м.
    /// </summary>
    private static float ComputeWallHalfThickness(GameObject wallPrefab)
    {
        WallPiece piece = wallPrefab.GetComponent<WallPiece>();

        if (piece != null && WallPiece.TryGetGeometryBounds(piece, out Bounds bounds))
        {
            return Mathf.Max(0.02f, Mathf.Min(bounds.size.x, bounds.size.z) * 0.5f);
        }

        return 0.1f;
    }

    /// <summary>
    /// v1.6: мировые габариты инстанса створки (рендереры + коллайдеры) в
    /// ТЕКУЩЕЙ позе. В identity-позе служит для определения фактической оси
    /// полотна (DetectLeafFrame), в закрытой — для отступа распахнутой
    /// уличной створки от фасада.
    /// </summary>
    private static Bounds ComputeLeafBounds(GameObject leaf)
    {
        Bounds bounds = new Bounds(leaf.transform.position, Vector3.zero);
        bool any = false;

        Renderer[] renderers = leaf.GetComponentsInChildren<Renderer>();

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;

            if (!any)
            {
                bounds = renderer.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        Collider[] colliders = leaf.GetComponentsInChildren<Collider>();

        foreach (Collider collider in colliders)
        {
            if (collider == null) continue;

            if (!any)
            {
                bounds = collider.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return bounds;
    }

    /// <summary>
    /// v1.6: yaw ЗАКРЫТОЙ створки — полотно ложится вдоль проёма, от петли
    /// в сторону проёма (AlongX-проём → полотно вдоль +X, AlongZ → вдоль +Z).
    /// Направление полотна берётся из фактической геометрии префаба (leaf от
    /// DetectLeafFrame); геометрии нет — ручной выбор doorLeafAxis.
    /// Поворот мира в Unity: yaw 90 переводит +Z в +X, 180 → -Z, 270 → -X.
    /// </summary>
    private float ComputeDoorYaw(bool alongX, LeafFrame leaf)
    {
        // yaw нативного направления полотна относительно +Z (при yaw 0).
        float nativeYaw;

        if (leaf.valid)
        {
            nativeYaw = leaf.alongX
                ? (leaf.dirSign > 0f ? 90f : 270f)   // +X → 90, -X → 270
                : (leaf.dirSign > 0f ? 0f : 180f);   // +Z → 0, -Z → 180
        }
        else
        {
            nativeYaw = doorLeafAxis == DoorLeafAxis.PlusX ? 90f : 0f;
        }

        // yaw цели: полотно вдоль +X → 90, вдоль +Z → 0.
        float targetYaw = alongX ? 90f : 0f;

        return Mathf.Repeat(targetYaw - nativeYaw, 360f);
    }

    /// <summary>
    /// v1.6: фактическое направление полотна в префабе, снятое с геометрии в
    /// identity-позе: длинная горизонтальная сторона габаритов = полотно,
    /// знак центра = куда оно смотрит от пивота. Геометрии нет (пустой
    /// префаб) — valid=false, используется ручная ось doorLeafAxis.
    /// pivotCentered: пивот в ЦЕНТРЕ полотна (геометрия уходит в обе
    /// стороны) — такую створку генератор центрирует на проём.
    /// </summary>
    private static LeafFrame DetectLeafFrame(GameObject leaf)
    {
        LeafFrame frame;
        frame.valid = false;
        frame.alongX = true;
        frame.dirSign = 1f;
        frame.pivotCentered = false;

        Bounds bounds = ComputeLeafBounds(leaf);
        Vector3 origin = leaf.transform.position;

        float sizeX = bounds.size.x;
        float sizeZ = bounds.size.z;

        if (Mathf.Max(sizeX, sizeZ) < 0.05f)
        {
            return frame; // геометрии нет — ручная ось doorLeafAxis
        }

        frame.valid = true;
        frame.alongX = sizeX >= sizeZ;

        float min = frame.alongX ? bounds.min.x - origin.x
                                 : bounds.min.z - origin.z;
        float max = frame.alongX ? bounds.max.x - origin.x
                                 : bounds.max.z - origin.z;

        frame.dirSign = (min + max) >= 0f ? 1f : -1f;
        frame.pivotCentered = min < -0.02f && max > 0.02f;

        return frame;
    }

    /// <summary>Габариты полотна в префабе: ось, знак и пивот (см. DetectLeafFrame).</summary>
    private struct LeafFrame
    {
        public bool valid;
        public bool alongX;
        public float dirSign;
        public bool pivotCentered;
    }

    /// <summary>Стабильный порядок рёбер: ось → X → Z.</summary>
    private static int CompareWallEdges(
        GridPositioningSystem.WallEdgeKey a,
        GridPositioningSystem.WallEdgeKey b)
    {
        if (a.axis != b.axis) return ((int)a.axis).CompareTo((int)b.axis);
        if (a.x != b.x) return a.x.CompareTo(b.x);
        return a.z.CompareTo(b.z);
    }

    // ============================================================
    //  ПОСТАНОВКА СТЕН (чистая геометрия, как в GridSnapEditor)
    // ============================================================

    /// <summary>
    /// Ставит инстанс на ребро: центр геометрии WallPiece — на середину ребра,
    /// длинная ось — вдоль ребра. Якорь — WallPiece (Component-API): BaseCounter
    /// на стене не нужен (v1.1). РЕЕСТРЫ СЕТКИ НЕ ТРОГАЮТСЯ: в edit mode они
    /// пусты, запись туда оставляла бы фантомные рёбра после ручного удаления
    /// стен до запуска игры.
    /// </summary>
    private static void PlaceWallOnEdge(
        GridPositioningSystem grid,
        GameObject wallInstance,
        GridPositioningSystem.WallEdgeKey edge)
    {
        bool edgeAlongX = edge.axis == GridPositioningSystem.WallEdgeAxis.AlongX;
        Vector3 midpoint = grid.GetWallEdgeMidpoint(edge);
        Transform t = wallInstance.transform;

        Quaternion alignedRotation =
            Quaternion.Euler(0f, ComputeEdgeAlignedYaw(t, edgeAlongX), 0f);

        WallPiece piece = wallInstance.GetComponent<WallPiece>();
        Vector3 center;

        if (piece != null && WallPiece.TryGetGeometryCenter(piece, out center))
        {
            // Локальный оффсет «пивот → центр геометрии» в текущей ориентации,
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
    /// ★ v1.1: АБСОЛЮТНЫЙ yaw, при котором длинная ось геометрии ложится вдоль ребра.
    /// Две ошибки v1.0 исправлены:
    /// 1) якорь — геометрия WallPiece (Component), а не BaseCounter: после удаления
    ///    BaseCounter со стены v1.0 возвращал 0 для ЛЮБОЙ стены — вдоль Z-рёбер
    ///    стены ложились поперёк;
    /// 2) yaw СОХРАНЯЕТ «четверть» текущего поворота (0/90/180/270) и добавляет 90
    ///    только когда длинная ось смотрит не вдоль ребра. v1.0 возвращал абсолютные
    ///    0/90 — у префабов с поворотом 90/270 оси инвертировались. Семантика та же,
    ///    что во встроенном FurnitureMovingController.GetWallAlignedYaw.
    /// У свежего инстанса (PrefabUtility.InstantiatePrefab) текущий поворот — это
    /// поворот самого префаба, поэтому генерация работает с любым префабом.
    /// </summary>
    private static float ComputeEdgeAlignedYaw(Component wall, bool edgeAlongX)
    {
        // Текущий поворот инстанса, приведённый к ближайшему прямому углу
        // (префабы с некратными 90° углами не поддерживаются — см. WallPiece).
        float currentYaw = NormalizeAngleTo360(wall.transform.rotation.eulerAngles.y);
        int quarter = Mathf.RoundToInt(currentYaw / 90f);
        float snappedYaw = NormalizeAngleTo360(quarter * 90f);

        bool longAxisIsWorldX = WallRunsAlongX(wall);

        return (longAxisIsWorldX == edgeAlongX)
            ? snappedYaw
            : NormalizeAngleTo360(snappedYaw + 90f);
    }

    /// <summary>
    /// ★ v1.1: текущая ориентация стены — true, если длинная ось геометрии смотрит
    /// вдоль мирового X. Локальное зеркало GridSnapEditor.WallRunsAlongX (v2.5):
    /// размеры берутся из геометрии (BoxCollider отдаёт их в локальных величинах),
    /// четверть текущего поворота показывает, куда плечо смотрит сейчас.
    /// САМ ПОВОРОТ СТЕНЫ ЭТА ФУНКЦИЯ НЕ МЕНЯЕТ.
    /// </summary>
    private static bool WallRunsAlongX(Component wall)
    {
        if (!WallPiece.TryGetGeometryBounds(wall, out Bounds bounds))
        {
            return true; // оси геометрии неизвестны — не инвертируем поворот
        }

        float currentYaw = NormalizeAngleTo360(wall.transform.rotation.eulerAngles.y);
        int quarter = Mathf.RoundToInt(currentYaw / 90f) % 2;

        return (quarter == 0)
            ? bounds.size.x >= bounds.size.z
            : bounds.size.z >= bounds.size.x;
    }

    private static float NormalizeAngleTo360(float angle)
    {
        return ((angle % 360f) + 360f) % 360f;
    }

    /// <summary>
    /// ★ v1.1: рёбра, занятые стенами, уже стоящими в сцене (кроме снесённого корня).
    /// Ищем по WallPiece — стены БЕЗ BaseCounter тоже блокируют рёбра (v1.0 молча
    /// пропускал их и мог поставить генерированную стену поверх ручной). Ребро
    /// выводится локальным зеркалом GridPositioningSystem.DeriveWallEdgeFromCurrentPosition
    /// (та же математика, что в GridSnapEditor v2.5): геометрический центр +
    /// ориентация длинной оси.
    /// </summary>
    private static HashSet<GridPositioningSystem.WallEdgeKey> CollectExistingWallEdges(
        GridPositioningSystem grid)
    {
        HashSet<GridPositioningSystem.WallEdgeKey> edges =
            new HashSet<GridPositioningSystem.WallEdgeKey>();

        WallPiece[] walls = Object.FindObjectsByType<WallPiece>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (WallPiece wp in walls)
        {
            if (wp == null) continue;

            edges.Add(DeriveWallEdgeLocal(grid, wp));
        }

        return edges;
    }

    /// <summary>
    /// Локальное зеркало GridPositioningSystem.DeriveWallEdgeFromCurrentPosition для
    /// Component: стены в редакторе живут БЕЗ BaseCounter, а версия GPS принимает
    /// только BaseCounter. Логика идентична v1.6 (и зеркалу в GridSnapEditor v2.5):
    /// геометрический центр (падает на середину ребра при снапе) + ориентация
    /// длинной оси по размерам геометрии.
    /// </summary>
    private static GridPositioningSystem.WallEdgeKey DeriveWallEdgeLocal(
        GridPositioningSystem grid, Component wall)
    {
        Vector3 probe = wall.transform.position;

        if (WallPiece.TryGetGeometryCenter(wall, out Vector3 center))
        {
            probe = center;
        }

        float size = grid.GetCellSize();
        Vector3 origin = grid.GetGridOrigin();
        Vector2Int bounds = grid.GetGridBounds();

        if (WallRunsAlongX(wall))
        {
            int lineZ = Mathf.Clamp(Mathf.RoundToInt((probe.z - origin.z) / size), 0, bounds.y);
            int cellX = Mathf.Clamp(Mathf.FloorToInt((probe.x - origin.x) / size), 0, bounds.x - 1);
            return new GridPositioningSystem.WallEdgeKey(cellX, lineZ, GridPositioningSystem.WallEdgeAxis.AlongX);
        }

        int lineX = Mathf.Clamp(Mathf.RoundToInt((probe.x - origin.x) / size), 0, bounds.x);
        int cellZ = Mathf.Clamp(Mathf.FloorToInt((probe.z - origin.z) / size), 0, bounds.y - 1);
        return new GridPositioningSystem.WallEdgeKey(lineX, cellZ, GridPositioningSystem.WallEdgeAxis.AlongZ);
    }

    // ============================================================
    //  v1.2: КОМНАТА-КУХНЯ + СТАНЦИИ И СТОЛЫ
    // ============================================================

    /// <summary>Сторона комнаты, к которой примыкает клетка при расстановке станций.</summary>
    private enum RoomSide
    {
        North = 0,
        South = 1,
        West = 2,
        East = 3
    }

    /// <summary>Клетка у стены комнаты + сторона, к которой она примыкает.</summary>
    private struct SideCell
    {
        public Vector2Int cell;
        public RoomSide side;
    }

    /// <summary>
    /// v1.2: оркестратор расстановки мебели. v1.7: работает с клеточными
    /// наборами комнат — комнаты могут быть ломаными (полиомино). Вызывается
    /// ПОСЛЕ постановки стен и ПОСЛЕ сноса предыдущего здания. Все rng-вызовы
    /// мебели идут ПОСЛЕ стен и дверей, поэтому стены и двери одного сида не
    /// зависят от списков мебели.
    /// </summary>
    private void PlaceFurniture(
        GridPositioningSystem grid,
        GameObject root,
        List<HashSet<Vector2Int>> roomCells,
        HashSet<Vector2Int> buildingCellSet,
        Dictionary<Vector2Int, int> cellOwner,
        HashSet<GridPositioningSystem.WallEdgeKey> doorSet,
        Vector2Int? entranceCell,
        System.Random rng,
        out int kitchenIndex,
        out int entranceRoomIndex,
        out int stationsPlaced,
        out int stationsMissing,
        out int tablesPlaced,
        out int tablesMissing)
    {
        kitchenIndex = -1;
        entranceRoomIndex = -1;
        stationsPlaced = 0;
        stationsMissing = 0;
        tablesPlaced = 0;
        tablesMissing = 0;

        HashSet<Vector2Int> kitchenCells = buildingCellSet;
        List<HashSet<Vector2Int>> diningCells = new List<HashSet<Vector2Int>>();

        if (buildInteriorWalls && roomCells.Count > 0)
        {
            // Комната, в которую ведёт вход с улицы, — всегда зал: она
            // исключается из кандидатов в кухню (ChooseKitchenIndex).
            // v1.7: владелец читается из клеточной модели по клетке входа.
            if (entranceCell.HasValue &&
                cellOwner.TryGetValue(entranceCell.Value, out int entranceOwner))
            {
                entranceRoomIndex = entranceOwner;
            }

            kitchenIndex = ChooseKitchenIndex(roomCells, rng, entranceRoomIndex);
            kitchenCells = roomCells[kitchenIndex];

            for (int i = 0; i < roomCells.Count; i++)
            {
                if (i != kitchenIndex)
                {
                    diningCells.Add(roomCells[i]);
                }
            }
        }

        // Занятые клетки: дверные проходы + чужая мебель. Один набор на ВСЮ
        // мебель — станции и столы не наступают друг другу на клетки.
        HashSet<Vector2Int> taken = CollectBlockedCells(grid, doorSet, keepDoorCellsFree);

        if (HasPendingEntries(stationEntries))
        {
            GameObject stationsRoot = new GameObject("BSP Stations");
            Undo.RegisterCreatedObjectUndo(stationsRoot, "BSP Stations");
            stationsRoot.transform.SetParent(root.transform, false);

            stationsPlaced = PlaceStationsInKitchen(
                grid, kitchenCells, buildingCellSet, cellOwner, taken,
                stationsRoot.transform, rng, out stationsMissing);
        }

        if (HasPendingEntries(tableEntries))
        {
            GameObject tablesRoot = new GameObject("BSP Tables");
            Undo.RegisterCreatedObjectUndo(tablesRoot, "BSP Tables");
            tablesRoot.transform.SetParent(root.transform, false);

            tablesPlaced = PlaceTablesInDiningRooms(
                grid, diningCells, taken, tablesRoot.transform, rng, out tablesMissing);
        }
    }

    /// <summary>
    /// Выбирает комнату-кухню: самая большая по ЧИСЛУ КЛЕТОК (v1.7 — комнаты
    /// полиомино) / случайная / первая. Зал у входа с улицы (excludeIndex) из
    /// кандидатов исключён — первая от входа комната всегда остаётся залом.
    /// При равенстве — первая по списку (детерминизм). Если кроме зала
    /// кандидатов нет (одна комната в здании), кухня — она же.
    /// </summary>
    private int ChooseKitchenIndex(
        List<HashSet<Vector2Int>> roomCells,
        System.Random rng,
        int excludeIndex)
    {
        List<int> candidates = new List<int>();

        for (int i = 0; i < roomCells.Count; i++)
        {
            if (i != excludeIndex)
            {
                candidates.Add(i);
            }
        }

        if (candidates.Count == 0) return 0;

        if (kitchenMode == KitchenPickMode.FirstRoom) return candidates[0];
        if (kitchenMode == KitchenPickMode.RandomRoom) return candidates[rng.Next(candidates.Count)];

        int best = candidates[0];

        for (int c = 1; c < candidates.Count; c++)
        {
            int i = candidates[c];

            if (roomCells[i].Count > roomCells[best].Count)
            {
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// Клетки, куда мебель ставить нельзя:
    /// - по клетке с обеих сторон каждого дверного проёма (проходы свободны);
    /// - клетки уже стоящих в сцене станций/столов. Стены (WallPiece) не считаются —
    ///   они живут на рёбрах и клетки не занимают. Вызывается ПОСЛЕ сноса
    ///   предыдущего здания, поэтому его мебель уже не попадает.
    /// </summary>
    private HashSet<Vector2Int> CollectBlockedCells(
        GridPositioningSystem grid,
        HashSet<GridPositioningSystem.WallEdgeKey> doorSet,
        bool reserveDoorCells)
    {
        HashSet<Vector2Int> blocked = new HashSet<Vector2Int>();

        if (reserveDoorCells)
        {
            foreach (GridPositioningSystem.WallEdgeKey edge in doorSet)
            {
                if (edge.axis == GridPositioningSystem.WallEdgeAxis.AlongX)
                {
                    // AlongX-ребро (x, z) — граница между клетками (x, z - 1) и (x, z).
                    blocked.Add(new Vector2Int(edge.x, edge.z - 1));
                    blocked.Add(new Vector2Int(edge.x, edge.z));
                }
                else
                {
                    // AlongZ-ребро (x, z) — граница между клетками (x - 1, z) и (x, z).
                    blocked.Add(new Vector2Int(edge.x - 1, edge.z));
                    blocked.Add(new Vector2Int(edge.x, edge.z));
                }
            }
        }

        BaseCounter[] existing = Object.FindObjectsByType<BaseCounter>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (BaseCounter counter in existing)
        {
            if (counter == null || WallPiece.IsWall(counter)) continue;

            blocked.Add(grid.GetGridPosition(counter.transform.position));
        }

        return blocked;
    }

    /// <summary>
    /// v1.7: клетки комнаты, примыкающие к её ФАКТИЧЕСКИМ стенам — соседняя
    /// клетка в этом направлении либо вне здания (улица), либо чужая комната.
    /// Порядок сторон перемешан rng (как раньше), клетки обходятся по
    /// возрастанию координат — ряды вдоль ступеней выстраиваются аккуратно.
    /// Для клетки берётся ПЕРВАЯ стена по перемешанному порядку сторон (углы
    /// не дублируются). Клетки из taken (дверные проходы, чужая мебель) в
    /// кандидаты не попадают.
    /// </summary>
    private static List<SideCell> BuildWallAdjacentCandidates(
        HashSet<Vector2Int> roomCellSet,
        HashSet<Vector2Int> buildingCellSet,
        Dictionary<Vector2Int, int> cellOwner,
        System.Random rng,
        HashSet<Vector2Int> taken)
    {
        List<SideCell> result = new List<SideCell>();

        if (roomCellSet == null || roomCellSet.Count == 0) return result;

        // Владелец один на всю комнату — берём с первой клетки.
        int owner = -1;

        foreach (Vector2Int probe in roomCellSet)
        {
            owner = cellOwner[probe];
            break;
        }

        List<RoomSide> sides = new List<RoomSide>
            { RoomSide.North, RoomSide.South, RoomSide.West, RoomSide.East };

        Shuffle(sides, rng);

        List<Vector2Int> sortedCells = new List<Vector2Int>(roomCellSet);
        sortedCells.Sort((a, b) => a.x != b.x
            ? a.x.CompareTo(b.x)
            : a.y.CompareTo(b.y));

        foreach (Vector2Int cell in sortedCells)
        {
            if (taken.Contains(cell)) continue;

            foreach (RoomSide side in sides)
            {
                Vector2Int neighbor = SideNeighbor(cell, side);

                bool isWall = !buildingCellSet.Contains(neighbor) ||
                              cellOwner[neighbor] != owner;

                if (isWall)
                {
                    SideCell candidate = new SideCell { cell = cell, side = side };
                    result.Add(candidate);
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>Соседняя клетка в сторону side.</summary>
    private static Vector2Int SideNeighbor(Vector2Int cell, RoomSide side)
    {
        if (side == RoomSide.North) return cell + Vector2Int.up;
        if (side == RoomSide.South) return cell + Vector2Int.down;
        if (side == RoomSide.West) return cell + Vector2Int.left;
        return cell + Vector2Int.right;
    }

    /// <summary>Все клетки комнаты из её клеточного набора (v1.7), отсортированы.</summary>
    private static List<Vector2Int> CollectInteriorCells(HashSet<Vector2Int> roomCellSet)
    {
        List<Vector2Int> cells = new List<Vector2Int>(roomCellSet);
        cells.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        return cells;
    }

    /// <summary>Fisher–Yates на System.Random — детерминизм по сиду.</summary>
    private static void Shuffle<T>(List<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int swap = rng.Next(i + 1);
            T tmp = list[i];
            list[i] = list[swap];
            list[swap] = tmp;
        }
    }

    /// <summary>
    /// yaw станции у стороны side — лицом ВНУТРЬ комнаты, с поправкой на «лицо»
    /// префаба (stationFront): итог = yaw стороны − смещение фронта.
    /// yaw 0 смотрит вдоль +Z, 90 — вдоль +X, 180 — вдоль -Z, 270 — вдоль -X.
    /// </summary>
    private float ComputeStationYaw(RoomSide side)
    {
        float facingYaw;

        if (side == RoomSide.North)
        {
            facingYaw = 180f;   // стена сверху — лицом вниз (-Z)
        }
        else if (side == RoomSide.South)
        {
            facingYaw = 0f;     // стена снизу — лицом вверх (+Z)
        }
        else if (side == RoomSide.West)
        {
            facingYaw = 90f;    // стена слева — лицом вправо (+X)
        }
        else
        {
            facingYaw = 270f;   // стена справа — лицом влево (-X)
        }

        return NormalizeAngleTo360(facingYaw - FrontOffset(stationFront));
    }

    /// <summary>Смещение yaw под «лицо» префаба станции.</summary>
    private static float FrontOffset(PrefabFront front)
    {
        if (front == PrefabFront.PlusX) return 90f;
        if (front == PrefabFront.MinusZ) return 180f;
        if (front == PrefabFront.MinusX) return 270f;
        return 0f;
    }

    /// <summary>
    /// Станции из списка — в кухню (v1.7: набор клеток, possibly ломаная):
    /// сначала клетки у фактических стен лицом в комнату, затем любые
    /// свободные клетки (случайный прямой угол). Возврат — сколько
    /// поставлено; missing — сколько не влезло из-за занятых клеток.
    /// </summary>
    private int PlaceStationsInKitchen(
        GridPositioningSystem grid,
        HashSet<Vector2Int> kitchenCells,
        HashSet<Vector2Int> buildingCellSet,
        Dictionary<Vector2Int, int> cellOwner,
        HashSet<Vector2Int> taken,
        Transform parent,
        System.Random rng,
        out int missing)
    {
        missing = 0;

        List<FurnitureEntry> pending = new List<FurnitureEntry>();

        foreach (FurnitureEntry entry in stationEntries)
        {
            if (entry != null && entry.prefab != null && entry.count > 0)
            {
                pending.Add(entry);
            }
        }

        if (pending.Count == 0 || kitchenCells.Count == 0) return 0;

        int wanted = 0;
        int placed = 0;

        List<SideCell> wallCandidates = stationsAlongWalls
            ? BuildWallAdjacentCandidates(kitchenCells, buildingCellSet, cellOwner, rng, taken)
            : new List<SideCell>();

        List<Vector2Int> freeCells = CollectInteriorCells(kitchenCells);
        freeCells.RemoveAll(cell => taken.Contains(cell));
        Shuffle(freeCells, rng);

        int wallCursor = 0;
        int freeCursor = 0;

        foreach (FurnitureEntry entry in pending)
        {
            wanted += entry.count;

            for (int i = 0; i < entry.count; i++)
            {
                // Ряды у стен: пропускаем клетки, занятые уже поставленными.
                while (wallCursor < wallCandidates.Count &&
                       taken.Contains(wallCandidates[wallCursor].cell))
                {
                    wallCursor++;
                }

                if (wallCursor < wallCandidates.Count)
                {
                    SideCell candidate = wallCandidates[wallCursor];
                    wallCursor++;

                    PlaceOneStation(grid, entry.prefab, parent,
                        candidate.cell, ComputeStationYaw(candidate.side));

                    taken.Add(candidate.cell);
                    placed++;
                    continue;
                }

                // Запасные клетки: любая свободная внутри кухни.
                while (freeCursor < freeCells.Count && taken.Contains(freeCells[freeCursor]))
                {
                    freeCursor++;
                }

                if (freeCursor >= freeCells.Count) break;

                Vector2Int cell = freeCells[freeCursor];
                freeCursor++;

                PlaceOneStation(grid, entry.prefab, parent, cell, rng.Next(4) * 90f);

                taken.Add(cell);
                placed++;
            }
        }

        missing = wanted - placed;
        return placed;
    }

    /// <summary>Один инстанс станции: центр ячейки + рассчитанный yaw. Высота —
    /// как у префаба (вертикально генератор ничего не двигает).</summary>
    private static GameObject PlaceOneStation(
        GridPositioningSystem grid,
        GameObject prefab,
        Transform parent,
        Vector2Int cell,
        float yaw)
    {
        GameObject instance = InstantiateFurniture(prefab, parent, "BSP station");

        Vector3 center = grid.GetWorldPosition(cell);
        instance.transform.SetPositionAndRotation(
            new Vector3(center.x, instance.transform.position.y, center.z),
            Quaternion.Euler(0f, yaw, 0f));

        return instance;
    }

    /// <summary>
    /// Столы из списка — по ВСЕМ столовым комнатам здания (всё, кроме кухни)
    /// одной общей очередью, а не по одному залу за раз (v1.3): свободно
    /// стоящие клетки (v1.7: все 4 соседа в ТОЙ ЖЕ комнате по клеточному
    /// набору и свободны) собираются со всех столовых и перемешиваются
    /// вместе, затем остальные. Поворот не трогается: стулья настроены в
    /// префабе относительно стола.
    /// </summary>
    private int PlaceTablesInDiningRooms(
        GridPositioningSystem grid,
        List<HashSet<Vector2Int>> diningRooms,
        HashSet<Vector2Int> taken,
        Transform parent,
        System.Random rng,
        out int missing)
    {
        missing = 0;

        List<FurnitureEntry> pending = new List<FurnitureEntry>();

        foreach (FurnitureEntry entry in tableEntries)
        {
            if (entry != null && entry.prefab != null && entry.count > 0)
            {
                pending.Add(entry);
            }
        }

        if (pending.Count == 0 || diningRooms.Count == 0) return 0;

        // v1.3: одна общая очередь на всё здание. Раньше очередь строилась
        // по комнатам по порядку — первая столовая забивалась столами под
        // завязку, и только потом они перетекали в следующую. Теперь клетки
        // собираются со всех столовых сразу и рассыпаются по всему зданию.
        List<Vector2Int> freestandingAll = new List<Vector2Int>();
        List<Vector2Int> restAll = new List<Vector2Int>();

        foreach (HashSet<Vector2Int> roomCellSet in diningRooms)
        {
            List<Vector2Int> roomCells = CollectInteriorCells(roomCellSet);
            roomCells.RemoveAll(cell => taken.Contains(cell));

            freestandingAll.AddRange(roomCells.FindAll(
                cell => HasFreeNeighbors(cell, roomCellSet, taken)));
            restAll.AddRange(roomCells.FindAll(
                cell => !HasFreeNeighbors(cell, roomCellSet, taken)));
        }

        Shuffle(freestandingAll, rng);
        Shuffle(restAll, rng);

        List<Vector2Int> queue = new List<Vector2Int>();
        queue.AddRange(freestandingAll);
        queue.AddRange(restAll);

        int wanted = 0;
        int placed = 0;
        int cursor = 0;

        foreach (FurnitureEntry entry in pending)
        {
            wanted += entry.count;

            for (int i = 0; i < entry.count; i++)
            {
                // Пропускаем клетки, занятые уже поставленной мебелью.
                while (cursor < queue.Count && taken.Contains(queue[cursor]))
                {
                    cursor++;
                }

                if (cursor >= queue.Count) break;

                Vector2Int cell = queue[cursor];
                cursor++;

                GameObject instance = InstantiateFurniture(entry.prefab, parent, "BSP table");

                Vector3 center = grid.GetWorldPosition(cell);
                instance.transform.position = new Vector3(
                    center.x, instance.transform.position.y, center.z);

                taken.Add(cell);
                placed++;
            }
        }

        missing = wanted - placed;
        return placed;
    }

    /// <summary>v1.7: все 4 соседа клетки — в ТОЙ ЖЕ комнате (по клеточному набору) и свободны.</summary>
    private static bool HasFreeNeighbors(
        Vector2Int cell, HashSet<Vector2Int> roomCellSet, HashSet<Vector2Int> taken)
    {
        Vector2Int[] neighbors =
        {
            cell + Vector2Int.right,
            cell + Vector2Int.left,
            cell + Vector2Int.up,
            cell + Vector2Int.down
        };

        foreach (Vector2Int neighbor in neighbors)
        {
            if (!roomCellSet.Contains(neighbor)) return false;
            if (taken.Contains(neighbor)) return false;
        }

        return true;
    }

    /// <summary>Есть ли в списке хотя бы одна активная строка (префаб + count &gt; 0).</summary>
    private static bool HasPendingEntries(List<FurnitureEntry> entries)
    {
        foreach (FurnitureEntry entry in entries)
        {
            if (entry != null && entry.prefab != null && entry.count > 0) return true;
        }

        return false;
    }

    /// <summary>
    /// Инстанс префаба мебели: PrefabUtility (связь с ассетом, как у стен) + Undo +
    /// родитель. Позицию и поворот задаёт вызывающий код.
    /// </summary>
    private static GameObject InstantiateFurniture(
        GameObject prefab, Transform parent, string undoName)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(instance, undoName);
        instance.transform.SetParent(parent, true);
        return instance;
    }

    private static void DrawEntryList(List<FurnitureEntry> entries, string noun)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();

            entries[i].prefab = (GameObject)EditorGUILayout.ObjectField(
                entries[i].prefab, typeof(GameObject), false);
            entries[i].count = Mathf.Max(0, EditorGUILayout.IntField(
                entries[i].count, GUILayout.Width(46f)));

            if (GUILayout.Button("×", GUILayout.Width(20f)))
            {
                entries.RemoveAt(i);
            }

            EditorGUILayout.EndHorizontal();
        }

        if (GUILayout.Button("+ Добавить " + noun))
        {
            entries.Add(new FurnitureEntry());
        }
    }

    // ============================================================
    //  СЛУЖЕБНОЕ
    // ============================================================

    /// <summary>Поиск сетки — та же логика, что в GridSnapEditor.FindActiveGrid.</summary>
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

    /// <summary>Корень последнего сгенерированного здания среди загруженных сцен.</summary>
    private static GameObject FindBuildingRoot()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);

            if (!scene.isLoaded) continue;

            GameObject[] roots = scene.GetRootGameObjects();

            foreach (GameObject go in roots)
            {
                if (go != null && go.name == BuildingRootName)
                {
                    return go;
                }
            }
        }

        return null;
    }
}
