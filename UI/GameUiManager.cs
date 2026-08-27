using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;

public class GameUIManager : MonoBehaviour
{
    [Header("Панели состояний (GameObject)")]
    [SerializeField] private GameObject preparationPanel;
    [SerializeField] private GameObject countdownPanel;
    [SerializeField] private GameObject gameplayPanel;
    [SerializeField] private GameObject resultsPanel;
    [SerializeField] private GameObject gameOverPanel;

    [Header("Элементы панели подготовки")]
    [SerializeField] private TextMeshProUGUI pressEnterText;
    [SerializeField] private float pulseSpeed = 2f; // Скорость пульсации надписи

    [Header("Элементы панели обратного отсчета")]
    [SerializeField] private TextMeshProUGUI countdownText;
    [SerializeField] private float countdownPunchScale = 1.8f; // Насколько сильно сжимается/разжимается цифра
    [SerializeField] private float countdownPunchDuration = 0.3f; // Время анимации цифры

    [Header("Элементы игровой панели (Gameplay)")]
    [SerializeField] private TextMeshProUGUI dayText;
    [SerializeField] private TextMeshProUGUI goldText;
    [SerializeField] private Image timerBarImage; // Индикатор дневной нормы (Type: Filled)
    [SerializeField] private TextMeshProUGUI dailyProgressText; // Необязательно: "3 / 6" рядом с индикатором

    [Header("Элементы панели результатов (Results)")]
    [SerializeField] private TextMeshProUGUI resultsDayTitleText;
    [SerializeField] private TextMeshProUGUI earnedTodayText;
    [SerializeField] private TextMeshProUGUI lostTodayText;
    [SerializeField] private TextMeshProUGUI netProfitTodayText;
    [SerializeField] private TextMeshProUGUI totalGoldResultsText;
    [SerializeField] private TextMeshProUGUI successfulDeliveriesText;
    [SerializeField] private TextMeshProUGUI failedDeliveriesText;

    [Header("Элементы панели поражения (Game Over)")]
    [SerializeField] private TextMeshProUGUI gameOverReasonText;

    private int lastDisplayedCountdownValue = -1;
    private Coroutine countdownPunchCoroutine;

    private void Start()
    {
        // Подписываемся на события менеджера игрового цикла
        if (GameLoopManager.Instance != null)
        {
            GameLoopManager.Instance.OnStateChanged += GameLoopManager_OnStateChanged;
            GameLoopManager.Instance.OnCountdownTimerChanged += GameLoopManager_OnCountdownTimerChanged;
            GameLoopManager.Instance.OnGoldChanged += GameLoopManager_OnGoldChanged;
            GameLoopManager.Instance.OnDayChanged += GameLoopManager_OnDayChanged;
        }

        // Первоначальное обновление интерфейса при старте сцены
        UpdateUIState();
        UpdateGoldUI(GameLoopManager.Instance != null ? GameLoopManager.Instance.GetTotalGold() : 0);
        UpdateDayUI();
    }

    private void OnDestroy()
    {
        // Предотвращаем утечки памяти при уничтожении UI
        if (GameLoopManager.Instance != null)
        {
            GameLoopManager.Instance.OnStateChanged -= GameLoopManager_OnStateChanged;
            GameLoopManager.Instance.OnCountdownTimerChanged -= GameLoopManager_OnCountdownTimerChanged;
            GameLoopManager.Instance.OnGoldChanged -= GameLoopManager_OnGoldChanged;
            GameLoopManager.Instance.OnDayChanged -= GameLoopManager_OnDayChanged;
        }
    }

    private void Update()
    {
        // 1. Пульсация текста "Нажмите Enter" на этапе подготовки
        if (GameLoopManager.Instance != null && GameLoopManager.Instance.IsPreparationActive())
        {
            if (pressEnterText != null)
            {
                float alpha = Mathf.PingPong(Time.time * pulseSpeed, 1f);
                Color color = pressEnterText.color;
                color.a = Mathf.Lerp(0.3f, 1f, alpha); // Плавная прозрачность от 30% до 100%
                pressEnterText.color = color;
            }
        }

        // 2. Плавная работа индикатора дневной нормы (раньше — таймер, теперь прогресс по группам)
        if (GameLoopManager.Instance != null && GameLoopManager.Instance.IsGamePlaying())
        {
            if (timerBarImage != null && CustomerManager.Instance != null)
            {
                timerBarImage.fillAmount = CustomerManager.Instance.GetDailyProgressNormalized();
            }

            if (dailyProgressText != null && CustomerManager.Instance != null)
            {
                dailyProgressText.text = CustomerManager.Instance.GetGroupsSpawnedToday() +
                    " / " + CustomerManager.Instance.GetDailyGroupTarget();
            }
        }
    }

    private void GameLoopManager_OnStateChanged(object sender, EventArgs e)
    {
        UpdateUIState();
    }

