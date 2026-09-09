using UnityEngine;

/// <summary>
/// Категории товаров в магазине ведьмовской лавки.
///
/// Используется ShopUI для группировки карточек по вкладкам и фильтрации.
/// Порядок значений = порядок вкладок в UI.
/// </summary>
public enum ShopItemCategory
{
    Altar,        // Алтари для клиентов (бывшие DiningTable)
    Ingredient,   // Ларцы с магическими ингредиентами (ContainerCounter)
    Station,      // Магические станции: ступки, котлы, чаши (CuttingCounter, StoveCounter, SinkCounter)
    Utility,      // Вспомогательные: полка зелий, бездна, алхимический стол (PlatesCounter, TrashCounter, ClearCounter)
    Upgrade,      // Улучшения для существующих станций (руны, вечный огонь и т.д.)
    Decoration,   // Декор: хрустальный шар, метла, чёрный кот. Когда появятся чаевые — дадут бонус
    Legendary,    // Легендарные предметы endgame: перо феникса, чешуя дракона
}