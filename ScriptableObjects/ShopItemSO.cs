using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Описание одного товара в магазине ведьмовской лавки.
///
/// Каждый .asset — это либо стол-алтарь (клиенты придут и сядут), либо станция-алтарь
/// (игрок готовит на ней зелья), либо контейнер-ларец (выдаёт ингредиенты по E),
/// либо декор/улучшение. Префаб указывает, какой именно BaseCounter создать при покупке.
///
/// ВАЖНО: для апгрейдов (category == Upgrade) prefab может быть null — тогда эффект
/// применяется через applyUpgradeMethod, без спавна нового объекта. Это позволяет
/// продавать "улучшить плиту" без создания второй плиты.
/// </summary>
[CreateAssetMenu(fileName = "ShopItem_", menuName = "KitchenGame/Shop Item")]
public class ShopItemSO : ScriptableObject
{
    [Header("Идентификация")]
    [Tooltip("Уникальный идентификатор. Используется в SaveManager для сериализации купленных предметов.")]
    public string itemId;

    [Tooltip("Отображаемое в UI название. Например, 'Малый алтарь' или 'Ларец трав'")]
    public string displayName;

    [Tooltip("Описание для карточки товара. 1-2 предложения, что делает и зачем нужно")]
    [TextArea] public string description;

    [Tooltip("Иконка для UI. Спрайт, который видит игрок в карточке товара. Например, силуэт котла или банки")]
    public Sprite icon;

    [Header("Категория")]
    public ShopItemCategory category;

    [Header("Префаб")]
    [Tooltip("Префаб BaseCounter, который заспавнится при покупке. Для апгрейдов — может быть null")]
    public BaseCounter prefab;

    [Header("Экономика")]
    [Tooltip("Сколько золота стоит этот товар")]
    public int cost;

    [Tooltip("С какого дня этот товар доступен в магазине. 1 = с первого дня. Если 3 — игрок увидит его только с 3-го дня")]
    public int availableFromDay = 1;

    [Tooltip("Сколько всего таких предметов можно купить за всю игру. 0 = без лимита. Например, легендарные предметы — 1 шт., обычные столы — без лимта")]
    public int maxPurchaseCount = 0;

    [Header("Апгрейд (опционально)")]
    [Tooltip("Для категории Upgrade — какой метод вызвать на уже существующем объекте. Для остальных категорий — пустая строка")]
    public UpgradeEffect upgradeEffect = UpgradeEffect.None;

    /// <summary>
    /// Проверяет, доступен ли товар для покупки в данный момент.
    /// </summary>
    public bool IsAvailableOnDay(int currentDay, int alreadyPurchasedCount)
    {
        if (currentDay < availableFromDay) return false;
        if (maxPurchaseCount > 0 && alreadyPurchasedCount >= maxPurchaseCount) return false;
        return true;
    }
}

/// <summary>
/// Типы апгрейдов для категории Upgrade. Каждый соответствует методу на соответствующем Counter'е.
/// ShopManager.ApplyUpgradeEffect() по этому enum решает, что вызывать.
/// </summary>
public enum UpgradeEffect
{
    None,                 // Не апгрейд, а обычный товар
    RunicMortar,          // CuttingCounter.UpgradeToRunic() — быстрее перемалывает
    EternalFire,          // StoveCounter.UpgradeToEternalFire() — быстрее варит
    PurifyingWater,      // SinkCounter.UpgradeToPurifyingWater() — быстрее моет
    SelfRefilling,        // PlatesCounter.UpgradeToSelfRefilling() — больше тарелок в стопке
    Apprentice,          // будущая фича — помощник-ученик
    BookshelfKnowledge,  // будущая фича — открывает редкие рецепты
}
