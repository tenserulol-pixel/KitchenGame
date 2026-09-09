using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Каталог всех товаров магазина. Один общий .asset на проект, ссылается на все ShopItemSO.
/// ShopManager читает отсюда список доступных товаров, чтобы заполнить UI.
///
/// Создаётся через CreateAssetMenu: KitchenGame → Shop Catalog.
/// </summary>
[CreateAssetMenu(fileName = "ShopCatalog", menuName = "KitchenGame/Shop Catalog")]
public class ShopCatalogSO : ScriptableObject
{
    [Tooltip("Полный список всех товаров. Порядок не важен — UI сам сгруппирует по категориям.")]
    public List<ShopItemSO> items = new List<ShopItemSO>();
}
