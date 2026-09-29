using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Одна карточка в черновике улучшений (страница гримуара). Аналог ShopItemCardUI,
/// но для UpgradeCardSO: отображает иконку/название/flavor-описание/сводку эффектов
/// и по клику дёргает коллбек, переданный UpgradeDraftUI.
///
/// Логики применения эффектов здесь НЕТ — карточка глупая вьюха: показ + клик + ховер.
/// Редкости нет сознательно: все карты — «страницы гримуара», рамка одна для всех.
///
/// Требования к префабу:
/// - на корне: Image (рамка, raycastTarget = true) + Button (onClick → OnCardClicked);
///   именно Image на корне обеспечивает работу OnPointerEnter/Exit;
/// - Icon / NameText / DescriptionText / EffectSummary — дочерние элементы.
/// </summary>
public class UpgradeCardUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("UI элементы карточки")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private TextMeshProUGUI effectSummaryText;
    [SerializeField] private Image frameImage; // рамка карточки (обычно Image на корне)

    [Header("Ховер-анимация")]
    [SerializeField] private float hoverScale = 1.06f;
    [SerializeField] private float hoverLerpSpeed = 10f;
    [SerializeField] private Color normalFrameColor = new Color(0.79f, 0.59f, 0.23f); // #c9973b
    [SerializeField] private Color hoverFrameColor = new Color(0.95f, 0.86f, 0.66f);  // светлый пергамент

    private UpgradeCardSO card;
    private System.Action<UpgradeCardSO> onPicked;
    private Vector3 baseScale;
    private bool hovered;
    private float scaleT; // 0 = покой, 1 = ховер

    private void Awake()
    {
        baseScale = transform.localScale;
    }

    /// <summary>
    /// Заполняет карточку данными. Вызывается UpgradeDraftUI после Instantiate.
    /// onPicked может быть null — тогда карточка работает как «просмотр»
    /// (пригодится для экрана имеющихся карт после Патча 4).
    /// </summary>
    public void Setup(UpgradeCardSO card, System.Action<UpgradeCardSO> onPicked)
    {
        this.card = card;
        this.onPicked = onPicked;

        if (card == null)
        {
            gameObject.SetActive(false);
            return;
        }

        // Иконка: без ассета просто скрываем слот, не оставляя дырки
        if (iconImage != null)
        {
            if (card.icon != null)
            {
                iconImage.sprite = card.icon;
                iconImage.enabled = true;
            }
            else
            {
                iconImage.enabled = false;
            }
        }

        if (nameText != null) nameText.text = card.cardName;
        if (descriptionText != null) descriptionText.text = card.description;
        if (effectSummaryText != null) effectSummaryText.text = card.GetEffectSummary();

        // Сброс ховера — карточки пересоздаются каждый день
        hovered = false;
        scaleT = 0f;
        transform.localScale = baseScale;
        if (frameImage != null) frameImage.color = normalFrameColor;
    }

    /// <summary>
    /// Повесь Button (на корне префаба) onClick на этот метод в инспекторе.
    /// </summary>
    public void OnCardClicked()
    {
        if (card == null) return;
        onPicked?.Invoke(card);
    }

    public void OnPointerEnter(PointerEventData eventData) => hovered = true;
    public void OnPointerExit(PointerEventData eventData) => hovered = false;

    private void Update()
    {
        float target = hovered ? 1f : 0f;
        if (Mathf.Approximately(scaleT, target)) return;

        scaleT = Mathf.MoveTowards(scaleT, target, hoverLerpSpeed * Time.deltaTime);
        transform.localScale = baseScale * (1f + (hoverScale - 1f) * scaleT);

        if (frameImage != null)
        {
            frameImage.color = Color.Lerp(normalFrameColor, hoverFrameColor, scaleT);
        }
    }
}