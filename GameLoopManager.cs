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

    private State state;
    private float countdownToStartTimer;
    
    [Header("Цикл Дней и Прогрессия")]
    private int currentDay = 1;

    [Header("Экономика и Очки")]
    [SerializeField] private int basePenaltyPerOrder = 20; // Штраф за проваленный заказ

    [Header("Условие поражения")]
    [Tooltip("Если за одну смену уходит недовольными больше этого числа клиентов — игра окончена")]
    [SerializeField] private int maxAngryCustomersPerDay = 5;
    
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
                // День больше не идёт по таймеру — заканчивается, когда сегодняшняя
                // норма групп заспавнена и зал полностью опустел (см. CustomerManager).
                if (CustomerManager.Instance != null && CustomerManager.Instance.IsDailyWorkloadComplete())
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

        goldEarnedToday = 0;
        goldLostToday = 0;
        successfulDeliveriesToday = 0;
        failedDeliveriesToday = 0;
    }

    /// <summary>
    /// Метод начисления золота за успешно выполненный заказ.
    /// amount — стоимость конкретного рецепта (RecipeSO.Cost), а не фиксированная награда,
    /// чтобы дорогие блюда были выгоднее дешёвых.
    /// </summary>
    public void AddOrderGold(int amount)
    {
        if (!IsGamePlaying()) return;

        goldEarnedToday += amount;
        successfulDeliveriesToday++;
        
        OnGoldChanged?.Invoke(this, new OnGoldChangedEventArgs { 
            currentTotalGold = totalGold + (goldEarnedToday - goldLostToday), 
            changeAmount = amount 
        });
    }

    /// <summary>
    /// Начисление штрафа при уходе недовольного клиента или просрочке заказа.
    /// Если недовольных за смену набирается больше maxAngryCustomersPerDay — смена
    /// обрывается немедленно поражением, не дожидаясь конца таймера.
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

        if (failedDeliveriesToday >= maxAngryCustomersPerDay)
        {
            TriggerGameOver();
        }
    }

    /// <summary>
    /// Немедленный переход в состояние поражения — смена прерывается на месте,
    /// таймер и спавн клиентов останавливаются сами (Update/CustomerManager смотрят на state).
    /// </summary>
    private void TriggerGameOver()
    {
        state = State.GameOver;
        OnStateChanged?.Invoke(this, EventArgs.Empty);

        Debug.Log($"[GameLoop] День {currentDay}: недовольных клиентов за смену — {failedDeliveriesToday}. Игра окончена.");
    }

    /// <summary>
    /// Списывает деньги с накопленного баланса (totalGold) — например, при покупке нового
    /// стола/оборудования в фазе подготовки. Возвращает false, если денег не хватает.
    /// Работает с totalGold, а не с дневной статистикой, т.к. тратить предполагается
    /// между днями, когда goldEarnedToday/goldLostToday уже обнулены.
    /// </summary>
    public bool SpendMoney(int amount)
    {
        if (totalGold < amount) return false;

        totalGold -= amount;

        OnGoldChanged?.Invoke(this, new OnGoldChangedEventArgs {
            currentTotalGold = totalGold + (goldEarnedToday - goldLostToday),
            changeAmount = -amount
        });

        return true;
    }

    public bool HasEnoughMoney(int amount) => totalGold >= amount;

    // Для UpgradeManager — карта "терпимость к недовольным" поднимает порог поражения.
    public void IncreaseAngryCustomerTolerance(int amount) => maxAngryCustomersPerDay += amount;

    // Для карты "Мягкая рука" — снижает штраф за проваленный заказ, не ниже нуля
    // (отрицательный штраф означал бы получать деньги за провал, что не имеет смысла).
    public void ReducePenaltyPerOrder(int amount) => basePenaltyPerOrder = Mathf.Max(0, basePenaltyPerOrder - amount);

    // Для карты "Задаток покровителя" — разовая прибавка к накопленному золоту в момент
    // взятия карты, а не постоянный множитель. В отличие от AddOrderGold, не идёт через
    // дневную статистику (goldEarnedToday) — это не заработок за смену, а сам факт карты.
    public void AddBonusGold(int amount)
    {
        totalGold += amount;

        OnGoldChanged?.Invoke(this, new OnGoldChangedEventArgs {
            currentTotalGold = totalGold + (goldEarnedToday - goldLostToday),
            changeAmount = amount
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