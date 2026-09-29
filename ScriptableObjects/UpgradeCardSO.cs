using UnityEngine;

// Небольшой, сознательно ограниченный набор эффектов — каждый трогает одно простое
// поле на конкретном скрипте, а не общий ScriptableObject-рецепт (кроме случая с
// ценой рецептов ниже — там множитель применяется в момент выплаты, а не в самом
// RecipeSO, чтобы не мутировать общий на всех ассет).
//
// Синергетический набор (v6): эффекты копятся как МОДИФИКАТОРЫ в UpgradeManager
// (rewardBonus, cookSpeedMultiplier и т.д.), а другие системы читают их через
// геттеры. Синергии между картами не запрограммированы — они возникают сами
// из стека модификаторов (как в PlateUp).
public enum UpgradeEffectType
{
    MoveSpeed,
    InteractDistance,
    AngryCustomerTolerance,
    UnstableMagic,
    PenaltyReduction,     // Мягкая рука — снижает штраф за недовольного клиента
    BonusGold,            // Задаток покровителя — разовая прибавка золота при взятии карты
    SlowerQuotaGrowth,    // Щедрый день — снижает прирост дневной нормы групп
    DarkDeal,             // Тёмная сделка — цена рецептов выше (value), терпимость ниже (secondaryValue)
    BiggerRarerGroups,    // Большие компании (старая) — baseMaxGroupSize+, baseDailyGroupTarget-
    RushHour,             // Час пик — baseSpawnInterval- (value), baseMaxCustomers- (secondaryValue)

    // === Синергетический набор (v6) ===
    GroupSizeUp,       // Большие компании — группы +value клиент(а), без минусов
    BigPortions,       // Большие порции — группе нужно +value блюдо, награда +secondaryValue
    SameDishChance,    // Общий заказ — шанс value, что гость скопирует заказ соседа по столу
    FastFire,          // Быстрый огонь — котёл быстрее на value, но блюдо горит быстрее на secondaryValue
    HurriedGuests,     // Спешащие гости — терпение -value, награда +secondaryValue
    AlchemyChaos,      // Алхимический хаос — один ингредиент случайного рецепта меняется каждый день
    UnstableCauldron,  // Нестабильный котёл — +value перемешиваний, блюда из котла дороже на secondaryValue
}

[CreateAssetMenu]
public class UpgradeCardSO : ScriptableObject
{
    public string cardName;
    [TextArea] public string description;
    public UpgradeEffectType effectType;
    public float value;
    [Tooltip("Нужно не всем эффектам — используется UnstableMagic, DarkDeal и синергетическими картами (BigPortions, FastFire, HurriedGuests, UnstableCauldron)")]
    public float secondaryValue;

    [Header("UI карточек")]
    [Tooltip("Иконка для UI карточек. Может быть пустой — тогда UI скроет слот")]
    public Sprite icon;

    /// <summary>
    /// Каноническая сводка механики карты для UI. Строится из тех же значений,
    /// которые читает UpgradeManager.ApplyEffect() — тултип не разойдётся с эффектом.
    ///
    /// ВАЖНО: строка DarkDeal написана под Патч 2 (DecreaseAngryCustomerTolerance).
    /// Пока Патч 2 не применён, фактический эффект противоположен тексту.
    /// </summary>
    public string GetEffectSummary()
    {
        switch (effectType)
        {
            case UpgradeEffectType.MoveSpeed:
                return $"Скорость движения +{value:0.#}";

            case UpgradeEffectType.InteractDistance:
                return $"Дистанция взаимодействия +{value:0.#}";

            case UpgradeEffectType.AngryCustomerTolerance:
                return $"Лимит недовольных клиентов +{value:0}";

            case UpgradeEffectType.UnstableMagic:
                return $"Скорость резки +{value * 100f:0}%, шанс порчи блюда +{secondaryValue * 100f:0}%";

            case UpgradeEffectType.PenaltyReduction:
                return $"Штраф за недовольного клиента -{value:0}g";

            case UpgradeEffectType.BonusGold:
                return $"Золото сейчас +{value:0}g";

            case UpgradeEffectType.SlowerQuotaGrowth:
                return $"Прирост дневной нормы -{value:0}";

            case UpgradeEffectType.DarkDeal:
                return $"Цены рецептов +{value * 100f:0}%, лимит недовольных -{secondaryValue:0}";

            case UpgradeEffectType.BiggerRarerGroups:
                return $"Макс. размер группы +{value:0}, дневная норма -{secondaryValue:0}";

            case UpgradeEffectType.RushHour:
                return $"Интервал спавна -{value:0.#}с, макс. клиентов -{secondaryValue:0}";

            // === Синергетический набор (v6) ===

            case UpgradeEffectType.GroupSizeUp:
                return $"Группы гостей: +{value:0} клиент(а) к максимуму";

            case UpgradeEffectType.BigPortions:
                return $"Каждой группе нужно +{value:0} блюдо, награда за заказ +{secondaryValue * 100f:0}%";

            case UpgradeEffectType.SameDishChance:
                return $"Гость копирует заказ соседа по столу с шансом {value * 100f:0}%";

            case UpgradeEffectType.FastFire:
                return $"Котёл готовит быстрее на {value * 100f:0}%, но блюдо горит быстрее на {secondaryValue * 100f:0}%";

            case UpgradeEffectType.HurriedGuests:
                return $"Терпение гостей -{value * 100f:0}%, награда за заказ +{secondaryValue * 100f:0}%";

            case UpgradeEffectType.AlchemyChaos:
                return "Каждый день один ингредиент случайного рецепта меняется на другой";

            case UpgradeEffectType.UnstableCauldron:
                return $"Котёл требует +{value:0} перемешиваний, но блюда из котла дороже на {secondaryValue * 100f:0}%";

            default:
                return string.Empty;
        }
    }
}