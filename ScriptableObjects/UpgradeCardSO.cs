using UnityEngine;

// Небольшой, сознательно ограниченный набор эффектов — каждый трогает одно простое
// поле на конкретном скрипте, а не общий ScriptableObject-рецепт (кроме случая с
// ценой рецептов ниже — там множитель применяется в момент выплаты, а не в самом
// RecipeSO, чтобы не мутировать общий на всех ассет).
//
// UnstableMagic и DarkDeal используют secondaryValue — единственные два эффекта
// с двумя числами вместо одного.
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
    BiggerRarerGroups,    // Крупные компании — размер группы больше (value), но дневная норма меньше (secondaryValue)
    RushHour,             // Час пик — интервал спавна короче (value), но лимит клиентов в зале ниже (secondaryValue)
}

[CreateAssetMenu]
public class UpgradeCardSO : ScriptableObject
{
    public string cardName;
    [TextArea] public string description;
    public UpgradeEffectType effectType;
    public float value;
    [Tooltip("Нужно не всем эффектам — используется UnstableMagic и DarkDeal")]
    public float secondaryValue;
}
