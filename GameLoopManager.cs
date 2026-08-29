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

        // Если есть сохранение — применяем его (currentDay, totalGold, owned upgrades).
        // SaveManager к этому моменту уже создан в BootScene и пережил загрузку GameScene,
        // потому что DontDestroyOnLoad. Если SaveManager ещё не успел инициализироваться
        // (что маловероятно, т.к. BootStrapper инстанциирует его до загрузки GameScene),
        // мы просто начнём новую игру с 1-го дня.
        LoadProgressFromSaveIfAvailable();
    }

    /// <summary>
    /// Если у SaveManager есть сохранение — восстанавливает из него currentDay и totalGold.
    /// Owned upgrades пока не восстанавливаются (UpgradeManager ещё не имеет публичного
    /// API для этого — добавим в шаге 5, когда будем полностью подключать сохранения).
    /// </summary>
    private void LoadProgressFromSaveIfAvailable()
    {
        if (SaveManager.Instance == null || !SaveManager.Instance.HasSave())
        {
            return;
        }

        GameSaveData save = SaveManager.Instance.GetCurrentSave();
        if (save == null) return;

        currentDay = save.currentDay;
        totalGold = save.totalGold;

        Debug.Log($"[GameLoop] Загружено сохранение: день {currentDay}, золота {totalGold}.");
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
                // На экране итогов дня игрок нажимает Пробел или Enter, чтобы перейти к подготовке следующего дня.
                // Escape — выход в главное меню с сохранением текущего прогресса (toContinue в следующий раз).
                if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
                {
                    StartNextDayPreparation();
                }
                else if (Input.GetKeyDown(KeyCode.Escape))
                {
                    QuitToMainMenu(saveProgress: true);
                }
                break;

            case State.GameOver:
                // Окончательный конец игры: R — перезагрузка с 1-го дня, Escape — выход в меню.
                // При поражении сохранение уже очищено в TriggerGameOver, так что в меню
                // кнопка "Продолжить" будет неактивна.
                if (Input.GetKeyDown(KeyCode.R))
                {
                    RestartEntireGame();
                }
                else if (Input.GetKeyDown(KeyCode.Escape))
                {
                    QuitToMainMenu(saveProgress: false);
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

        // Сохраняем прогресс в SaveManager — на случай, если игрок закроет игру
        // прямо здесь, на экране результатов, не переходя на следующий день.
        // Без этого SaveManager.HasSave() возвращал бы true при следующем запуске,
        // но текущий день был бы устаревшим (старое значение до завершения).
        SaveCurrentProgress();

        OnStateChanged?.Invoke(this, EventArgs.Empty);

        Debug.Log($"[GameLoop] День {currentDay} завершен! Статистика: " +
                  $"Успешно: {successfulDeliveriesToday} (Заработано: {goldEarnedToday}g), " +
                  $"Провалено: {failedDeliveriesToday} (Штрафы: {goldLostToday}g). " +
                  $"Текущий общий баланс: {totalGold}g.");
    }

    /// <summary>
    /// Сохраняет текущее состояние игры через SaveManager. Вызывается:
    /// - после завершения дня (FinishActiveDay)
    /// - при выходе в главное меню через QuitToMainMenu(saveProgress: true)
    /// - (в будущем) при покупке/продаже стола, взятии карты апгрейда и т.п.
    /// </summary>
    public void SaveCurrentProgress()
    {
        if (SaveManager.Instance == null)
        {
            // SaveManager мог не инициализироваться только в редакторе при запуске
            // GameScene напрямую, минуя BootScene. В билде этого не бывает.
            return;
        }

        var data = new GameSaveData
        {
            currentDay = currentDay,
            totalGold = totalGold,
            // ownedUpgradeCardNames и placedObjects заполним в шаге 5, когда
            // UpgradeManager получит публичный GetOwnedCardNames(), а ShopManager —
            // метод CollectPlacedObjectsForSave(). Сейчас список останется пустым,
            // что приемлемо для базового цикла игры.
            ownedUpgradeCardNames = new System.Collections.Generic.List<string>(),
            placedObjects = new System.Collections.Generic.List<PlacedObjectSaveData>()
        };

        SaveManager.Instance.Save(data);
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

        // При поражении очищаем сохранение — игрок не должен иметь возможность
        // "Продолжить" после проигрыша. Иначе он бы перезагружался прямо перед
        // тем моментом, который его и убил, и попадал в бесконечный цикл game over.
        if (SaveManager.Instance != null)
        {
            SaveManager.Instance.ClearSave();
        }

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
    /// Полный сброс игры при поражении. Переводит в главное меню (а не перезагружает
    /// текущую сцену, как раньше) — теперь у нас есть SceneLoader и MainMenuScene,
    /// и при следующем запуске "Новой игры" из меню прогресс начнётся с чистого листа.
    /// Сохранение уже очищено в TriggerGameOver, так что SaveManager.HasSave() вернёт false.
    /// </summary>
    private void RestartEntireGame()
    {
        QuitToMainMenu(saveProgress: false);
    }

    /// <summary>
    /// Переход в главное меню. Может вызываться:
    /// - из DayResults при нажатии Escape (с сохранением прогресса)
    /// - из GameOver при нажатии R или Escape (без сохранения — оно уже очищено)
    /// - из UI кнопки "Выйти в меню" на экране результатов (с сохранением)
    /// - из RestartEntireGame (без сохранения)
    ///
    /// saveProgress: true — вызвать SaveCurrentProgress() перед выходом.
    ///               false — не сохранять (например, при поражении, когда уже очищено).
    /// </summary>
    public void QuitToMainMenu(bool saveProgress)
    {
        if (saveProgress)
        {
            SaveCurrentProgress();
        }

        if (SceneLoader.Instance != null)
        {
            SceneLoader.Instance.LoadMainMenu(showLoadingScreen: true);
        }
        else
        {
            // Запасной вариант для случая, когда SceneLoader не инициализирован
            // (например, при запуске GameScene напрямую в редакторе минуя BootScene).
            // В билде этого не бывает — BootStrapper гарантирует создание SceneLoader.
            Debug.LogWarning("[GameLoopManager] SceneLoader не найден, использую прямой SceneManager.");
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenuScene");
        }
    }
}