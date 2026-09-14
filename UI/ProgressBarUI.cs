using UnityEngine;
using UnityEngine.UI;
using System;

/// <summary>
/// Универсальный прогресс-бар. Подписывается на IHasProgress.OnProgressChanged и
/// обновляет fillAmount у Image.
///
/// Поведение скрытия:
/// - При progressNormalized == 0f → Hide() (всегда)
/// - При progressNormalized == 1f → Hide() только если hideOnFullProgress=true.
///   Для станций (CuttingCounter, StoveCounter) — true: бар исчезает при завершении
///   резки/варки. Для клиентов — false: бар должен оставаться видимым при полном
///   терпении, чтобы игрок видел "полный зелёный".
///
/// Цветовая индикация (опционально):
/// - Если useColorGradient=true, бар меняет цвет в зависимости от progressNormalized:
///   < 0.3f → красный (критично)
///   < 0.6f → жёлтый (посредственно)
///   >= 0.6f → зелёный (нормально)
/// </summary>
public class ProgressBarUI : MonoBehaviour
{
    [SerializeField] private GameObject hasProgressGameObject;
    private IHasProgress hasProgress;
    [SerializeField] private Image barImage;

    [Header("Поведение скрытия")]
    [Tooltip("Если true — бар скрывается при progressNormalized == 1f. Для станций (CuttingCounter, StoveCounter) — true, для клиентов — false")]
    [SerializeField] private bool hideOnFullProgress = true;

    [Header("Цветовая индикация (опционально)")]
    [Tooltip("Если true — бар меняет цвет в зависимости от прогресса. Для клиентов с терпением — true: красный → жёлтый → зелёный. Для станций — false: один цвет")]
    [SerializeField] private bool useColorGradient = false;

    [SerializeField] private Color criticalColor = new Color(0.8f, 0.2f, 0.2f); // красный
    [SerializeField] private Color warningColor = new Color(0.95f, 0.75f, 0.2f); // жёлтый
    [SerializeField] private Color normalColor = new Color(0.2f, 0.8f, 0.3f); // зелёный

    private const float Epsilon = 0.001f;

    private void Start()
    {
        if (hasProgressGameObject == null)
        {
            Debug.LogError($"[ProgressBarUI] '{name}': HasProgressGameObject не назначен!");
            Hide();
            return;
        }

        hasProgress = hasProgressGameObject.GetComponent<IHasProgress>();
        if (hasProgress == null)
        {
            Debug.LogError($"[ProgressBarUI] '{name}': на {hasProgressGameObject.name} нет компонента с IHasProgress!");
            Hide();
            return;
        }

        hasProgress.OnProgressChanged += HasProgress_OnProgressBarChanged;
        barImage.fillAmount = 0f;
        Hide();
    }

    private void OnDestroy()
    {
        if (hasProgress != null)
        {
            hasProgress.OnProgressChanged -= HasProgress_OnProgressBarChanged;
        }
    }

    private void HasProgress_OnProgressBarChanged(object sender, IHasProgress.OnProgressChangedEventArgs e)
    {
        barImage.fillAmount = e.progressNormalized;

        // Цветовая индикация — если включена
        if (useColorGradient && barImage != null)
        {
            if (e.progressNormalized < 0.3f)
            {
                barImage.color = criticalColor;
            }
            else if (e.progressNormalized < 0.6f)
            {
                barImage.color = warningColor;
            }
            else
            {
                barImage.color = normalColor;
            }
        }

        // Логика скрытия:
        // - При ~0f — всегда скрываем (бар не нужен)
        // - При ~1f — скрываем только если hideOnFullProgress=true
        //   (станции — да, клиенты — нет)
        if (e.progressNormalized < Epsilon)
        {
            Hide();
        }
        else if (e.progressNormalized > 1f - Epsilon && hideOnFullProgress)
        {
            Hide();
        }
        else
        {
            Show();
        }
    }

    private void Show()
    {
        gameObject.SetActive(true);
    }

    private void Hide()
    {
        gameObject.SetActive(false);
    }
}