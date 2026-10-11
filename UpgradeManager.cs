using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Раз в день (кроме первого — OnDayChanged стреляет со 2-го) предлагает cardsPerDraft
/// случайных, ещё не полученных карт улучшений.
///
/// UI выбора карт (UpgradeDraftUI) подписывается на событие OnOfferChanged:
/// - новый черновик → UI строит карточки из GetCurrentOffer();
/// - PickCard / SkipDraft → currentOffer очищается → UI скрывается сам.
///
/// МОДАЛЬНОСТЬ: пока черновик открыт (IsDraftActive == true), ПЕРСОНАЖ заморожен
/// гейтом в Player.Update — он не ходит, не выбирает станции, не тянет мебель,
/// буферизованные E/F у него тоже глушатся.
/// ВАЖНО: Action Map "Player" при этом ОСТАЁТСЯ ВКЛЮЧЁННОЙ! Клавиши A/D и E — это
/// действия Move и Interact с той самой карты, и они нужны самому черновику
/// (см. «Управление черновиком» ниже). Блокировка игрока целиком живёт в гейтах
/// Player.cs, а НЕ в GameInput.DisablePlayerInput().
///
/// Управление черновиком с клавиатуры:
/// - A / D  — листать подсветку по карточкам. Читается СЫРОЕ значение действия Move
///   (GameInput.GetRawMoveVector) с edge-детектом и автоповтором. Тот же Move, на
///   котором висит ходьба: когда в будущем стик геймпада будет привязан к Move
///   (обычно уже привязан), навигация заработает на геймпаде без правок кода.
/// - E      — подтвердить выбор. Это событие OnInteractAction (Interact), то же,
///   что взаимодействие со столами: Player во время черновика его игнорирует
///   (гейт), а UpgradeManager берёт карту. Кнопка South (A/Cross), привязанная
///   к Interact, подтвердит выбор на геймпаде тоже без правок кода.
/// - Escape — пропустить черновик. Осознанно легаси-клавиша: это клавиша UI,
///   а не геймплея, и она обязана работать при любом состоянии Action Map.
/// Мышь работает как раньше: клик по карточке = взять её.
///
/// Эффекты применяются через центральный switch в ApplyEffect() — по образцу
/// "enum + применятор". Простые эффекты трогают поля Player/GameLoopManager/
/// CustomerManager напрямую; синергетические карты копят МОДИФИКАТОРЫ здесь,
/// а другие системы (StoveCounter, CustomerAI, DiningTable, DeliveryManager) читают
/// их через публичные геттеры. Связь односторонняя: системы не знают, какие карты
/// взяты, а карты не знают про системы — поэтому добавить новую карту, выражаемую
/// существующими модификаторами, можно без правок чужих систем.
///
/// Синергии (Большие компании + Большие порции, Быстрый огонь + Спешащие гости и т.п.)
/// НЕ запрограммированы — они возникают сами из стека модификаторов, как в PlateUp.
///
/// Алхимический хаос: каждый день один рецепт из chaosRecipeListSO меняет один
/// "сырой" ингредиент (из chaosIngredientPool) на другой. Мутирует РАНТАЙМ-копия
/// RecipeSO (Instantiate) — оригинальные ассеты никогда не изменяются, иначе правка
/// пережила бы выход из Play-режима в редакторе. Ограничение "подменять только
/// ингредиенты из пула" гарантирует, что mutated-заказ остаётся приготовимым:
/// подменяются лишь предметы, которые игрок берёт с ContainerCounter, а не
/// промежуточные продукты котла/резки.
/// </summary>
public class UpgradeManager : MonoBehaviour
{
    public static UpgradeManager Instance { get; private set; }

    [SerializeField] private UpgradeCardListSO upgradeCardListSO;
    [SerializeField] private int cardsPerDraft = 3;

