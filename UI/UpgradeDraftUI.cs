using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Оверлей выбора карт дня («гримуар»). НЕ управляется GameUIManager: сам подписан
/// на UpgradeManager.OnOfferChanged, перестраивает карточки и виден, только пока
/// предложение непусто. Оверлей поверх preparationPanel — игрок в подготовке может
/// ходить и двигать мебель, карточки кликаются мышью.
///
/// Структура (собирается в Unity):
/// - UpgradeDraftRoot (этот компонент, GameObject выключен в сцене)
///   - Dim          (Image #000000 alpha ~150 на весь экран, raycastTarget = true —
///                   блокирует клики мимо карточек)
///   - Title        (TMP — заголовок черновика)
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