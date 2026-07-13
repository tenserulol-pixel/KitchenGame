using UnityEngine;
using System;

public class GameLoopManager : MonoBehaviour
{
    // Одиночка (Singleton) для легкого доступа из любой части кода (UI, спавнеры, игроки)
    public static GameLoopManager Instance { get; private set; }

    // События изменения состояний игры для подписки UI, спавнеров и звуковых систем
    public event EventHandler OnStateChanged;
    public event EventHandler OnCountdownTimerChanged;
    public event EventHandler OnDayChanged;

    // Расширенное перечисление всех фаз игрового цикла
    public enum State
    {
        DayPreparation,     // Подготовка: расстановка столов, покупка оборудования, нет таймера
        CountdownToStart,   // Обратный отсчет перед началом дня (3... 2... 1... День начался!)
        GamePlaying,        // Рабочий день: идет время, приходят гости
        DayResults,         // Конец дня: показывается статистика доходов, расходов и штрафов
        GameOver            // Окончательный проигрыш (например, если штрафы превысили лимит)
    }

    [Header("Тайминги игры")]
    [SerializeField] private float countdownToStartTimerMax = 3f; // Время обратного отсчета
    [SerializeField] private float gamePlayingTimerMax = 120f;    // Длительность рабочего дня (в секундах)

    private State state;
    private float countdownToStartTimer;
    private float gamePlayingTimer;
    
    [Header("Цикл Дней и Прогрессия")]
    private int currentDay = 1;

    [Header("Экономика и Очки")]
    [SerializeField] private int baseRewardPerOrder = 50; // Базовая оплата за успешное блюдо
    [SerializeField] private int basePenaltyPerOrder = 20; // Штраф за проваленный заказ
    
    private int totalGold = 0; // Накопленное золото (всего у игрока)
    private int goldEarnedToday = 0; // Заработано золота за сегодня
    private int goldLostToday = 0; // Потеряно золота (штрафы) за сегодня

    private int successfulDeliveriesToday = 0; // Успешных заказов за сегодня
    private int failedDeliveriesToday = 0; // Сгоревших/проваленных заказов за сегодня

    // События для обновления золота и очков в UI
    public event EventHandler<OnGoldChangedEventArgs> OnGoldChanged;
    public class OnGoldChangedEventArgs : EventArgs {
        public int currentTotalGold;
        public int changeAmount;
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // Начинаем игру с фазы подготовки первого дня
        state = State.DayPreparation;
    }

    private void Start()
    {
        ResetDayStats();
    }

    private void Update()
    {
        switch (state)
        {
            case State.DayPreparation:
                // В режиме подготовки игрок может нажать ENTER или специальную кнопку, чтобы начать рабочий день
                if (Input.GetKeyDown(KeyCode.Return))
                {
                    StartCountdown();
                }
                break;

            case State.CountdownToStart:
                countdownToStartTimer -= Time.deltaTime;
                OnCountdownTimerChanged?.Invoke(this, EventArgs.Empty);

                if (countdownToStartTimer <= 0f)
                {
                    state = State.GamePlaying;
                    OnStateChanged?.Invoke(this, EventArgs.Empty);
                }
                break;

            case State.GamePlaying:
                gamePlayingTimer -= Time.deltaTime;

                if (gamePlayingTimer <= 0f)
                {
                    FinishActiveDay();
                }
                break;

            case State.DayResults:
                // На экране итогов дня игрок нажимает Пробел или Enter, чтобы перейти к подготовке следующего дня
                if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
                {
                    StartNextDayPreparation();
                }
                break;

            case State.GameOver:
                // Окончательный конец игры (если нужно перезагрузить весь прогресс с 1-го дня)
                if (Input.GetKeyDown(KeyCode.R))
                {
                    RestartEntireGame();
                }
                break;
        }
    }