    [Header("Навигация A/D по карточкам")]
    [Tooltip("Порог отклонения оси Move, при котором A/D считается нажатой (для стика геймпада)")]
    [SerializeField] private float navAxisThreshold = 0.5f;
    [Tooltip("Сколько секунд нужно держать A/D до начала автоповтора")]
    [SerializeField] private float navRepeatDelay = 0.45f;
    [Tooltip("Интервал автоповтора шага при удержании A/D")]
    [SerializeField] private float navRepeatInterval = 0.14f;

    [Header("Алхимический хаос")]
    [Tooltip("Список рецептов, который мутирует хаос. Обычно тот же ассет RecipeListSO, что назначен клиентам (CustomerAI).")]
    [SerializeField] private RecipeListSO chaosRecipeListSO;
    [Tooltip("Пул СЫРЫХ ингредиентов для подмены — только те, что игрок может взять с ContainerCounter в сцене. Минимум 2, иначе хаос не сработает.")]
    [SerializeField] private List<KitchenObjectSO> chaosIngredientPool = new List<KitchenObjectSO>();

    /// <summary>
    /// UI-событие: состав предложения изменился (новый черновик / карта взята / скип).
    /// Подписчик перестраивается из GetCurrentOffer(); пустой список = «скрыть панель».
    /// </summary>
    public event EventHandler OnOfferChanged;

    /// <summary>
    /// UI-событие: сменилась подсвеченная (выбранная клавишами A/D) карточка.
    /// Подписчик (UpgradeDraftUI) подсвечивает карточку с этим индексом.
    /// </summary>
    public event EventHandler<OnSelectionChangedEventArgs> OnSelectionChanged;
    public class OnSelectionChangedEventArgs : EventArgs
    {
        public int selectedIndex;
        public int offerCount;
    }

    /// <summary>
    /// True, пока на столе есть непройденный черновик (гримуар открыт). По этому флагу
    /// GameLoopManager блокирует старт дня (Enter), а Player замораживает персонажа.
    /// </summary>
    public bool IsDraftActive => currentOffer.Count > 0;

    private readonly List<UpgradeCardSO> ownedCards = new List<UpgradeCardSO>();
    private readonly List<UpgradeCardSO> currentOffer = new List<UpgradeCardSO>();

    // Индекс карточки, подсвеченной клавиатурной навигацией A/D. Для мыши не важен:
    // клик берёт карту напрямую, подсветка лишь следует за A/D.
    private int selectedDraftIndex;

    // Состояние навигации A/D: сырое значение оси X на прошлом кадре (edge-детект)
    // и таймеры автоповтора при удержании.
    private float previousNavX;
    private float navHeldTime;
    private float navRepeatTimer;

    // === Модификаторы синергетических карт (стартовые значения = "карт нет") ===
    private float rewardBonus = 0f;             // Большие порции + Спешащие гости: +доля к выплате
    private float patienceMultiplier = 1f;      // Спешащие гости: множитель терпения (< 1)
    private float cookSpeedMultiplier = 1f;     // Быстрый огонь: перемешивание/варка быстрее
    private float burnRateMultiplier = 1f;      // Быстрый огонь: блюдо горит быстрее
    private float cauldronPriceMultiplier = 1f; // Нестабильный котёл: блюда из котла дороже
    private int cauldronExtraStirs = 0;         // Нестабильный котёл: доп. перемешиваний на варку
    private float sameDishChance = 0f;          // Общий заказ: шанс скопировать заказ соседа
    private int bigPortionsExtraDishes = 0;     // Большие порции: доп. блюд на каждую группу
    private bool alchemyChaosActive = false;    // Алхимический хаос включён

