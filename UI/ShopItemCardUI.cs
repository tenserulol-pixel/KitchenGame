using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Карточка одного товара в UI магазина. Создаётся ShopUI через Instantiate для каждого
/// элемента каталога.
///
/// Содержит:
/// - Иконку товара (Image)
/// - Название (TMP)
/// - Описание (TMP)
/// - Цену (TMP)
/// - Кнопку "Купить" (Button)
/// - Статус доступности: если товар недоступен (не наступил день / лимит исчерпан) —
///   кнопка "Купить" блокируется, поверх карточки показывается "Доступно с дня N"
///   или "Распродано".
///
/// Логика покупки не здесь — карточка только отображает состояние и дёргает
/// ShopManager.TryBuyItem при клике. ShopManager сам проверяет деньги, спавнит
/// префаб, списывает золото и стреляет OnItemPurchased.
/// </summary>
public class ShopItemCardUI : MonoBehaviour
{
    [Header("UI элементы карточки")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private TextMeshProUGUI priceText;
    [SerializeField] private Button buyButton;
    [SerializeField] private TextMeshProUGUI buyButtonText;
    [SerializeField] private GameObject lockedOverlay;
    [SerializeField] private TextMeshProUGUI lockedReasonText;

    [Header("Цвета кнопки")]
    [SerializeField] private Color affordableColor = new Color(0.2f, 0.8f, 0.2f);
    [SerializeField] private Color tooExpensiveColor = new Color(0.8f, 0.3f, 0.3f);

    private ShopItemSO currentItem;
    private ShopManager shopManager;

    private void Awake()
    {
        if (buyButton != null)
        {
            buyButton.onClick.AddListener(OnBuyClicked);
        }
    }

    private void OnDestroy()
    {
        if (buyButton != null)
        {
            buyButton.onClick.RemoveListener(OnBuyClicked);
        }
    }

    /// <summary>
    /// Заполняет карточку данными товара. Вызывается ShopUI после Instantiate карточки.
    /// </summary>
    public void Setup(ShopItemSO item, ShopManager manager)
    {
        currentItem = item;
        shopManager = manager;

        if (item == null || manager == null)
        {
            gameObject.SetActive(false);
            return;
        }

        // Иконка
        if (iconImage != null)
        {
            if (item.icon != null)
            {
                iconImage.sprite = item.icon;
                iconImage.enabled = true;
            }
            else
            {
                iconImage.enabled = false;
            }
        }

        // Название
        if (nameText != null)
        {
            nameText.text = item.displayName;
        }

        // Описание
        if (descriptionText != null)
        {
            descriptionText.text = item.description;
        }

        // Цена
        if (priceText != null)
        {
            priceText.text = item.cost + "g";
        }

        RefreshInteractable();
    }

    /// <summary>
    /// Обновляет доступность кнопки "Купить" и цвет цены. Вызывается:
    /// - при первичной Setup
    /// - когда ShopManager стреляет OnItemPurchased (могли измениться деньги или лимит)
    /// - когда меняется состояние магазина (открыт/закрыт)
    /// </summary>
    public void RefreshInteractable()
    {
        if (currentItem == null || shopManager == null) return;

        bool isAvailable = shopManager.IsItemAvailable(currentItem);
        int currentDay = GameLoopManager.Instance != null ? GameLoopManager.Instance.GetCurrentDay() : 1;
        bool hasEnoughMoney = GameLoopManager.Instance != null &&
                              GameLoopManager.Instance.HasEnoughMoney(currentItem.cost);

        // Если товар недоступен по дню/лимиту — блокируем полностью
        if (!isAvailable)
        {
            if (buyButton != null) buyButton.interactable = false;

            if (lockedOverlay != null) lockedOverlay.SetActive(true);

            if (lockedReasonText != null)
            {
                if (currentDay < currentItem.availableFromDay)
                {
                    lockedReasonText.text = $"Доступно с дня {currentItem.availableFromDay}";
                }
                else
                {
                    int purchased = shopManager.GetPurchaseCount(currentItem);
                    lockedReasonText.text = $"Распродано ({purchased}/{currentItem.maxPurchaseCount})";
                }
            }

            // Цена — серая
            if (priceText != null) priceText.color = Color.gray;
            return;
        }

        // Товар доступен — убираем overlay
        if (lockedOverlay != null) lockedOverlay.SetActive(false);

        // Кнопка активна, только если хватает денег
        bool canAfford = hasEnoughMoney && shopManager.IsShopOpen;

        if (buyButton != null)
        {
            buyButton.interactable = canAfford;

            // Меняем цвет кнопки через Image кнопки
            Image buttonImage = buyButton.targetGraphic as Image;
            if (buttonImage != null)
            {
                buttonImage.color = hasEnoughMoney ? affordableColor : tooExpensiveColor;
            }
        }

        if (buyButtonText != null)
        {
            buyButtonText.text = hasEnoughMoney ? "Купить" : "Мало золота";
        }

        // Цвет цены: зелёный если хватает, красный если нет
        if (priceText != null)
        {
            priceText.color = hasEnoughMoney ? affordableColor : tooExpensiveColor;
        }
    }

    private void OnBuyClicked()
    {
        if (currentItem == null || shopManager == null) return;

        bool success = shopManager.TryBuyItem(currentItem);

        if (success)
        {
            // Покупка прошла — RefreshInteractable обновит все карточки через ShopUI.
            // Здесь просто логируем, на случай если захочешь звук/анимацию.
            Debug.Log($"[ShopItemCardUI] Куплен '{currentItem.displayName}'.");
        }
    }
}
