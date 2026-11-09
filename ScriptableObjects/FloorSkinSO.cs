using UnityEngine;

/// <summary>
/// ★ v1.5 (фича «Полы по зонам»): один скин пола — материал, который игрок может
/// купить в лавке и назначить любой зоне (кухня / обеденный зал / вход).
///
/// СВЯЗЬ С МАГАЗИНОМ: у FloorSkinSO поле skinId должно СОВПАДАТЬ с itemId
/// ShopItemSO, которым этот скин продаётся (категория FloorSkin, prefab = null).
/// Именно по skinId FloorManager спрашивает у ShopManager «куплен ли этот скин»,
/// и по skinId же скин пишется в сохранение. Это тот же приём, что у карт
/// улучшений (cardName) и мебели (itemId): строковый идентификатор вместо ссылки,
/// потому что JsonUtility не сериализует ScriptableObject-ссылки.
///
/// ВАЖНО ПРО TILING: у каждого материала свой масштаб плитки (Tiling в
/// инспекторе материала). Настрой его заранее в ассете материала — FloorManager
/// подставляет материал как есть, масштаб на рантайме не пересчитывает.
///
/// Создаётся через CreateAssetMenu: KitchenGame → Floor Skin.
/// </summary>
[CreateAssetMenu(fileName = "FloorSkin_", menuName = "KitchenGame/Floor Skin")]
public class FloorSkinSO : ScriptableObject
{
    [Header("Идентификация")]
    [Tooltip("Уникальный идентификатор скина. ДОЛЖЕН совпадать с itemId ShopItemSO, " +
             "которым этот скин продаётся в лавке (категория FloorSkin). По этому же id " +
             "скин сохраняется в сейв.")]
    public string skinId;

    [Tooltip("Отображаемое название. Например, «Обсидиановые плиты»")]
    public string displayName;

    [Tooltip("Описание для меню полов. 1 предложение — откуда пол или чем хорош")]
    [TextArea] public string description;

    [Tooltip("Иконка для меню полов (опционально). Например, маленький скриншот текстуры")]
    public Sprite icon;

    [Header("Материал")]
    [Tooltip("Материал, который подставится на рендереры зоны. Настрой Tiling " +
             "в самом материале под размер зоны")]
    public Material floorMaterial;
}