    // Актуальный список заказов дня: либо оригинальный, либо с мутировавшей копией.
    private readonly List<RecipeSO> chaosOrderList = new List<RecipeSO>();

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (GameLoopManager.Instance != null)
        {
            GameLoopManager.Instance.OnDayChanged += GameLoopManager_OnDayChanged;

            // Загрузка сейва с днём >= 2: OnDayChanged при старте не стреляет
            // (событие поднимается только при переходе на СЛЕДУЮЩИЙ день),
            // поэтому первый черновик предлагаем вручную.
            if (GameLoopManager.Instance.IsPreparationActive() && GameLoopManager.Instance.GetCurrentDay() > 1)
            {
                RebuildChaosOrders();
                OfferDraft();
                OnOfferChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        // E = «подтвердить выбранную карту», пока черновик открыт.
        // GameInput персистентный (BootScene) — к GameScene он уже существует.
        // Player подписан на то же событие, но во время черновика его гейт
        // игнорирует нажатие — конфликтов нет.
        if (GameInput.Instance != null)
        {
            GameInput.Instance.OnInteractAction += GameInput_OnInteractAction;
        }
    }

    private void OnDestroy()
    {
        if (GameLoopManager.Instance != null)
        {
            GameLoopManager.Instance.OnDayChanged -= GameLoopManager_OnDayChanged;
        }

        if (GameInput.Instance != null)
        {
            GameInput.Instance.OnInteractAction -= GameInput_OnInteractAction;
        }
    }

    private void GameLoopManager_OnDayChanged(object sender, EventArgs e)
    {
        // Хаос мутирует рецепт на новый день — до раздачи черновика, чтобы логи дня
        // шли в естественном порядке: хаос → черновик.
        RebuildChaosOrders();

        OfferDraft();

        // UI перестраивается в любом случае: есть карты — покажет черновик,
        // пусто — скроется (например, когда пул карт исчерпан).
        OnOfferChanged?.Invoke(this, EventArgs.Empty);
    }

    private void GameInput_OnInteractAction(object sender, EventArgs e)
    {
        // E во время черновика = подтвердить подсвеченную карту.
        // В остальные фазы этот хендлер молчит: взаимодействие со столами
        // обрабатывает Player через то же событие.
        if (currentOffer.Count == 0) return;
        if (GameLoopManager.Instance == null || !GameLoopManager.Instance.IsPreparationActive()) return;

        ConfirmSelection();
    }

    private void Update()
    {
        if (currentOffer.Count == 0) return;

        // Страховка от «вечного замка»: если подготовка кончилась при открытом
        // черновике (любой будущий путь кода), принудительно закрываем его —
        // карта просто не берётся. Штатно не срабатывает: StartCountdown
        // не даёт начать день, пока черновик открыт.
        if (GameLoopManager.Instance == null || !GameLoopManager.Instance.IsPreparationActive())
        {
            SkipDraft();
            return;
        }

        // A/D — листать подсветку по карточкам
        HandleDraftNavigation();

        // Escape — пропустить черновик с клавиатуры (то же, что кнопка «Пропустить»).
        // Легаси-ввод осознанно: клавиша UI, работает при любом состоянии Action Map.
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            SkipDraft();
        }
    }

    /// <summary>
    /// Навигация A/D по карточкам черновика. Читаем СЫРУЮ ось X действия Move:
    /// клавиши A/D дают ровно -1/+1, стик геймпада — аналоговое значение.
    /// Одно нажатие = один шаг; при удержании включается автоповтор
    /// (navRepeatDelay → navRepeatInterval) — так навигация одинаково комфортна
    /// и на клавиатуре, и на стике геймпада.
    /// </summary>
    private void HandleDraftNavigation()
    {
        Vector2 rawMove = GameInput.Instance != null ? GameInput.Instance.GetRawMoveVector() : Vector2.zero;
        float navX = rawMove.x; // A = -1, D = +1 (клавиатура); стик — аналогово

        // --- Одноразовый шаг: ось пересекла порог с прошлого кадра ---
        bool pressedNow = Mathf.Abs(navX) >= navAxisThreshold;
        bool wasPressed = Mathf.Abs(previousNavX) >= navAxisThreshold;

        if (pressedNow && !wasPressed)
        {
            MoveSelection(navX > 0f ? 1 : -1);
            navHeldTime = 0f;
            navRepeatTimer = 0f;
        }
        else if (pressedNow && wasPressed)
        {
            // --- Автоповтор при удержании (нужно для стика) ---
            navHeldTime += Time.deltaTime;

            if (navHeldTime >= navRepeatDelay)
            {
                navRepeatTimer += Time.deltaTime;
                if (navRepeatTimer >= navRepeatInterval)
                {
                    MoveSelection(navX > 0f ? 1 : -1);
                    navRepeatTimer = 0f;
                }
            }
        }
        else
        {
            // Ось отпущена — сбрасываем удержание
            navHeldTime = 0f;
            navRepeatTimer = 0f;
        }

        previousNavX = navX;
    }

