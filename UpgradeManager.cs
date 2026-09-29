using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Раз в день (кроме первого — OnDayChanged стреляет со 2-го) предлагает cardsPerDraft
/// случайных, ещё не полученных карт улучшений.
///
/// Простые эффекты трогают поля Player/GameLoopManager/CustomerManager напрямую;
/// синергетические карты (v6) копят МОДИФИКАТОРЫ здесь, а другие системы
/// (StoveCounter, CustomerAI, DiningTable, DeliveryManager) читают их через геттеры.
/// Синергии НЕ запрограммированы — возникают сами из стека модификаторов.
///
/// Алхимический хаос: каждый день один рецепт меняет один "сырой" ингредиент на
/// другой из пула. Мутирует РАНТАЙМ-копия RecipeSO (Instantiate) — оригинальные
/// ассеты никогда не изменяются, иначе правка пережила бы выход из Play-режима.
/// </summary>
public class UpgradeManager : MonoBehaviour
{
    public static UpgradeManager Instance { get; private set; }

    [SerializeField] private UpgradeCardListSO upgradeCardListSO;
    [SerializeField] private int cardsPerDraft = 3;

    [Header("Алхимический хаос")]
    [Tooltip("Список рецептов, который мутирует хаос. Обычно тот же ассет RecipeListSO, что назначен клиентам (CustomerAI).")]
    [SerializeField] private RecipeListSO chaosRecipeListSO;
    [Tooltip("Пул СЫРЫХ ингредиентов для подмены — только те, что игрок может взять с ContainerCounter в сцене. Минимум 2, иначе хаос не сработает.")]
    [SerializeField] private List<KitchenObjectSO> chaosIngredientPool = new List<KitchenObjectSO>();

    /// <summary>
    /// UI-событие: состав предложения изменился (новый черновик / карта взята / скип).
    /// </summary>
    public event EventHandler OnOfferChanged;

    private readonly List<UpgradeCardSO> ownedCards = new List<UpgradeCardSO>();
    private readonly List<UpgradeCardSO> currentOffer = new List<UpgradeCardSO>();

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
    }

    private void OnDestroy()
    {
        if (GameLoopManager.Instance != null)
        {
            GameLoopManager.Instance.OnDayChanged -= GameLoopManager_OnDayChanged;
        }
    }

    private void GameLoopManager_OnDayChanged(object sender, EventArgs e)
    {
        // Хаос мутирует рецепт на новый день — до раздачи черновика.
        RebuildChaosOrders();

        OfferDraft();

        // UI перестраивается в любом случае: есть карты — покажет черновик,
        // пусто — скроется (например, когда пул карт исчерпан).
        OnOfferChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Update()
    {
        // Резервный ввод цифрами — только пока есть что предложить и идёт подготовка.
        if (currentOffer.Count == 0) return;
        if (GameLoopManager.Instance == null || !GameLoopManager.Instance.IsPreparationActive()) return;

        for (int i = 0; i < currentOffer.Count && i < 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                PickCard(i);
                break;
            }
        }
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

        string log = "[UpgradeManager] Новые карты дня:\n";
        for (int i = 0; i < currentOffer.Count; i++)
        {
            log += $"  {i + 1}) {currentOffer[i].cardName} — {currentOffer[i].GetEffectSummary()}\n";
        }
        log += "Выбери карту в окне гримуара (или клавишей с соответствующей цифрой), пока идёт подготовка.";
        Debug.Log(log);
    }

    /// <summary>
    /// Взять карту по индексу предложения. Общий путь для UI-клика и клавиш 1/2/3.
    /// </summary>
    public void PickCard(int index)
    {
        if (index < 0 || index >= currentOffer.Count) return;

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
                // ПАТЧ 2: заменить IncreaseAngryCustomerTolerance на DecreaseAngryCustomerTolerance
                // (метод появится в GameLoopManager после применения Патча 2).
                if (Player.Instance != null) Player.Instance.IncreaseRecipeCostMultiplier(card.value);
                if (GameLoopManager.Instance != null)
                    GameLoopManager.Instance.IncreaseAngryCustomerTolerance(Mathf.RoundToInt(card.secondaryValue));
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
                // Большие компании: метод уже существовал для BiggerRarerGroups.
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
    // Системы НЕ знают, какие карты взяты. Null-проверки в них обязательны:
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
    /// Пул рецептов для заказов клиентов. При активном хаосе и совпадении с настроенным
    /// списком возвращается мутировавший, иначе — оригинальный. Заказы, сделанные ДО
    /// мутации, продолжают жить со своим старым RecipeSO и остаются выполнимыми.
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