    private void GameLoopManager_OnCountdownTimerChanged(object sender, EventArgs e)
    {
        if (GameLoopManager.Instance == null || countdownText == null) return;

        float timer = GameLoopManager.Instance.GetCountdownToStartTimer();
        int currentCeilValue = Mathf.CeilToInt(timer);

        // Запускаем сочную анимацию "удара" только при фактическом изменении секунды (3 -> 2 -> 1)
        if (currentCeilValue != lastDisplayedCountdownValue && currentCeilValue > 0)
        {
            lastDisplayedCountdownValue = currentCeilValue;
            countdownText.text = currentCeilValue.ToString();

            if (countdownPunchCoroutine != null)
            {
                StopCoroutine(countdownPunchCoroutine);
            }
            countdownPunchCoroutine = StartCoroutine(PunchCountdownTextRoutine());
        }
    }

    /// <summary>
    /// Корутина для сочного эффекта пульсации цифры обратного отсчета.
    /// </summary>
    private IEnumerator PunchCountdownTextRoutine()
    {
        Vector3 originalScale = Vector3.one;
        Vector3 targetScale = Vector3.one * countdownPunchScale;

        // Фаза резкого увеличения
        countdownText.transform.localScale = targetScale;

        // Фаза плавного возврата в исходный размер
        float elapsed = 0f;
        while (elapsed < countdownPunchDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / countdownPunchDuration;
            // Используем квадратичное сглаживание для упругости
            countdownText.transform.localScale = Vector3.Lerp(targetScale, originalScale, t * t);
            yield return null;
        }

        countdownText.transform.localScale = originalScale;
    }

    private void GameLoopManager_OnGoldChanged(object sender, GameLoopManager.OnGoldChangedEventArgs e)
    {
        UpdateGoldUI(e.currentTotalGold);
    }

    private void GameLoopManager_OnDayChanged(object sender, EventArgs e)
    {
        UpdateDayUI();
    }

    private void UpdateUIState()
    {
        if (GameLoopManager.Instance == null) return;

        HideAllPanels();

        if (GameLoopManager.Instance.IsPreparationActive())
        {
            preparationPanel.SetActive(true);
            lastDisplayedCountdownValue = -1; // Сбрасываем кэш отсчета
        }
        else if (GameLoopManager.Instance.IsCountdownToStartActive())
        {
            countdownPanel.SetActive(true);
        }
        else if (GameLoopManager.Instance.IsGamePlaying())
        {
            gameplayPanel.SetActive(true);
        }
        else if (GameLoopManager.Instance.IsDayResultsActive())
        {
            resultsPanel.SetActive(true);
            PopulateResultsUI();
        }
        else if (GameLoopManager.Instance.IsGameOver())
        {
            gameOverPanel.SetActive(true);
            PopulateGameOverUI();
        }
    }

    private void HideAllPanels()
    {
        if (preparationPanel != null) preparationPanel.SetActive(false);
        if (countdownPanel != null) countdownPanel.SetActive(false);
        if (gameplayPanel != null) gameplayPanel.SetActive(false);
        if (resultsPanel != null) resultsPanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
    }

    private void UpdateGoldUI(int currentGold)
    {
        if (goldText != null)
        {
            goldText.text = currentGold.ToString() + "g";
        }
    }

    private void UpdateDayUI()
    {
        if (GameLoopManager.Instance != null && dayText != null)
        {
            dayText.text = "День: " + GameLoopManager.Instance.GetCurrentDay().ToString();
        }
    }

    private void PopulateResultsUI()
    {
        if (GameLoopManager.Instance == null) return;

        if (resultsDayTitleText != null)
            resultsDayTitleText.text = "ИТОГИ ДНЯ " + GameLoopManager.Instance.GetCurrentDay().ToString();

        if (earnedTodayText != null)
            earnedTodayText.text = "+" + GameLoopManager.Instance.GetGoldEarnedToday().ToString() + "g";

        if (lostTodayText != null)
            lostTodayText.text = "-" + GameLoopManager.Instance.GetGoldLostToday().ToString() + "g";

        int netProfit = GameLoopManager.Instance.GetNetProfitToday();
        if (netProfitTodayText != null)
        {
            string sign = netProfit >= 0 ? "+" : "";
            netProfitTodayText.text = sign + netProfit.ToString() + "g";
            netProfitTodayText.color = netProfit >= 0 ? Color.green : Color.red;
        }

        if (totalGoldResultsText != null)
            totalGoldResultsText.text = "Баланс: " + GameLoopManager.Instance.GetTotalGold().ToString() + "g";

        if (successfulDeliveriesText != null)
            successfulDeliveriesText.text = "Выполнено заказов: " + GameLoopManager.Instance.GetSuccessfulDeliveriesToday().ToString();

        if (failedDeliveriesText != null)
            failedDeliveriesText.text = "Упущено заказов: " + GameLoopManager.Instance.GetFailedDeliveriesToday().ToString();
    }

    private void PopulateGameOverUI()
    {
        if (GameLoopManager.Instance == null || gameOverReasonText == null) return;

        gameOverReasonText.text = "День " + GameLoopManager.Instance.GetCurrentDay() +
            ": слишком много недовольных клиентов (" +
            GameLoopManager.Instance.GetFailedDeliveriesToday() + ")";
    }
}