    // ===== Публичное API черновика (UI-клики, клавиатура, будущий геймпад) =====

    /// <summary>
    /// Индекс карточки, подсвеченной клавиатурной навигацией A/D.
    /// </summary>
    public int GetSelectedDraftIndex() => selectedDraftIndex;

    /// <summary>
    /// Сдвигает подсветку на direction шагов (A = -1, D = +1). По краям цикл:
    /// с последней карточки на первую и наоборот. Стреляет OnSelectionChanged.
    /// </summary>
    public void MoveSelection(int direction)
    {
        if (currentOffer.Count == 0) return;
        if (GameLoopManager.Instance == null || !GameLoopManager.Instance.IsPreparationActive()) return;

        int count = currentOffer.Count;
        selectedDraftIndex = (selectedDraftIndex + direction % count + count) % count;

        OnSelectionChanged?.Invoke(this, new OnSelectionChangedEventArgs
        {
            selectedIndex = selectedDraftIndex,
            offerCount = count
        });
    }

    /// <summary>
    /// Подтвердить выбор (E): берёт карточку под подсвеченным индексом.
    /// </summary>
    public void ConfirmSelection()
    {
        if (currentOffer.Count == 0) return;

        PickCard(selectedDraftIndex);
    }

    private void OfferDraft()
    {
        currentOffer.Clear();

        if (upgradeCardListSO == null || upgradeCardListSO.upgradeCardSOList == null) return;

        List<UpgradeCardSO> pool = new List<UpgradeCardSO>();
        foreach (UpgradeCardSO card in upgradeCardListSO.upgradeCardSOList)
        {
            if (card != null && !ownedCards.Contains(card))
            {
                pool.Add(card);
            }
        }

        // Перемешиваем весь доступный пул и берём первые cardsPerDraft — так карты
        // внутри одного предложения гарантированно не повторяются.
        for (int i = 0; i < pool.Count; i++)
        {
            int swapIndex = UnityEngine.Random.Range(i, pool.Count);
            (pool[i], pool[swapIndex]) = (pool[swapIndex], pool[i]);
        }

        int offerCount = Mathf.Min(cardsPerDraft, pool.Count);
        for (int i = 0; i < offerCount; i++)
        {
            currentOffer.Add(pool[i]);
        }

        if (currentOffer.Count == 0)
        {
            Debug.Log("[UpgradeManager] Свободных карт для предложения больше нет.");
            return;
        }

        // Черновик открыт: подсветка на первой карточке, состояние навигации чистое
        // (иначе «залипший» с ходьбы A/D дёрнул бы подсветку в первом же кадре).
        selectedDraftIndex = 0;
        previousNavX = 0f;
        navHeldTime = 0f;
        navRepeatTimer = 0f;

        OnSelectionChanged?.Invoke(this, new OnSelectionChangedEventArgs
        {
            selectedIndex = selectedDraftIndex,
            offerCount = currentOffer.Count
        });

        string log = "[UpgradeManager] Новые карты дня:\n";
        for (int i = 0; i < currentOffer.Count; i++)
        {
            log += $"  {i + 1}) {currentOffer[i].cardName} — {currentOffer[i].GetEffectSummary()}\n";
        }
        log += "Выбор: A/D — листать, E — взять (или мышкой), Escape — пропустить.";
        Debug.Log(log);
    }

