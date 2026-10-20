using UnityEngine;
using System.Collections.Generic;

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
/// ★ v2.1 (диагностика и интеграция):
/// - Призрак больше не молчит: если прыжок к ячейке заблокирован, в Console
///   раз в полсекунды пишется ТОЧНАЯ причина — «ячейка занята 'X'» или
///   «стена: коллайдер 'Y' (слой Z)». Это мгновенно вскрывает причины вида
///   «автопостановка перестала работать»: забытый статичный декор, кинематичная
///   створка двери, случайный коллайдер и т.п.
/// - GridPositioningSystem ищется лениво (ResolveInstance): если Instance
///   потерян (объект отключён/задублирован), система восстанавливается сама,
///   а в Console падает явный Warning с причиной.
/// - Перенос DiningTable теперь вызывает DisableNavMeshObstacle/EnableNavMeshObstacle
///   (DiningTable v7 их предоставляет и ожидает этих вызовов): раньше carving-дыра
///   в NavMesh таскалась за столом и ломала пути клиентов после перестановки.
/// - Клавиши переноса глушатся и при открытом черновике карт (IsDraftActive):
///   раньше гасилась только ходьба игрока, а T продолжал работать под гримуаром.
///
/// ★ v2.2 (запрет наложения мебели):
/// - Раньше пересечение мебели с мебелью отслеживала ТОЛЬКО сетка (одна pivot-ячейка
///   на предмет): станция шире ячейки, съехавший с центра pivot или незарегистрированный
///   объект сеткой не ловились — и станции можно было ставить друг в друга.
///   Теперь есть ФИЗИЧЕСКАЯ проверка: призрак не заходит в место, где его бокс
///   пересекает коллайдеры ЧУЖОЙ мебели (BaseCounter/Chair), а подтверждение такой
///   постановки блокируется с логом «место занято мебелью 'X'».
/// - Попутно: KitchenObject (тарелки/ингредиенты на станциях) больше не считается
///   стеной — раньше крупный предмет на соседней станции мог ложно заморозить призрак.
///
/// ★ v2.3 (поворот, подсветка призрака, звуки, постановка покупок):
/// - R — поворот призрака на 90° (шаг настраивается). Проверки стен и чужой
///   мебели стали поворотозависимыми: проверочный бокс вращается вместе с
///   предметом (раньше там стоял Quaternion.identity — с поворотом станцию
///   можно было «просунуть» в стену боком).
/// - Призрак подкрашивается: зелёный — место можно подтвердить, красный —
///   нельзя (стена/чужая мебель). Подкраска через MaterialPropertyBlock:
///   инстансы материалов не плодятся, на выходе из переноса блок снимается.
/// - Звуки: подъём, постановка, отказ (клипы в инспекторе; пусто — тихо).
/// - ПОСТАНОВКА ПОКУПОК: ShopManager больше не бросает купленный предмет в
///   первую попавшуюся ячейку — он передаёт инстанс сюда
///   (BeginPlacingPurchasedItem), и игрок сам ставит его как призрак.
///   Золото списывается ТОЛЬКО после подтверждения места
///   (ShopManager.CompletePendingPurchase); Escape — покупка отменяется,
///   инстанс уничтожается, деньги не тронуты.
///
/// Переключение стульев (убрать/вернуть) сюда не входит — оно висит
/// на обычной кнопке E через DiningTable.Interact(), см. этот файл.
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
             "кроме мебели/стульев/игроков/клиентов/дверного полотна/пола). " +
             "Настройка сцены не требуется. Если выбрать слои — стенами " +
             "считаются ТОЛЬКО они (ручной режим).")]
    [SerializeField] private LayerMask wallMask = 0;
    [Tooltip("Допустимый зазор в метрах. Проверочный бокс сжимается на эту " +
             "величину с каждой стороны — касание стены или соседней мебели " +
             "вплотную не блокирует постановку, только реальное пересечение.")]
    [SerializeField] private float wallClearance = 0.05f;

    [Header("Диагностика")]
    [Tooltip("Писать в Console причину, по которой призрак не идёт в ячейку " +
             "(занята другим объектом / стена). Раз в 0.5 с, только пока блокирует.")]
    [SerializeField] private bool logBlockedReasons = true;

    [Header("Поворот мебели (★ v2.3)")]
    [Tooltip("Клавиша поворота призрака во время переноса и постановки покупки")]
    [SerializeField] private KeyCode rotateFurnitureKey = KeyCode.R;
    [Tooltip("Шаг поворота в градусах. 90 — классика для сетки; мебель с асимметричным " +
             "пивотом лучше смотрится именно на кратных 90 поворотах")]
    [SerializeField] private float rotationStepDegrees = 90f;

    [Header("Подсветка призрака (★ v2.3)")]
    [Tooltip("Цвет, в который подмешивается окраска мебели, когда место ПОДХОДИТ")]
    [SerializeField] private Color validGhostTint = new Color(0.35f, 1f, 0.45f, 1f);
    [Tooltip("Цвет, когда место занято стеной/мебелью — подтверждение будет заблокировано")]
    [SerializeField] private Color invalidGhostTint = new Color(1f, 0.3f, 0.25f, 1f);
    [Range(0f, 1f)]
    [Tooltip("Сила подкраски: 0 — не красить вовсе, 1 — полностью перекрыть родной цвет")]
    [SerializeField] private float ghostTintStrength = 0.5f;

    [Header("Звуки (★ v2.3)")]
    [Tooltip("Звук подъёма мебели (и выдачи покупки на постановку). Пусто — без звука")]
    [SerializeField] private AudioClip pickupSound;
    [Tooltip("Звук успешной постановки")]
    [SerializeField] private AudioClip placeSound;
    [Tooltip("Звук отказа (подтверждение на недопустимом месте)")]
    [SerializeField] private AudioClip blockedSound;

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

    // ★ v2.1: кэш сетки (ленивое восстановление через ResolveInstance)
    // и состояние троттлинга диагностических логов.
    private GridPositioningSystem grid;
    private float blockLogTimer;
    private Vector2Int lastBlockedCell;
    private bool hasBlockedCell;

    // ★ v2.1: последний коллайдер-«стена», найденный проверкой, — для лога причины.
    private Collider lastWallHit;

    // ★ v2.2: последний коллайдер ЧУЖОЙ мебели, найденный проверкой наложения, — для лога причины.
    private Collider lastFurnitureHit;

    // ★ v2.1: ссылка на стол с NavMeshObstacle, поднятый прямо сейчас, — чтобы
    // гарантированно вернуть obstacle при подтверждении И при отмене.
    private DiningTable liftedTableWithObstacle;

    // ★ v2.3: поворот призрака. capturedRotation — поворот предмета в МОМЕНТ
    // подъёма: смещение проверочного бокса (wallBoxOffset) снималось именно в
    // этой ориентации. Чтобы получить корректный оффсет в текущей ориентации,
    // домножаем на дельту «текущий поворот * inverse(захваченный)».
    private Quaternion capturedRotation = Quaternion.identity;

    // ★ v2.3: подсветка призрака. Рендеры и их родные цвета снимаются один раз
    // при подъёме; MaterialPropertyBlock не плодит инстансы материалов,
    // а на выходе из переноса снимается целиком (SetPropertyBlock(null)).
    private Renderer[] movingRenderers = new Renderer[0];
    private readonly List<Color> originalRendererColors = new List<Color>();
    private readonly List<bool> originalColorUsesBaseColor = new List<bool>();
    private bool? lastGhostValid; // null = ещё не красили в этот перенос
    private MaterialPropertyBlock tintBlock;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP/HDRP
    private static readonly int ColorId = Shader.PropertyToID("_Color");         // Built-in

    // ★ v2.3: режим постановки КУПЛЕННОГО предмета. Отличается от обычного
    // переноса тем, что: (1) предмет freshly-спавнен из префаба магазина;
    // (2) деньги ещё НЕ списаны — списание происходит после подтверждения места
    // через ShopManager.CompletePendingPurchase; (3) Escape уничтожает инстанс,
    // а не возвращает его на исходное место.
    private ShopItemSO purchasedItemContext;

    /// <summary>★ v2.3: идёт ли сейчас постановка купленного в магазине предмета.</summary>
    public bool IsPlacingPurchase => purchasedItemContext != null;

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
        // ★ v2.1: магазин теперь честно Push'ит блокировку, а черновик карт
        // гейтится напрямую через IsDraftActive (он блокировку не pus'ит).
        if (GameLoopManager.Instance.IsUiInputLocked)
        {
            return;
        }

        if (UpgradeManager.Instance != null && UpgradeManager.Instance.IsDraftActive)
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

            // ★ v2.3: поворот работает и при переносе, и при постановке покупки.
            if (Input.GetKeyDown(rotateFurnitureKey))
            {
                RotateGhost();
            }

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

    /// <summary>
    /// ★ v2.1: ленивое получение сетки с самовосстановлением. Если Instance
    /// потерян (объект GridPositioningSystem отключён/удалён/задублирован),
    /// ResolveInstance ищет его по сцене и пишет явный Warning — молчаливого
    /// «перестало работать» больше не бывает.
    /// </summary>
    private bool EnsureGrid()
    {
        if (grid != null)
        {
            return true;
        }

        grid = GridPositioningSystem.ResolveInstance();
        return grid != null;
    }

    /// <summary>
    /// ★ v2.3: поворачивает призрак на один шаг. Валидность места после
    /// поворота могла измениться — цвет призрака обновится в UpdateGhostPosition
    /// этого же/следующего кадра, а подтверждение честно перепроверяет физику.
    /// </summary>
    private void RotateGhost()
    {
        if (movingCounter == null) return;

        movingCounter.transform.Rotate(0f, rotationStepDegrees, 0f);
    }

    /// <summary>
    /// ★ v2.3: дельта поворота «текущий − захваченный». Смещение центра
    /// проверочного бокса (wallBoxOffset) снималось в ориентации capturedRotation,
    /// поэтому в текущей ориентации его нужно довернуть этой дельтой.
    /// </summary>
    private Quaternion GetCurrentBoxRotation()
    {
        return movingCounter != null
            ? movingCounter.transform.rotation * Quaternion.Inverse(capturedRotation)
            : Quaternion.identity;
    }

    /// <summary>
    /// ★ v2.3: снимает рендеры предмета и их родные цвета (до первой подкраски).
    /// Ключ цвета зависит от пайплайна: _BaseColor (URP/HDRP) или _Color (Built-in).
    /// </summary>
    private void CaptureRenderersForTint()
    {
        originalRendererColors.Clear();
        originalColorUsesBaseColor.Clear();

        movingRenderers = movingCounter.GetComponentsInChildren<Renderer>(true);

        foreach (Renderer rend in movingRenderers)
        {
            Material sharedMat = rend != null ? rend.sharedMaterial : null;

            if (sharedMat != null && sharedMat.HasProperty(BaseColorId))
            {
                originalRendererColors.Add(sharedMat.GetColor(BaseColorId));
                originalColorUsesBaseColor.Add(true);
            }
            else if (sharedMat != null && sharedMat.HasProperty(ColorId))
            {
                originalRendererColors.Add(sharedMat.GetColor(ColorId));
                originalColorUsesBaseColor.Add(false);
            }
            else
            {
                // Нестандартный шейдер — красить нечем, но индексы держим параллельно.
                originalRendererColors.Add(Color.white);
                originalColorUsesBaseColor.Add(false);
            }
        }

        lastGhostValid = null;
    }

    /// <summary>★ v2.3: подкрасить призрак зелёным/красным. Перекрашиваем только при смене состояния.</summary>
    private void ApplyGhostTint(bool valid)
    {
        if (lastGhostValid.HasValue && lastGhostValid.Value == valid) return;

        lastGhostValid = valid;

        if (tintBlock == null) tintBlock = new MaterialPropertyBlock();

        Color tint = valid ? validGhostTint : invalidGhostTint;

        for (int i = 0; i < movingRenderers.Length; i++)
        {
            Renderer rend = movingRenderers[i];

            if (rend == null) continue;

            rend.GetPropertyBlock(tintBlock);
            tintBlock.SetColor(
                originalColorUsesBaseColor[i] ? BaseColorId : ColorId,
                Color.Lerp(originalRendererColors[i], tint, ghostTintStrength));
            rend.SetPropertyBlock(tintBlock);
        }
    }

    /// <summary>★ v2.3: снять подкраску — предмет снова выглядит как обычно.</summary>
    private void ClearGhostTint()
    {
        for (int i = 0; i < movingRenderers.Length; i++)
        {
            if (movingRenderers[i] != null)
            {
                movingRenderers[i].SetPropertyBlock(null);
            }
        }

        movingRenderers = new Renderer[0];
        lastGhostValid = null;
    }

    /// <summary>★ v2.3: проиграть клип в мировой точке. Пустой клип — тихо, без ошибок.</summary>
    private static void PlaySoundAt(AudioClip clip, Vector3 position)
    {
        if (clip == null) return;

        AudioSource.PlayClipAtPoint(clip, position);
    }

    /// <summary>
    /// ★ v2.3: начать ПОСТАНОВКУ КУПЛЕННОГО предмета. Вызывается ShopManager'ом
    /// сразу после Instantiate префаба товара. Деньги ещё НЕ списаны: они спишутся
    /// только при успешном подтверждении места (см. TryConfirmPlacement), а Escape
    /// уничтожит инстанс без списания.
    /// </summary>
    public bool BeginPlacingPurchasedItem(ShopItemSO item, BaseCounter instance)
    {
        if (item == null || instance == null)
        {
            return false;
        }

        if (IsMoving)
        {
            Debug.LogWarning("[FurnitureMoving] Нельзя начать постановку покупки — " +
                             "сейчас уже переносится другая мебель.");
            return false;
        }

        movingCounter = instance;
        purchasedItemContext = item;
        originalWorldPosition = instance.transform.position;
        capturedRotation = instance.transform.rotation;

        // Купленному столу глушим NavMeshObstacle на время призрака — та же
        // логика, что при обычном переносе. При отмене obstacle умрёт вместе
        // с объектом, при подтверждении вернётся штатно.
        liftedTableWithObstacle = instance as DiningTable;

        if (liftedTableWithObstacle != null)
        {
            liftedTableWithObstacle.DisableNavMeshObstacle();
        }

        // Awake инстанса уже зарегистрировал его в сетке на точке спавна.
        // Снимаем регистрацию: предмет «в воздухе», ячейку забронирует только
        // подтверждение. Иначе точка спавна осталась бы занятой фантомно.
        if (EnsureGrid())
        {
            grid.RemoveCounter(instance);
        }

        movingColliders = instance.GetComponentsInChildren<Collider>();

        CaptureWallBox();

        foreach (Collider col in movingColliders)
        {
            col.enabled = false;
        }

        CaptureRenderersForTint();

        hasBlockedCell = false;
        lastWallHit = null;
        lastFurnitureHit = null;

        PlaySoundAt(pickupSound, instance.transform.position);

        Debug.Log(
            $"[FurnitureMoving] Куплен '{item.displayName}'. Поставь его: " +
            $"{moveFurnitureKey} — поставить (списание {item.cost}g), " +
            $"{rotateFurnitureKey} — повернуть, {cancelKey} — отменить покупку.");

        return true;
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

        // ★ v2.3: фиксируем ориентацию на момент подъёма — от неё считается
        // дельта поворота проверочного бокса (см. GetCurrentBoxRotation).
        capturedRotation = selectedCounter.transform.rotation;

        // ★ v2.1: переносимому столу глушим NavMeshObstacle — иначе carving-дыра
        // в NavMesh поедет за столом и не успеет перестроиться (DiningTable v7
        // создаёт эти методы именно для этого вызова).
        liftedTableWithObstacle = movingCounter as DiningTable;

        if (liftedTableWithObstacle != null)
        {
            liftedTableWithObstacle.DisableNavMeshObstacle();
        }

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

        // ★ v2.3: снимаем рендеры для подкраски призрака (зелёный/красный).
        CaptureRenderersForTint();

        LogWallCheckDiagnostics();

        hasBlockedCell = false;
        lastWallHit = null;
        lastFurnitureHit = null;

        PlaySoundAt(pickupSound, selectedCounter.transform.position);

        Debug.Log(
            $"[FurnitureMoving] '{movingCounter.name}' поднят. " +
            $"{moveFurnitureKey} — поставить, {rotateFurnitureKey} — повернуть, {cancelKey} — отменить.");
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
        if (Player.Instance == null || !EnsureGrid())
        {
            return;
        }

        Vector3 aheadPoint =
            Player.Instance.transform.position +
            Player.Instance.transform.forward * placementDistance;

        Vector2Int candidateCell = grid.GetGridPosition(aheadPoint);

        if (grid.IsCellFreeFor(candidateCell, movingCounter))
        {
            hasBlockedCell = false;

            Vector3 snapped = grid.GetWorldPosition(candidateCell);

            snapped.y = originalWorldPosition.y;

            // ★ Автокоррекция стены: в ячейку, пересекающую стену, призрак НЕ
            // заходит — он остаётся на текущем (уже корректном) месте. Со стороны
            // это выглядит как «мебель упёрлась в стену», позиция всегда валидна
            // и её можно сразу подтвердить.
            // ★ v2.2: то же самое для ЧУЖОЙ мебели — призрак не заходит в место,
            // где его бокс пересекает коллайдеры другой станции/стула.
            if (!IsPlacementBlocked(snapped))
            {
                movingCounter.transform.position = snapped;
            }
            else
            {
                LogBlockedPlacementReason(snapped);
            }
        }
        else
        {
            LogBlockedCellReason(candidateCell);
        }

        // ★ v2.3: цвет призрака — честный ответ на вопрос «подтвердится ли
        // место ПРЯМО СЕЙЧАС» (ячейка свободна И бокс ни во что не упирается).
        Vector3 ghostPosition = movingCounter.transform.position;
        bool placeable =
            grid.IsCellFreeFor(grid.GetGridPosition(ghostPosition), movingCounter) &&
            !IsPlacementBlocked(ghostPosition);

        ApplyGhostTint(placeable);
    }

    /// <summary>
    /// ★ v2.1: почему призрак не идёт в ячейку — «занята объектом X».
    /// Троттлинг: не чаще раза в 0.5 с и только пока блокирует та же ячейка.
    /// </summary>
    private void LogBlockedCellReason(Vector2Int cell)
    {
        if (!logBlockedReasons)
        {
            return;
        }

        blockLogTimer -= Time.deltaTime;

        bool sameCell = hasBlockedCell && lastBlockedCell.Equals(cell);

        if (sameCell && blockLogTimer > 0f)
        {
            return;
        }

        blockLogTimer = 0.5f;
        lastBlockedCell = cell;
        hasBlockedCell = true;

        string occupierName = "неизвестно";

        if (grid.TryGetCellOwner(cell, out BaseCounter owner) && owner != null)
        {
            occupierName = owner.gameObject.name;
        }

        Debug.Log($"[FurnitureMoving] Ячейка {cell} занята '{occupierName}' — призрак ждёт свободную.");
    }

    /// <summary>
    /// ★ v2.1: почему призрак не идёт в позицию — «стена: коллайдер 'X' (слой Y)»
    /// или (v2.2) «место занято мебелью 'X'». Заполняется внутри IsOverlappingWall /
    /// IsOverlappingOtherFurniture, сбрасывается в IsPlacementBlocked.
    /// </summary>
    private void LogBlockedPlacementReason(Vector3 testPosition)
    {
        if (!logBlockedReasons)
        {
            return;
        }

        blockLogTimer -= Time.deltaTime;

        // grid может быть ещё не разрешён (проверка на подтверждении идёт до EnsureGrid) —
        // сравнение «та же ячейка» просто не делаем.
        bool samePlace = hasBlockedCell && grid != null &&
                         lastBlockedCell.Equals(grid.GetGridPosition(testPosition));

        if (samePlace && blockLogTimer > 0f)
        {
            return;
        }

        blockLogTimer = 0.5f;

        if (grid != null)
        {
            lastBlockedCell = grid.GetGridPosition(testPosition);
        }

        hasBlockedCell = true;

        // ★ v2.2: два рода причин — чужая мебель и стена. Формулировка зависит
        // от того, что именно нашлось (оба поля чистятся в IsPlacementBlocked,
        // поэтому «протухших» значений с прошлого кадра здесь не бывает).
        if (lastFurnitureHit != null)
        {
            Debug.Log($"[FurnitureMoving] Место занято мебелью: '{lastFurnitureHit.gameObject.name}' " +
                      $"(объект '{lastFurnitureHit.transform.root.name}') — призрак ждёт свободную ячейку.");
            return;
        }

        string wallDescription = "не найден (пересечение без имени)";

        if (lastWallHit != null)
        {
            wallDescription =
                $"'{lastWallHit.gameObject.name}' " +
                $"(слой {LayerMask.LayerToName(lastWallHit.gameObject.layer)}, " +
                $"объект '{lastWallHit.transform.root.name}')";
        }

        Debug.Log($"[FurnitureMoving] Место блокирует стена/препятствие: {wallDescription}. " +
                  "Если это НЕ стена — объект попал в АВТО-фильтр стен, см. чеклист.");
    }

    private void TryConfirmPlacement()
    {
        if (movingCounter == null)
        {
            return;
        }

        Vector3 candidatePos = movingCounter.transform.position;

        // ★ Страховка на подтверждении. Штатно призрак не заходит ни в стену,
        // ни в чужую мебель, но проверка защищает от любых будущих путей
        // постановки (смена пивота, ресайз коллайдера, правки UpdateGhostPosition).
        // ★ v2.2: теперь единая проверка — стена ИЛИ чужая мебель.
        if (IsPlacementBlocked(candidatePos))
        {
            LogBlockedPlacementReason(candidatePos);

            Debug.Log("[FurnitureMoving] Здесь мебель ставить нельзя — стена или уже стоящая мебель.");

            PlaySoundAt(blockedSound, candidatePos);

            return;
        }

        if (!EnsureGrid())
        {
            Debug.LogWarning("[FurnitureMoving] GridPositioningSystem не найден — поставить мебель нельзя. См. чеклист сцены.");
            return;
        }

        if (grid.TryPlaceCounter(movingCounter, candidatePos))
        {
            Debug.Log(
                $"[FurnitureMoving] '{movingCounter.name}' размещён на новом месте.");

            // ★ v2.1: возвращаем столу NavMeshObstacle ДО FinishMoving —
            // NavMesh перестроится под новой позицией, клиенты снова обходят стол.
            if (liftedTableWithObstacle != null)
            {
                liftedTableWithObstacle.EnableNavMeshObstacle();
                liftedTableWithObstacle = null;
            }

            // Сначала FinishMoving, потом проверка стульев: FinishMoving
            // возвращает коллайдеры переносимой мебели — без них мебель невидима
            // для физики, и стулья соседних столов не поняли бы, что им тесно.
            FinishMoving();

            // ★ v2.3: если это была ПОКУПКА — только теперь списываем золото.
            // Контекст сбрасываем ДО вызова, чтобы CompletePendingPurchase
            // не увидел «висящую» покупку при реентерабельности.
            if (purchasedItemContext != null)
            {
                ShopItemSO purchased = purchasedItemContext;
                purchasedItemContext = null;

                if (ShopManager.Instance != null)
                {
                    ShopManager.Instance.CompletePendingPurchase(purchased);
                }
                else
                {
                    Debug.LogError(
                        "[FurnitureMoving] ShopManager не найден — не удалось списать золото " +
                        $"за '{purchased.displayName}'. Предмет остаётся у игрока бесплатно.");
                }
            }

            // Проверяем стулья ВСЕХ столов, а не только переставленного:
            // новая мебель (стол, печь, прилавок) могла перекрыть чужие стулья.
            // Сами столы решают, убирать ли стул, через DiningTable.RemoveChairsWithoutRoom.
            RecheckAllTableChairs();

            PlaySoundAt(placeSound, candidatePos);
        }
        else
        {
            Debug.Log(
                "[FurnitureMoving] Это место занято — выбери другую ячейку.");

            PlaySoundAt(blockedSound, candidatePos);
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
        lastWallHit = null;

        if (!wallBoxValid)
        {
            return false;
        }

        Vector3 half = wallBoxHalfExtents;
        half.x = Mathf.Max(0.01f, half.x - wallClearance);
        half.y = Mathf.Max(0.01f, half.y - wallClearance);
        half.z = Mathf.Max(0.01f, half.z - wallClearance);

        // ★ v2.3: бокс вращается вместе с предметом — смещение центра и сама
        // ориентация бокса зависят от текущего поворота призрака.
        Quaternion boxRotation = GetCurrentBoxRotation();
        Vector3 center = testPosition + boxRotation * wallBoxOffset;

        // ── Ручной режим: только выбранные слои ──
        if (wallMask.value != 0)
        {
            int layerHits = Physics.OverlapBoxNonAlloc(
                center,
                half,
                wallOverlapBuffer,
                boxRotation,
                wallMask,
                QueryTriggerInteraction.Ignore);

            if (layerHits > 0 && wallOverlapBuffer[0] != null)
            {
                lastWallHit = wallOverlapBuffer[0];
            }

            return layerHits > 0;
        }

        // ── АВТО-режим: стены определяются сами ──
        int hits = Physics.OverlapBoxNonAlloc(
            center,
            half,
            wallOverlapBuffer,
            boxRotation, // ★ v2.3: повернут вместе с предметом
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
            // ★ ВАЖНО: если створка двери ВДРУГ стала «стеной» — проверь,
            // не стоит ли у её Rigidbody галка Is Kinematic (замок PhysicalDoor
            // тоже делает тело кинематичным: startLocked / SetLocked(true)).
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
            // замораживать перестановку мебели. Ловит и капсулу CustomerCollision,
            // и любые дочерние коллайдеры префаба гостя.
            if (hit.GetComponentInParent<CustomerAI>() != null)
            {
                continue;
            }

            // ★ v2.2: предмет на станции (тарелка, ингредиент) — не стена:
            // крупный KitchenObject на соседней станции раньше ложно
            // замораживал призрак рядом с ней.
            if (hit.GetComponentInParent<KitchenObject>() != null)
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
            lastWallHit = hit;
            return true;
        }

        return false;
    }

    /// <summary>
    /// ★ v2.2: полная проверка места постановки — стена ИЛИ чужая мебель.
    /// Единственная точка входа для призрака, подтверждения и гизмо:
    /// оба поля-«причины» чистятся в начале, поэтому лог всегда честный.
    /// </summary>
    private bool IsPlacementBlocked(Vector3 testPosition)
    {
        lastWallHit = null;
        lastFurnitureHit = null;

        if (IsOverlappingWall(testPosition))
        {
            return true;
        }

        return IsOverlappingOtherFurniture(testPosition);
    }

    /// <summary>
    /// ★ v2.2: пересекает ли проверочный бокс предмета в позиции testPosition
    /// коллайдеры ЧУЖОЙ мебели (BaseCounter/Chair).
    ///
    /// Зачем это нужно, если есть сетка: GridPositioningSystem знает только ОДНУ
    /// pivot-ячейку на предмет. Станция шире ячейки, pivot съехал с центра,
    /// объект не зарегистрирован (конфликт при старте сцены) — сетка его не видит,
    /// и раньше станции ставились друг в друга. Физическая проверка ловит ВСЕ
    /// эти случаи, потому что смотрит на коллайдеры напрямую.
    ///
    /// Свои коллайдеры (и стулья переносимого стола — они его дети) выключены
    /// на время переноса и дополнительно отсекаются по иерархии — ложного
    /// самопересечения нет. Игрок/клиенты/динамические тела — не мебель,
    /// их блокировку (если нужно) делает IsOverlappingWall.
    /// </summary>
    private bool IsOverlappingOtherFurniture(Vector3 testPosition)
    {
        lastFurnitureHit = null;

        if (!wallBoxValid)
        {
            return false;
        }

        Vector3 half = wallBoxHalfExtents;
        half.x = Mathf.Max(0.01f, half.x - wallClearance);
        half.y = Mathf.Max(0.01f, half.y - wallClearance);
        half.z = Mathf.Max(0.01f, half.z - wallClearance);

        // ★ v2.3: тот же поворотозависимый бокс, что и в проверке стен.
        Quaternion boxRotation = GetCurrentBoxRotation();
        Vector3 center = testPosition + boxRotation * wallBoxOffset;

        int hits = Physics.OverlapBoxNonAlloc(
            center,
            half,
            wallOverlapBuffer,
            boxRotation,
            ~0, // все слои — мебель может стоять на любом
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits; i++)
        {
            Collider hit = wallOverlapBuffer[i];

            if (hit == null)
            {
                break;
            }

            // Свой предмет целиком (страховка: коллайдеры выключены, но мало ли).
            if (IsOwnCollider(hit) || hit.transform.IsChildOf(movingCounter.transform))
            {
                continue;
            }

            // Чужая мебель — именно её мы и ловим.
            BaseCounter otherCounter = hit.GetComponentInParent<BaseCounter>();

            if (otherCounter != null && otherCounter != movingCounter)
            {
                lastFurnitureHit = hit;
                return true;
            }

            // Стул чужого стола — тоже мебель: станцию на стул ставить нельзя.
            // (Стулья переносимого стола — его дети, отсечены проверкой выше.)
            if (hit.GetComponentInParent<Chair>() != null)
            {
                lastFurnitureHit = hit;
                return true;
            }

            // Всё остальное — не мебель: стены/пол/игрок/клиенты проверяет
            // IsOverlappingWall, мебельных причин у этого хита нет.
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

        // ★ v2.3: центр бокса с учётом поворота предмета.
        Vector3 center = movingCounter.transform.position +
                         GetCurrentBoxRotation() * wallBoxOffset;

        Gizmos.color = IsPlacementBlocked(movingCounter.transform.position)
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
        // ★ v2.3: отмена ПОКУПКИ — инстанс уничтожается, деньги НЕ списаны.
        // Должна обрабатываться ДО обычной ветки: возвращать нечего — предмета
        // в сцене «не было», мы сами его заспавнили из префаба товара.
        if (purchasedItemContext != null)
        {
            ShopItemSO cancelled = purchasedItemContext;
            purchasedItemContext = null;

            // Obstacle умрёт вместе с объектом — ссылку чистим заранее,
            // чтобы не дёргать методы уничтоженного компонента.
            liftedTableWithObstacle = null;

            Vector3 destroyPosition = movingCounter != null
                ? movingCounter.transform.position
                : Vector3.zero;

            if (movingCounter != null)
            {
                Destroy(movingCounter.gameObject);
            }

            FinishMoving();

            PlaySoundAt(blockedSound, destroyPosition);

            Debug.Log(
                $"[FurnitureMoving] Покупка '{cancelled.displayName}' отменена — предмет удалён, " +
                $"золото не списано. Открыть магазин заново: {KeyCode.B}.");

            return;
        }

        if (movingCounter != null)
        {
            movingCounter.transform.position = originalWorldPosition;

            Debug.Log(
                $"[FurnitureMoving] Перенос отменён, '{movingCounter.name}' вернулся на место.");
        }

        // ★ v2.1: столу, у которого глушали obstacle, возвращаем его и при отмене.
        if (liftedTableWithObstacle != null)
        {
            liftedTableWithObstacle.EnableNavMeshObstacle();
            liftedTableWithObstacle = null;
        }

        FinishMoving();
    }

    private void FinishMoving()
    {
        // ★ v2.3: снимаем подкраску призрака — предмет снова выглядит обычно.
        ClearGhostTint();

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
        hasBlockedCell = false;
        lastWallHit = null;
        lastFurnitureHit = null;
    }
}
