using UnityEngine;
using System;

/// <summary>
/// ★ v2 (БАГФИКС-ПРОХОД) — СЛИЯНИЕ ДВУХ ВЕТОК ЭТОГО ФАЙЛА.
///
/// Предыдущий деливерабл (download/grid/GameLoopManager.cs) был собран на старой базе
/// и терял важные методы v7, из-за чего проект с ShopManager v1.3+/FurnitureMovingController
/// v2.3/UpgradeManager (карты) НЕ КОМПИЛИРОВАЛСЯ. Эта версия содержит ВСЁ:
///
/// - База v7: PushUiInputLock/PopUiInputLock/IsUiInputLocked (модальные экраны),
///   гейт Enter поверх модальных экранов, гейт черновика карт в StartCountdown,
///   DecreaseAngryCustomerTolerance (Патч 2, карта «Тёмная сделка» — без него DarkDeal
///   из card_ui/UpgradeManager.cs не компилируется и работает инвертированно).
/// - Ветка сетки (grid-save): применение сохранённой расстановки мебели через кадр
///   после старта сцены (FurnitureSaveUtility.ApplySavedLayout) и заполнение
///   placedObjects при сохранении (FurnitureSaveUtility.CollectPlacedObjects).
/// - НОВОЕ (багфикс): сохранение и восстановление счётчиков покупок магазина
///   (purchaseCounts). Раньше они вообще не сохранялись — после «Продолжить» лимитные
///   товары (maxPurchaseCount) можно было покупать заново, а RestorePurchaseCounts
///   оставался мёртвым кодом.
/// </summary>
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

    // === Система блокировки ввода под модальные экраны (счётчик, а не bool) ===
    // Гримуар карт и любые будущие модальные экраны (магазин, пауза) pus'ят блокировку
    // на время своей жизни и pop'ают при закрытии. Именно СЧЁТЧИК: если один экран
    // открыт поверх другого, закрытие нижнего не снимет защиту верхнего.
    // Потребители: FurnitureMovingController.Update (перенос мебели замирает),
    // гейт Enter в DayPreparation ниже (день нельзя начать поверх экрана).
    private int uiInputLockCount = 0;

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

        // ★ v2 (ветка сетки): применяем сохранённую расстановку мебели ЧЕРЕЗ ОДИН КАДР
        // после старта сцены. Почему так:
        // - все BaseCounter.Awake уже отработали и зарегистрировались в сетке;
        // - StartingLayoutRandomizer тоже успел уводить прилавки в резерв (его Start) —
        //   и на ПРОДОЛЖЕНИИ игры сохранённая расстановка побеждает случайную;
        // - на НОВОЙ игре (сохранения нет) ApplySavedLayout просто ничего не делает.
        StartCoroutine(ApplySavedFurnitureLayoutNextFrame());

        // ★ v2 (багфикс): восстанавливаем счётчики покупок магазина из сейва.
        // Внутри RestorePurchaseCounts также повторно применяются эффекты всех
        // купленных апгрейдов («Очищенная вода», «Вечный огонь» и т.д.).
        RestoreShopStateFromSave();
    }

    /// <summary>
    /// ★ v2: задержка на один кадр перед применением сохранённой расстановки —
    /// см. комментарий в Start(). Сама логика — в FurnitureSaveUtility.ApplySavedLayout().
    /// </summary>
    private System.Collections.IEnumerator ApplySavedFurnitureLayoutNextFrame()
    {
        yield return null;
        FurnitureSaveUtility.ApplySavedLayout();
    }

    /// <summary>
    /// ★ v2 (багфикс): восстанавливает счётчики покупок магазина из сохранения.
    /// Раньше RestorePurchaseCounts существовал, но никто его не вызывал (мёртвый код):
    /// после «Продолжить» лимиты maxPurchaseCount сбрасывались, и ограниченные товары
    /// можно было покупать заново.
    /// </summary>
    private void RestoreShopStateFromSave()
    {
        if (SaveManager.Instance == null || !SaveManager.Instance.HasSave()) return;

        GameSaveData save = SaveManager.Instance.GetCurrentSave();
        if (save == null) return;

        if (ShopManager.Instance != null)
        {
            ShopManager.Instance.RestorePurchaseCounts(save.purchaseCounts);
        }
    }

    private void Update()
    {
        switch (state)
        {
            case State.DayPreparation:
                // Enter стартует день, но НЕ поверх модального экрана (гримуар, магазин
                // и любые будущие PushUiInputLock-экраны): иначе день начался бы,
                // пока игрок ещё выбирает карту или покупает мебель.
                if (Input.GetKeyDown(KeyCode.Return) && !IsUiInputLocked)
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

    // ===== API блокировки ввода под модальные экраны =====

    /// <summary>
    /// Модальный экран открылся — «прижать» ввод игрока. Каждому Push обязан
    /// соответствовать ровно один Pop (при закрытии экрана / в OnDestroy).
    /// Вложенность безопасна: счётчик снимется только когда закроются ВСЕ экраны.
    /// </summary>
    public void PushUiInputLock() => uiInputLockCount++;

    /// <summary>
    /// Модальный экран закрылся — отпустить ввод. Пол по нулю: лишний/двойной Pop
    /// не уведёт счётчик в минус (иначе одна ошибка навсегда заблокировала бы ввод).
    /// </summary>
    public void PopUiInputLock() => uiInputLockCount = Mathf.Max(0, uiInputLockCount - 1);

    /// <summary>
    /// True, пока хоть один модальный экран держит блокировку.
    /// Читается FurnitureMovingController (перенос мебели замирает) и гейтом Enter выше.
    /// </summary>
    public bool IsUiInputLocked => uiInputLockCount > 0;

    /// <summary>
    /// Запуск обратного отсчета для начала рабочего дня.
    /// </summary>
    public void StartCountdown()
    {
        if (state != State.DayPreparation) return;

        // Черновик карт обязан быть закрыт до старта дня: гримуар — модальный экран,
        // персонаж заморожен гейтом в Player.cs. Если бы день начался при открытом
        // черновике, карты «провисли» бы до следующего дня (страховка-автоскип в
        // UpgradeManager всё же закрыла бы их, но игрок потерял бы выбор).
        // Путь навигации: A/D — подсветка, E — взять; или мышь; или Escape — пропустить.
        if (UpgradeManager.Instance != null && UpgradeManager.Instance.IsDraftActive)
        {
            Debug.Log("[GameLoop] Сначала выбери карту улучшения: A/D — листать, E — взять " +
                      "(или мышкой), Escape — пропустить.");
            return;
        }

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
    /// - при покупке в магазине (ShopManager.FinalizePurchase / CompletePendingPurchase)
    ///
    /// ★ v2: заполняется и placedObjects — снимок расстановки мебели собирает
    /// FurnitureSaveUtility прямо с реестра занятых ячеек сетки; и purchaseCounts —
    /// сколько раз куплен каждый товар магазина (лимиты maxPurchaseCount переживают
    /// перезапуск).
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
            // ownedUpgradeCardNames заполним в шаге 5, когда UpgradeManager получит
            // публичный GetOwnedCardNames(). Сейчас список останется пустым,
            // что приемлемо для базового цикла игры.
            ownedUpgradeCardNames = new System.Collections.Generic.List<string>(),
            // ★ v2: расстановка мебели — вся зарегистрированная в сетке
            // (и авторская, и купленная), с позициями, поворотами и itemId.
            placedObjects = FurnitureSaveUtility.CollectPlacedObjects(),
            // ★ v2 (багфикс): счётчики покупок магазина — лимиты и эффекты апгрейдов
            // переживают «Продолжить».
            purchaseCounts = ShopManager.Instance != null
                ? ShopManager.Instance.GetPurchaseCountsForSave()
                : new System.Collections.Generic.List<PurchaseCountSaveData>()
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

    // Для карты "Тёмная сделка" (Патч 2) — снижает порог поражения, но не ниже 1:
    // порог 0 означал бы мгновенное поражение от первого же недовольного клиента.
    // ★ v2: метод обязателен — card_ui/UpgradeManager.cs (DarkDeal) вызывает его,
    // без него проект не компилируется.
    public void DecreaseAngryCustomerTolerance(int amount) =>
        maxAngryCustomersPerDay = Mathf.Max(1, maxAngryCustomersPerDay - amount);

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
