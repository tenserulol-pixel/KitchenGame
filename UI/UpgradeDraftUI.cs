using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Оверлей выбора карт дня («гримуар»). НЕ управляется GameUIManager: сам подписан
/// на UpgradeManager.OnOfferChanged, перестраивает карточки и виден, только пока
/// предложение непусто. Оверлей поверх preparationPanel — игрок в подготовке
/// заморожен (гейт в Player.cs), карточки выбираются мышью ИЛИ клавиатурой.
///
/// Управление:
/// - мышь: клик по карточке (как раньше);
/// - клавиатура: A/D — подсветка карточки (UpgradeManager стреляет OnSelectionChanged,
///   здесь мы её отрисовываем), E — взять подсвеченную (ловит UpgradeManager сам,
///   сюда нажатие не приходит).
///
/// Структура (собирается в Unity):
/// - UpgradeDraftRoot (этот компонент, GameObject выключен в сцене)
///   - Dim          (Image #000000 alpha ~150 на весь экран, raycastTarget = true —
///                   блокирует клики мимо карточек)
///   - Title        (TMP — заголовок черновика)
///   - Hint         (TMP — ОПЦИОНАЛЬНО: строка «A/D — выбор · E — взять · Esc — пропустить»;
///                   если поле не назначено в инспекторе, подсказка просто не показывается)
///   - CardsContainer (GridLayoutGroup, 3 колонки, cellSize ~260x360, spacing 24)
///   - CardTemplate (префаб UpgradeCardUI, выключен)
///   - SkipButton   (Button «Пропустить»)
/// </summary>
public class UpgradeDraftUI : MonoBehaviour
{
    [Header("Ссылки")]
    [SerializeField] private Transform cardsContainer;
    [SerializeField] private UpgradeCardUI cardTemplate;
    [SerializeField] private Button skipButton;
    [SerializeField] private TextMeshProUGUI titleText;

    [Tooltip("Опционально: строка подсказки управления. Можно оставить пустым — тогда подсказки просто не будет")]
    [SerializeField] private TextMeshProUGUI hintText;

    [Header("Поведение")]
    [Tooltip("Страховка: прятать оверлей, когда фаза подготовки закончилась " +
             "(игрок нажал Enter, не разобрав черновик)")]
    [SerializeField] private bool hideWhenPreparationEnds = true;

    private readonly List<UpgradeCardUI> spawnedCards = new List<UpgradeCardUI>();

    private void Start()
    {
        gameObject.SetActive(false);
        if (cardTemplate != null) cardTemplate.gameObject.SetActive(false);

        if (UpgradeManager.Instance != null)
        {
            UpgradeManager.Instance.OnOfferChanged += UpgradeManager_OnOfferChanged;
            UpgradeManager.Instance.OnSelectionChanged += UpgradeManager_OnSelectionChanged;
        }

        if (skipButton != null)
        {
            skipButton.onClick.AddListener(() =>
            {
                if (UpgradeManager.Instance != null) UpgradeManager.Instance.SkipDraft();
            });
        }

        // Если черновик начался до того, как UI проснулся (порядок Start не гарантирован),
        // сразу строим из текущего состояния.
        RefreshFromCurrentOffer();
    }

    private void OnDestroy()
    {
        if (UpgradeManager.Instance != null)
        {
            UpgradeManager.Instance.OnOfferChanged -= UpgradeManager_OnOfferChanged;
            UpgradeManager.Instance.OnSelectionChanged -= UpgradeManager_OnSelectionChanged;
        }
        if (skipButton != null) skipButton.onClick.RemoveAllListeners();
    }

    private void Update()
    {
        // Подготовка кончилась — оверлей обязан уйти, даже если игрок не взял карту.
        if (hideWhenPreparationEnds &&
            GameLoopManager.Instance != null &&
            !GameLoopManager.Instance.IsPreparationActive() &&
            gameObject.activeSelf)
        {
            gameObject.SetActive(false);
        }
    }

    private void UpgradeManager_OnOfferChanged(object sender, System.EventArgs e)
    {
        RefreshFromCurrentOffer();
    }

    private void UpgradeManager_OnSelectionChanged(object sender, UpgradeManager.OnSelectionChangedEventArgs e)
    {
        // A/D листают подсветку — отрисовываем её на созданных карточках
        ApplyHighlight(e.selectedIndex);
    }

    private void RefreshFromCurrentOffer()
    {
        if (UpgradeManager.Instance == null) return;

        List<UpgradeCardSO> offer = UpgradeManager.Instance.GetCurrentOffer();

        if (offer == null || offer.Count == 0)
        {
            gameObject.SetActive(false);
            return;
        }

        gameObject.SetActive(true);
        RebuildCards(offer);
        UpdateHintText();
    }

    private void RebuildCards(List<UpgradeCardSO> offer)
    {
        // Сносим карточки прошлого черновика
        foreach (UpgradeCardUI cardUI in spawnedCards)
        {
            if (cardUI != null) Destroy(cardUI.gameObject);
        }
        spawnedCards.Clear();

        if (titleText != null)
        {
            int day = GameLoopManager.Instance != null ? GameLoopManager.Instance.GetCurrentDay() : 0;
            titleText.text = $"ДЕНЬ {day} — ГРИМУАР ПРЕДЛАГАЕТ";
        }

        foreach (UpgradeCardSO card in offer)
        {
            UpgradeCardUI cardUI = Instantiate(cardTemplate, cardsContainer);
            cardUI.gameObject.SetActive(true); // Awake отработает здесь, до Setup
            cardUI.Setup(card, OnCardPicked);
            spawnedCards.Add(cardUI);
        }

        // Подсветка клавиатурного выбора (A/D): берём актуальный индекс у менеджера.
        // Это же покрывает случай, когда OnSelectionChanged прилетел ДО перестройки
        // карточек (порядок событий при открытии черновика).
        ApplyHighlight(UpgradeManager.Instance.GetSelectedDraftIndex());
    }

    /// <summary>
    /// Подсвечивает карточку с индексом selectedIndex (клавиатурный выбор A/D),
    /// остальные возвращает в обычное состояние. Мышинный ховер живёт отдельно
    /// и с подсветкой не конфликтует.
    /// </summary>
    private void ApplyHighlight(int selectedIndex)
    {
        for (int i = 0; i < spawnedCards.Count; i++)
        {
            if (spawnedCards[i] != null)
            {
                spawnedCards[i].SetHighlighted(i == selectedIndex);
            }
        }
    }

    private void UpdateHintText()
    {
        if (hintText == null) return;

        hintText.text = "A / D — выбор      E — взять      Esc — пропустить";
    }

    private void OnCardPicked(UpgradeCardSO pickedCard)
    {
        if (UpgradeManager.Instance == null) return;

        // Защита от клика «на грани» — например, в кадр, когда подготовка уже кончилась.
        if (GameLoopManager.Instance != null && !GameLoopManager.Instance.IsPreparationActive()) return;

        int index = UpgradeManager.Instance.GetCurrentOffer().IndexOf(pickedCard);
        if (index >= 0)
        {
            UpgradeManager.Instance.PickCard(index);
            // Панель спрячется сама: PickCard чистит currentOffer и стреляет OnOfferChanged.
        }
    }
}