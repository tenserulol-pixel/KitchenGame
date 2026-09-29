using UnityEngine;
using System;
using System.Collections.Generic;

public class DeliveryManager : MonoBehaviour
{
    public event EventHandler OnRecipeSpawned;
    public event EventHandler OnRecipeCompleted;
    public static DeliveryManager Instance { get; private set; }

    public struct Order {
        public RecipeSO recipeSO;
        public DiningTable targetTable;
    }

    private List<Order> waitingOrderList;

    private void Awake()
    {
        waitingOrderList = new List<Order>();
        Instance = this;
    }

    private void Start()
    {
        // КАРТА «Алхимический хаос»: мутация рецептов происходит раз в день —
        // подписываемся на смену дня, чтобы бросок делался в начале подготовки.
        if (GameLoopManager.Instance != null)
        {
            GameLoopManager.Instance.OnDayChanged += GameLoopManager_OnDayChanged;
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
        if (GameLoopManager.Instance != null)
        {
            RecipeChaos.RollForNewDay(GameLoopManager.Instance.GetCurrentDay());
        }
    }

    public void AddOrderFromTable(RecipeSO recipeSO, DiningTable diningTable)
    {
        Order newOrder = new Order {
            recipeSO = recipeSO,
            targetTable = diningTable
        };

        waitingOrderList.Add(newOrder);
        OnRecipeSpawned?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveOrderFromTable(DiningTable diningTable)
    {
        for (int i = 0; i < waitingOrderList.Count; i++)
        {
            if (waitingOrderList[i].targetTable == diningTable)
            {
                waitingOrderList.RemoveAt(i);
                OnRecipeCompleted?.Invoke(this, EventArgs.Empty);
                break;
            }
        }
    }

    public void RemoveOrder(RecipeSO recipeSO, DiningTable diningTable)
    {
        for (int i = 0; i < waitingOrderList.Count; i++)
        {
            if (waitingOrderList[i].targetTable == diningTable && waitingOrderList[i].recipeSO == recipeSO)
            {
                waitingOrderList.RemoveAt(i);
                OnRecipeCompleted?.Invoke(this, EventArgs.Empty);
                break;
            }
        }
    }

    public bool TryDeliverRecipeToTable(PlateKitchenObject plateKitchenObject, DiningTable diningTable)
    {
        if (plateKitchenObject == null || diningTable == null) return false;

        bool foundOrderForTable = false;

        for (int i = 0; i < waitingOrderList.Count; i++)
        {
            Order order = waitingOrderList[i];

            // Ищем заказ, привязанный именно к этому столу
            if (order.targetTable != diningTable) continue;
            foundOrderForTable = true;

            List<KitchenObjectSO> plateContents = plateKitchenObject.GetKitchenObjectSOList();
            List<KitchenObjectSO> recipeContents = order.recipeSO.kitchenObjectSOList;

            bool plateContentsMatchesRecipe = true;

            if (plateContents.Count != recipeContents.Count)
            {
                plateContentsMatchesRecipe = false;
            }
            else
            {
                foreach (KitchenObjectSO recipeKitchenObjectSO in recipeContents)
                {
                    bool ingredientFound = false;
                    foreach (KitchenObjectSO plateKitchenObjectSO in plateContents)
                    {
                        if (plateKitchenObjectSO == recipeKitchenObjectSO)
                        {
                            ingredientFound = true;
                            break;
                        }
                    }
                    if (!ingredientFound)
                    {
                        plateContentsMatchesRecipe = false;
                        break;
                    }
                }
            }

            // Если ингредиенты совпали со спецификацией заказа стола
            if (plateContentsMatchesRecipe)
            {
                // Пытаемся передать еду клиентам за этим столом
                // (клиентский заказ ИЛИ доп. блюдо «Больших порций» — решает DiningTable.TryServe)
                if (diningTable.TryServe(order.recipeSO))
                {
                    waitingOrderList.RemoveAt(i);

                    int payout = CalculatePayout(order.recipeSO, out string breakdown);
                    if (GameLoopManager.Instance != null)
                    {
                        GameLoopManager.Instance.AddOrderGold(payout);
                    }

                    OnRecipeCompleted?.Invoke(this, EventArgs.Empty);

                    plateKitchenObject.DestroySelf();

                    Debug.Log($"[DeliveryManager] УСПЕХ: «{order.recipeSO.RecipeName}» → стол '{diningTable.name}'. " +
                              $"Выплата: {payout} золота ({breakdown}).");
                    return true;
                }
                else
                {
                    Debug.Log($"[DeliveryManager] Совпало с заказом «{order.recipeSO.RecipeName}», но за столом его " +
                              $"некому есть и доп. блюдо уже подано. Блюдо осталось у игрока.");
                }
            }
            else
            {
                Debug.Log($"[DeliveryManager] Несовпадение: ожидалось {recipeContents.Count} [{Names(recipeContents)}] | " +
                          $"на тарелке {plateContents.Count} [{Names(plateContents)}].");
            }
        }

        if (!foundOrderForTable)
        {
            Debug.Log($"[DeliveryManager] У стола '{diningTable.name}' нет активных заказов — подача отклонена.");
        }

        return false;
    }

    /// <summary>
    /// Центральный расчёт выплаты за заказ — ЕДИНСТВЕННОЕ место, где сходятся все
    /// денежные модификаторы карт. Так тултипы/логи не разойдутся с реальной выплатой
    /// (тот же принцип, что и GetEffectSummary в UpgradeCardSO).
    ///
    /// Затронутые карты:
    ///  - «Тёмная сделка» (DarkDeal)        — множитель цены рецептов на Player (уже было);
    ///  - «Большие порции» (BigPortions)    — secondaryValue: +X% к выплате (компенсация за лишнее блюдо);
    ///  - «Спешащие гости» (HastyGuests)    — secondaryValue: +X% к выплате за спешку;
    ///  - «Нестабильный котёл» (UnstableCauldron) — value: +X% к цене блюд.
    /// </summary>
    private int CalculatePayout(RecipeSO recipeSO, out string breakdown)
    {
        float payout = recipeSO.Cost;
        breakdown = $"база {recipeSO.Cost}";

        // «Тёмная сделка» — множитель цены рецептов на Player
        if (Player.Instance != null)
        {
            float costMult = Player.Instance.GetRecipeCostMultiplier();
            if (!Mathf.Approximately(costMult, 1f))
            {
                payout *= costMult;
                breakdown += $" × {costMult:F2} (цена)";
            }
        }

        // КАРТА «Большие порции» — бонус к награде (secondaryValue, например 0.15 = +15%)
        float bigPortionsBonus = GetUpgradeSecondaryValue(UpgradeEffectType.BigPortions);
        if (bigPortionsBonus > 0f)
        {
            payout *= 1f + bigPortionsBonus;
            breakdown += $" × {1f + bigPortionsBonus:F2} (Большие порции)";
        }

        // КАРТА «Спешащие гости» — бонус к награде (secondaryValue, например 0.30 = +30%)
        float hastyBonus = GetUpgradeSecondaryValue(UpgradeEffectType.HurriedGuests);
        if (hastyBonus > 0f)
        {
            payout *= 1f + hastyBonus;
            breakdown += $" × {1f + hastyBonus:F2} (Спешащие гости)";
        }

        // КАРТА «Нестабильный котёл» — надбавка к цене блюд (value, например 0.20 = +20%)
        float cauldronBonus = GetUpgradeValue(UpgradeEffectType.UnstableCauldron);
        if (cauldronBonus > 0f)
        {
            payout *= 1f + cauldronBonus;
            breakdown += $" × {1f + cauldronBonus:F2} (Нестабильный котёл)";
        }

        return Mathf.RoundToInt(payout);
    }

    public List<RecipeSO> GetWaitingRecipeSOList()
    {
        List<RecipeSO> recipeSOList = new List<RecipeSO>();
        foreach (Order order in waitingOrderList)
        {
            recipeSOList.Add(order.recipeSO);
        }
        return recipeSOList;
    }

    public int GetWaitingOrderCount() => waitingOrderList.Count;

    // ==================================================================
    // === ХЕЛПЕРЫ КАРТ (общие для DeliveryManager, DiningTable и RecipeChaos) ===
    // ==================================================================

    /// <summary>Взята ли карта с таким эффектом. Безопасно при отсутствии UpgradeManager.</summary>
    public static bool HasUpgrade(UpgradeEffectType effectType) => FindOwnedCard(effectType) != null;

    /// <summary>card.value у взятой карты с таким эффектом, иначе 0.</summary>
    public static float GetUpgradeValue(UpgradeEffectType effectType)
    {
        UpgradeCardSO card = FindOwnedCard(effectType);
        return card != null ? card.value : 0f;
    }

    /// <summary>card.secondaryValue у взятой карты с таким эффектом, иначе 0.</summary>
    public static float GetUpgradeSecondaryValue(UpgradeEffectType effectType)
    {
        UpgradeCardSO card = FindOwnedCard(effectType);
        return card != null ? card.secondaryValue : 0f;
    }

    private static UpgradeCardSO FindOwnedCard(UpgradeEffectType effectType)
    {
        if (UpgradeManager.Instance == null) return null;

        List<UpgradeCardSO> owned = UpgradeManager.Instance.GetOwnedCards();
        if (owned == null) return null;

        for (int i = 0; i < owned.Count; i++)
        {
            UpgradeCardSO card = owned[i];
            if (card != null && card.effectType == effectType)
            {
                return card;
            }
        }
        return null;
    }

    /// <summary>Имена ингредиентов для диагностических логов: [Mushroom, Tomato, ...].</summary>
    private static string Names(List<KitchenObjectSO> list)
    {
        if (list == null || list.Count == 0) return "пусто";

        string[] names = new string[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            names[i] = list[i] != null ? list[i].name : "null";
        }
        return string.Join(", ", names);
    }
}

// ======================================================================
// === КАРТА «АЛХИМИЧЕСКИЙ ХАОС» — ежедневная мутация рецептов ===
// ======================================================================
//
// Раз в день (пока карта взята) один случайный рецепт меняет один ингредиент:
// например, «Грибной суп» требует FireDust вместо Mushroom. Ключевые решения:
//
//  1. АССЕТЫ НЕ МУТИРУЕМ. RecipeSO в проекте общий — правка его списка меняла бы
//     рецепт навсегда (в редакторе Unity — даже между запусками). Вместо этого на
//     каждый день создаётся РАНТАЙМ-клон рецепта (ScriptableObject.CreateInstance),
//     а все клиенты, заказывающие «пострадавший» рецепт, получают ссылку на клон.
//     Клон живёт один день и исчезает сам (GC) на следующем дне.
//
//  2. ВИЗУАЛ СОГЛАСОВАН АВТОМАТИЧЕСКИ. CustomerAI.SelectRecipe пропускает свой выбор
//     через RecipeChaos.ApplyDailyMutation() — пузырь заказа над головой (DeliveryManagerSingleUI
//     читает kitchenObjectSOList рецепта) и боковой список заказов показывают уже
//     МУТИРОВАННЫЙ ингредиент. Игрок видит то, что реально требуется на тарелку.
//
//  3. ЗАМЕНА ТОЛЬКО НА ОСМЫСЛЕННЫЙ ИНГРЕДИЕНТ. Новый ингредиент берётся из пула
//     ингредиентов ВСЕХ известных рецептов — то есть он физически добывается на кухне —
//     и не должен уже присутствовать в рецепте (иначе тарелку невозможно собрать:
//     PlateKitchenObject не даёт класть два одинаковых ингредиента).
//
//  4. Клон кэшируется на день: все гости, заказавшие «пострадавший» рецепт, ссылаются
//     на ОДИН клон — сравнение при подаче идёт по ссылкам и всегда сходится.
public static class RecipeChaos
{
    // Все рецепты, которые хоть раз встретились (пополняется из CustomerAI.EnsurePool-вызова)
    private static readonly List<RecipeSO> knownRecipes = new List<RecipeSO>();
    // Общий пул ингредиентов всех известных рецептов — из него берём замену
    private static readonly List<KitchenObjectSO> ingredientPool = new List<KitchenObjectSO>();

    // Состояние СЕГОДНЯШНЕЙ мутации (всё null, если хаоса сегодня нет)
    private static RecipeSO mutatedOriginal;       // какой рецепт пострадал
    private static RecipeSO mutatedClone;          // его рантайм-клон с заменой
    private static KitchenObjectSO removedIngredient;
    private static KitchenObjectSO addedIngredient;

    private static int lastRolledDay = -1;         // за какой день бросок уже сделан
    private static bool pendingRoll = false;       // бросок отложен до появления пула

    /// <summary>
    /// Пополняет пул рецептов/ингредиентов. Вызывается из CustomerAI.SelectRecipe —
    /// это единственное место, где система знает recipeListSO.
    /// </summary>
    public static void EnsurePool(List<RecipeSO> recipes)
    {
        if (recipes == null) return;

        foreach (RecipeSO recipe in recipes)
        {
            if (recipe == null) continue;
            if (!knownRecipes.Contains(recipe)) knownRecipes.Add(recipe);

            if (recipe.kitchenObjectSOList == null) continue;
            foreach (KitchenObjectSO ingredient in recipe.kitchenObjectSOList)
            {
                if (ingredient != null && !ingredientPool.Contains(ingredient))
                {
                    ingredientPool.Add(ingredient);
                }
            }
        }

        // Первый бросок мог быть отложен из-за пустого пула (загрузка сейва сразу на день ≥ 2)
        TryDeferredRoll();
    }

    /// <summary>
    /// Бросок на новый день. Вызывается из DeliveryManager при OnDayChanged.
    /// Если карта не взята — тихо сбрасывает состояние (день без мутаций).
    /// </summary>
    public static void RollForNewDay(int day)
    {
        lastRolledDay = day;
        pendingRoll = false;
        mutatedOriginal = null;
        mutatedClone = null;
        removedIngredient = null;
        addedIngredient = null;

        if (!DeliveryManager.HasUpgrade(UpgradeEffectType.AlchemyChaos))
        {
            return; // карта не взята — молча, без спама в лог
        }

        if (ingredientPool.Count < 2)
        {
            // Пул ещё не собран (ни один клиент ещё не выбирал заказ) — встречается
            // только при загрузке сохранения сразу на день ≥ 2. Откладываем бросок
            // до первого заказа — TryDeferredRoll в EnsurePool.
            pendingRoll = true;
            Debug.Log("[Хаос] Пул ингредиентов ещё пуст — мутация применится к заказам первого гостя.");
            return;
        }

        RollMutation(day);
    }

    private static void TryDeferredRoll()
    {
        if (!pendingRoll) return;
        if (GameLoopManager.Instance == null) return;

        pendingRoll = false;
        RollMutation(GameLoopManager.Instance.GetCurrentDay());
    }

    private static void RollMutation(int day)
    {
        mutatedOriginal = null;
        mutatedClone = null;
        removedIngredient = null;
        addedIngredient = null;

        // Кандидаты — рецепты хотя бы с одним ингредиентом
        List<RecipeSO> candidates = new List<RecipeSO>();
        foreach (RecipeSO recipe in knownRecipes)
        {
            if (recipe != null && recipe.kitchenObjectSOList != null && recipe.kitchenObjectSOList.Count > 0)
            {
                candidates.Add(recipe);
            }
        }

        if (candidates.Count == 0 || ingredientPool.Count < 2)
        {
            Debug.Log("[Хаос] Нет подходящих рецептов или пула ингредиентов — сегодня без мутации.");
            return;
        }

        RecipeSO victim = candidates[UnityEngine.Random.Range(0, candidates.Count)];

        // Ищем замену: другой ингредиент пула, которого ещё нет в рецепте.
        // (Если добавить уже существующий — рецепт станет несобираемым: тарелка
        // не принимает дубликаты, количество не сойдётся.)
        KitchenObjectSO removed = victim.kitchenObjectSOList[UnityEngine.Random.Range(0, victim.kitchenObjectSOList.Count)];
        KitchenObjectSO added = null;

        for (int attempt = 0; attempt < 12; attempt++)
        {
            KitchenObjectSO candidate = ingredientPool[UnityEngine.Random.Range(0, ingredientPool.Count)];
            if (candidate == removed) continue;
            if (victim.kitchenObjectSOList.Contains(candidate)) continue;
            added = candidate;
            break;
        }

        if (added == null)
        {
            Debug.Log($"[Хаос] День {day}: замена для «{victim.RecipeName}» не нашлась — сегодня без мутации.");
            return;
        }

        // РАНТАЙМ-клон рецепта — ассет-оригинал не трогаем (важно для редактора Unity)
        mutatedClone = ScriptableObject.CreateInstance<RecipeSO>();
        mutatedClone.name = victim.name + "_ChaosClone";
        mutatedClone.RecipeName = victim.RecipeName;
        mutatedClone.Cost = victim.Cost;
        mutatedClone.kitchenObjectSOList = new List<KitchenObjectSO>(victim.kitchenObjectSOList);
        mutatedClone.kitchenObjectSOList[mutatedClone.kitchenObjectSOList.IndexOf(removed)] = added;

        mutatedOriginal = victim;
        removedIngredient = removed;
        addedIngredient = added;

        Debug.Log($"[Хаос] День {day}: рецепт «{victim.RecipeName}» изменился — {removed.name} → {added.name}!");
    }

    /// <summary>
    /// Пропускает выбранный гостем рецепт через сегодняшнюю мутацию.
    /// Вызывается из CustomerAI.SelectRecipe. Если хаоса сегодня нет (или рецепт не
    /// пострадал) — возвращается исходник без изменений.
    /// Заодно покрывает случай «карту взяли посреди дня»: черновик карт идёт уже ПОСЛЕ
    /// смены дня, поэтому если бросок за сегодня не делали — делаем его прямо здесь.
    /// </summary>
    public static RecipeSO ApplyDailyMutation(RecipeSO original)
    {
        if (original == null) return null;

        if (DeliveryManager.HasUpgrade(UpgradeEffectType.AlchemyChaos)
            && GameLoopManager.Instance != null
            && lastRolledDay < GameLoopManager.Instance.GetCurrentDay())
        {
            RollForNewDay(GameLoopManager.Instance.GetCurrentDay());
        }

        if (mutatedOriginal == null) return original;   // сегодня хаоса не было
        if (original == mutatedClone) return original;  // защита: клон повторно не мутируем
        return original == mutatedOriginal ? mutatedClone : original;
    }

    /// <summary>Пострадавший сегодня рецепт (для будущего UI-баннера «Сегодня: ...»). Может быть null.</summary>
    public static RecipeSO GetMutatedOriginal() => mutatedOriginal;

    /// <summary>Какой ингредиент исчез из рецепта сегодня (для UI). Может быть null.</summary>
    public static KitchenObjectSO GetRemovedIngredient() => removedIngredient;

    /// <summary>Какой ингредиент появился в рецепте сегодня (для UI). Может быть null.</summary>
    public static KitchenObjectSO GetAddedIngredient() => addedIngredient;

    /// <summary>Текстовое описание сегодняшней мутации или null (для логов/баннера дня).</summary>
    public static string DescribeToday()
    {
        if (mutatedOriginal == null || removedIngredient == null || addedIngredient == null) return null;
        return $"«{mutatedOriginal.RecipeName}»: {removedIngredient.name} → {addedIngredient.name}";
    }
}