    /// <summary>
    /// Запуск обратного отсчета для начала рабочего дня.
    /// </summary>
    public void StartCountdown()
    {
        if (state != State.DayPreparation) return;

        countdownToStartTimer = countdownToStartTimerMax;
        state = State.CountdownToStart;
        OnStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Завершение рабочего дня и подсчет выручки.
    /// </summary>
    private void FinishActiveDay()
    {
        state = State.DayResults;
        
        // Добавляем чистую дневную прибыль в общий кошелек игрока
        int netProfit = goldEarnedToday - goldLostToday;
        totalGold = Mathf.Max(0, totalGold + netProfit);

        OnStateChanged?.Invoke(this, EventArgs.Empty);
        
        Debug.Log($"[GameLoop] День {currentDay} завершен! Статистика: " +
                  $"Успешно: {successfulDeliveriesToday} (Заработано: {goldEarnedToday}g), " +
                  $"Провалено: {failedDeliveriesToday} (Штрафы: {goldLostToday}g). " +
                  $"Текущий общий баланс: {totalGold}g.");
    }

    /// <summary>
    /// Переход к этапу планирования следующего дня.
    /// </summary>
    private void StartNextDayPreparation()
    {
        currentDay++;
        ResetDayStats();
        
        state = State.DayPreparation;
        
        // Оповещаем другие системы (например, спавнер клиентов может увеличить сложность на основе дня)
        OnDayChanged?.Invoke(this, EventArgs.Empty);
        OnStateChanged?.Invoke(this, EventArgs.Empty);
        
        Debug.Log($"[GameLoop] Началась подготовка к Дню {currentDay}. Настройте кухню и нажмите Enter.");
    }

    /// <summary>
    /// Очистка дневной статистики перед началом нового дня.
    /// </summary>
    private void ResetDayStats()
    {
        countdownToStartTimer = countdownToStartTimerMax;
        gamePlayingTimer = gamePlayingTimerMax;

        goldEarnedToday = 0;
        goldLostToday = 0;
        successfulDeliveriesToday = 0;
        failedDeliveriesToday = 0;
    }

    /// <summary>
    /// Метод начисления золота за успешно выполненный заказ.
    /// </summary>
    public void AddOrderGold()
    {
        if (!IsGamePlaying()) return;

        goldEarnedToday += baseRewardPerOrder;
        successfulDeliveriesToday++;
        
        OnGoldChanged?.Invoke(this, new OnGoldChangedEventArgs { 
            currentTotalGold = totalGold + (goldEarnedToday - goldLostToday), 
            changeAmount = baseRewardPerOrder 
        });
    }

    /// <summary>
    /// Начисление штрафа при уходе недовольного клиента или просрочке заказа.
    /// </summary>
    public void DeductOrderGold()
    {
        if (!IsGamePlaying()) return;

        goldLostToday += basePenaltyPerOrder;
        failedDeliveriesToday++;
        
        OnGoldChanged?.Invoke(this, new OnGoldChangedEventArgs { 
            currentTotalGold = Mathf.Max(0, totalGold + (goldEarnedToday - goldLostToday)), 
            changeAmount = -basePenaltyPerOrder 
        });
    }

    // Вспомогательные методы проверки текущих состояний игры
    public bool IsPreparationActive() => state == State.DayPreparation;
    public bool IsGamePlaying() => state == State.GamePlaying;
    public bool IsCountdownToStartActive() => state == State.CountdownToStart;
    public bool IsDayResultsActive() => state == State.DayResults;
    public bool IsGameOver() => state == State.GameOver;

    // Геттеры для UI
    public int GetCurrentDay() => currentDay;
    public float GetCountdownToStartTimer() => countdownToStartTimer;
    public float GetGamePlayingTimerNormalized() => gamePlayingTimer / gamePlayingTimerMax;
    public float GetGamePlayingTimer() => gamePlayingTimer;

    // Финансовые геттеры для экрана результатов дня
    public int GetTotalGold() => totalGold;
    public int GetGoldEarnedToday() => goldEarnedToday;
    public int GetGoldLostToday() => goldLostToday;
    public int GetNetProfitToday() => goldEarnedToday - goldLostToday;
    public int GetSuccessfulDeliveriesToday() => successfulDeliveriesToday;
    public int GetFailedDeliveriesToday() => failedDeliveriesToday;

    /// <summary>
    /// Полный сброс игры при поражении.
    /// </summary>
    private void RestartEntireGame()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex
        );
    }
}