using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UI-панель магазина. Подписывается на события ShopManager:
/// - OnShopOpened → показываем панель, строим карточки товаров
/// - OnShopClosed → скрываем панель
/// - OnItemPurchased → обновляем все карточки (могли измениться деньги или лимит)
///
/// Структура UI (должна быть собрана в Unity):
/// - корневой GameObject с этим компонентом
/// - дочерний Container (Transform с GridLayoutGroup) — куда instantiate'ся карточки
/// - дочерний CardTemplate (SetActive=false) — префаб ShopItemCardUI для Instantiate
/// - дочерний PlayerMoneyText (TMP) — текущий баланс золота
/// - дочерний CloseButton (Button) — альтернатива клавише B/Escape
/// - дочерний DayText (TMP) — текущий день (для контекста)
///
/// Сам ShopUI НЕ блокирует движение игрока — это делает ShopManager через
/// GameInput.DisablePlayerInput(). Здесь только визуальная часть.
/// </summary>
public class ShopUI : MonoBehaviour
{
    [Header("Ссылки на UI элементы")]
    [Tooltip("Контейнер для карточек товаров. Обычно GameObject с GridLayoutGroup")]
    [SerializeField] private Transform cardsContainer;

    [Tooltip("Префаб карточки товара (ShopItemCardUI). Должен быть SetActive(false) в сцене")]
    [SerializeField] private ShopItemCardUI cardTemplate;

    [Tooltip("Текст с текущим балансом золота игрока")]
    [SerializeField] private TMPro.TextMeshProUGUI playerMoneyText;

    [Tooltip("Кнопка закрытия магазина (опционально — Escape/B тоже работают)")]
    [SerializeField] private UnityEngine.UI.Button closeButton;

    [Tooltip("Текст с номером текущего дня (опционально)")]
    [SerializeField] private TMPro.TextMeshProUGUI dayText;

    [Tooltip("Если true — панель скрывается при старте. Если false — нужно вручную SetActive(false) в сцене")]
    [SerializeField] private bool hideOnStart = true;

    // Все созданные карточки — чтобы можно было их обновить при OnItemPurchased
    private readonly List<ShopItemCardUI> spawnedCards = new List<ShopItemCardUI>();

    private void Start()
    {
        // Скрываем панель при старте
        if (hideOnStart)
        {
            gameObject.SetActive(false);
        }

        // Скрываем шаблон карточки — он нужен только для Instantiate
        if (cardTemplate != null)
        {
            cardTemplate.gameObject.SetActive(false);
        }

        // Подписываемся на события ShopManager
        if (ShopManager.Instance != null)
        {
            ShopManager.Instance.OnShopOpened += ShopManager_OnShopOpened;
            ShopManager.Instance.OnShopClosed += ShopManager_OnShopClosed;
            ShopManager.Instance.OnItemPurchased += ShopManager_OnItemPurchased;
        }

        // Подписываемся на событие изменения золота — чтобы обновлять баланс в реальном времени
        if (GameLoopManager.Instance != null)
        {
            GameLoopManager.Instance.OnGoldChanged += GameLoopManager_OnGoldChanged;
        }

        // Кнопка закрытия
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(() =>
            {
                if (ShopManager.Instance != null)
                {
                    ShopManager.Instance.CloseShop();
                }
            });
        }
    }

    private void OnDestroy()
    {
        if (ShopManager.Instance != null)
        {
            ShopManager.Instance.OnShopOpened -= ShopManager_OnShopOpened;
            ShopManager.Instance.OnShopClosed -= ShopManager_OnShopClosed;
            ShopManager.Instance.OnItemPurchased -= ShopManager_OnItemPurchased;
        }

        if (GameLoopManager.Instance != null)
        {
            GameLoopManager.Instance.OnGoldChanged -= GameLoopManager_OnGoldChanged;
        }

        if (closeButton != null)
        {
            closeButton.onClick.RemoveAllListeners();
        }
    }

    // ===== Обработчики событий ShopManager =====

    private void ShopManager_OnShopOpened(object sender, System.EventArgs e)
    {
        ShowShop();
    }

    private void ShopManager_OnShopClosed(object sender, System.EventArgs e)
    {
        HideShop();
    }

    private void ShopManager_OnItemPurchased(object sender, ShopManager.OnItemPurchasedEventArgs e)
    {
        // После покупки обновляем все карточки — могли измениться деньги или лимит покупок.
        RefreshAllCards();
        UpdateMoneyDisplay();
    }

    private void GameLoopManager_OnGoldChanged(object sender, GameLoopManager.OnGoldChangedEventArgs e)
    {
        UpdateMoneyDisplay();
    }

    // ===== Визуальная часть =====

    private void ShowShop()
    {
        gameObject.SetActive(true);

        // Строим карточки заново — потому что день мог поменяться, и товары могли
        // стать доступны/недоступны.
        BuildCards();
        UpdateMoneyDisplay();
        UpdateDayDisplay();
    }

    private void HideShop()
    {
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Создаёт карточку для каждого товара из каталога ShopManager. Старые карточки
    /// (если были) уничтожаются — это проще, чем diff, и для 10-20 товаров не даст
    /// заметного GC pressure.
    /// </summary>
    private void BuildCards()
    {
        // Чистим старые карточки
        foreach (ShopItemCardUI card in spawnedCards)
        {
            if (card != null && card != cardTemplate)
            {
                Destroy(card.gameObject);
            }
        }
        spawnedCards.Clear();

        if (ShopManager.Instance == null || cardTemplate == null || cardsContainer == null)
        {
            Debug.LogWarning("[ShopUI] Не все ссылки назначены — ShopManager, cardTemplate или cardsContainer null.");
            return;
        }

        List<ShopItemSO> items = ShopManager.Instance.GetCatalogItems();
        if (items == null) return;

        foreach (ShopItemSO item in items)
        {
            if (item == null) continue;

            ShopItemCardUI cardInstance = Instantiate(cardTemplate, cardsContainer);
            cardInstance.gameObject.SetActive(true);
            cardInstance.Setup(item, ShopManager.Instance);
            spawnedCards.Add(cardInstance);
        }

        Debug.Log($"[ShopUI] Создано {spawnedCards.Count} карточек товаров.");
    }

    /// <summary>
    /// Обновляет все карточки без пересоздания. Вызывается после покупки (деньги
    /// изменились) и при изменении баланса золота.
    /// </summary>
    private void RefreshAllCards()
    {
        foreach (ShopItemCardUI card in spawnedCards)
        {
            if (card != null)
            {
                card.RefreshInteractable();
            }
        }
    }

    private void UpdateMoneyDisplay()
    {
        if (playerMoneyText == null) return;

        int gold = GameLoopManager.Instance != null ? GameLoopManager.Instance.GetTotalGold() : 0;
        playerMoneyText.text = gold + "g";
    }

    private void UpdateDayDisplay()
    {
        if (dayText == null) return;

        int day = GameLoopManager.Instance != null ? GameLoopManager.Instance.GetCurrentDay() : 1;
        dayText.text = "День " + day;
    }
}