    /// <summary>
    /// Взять карту по индексу предложения. Общий путь для мыши (гримуар) и клавиатуры
    /// (E после A/D). По завершении очищает currentOffer и стреляет OnOfferChanged —
    /// UI скроется сам, а Player разблокирует персонажа (гейт IsDraftActive отпустит).
    /// </summary>
    public void PickCard(int index)
    {
        if (index < 0 || index >= currentOffer.Count) return;
        if (GameLoopManager.Instance != null && !GameLoopManager.Instance.IsPreparationActive()) return;

        UpgradeCardSO picked = currentOffer[index];
        ApplyEffect(picked);
        ownedCards.Add(picked);
        currentOffer.Clear();

        Debug.Log($"[UpgradeManager] Взята карта: {picked.cardName}.");
        OnOfferChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Пропустить черновик: карта не берётся, предложение закрывается до следующего дня.
    /// </summary>
    public void SkipDraft()
    {
        if (currentOffer.Count == 0) return;

        currentOffer.Clear();
        Debug.Log("[UpgradeManager] Черновик пропущен — карты дня остались невзятыми.");
        OnOfferChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyEffect(UpgradeCardSO card)
    {
        switch (card.effectType)
        {
            case UpgradeEffectType.MoveSpeed:
                if (Player.Instance != null) Player.Instance.IncreaseMoveSpeed(card.value);
                break;

            case UpgradeEffectType.InteractDistance:
                if (Player.Instance != null) Player.Instance.IncreaseInteractDistance(card.value);
                break;

            case UpgradeEffectType.AngryCustomerTolerance:
                if (GameLoopManager.Instance != null)
                    GameLoopManager.Instance.IncreaseAngryCustomerTolerance(Mathf.RoundToInt(card.value));
                break;

            case UpgradeEffectType.UnstableMagic:
                if (Player.Instance != null)
                {
                    Player.Instance.IncreaseCuttingSpeedMultiplier(card.value);
                    Player.Instance.IncreaseCuttingRuinChance(card.secondaryValue);
                }
                break;

            case UpgradeEffectType.PenaltyReduction:
                if (GameLoopManager.Instance != null)
                    GameLoopManager.Instance.ReducePenaltyPerOrder(Mathf.RoundToInt(card.value));
                break;

            case UpgradeEffectType.BonusGold:
                if (GameLoopManager.Instance != null)
                    GameLoopManager.Instance.AddBonusGold(Mathf.RoundToInt(card.value));
                break;

            case UpgradeEffectType.SlowerQuotaGrowth:
                if (CustomerManager.Instance != null)
                    CustomerManager.Instance.ReduceDailyGroupTargetGrowth(Mathf.RoundToInt(card.value));
                break;

            case UpgradeEffectType.DarkDeal:
                // Тёмная сделка (Патч 2): рецепты дешевле, но порог недовольных
                // СНИЖАЕТСЯ — у карты теперь есть реальная цена.
                if (Player.Instance != null) Player.Instance.IncreaseRecipeCostMultiplier(card.value);
                if (GameLoopManager.Instance != null)
                    GameLoopManager.Instance.DecreaseAngryCustomerTolerance(Mathf.RoundToInt(card.secondaryValue));
                break;

            case UpgradeEffectType.BiggerRarerGroups:
                if (CustomerManager.Instance != null)
                {
                    CustomerManager.Instance.IncreaseBaseMaxGroupSize(Mathf.RoundToInt(card.value));
                    CustomerManager.Instance.DecreaseBaseDailyGroupTarget(Mathf.RoundToInt(card.secondaryValue));
                }
                break;

            case UpgradeEffectType.RushHour:
                if (CustomerManager.Instance != null)
                {
                    CustomerManager.Instance.DecreaseBaseSpawnInterval(card.value);
                    CustomerManager.Instance.DecreaseBaseMaxCustomers(Mathf.RoundToInt(card.secondaryValue));
                }
                break;

            // === Синергетический набор (v6) ===

            case UpgradeEffectType.GroupSizeUp:
                // Большие компании: группы крупнее без минусов. Метод уже существовал
                // для BiggerRarerGroups — тут переиспользуем его.
                if (CustomerManager.Instance != null)
                    CustomerManager.Instance.IncreaseBaseMaxGroupSize(Mathf.RoundToInt(card.value));
                break;

            case UpgradeEffectType.BigPortions:
                // Большие порции: каждой группе нужно +value блюд (заказывает стол),
                // награда за любой заказ выше на secondaryValue.
                bigPortionsExtraDishes += Mathf.RoundToInt(card.value);
                rewardBonus += card.secondaryValue;
                break;

            case UpgradeEffectType.SameDishChance:
                // Общий заказ: шанс, что гость скопирует заказ уже севшего соседа.
                sameDishChance = Mathf.Clamp01(sameDishChance + card.value);
                break;

            case UpgradeEffectType.FastFire:
                // Быстрый огонь: перемешивание/варка быстрее (value),
                // но сгорание готового блюда тоже быстрее (secondaryValue).
                cookSpeedMultiplier += card.value;
                burnRateMultiplier += card.secondaryValue;
                break;

            case UpgradeEffectType.HurriedGuests:
                // Спешащие гости: терпение короче на value (не ниже 20%),
                // награда выше на secondaryValue.
                patienceMultiplier = Mathf.Max(0.2f, patienceMultiplier - card.value);
                rewardBonus += card.secondaryValue;
                break;

            case UpgradeEffectType.AlchemyChaos:
                // Алхимический хаос: включает ежедневную мутацию. Сразу строим мутацию,
                // чтобы эффект работал уже в текущей фазе подготовки.
                alchemyChaosActive = true;
                RebuildChaosOrders();
                break;

            case UpgradeEffectType.UnstableCauldron:
                // Нестабильный котёл: +value доп. перемешиваний на каждую варку,
                // блюда из котла дороже на secondaryValue.
                cauldronExtraStirs += Mathf.RoundToInt(card.value);
                cauldronPriceMultiplier += card.secondaryValue;
                break;
        }
    }

    // === Геттеры модификаторов для других систем ===
    // Системы (StoveCounter/CustomerAI/DiningTable/DeliveryManager) читают значения
    // отсюда и НЕ знают, какие карты взяты. Null-проверки в них обязательны:
    // UpgradeManager может отсутствовать в тестовых сценах.

    public float GetRewardBonus() => rewardBonus;                       // +доля к выплате за заказ
    public float GetPatienceMultiplier() => patienceMultiplier;         // множитель терпения гостей
    public float GetCookSpeedMultiplier() => cookSpeedMultiplier;       // скорость перемешивания/варки
    public float GetBurnRateMultiplier() => burnRateMultiplier;         // скорость сгорания блюда
    public float GetCauldronPriceMultiplier() => cauldronPriceMultiplier; // цена блюд из котла
    public int GetCauldronExtraStirs() => cauldronExtraStirs;           // доп. перемешивания на варку
    public float GetSameDishChance() => sameDishChance;                 // шанс общего заказа
    public int GetBigPortionsExtraDishes() => bigPortionsExtraDishes;   // доп. блюд на группу
    public bool IsAlchemyChaosActive() => alchemyChaosActive;

    // === Алхимический хаос ===

    /// <summary>
    /// Пересобирает список заказов дня из ОРИГИНАЛОВ (мутации не копятся между днями),
    /// затем мутирует ровно один рецепт: один ингредиент из пула меняется на другой
    /// из пула. Рантайм-копия через Instantiate — ассет-оригинал не трогаем.
    /// </summary>
    private void RebuildChaosOrders()
    {
        chaosOrderList.Clear();

        if (!alchemyChaosActive) return;

        if (chaosRecipeListSO == null || chaosRecipeListSO.recipeSOList == null)
        {
            Debug.LogWarning("[UpgradeManager] Алхимический хаос куплен, но chaosRecipeListSO не назначен в инспекторе — мутаций не будет.");
            return;
        }

        if (chaosIngredientPool == null || chaosIngredientPool.Count < 2)
        {
            Debug.LogWarning("[UpgradeManager] Алхимический хаос куплен, но в chaosIngredientPool меньше 2 ингредиентов — подменять нечем.");
            return;
        }

        // Стартуем с чистых оригиналов.
        foreach (RecipeSO recipe in chaosRecipeListSO.recipeSOList)
        {
            if (recipe != null) chaosOrderList.Add(recipe);
        }

        // Кандидаты: рецепты, где есть хоть один ингредиент из пула хаоса.
        List<RecipeSO> candidates = new List<RecipeSO>();
        foreach (RecipeSO recipe in chaosRecipeListSO.recipeSOList)
        {
            if (recipe == null || recipe.kitchenObjectSOList == null) continue;

            foreach (KitchenObjectSO item in recipe.kitchenObjectSOList)
            {
                if (item != null && chaosIngredientPool.Contains(item))
                {
                    candidates.Add(recipe);
                    break;
                }
            }
        }

        if (candidates.Count == 0)
        {
            Debug.Log("[UpgradeManager] Алхимический хаос: ни в одном рецепте нет ингредиентов из пула — день без мутаций.");
            return;
        }

        RecipeSO target = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        int targetIndex = chaosOrderList.IndexOf(target);

        int day = GameLoopManager.Instance != null ? GameLoopManager.Instance.GetCurrentDay() : 0;

        RecipeSO mutated = Instantiate(target);
        mutated.name = $"{target.name}_Chaos_D{day}";
        mutated.RecipeName = $"{target.RecipeName} (алхимия)";

        // Меняем ровно один ингредиент, который есть в пуле, на другой из пула.
        List<int> swappableIndices = new List<int>();
        for (int i = 0; i < mutated.kitchenObjectSOList.Count; i++)
        {
            KitchenObjectSO item = mutated.kitchenObjectSOList[i];
            if (item != null && chaosIngredientPool.Contains(item))
            {
                swappableIndices.Add(i);
            }
        }

        int swapIndex = swappableIndices[UnityEngine.Random.Range(0, swappableIndices.Count)];
        KitchenObjectSO oldIngredient = mutated.kitchenObjectSOList[swapIndex];

        List<KitchenObjectSO> replacements = chaosIngredientPool.FindAll(x => x != null && x != oldIngredient);
        KitchenObjectSO newIngredient = replacements[UnityEngine.Random.Range(0, replacements.Count)];

        mutated.kitchenObjectSOList[swapIndex] = newIngredient;
        chaosOrderList[targetIndex] = mutated;

        Debug.Log($"[UpgradeManager] Алхимический хаос дня: '{target.RecipeName}': " +
                  $"{oldIngredient.objectName} -> {newIngredient.objectName}.");
    }

    /// <summary>
    /// Пул рецептов для заказов клиентов. CustomerAI вызывает с "своим" списком:
    /// при активном хаосе и совпадении с настроенным списком возвращается
    /// мутировавший, иначе — оригинальный. Заказы, сделанные ДО мутации, продолжают
    /// жить со своим старым RecipeSO и остаются выполнимыми по старому составу.
    /// </summary>
    public List<RecipeSO> GetOrderRecipePool(RecipeListSO baseList)
    {
        if (alchemyChaosActive && baseList == chaosRecipeListSO && chaosOrderList.Count > 0)
        {
            return chaosOrderList;
        }
        return baseList.recipeSOList;
    }

    public List<UpgradeCardSO> GetOwnedCards() => ownedCards;
    public List<UpgradeCardSO> GetCurrentOffer() => currentOffer;
}