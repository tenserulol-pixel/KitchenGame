using System;
using System.Collections.Generic;
using UnityEngine;

public class CustomerManager : MonoBehaviour
{
    public static CustomerManager Instance { get; private set; }

    [Header("Spawn")]
    [SerializeField] private GameObject customerPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private float spawnInterval = 15f;

    [Header("Limits")]
    [SerializeField] private int maxCustomers = 20;
    [SerializeField] private int minGroupSize = 1;
    [SerializeField] private int maxGroupSize = 4;

    [Header("Прогрессия сложности по дням")]
    [Tooltip("Насколько секунд короче интервал спавна за каждый пройденный день")]
    [SerializeField] private float spawnIntervalReductionPerDay = 0.5f;
    [SerializeField] private float minSpawnInterval = 5f;
    [Tooltip("На сколько вырастает лимит одновременных клиентов за каждый пройденный день")]
    [SerializeField] private int maxCustomersIncreasePerDay = 2;
    [SerializeField] private int maxCustomersCap = 40;
    [Tooltip("Раз в сколько дней увеличивается максимальный размер группы (до maxGroupSizeCap)")]
    [SerializeField] private int daysPerGroupSizeIncrease = 3;
    [SerializeField] private int maxGroupSizeCap = 6;

    [Header("Дневная норма (вместо таймера)")]
    [Tooltip("Сколько групп клиентов приходит за первый день")]
    [SerializeField] private int dailyGroupTarget = 5;
    [Tooltip("На сколько групп растёт дневная норма за каждый пройденный день")]
    [SerializeField] private int dailyGroupTargetIncreasePerDay = 1;
    [SerializeField] private int dailyGroupTargetCap = 15;

    // Значения из инспектора трактуются как "баланс на 1-й день" — от них и масштабируем
    private float baseSpawnInterval;
    private int baseMaxCustomers;
    private int baseMaxGroupSize;
    private int baseDailyGroupTarget;

    // Сколько групп уже заспавнено сегодня — сбрасывается на каждый новый день
    private int groupsSpawnedToday = 0;

    // Список столов заполняется динамически: каждый DiningTable при своём Awake
    // вызывает RequestRegisterTable(this), а при OnDestroy — RequestUnregisterTable(this).
    // Это решает две проблемы старого подхода через FindObjectsOfType:
    // 1) FindObjectsOfType медленный и устаревший (FindObjectsByType быстрее);
    // 2) Главное — купленные в рантайме через магазин столы НЕ попадали бы в
    //    список при Awake-инициализации, и спавнер их не видел бы. Теперь стол
    //    регистрируется ровно в момент создания (через Instantiate из ShopManager
    //    в шаге 3.2) и автоматически доступен для заселения клиентами.
    private readonly List<DiningTable> allTables = new List<DiningTable>();

    // Список столов, которые были зарегистрированы ДО того, как CustomerManager.Awake
    // отработал (например, если DiningTable.Awake стреляет раньше CustomerManager.Awake).
    // Порядок Awake в Unity не гарантирован, поэтому буферизуем запросы и применяем их
    // в своём Awake — иначе стол "пропал бы" из-за того, что Instance ещё не был установлен.
    private static readonly List<DiningTable> pendingRegistrations = new List<DiningTable>();

    private readonly List<CustomerAI> customerList = new List<CustomerAI>();

    // Группы клиентов, ждущие в очереди, потому что при спавне свободного стола не нашлось.
    // Каждая «группа» — это список клиентов, которые заспавнены вместе и должны уйти вместе,
    // если хотя бы у одного из них в очереди истечёт терпение (см. DisbandQueuedGroup).
    // Решение отсылать в очередь вместо мгновенного ухода приняли в CustomerAI.LeaveQueueAngry —
    // там же, где решается штраф. Тут мы только храним группы и распускаем их по запросу.
    private readonly List<List<CustomerAI>> queuedGroups = new List<List<CustomerAI>>();

    // Точка, куда отправляются ждущие клиенты. Назначается в инспекторе — обычно рядом со
    // входом, чтобы визуально было «очередь к стойке». Если не назначена — будет
    // использоваться spawnPoint как запасной вариант.
    [Header("Очередь")]
    [Tooltip("Точка ожидания для групп, которым не хватило стола. Если не назначена — используется spawnPoint.")]
    [SerializeField] private Transform queueSpot;
    [Tooltip("Шаг смещения для каждой следующей группы в очереди, чтобы они не стояли друг в друге.")]
    [SerializeField] private float queueGroupSpacing = 1.5f;

    private float spawnTimer;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        baseSpawnInterval = spawnInterval;
        baseMaxCustomers = maxCustomers;
        baseMaxGroupSize = maxGroupSize;
        baseDailyGroupTarget = dailyGroupTarget;

        // Применяем все буферизированные регистрации столов, которые были вызваны
        // ДО нашего Awake (когда Instance ещё не существовал). Это обычная ситуация
        // в Unity — порядок Awake не гарантирован между разными MonoBehaviour.
        if (pendingRegistrations.Count > 0)
        {
            foreach (DiningTable pending in pendingRegistrations)
            {
                if (pending != null && !allTables.Contains(pending))
                {
                    allTables.Add(pending);
                }
            }
            pendingRegistrations.Clear();
            Debug.Log($"[CustomerManager] Применено {allTables.Count} отложенных регистраций столов.");
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        // Отписываемся от событий GameLoopManager — без этого при перезагрузке сцены
        // старая подписка осталась бы висеть, а OnDayChanged стрелял бы в уничтоженный
        // CustomerManager (гонка NullReferenceException при следующем запуске).
        if (GameLoopManager.Instance != null)
        {
            GameLoopManager.Instance.OnDayChanged -= GameLoopManager_OnDayChanged;
        }
    }

    private void Start()
    {
        if (GameLoopManager.Instance != null)
        {
            GameLoopManager.Instance.OnDayChanged += GameLoopManager_OnDayChanged;
        }
    }

    private void GameLoopManager_OnDayChanged(object sender, EventArgs e)
    {
        ApplyDayDifficulty(GameLoopManager.Instance.GetCurrentDay());
    }

    /// <summary>
    /// Регистрирует стол в списке доступных для заселения. Вызывается из DiningTable.Awake
    /// через статический RequestRegisterTable, который сам решает — применять сразу или
    /// складывать в pendingRegistrations, если Instance ещё не создан.
    /// После регистрации также пытаемся посадить кого-нибудь из очереди — вдруг новый
    /// стол появился именно в тот момент, когда толпа ждёт.
    /// </summary>
    public void RegisterTable(DiningTable table)
    {
        if (table == null) return;
        if (allTables.Contains(table)) return;

        allTables.Add(table);

        // Новый стол мог появиться (например, куплен в магазине) — пробуем посадить
        // первую подходящую группу из очереди.
        TryAssignQueuedGroupsToTables();
    }

    // Метод вызывается из DiningTable, когда стол освободился (гости ушли, посуда убрана).
    // Подаём сигнал очереди, что появилось место.
    public void NotifyTableBecameAvailable()
    {
        TryAssignQueuedGroupsToTables();
    }

    /// <summary>
    /// Снимает стол с учёта. Вызывается из DiningTable.OnDestroy.
    /// Важно: если стол был Destroy'нут (например, продан в магазине или выгружен
    /// со сценой), он не должен оставаться в allTables — иначе FindAvailableTable
    /// мог бы вернуть null-ссылку, и OccupyTable упал бы с NRE.
    /// </summary>
    public void UnregisterTable(DiningTable table)
    {
        if (table == null) return;
        allTables.Remove(table);
        pendingRegistrations.Remove(table);
    }

    /// <summary>
    /// Статический метод для DiningTable.Awake — буферизует регистрацию до того,
    /// как CustomerManager.Instance будет создан. Если Instance уже есть, регистрирует
    /// сразу. Если нет — складывает в pendingRegistrations, и CustomerManager.Awake
    /// заберёт их все разом. Это решает проблему непредсказуемого порядка Awake в Unity.
    /// </summary>
    public static void RequestRegisterTable(DiningTable table)
    {
        if (table == null) return;

        if (Instance != null)
        {
            Instance.RegisterTable(table);
            return;
        }

        // CustomerManager ещё не создан — буферизуем.
        if (!pendingRegistrations.Contains(table))
        {
            pendingRegistrations.Add(table);
        }
    }

    /// <summary>
    /// Статический метод для DiningTable.OnDestroy — снимает стол с учёта либо из
    /// активного списка, либо из pending-буфера (если Instance ещё не создан и стол
    /// был удалён до Awake).
    /// </summary>
    public static void RequestUnregisterTable(DiningTable table)
    {
        if (table == null) return;

        if (Instance != null)
        {
            Instance.UnregisterTable(table);
            return;
        }

        pendingRegistrations.Remove(table);
    }

    /// <summary>
    /// Пересчитывает параметры спавна на основе номера текущего дня.
    /// Значения из инспектора = баланс 1-го дня, дальше — постепенно сложнее, с ограничениями сверху/снизу.
    /// Здесь же сбрасывается groupsSpawnedToday — раз в день, в момент начала подготовки к нему.
    /// </summary>
    private void ApplyDayDifficulty(int day)
    {
        int daysPassed = Mathf.Max(0, day - 1);

        spawnInterval = Mathf.Max(minSpawnInterval, baseSpawnInterval - spawnIntervalReductionPerDay * daysPassed);
        maxCustomers = Mathf.Min(maxCustomersCap, baseMaxCustomers + maxCustomersIncreasePerDay * daysPassed);

        int groupSizeIncrease = daysPerGroupSizeIncrease > 0 ? daysPassed / daysPerGroupSizeIncrease : 0;
        maxGroupSize = Mathf.Min(maxGroupSizeCap, baseMaxGroupSize + groupSizeIncrease);

        dailyGroupTarget = Mathf.Min(dailyGroupTargetCap, baseDailyGroupTarget + dailyGroupTargetIncreasePerDay * daysPassed);
        groupsSpawnedToday = 0;

        Debug.Log($"[CustomerManager] День {day}: интервал спавна {spawnInterval:F1}с, " +
                  $"макс. клиентов {maxCustomers}, макс. размер группы {maxGroupSize}, " +
                  $"норма групп на день {dailyGroupTarget}, всего столов {allTables.Count}.");
    }

    private void Update()
    {
        // Не спавним людей, если игровой раунд еще не начался или уже закончился!
        if (GameLoopManager.Instance != null && !GameLoopManager.Instance.IsGamePlaying())
        {
            return;
        }

        spawnTimer += Time.deltaTime;

        if (spawnTimer >= spawnInterval)
        {
            spawnTimer = 0f;
            TrySpawnCustomerGroup();
        }
    }

    private void TrySpawnCustomerGroup()
    {
        // Сегодняшняя норма уже выполнена — новых групп больше не будет, день
        // завершится сам, как только последние клиенты разойдутся (см. IsDailyWorkloadComplete).
        if (groupsSpawnedToday >= dailyGroupTarget)
        {
            return;
        }

        int groupSize = UnityEngine.Random.Range(minGroupSize, maxGroupSize + 1);

        // Не превышаем лимит клиентов
        if (customerList.Count + groupSize > maxCustomers)
        {
            return;
        }

        // Заспавнили группу клиентов сразу — до поиска стола. Если стола не найдётся,
        // отправим их в очередь, а не отменим спавн (иначе потерянные Instantiate'ы).
        List<CustomerAI> group = new List<CustomerAI>();
        for (int i = 0; i < groupSize; i++)
        {
            Vector3 offset = new Vector3(
                UnityEngine.Random.Range(-0.5f, 0.5f),
                0f,
                UnityEngine.Random.Range(-0.5f, 0.5f)
            );

            GameObject customerObject = Instantiate(
                customerPrefab,
                spawnPoint.position + offset,
                Quaternion.identity
            );

            CustomerAI customer = customerObject.GetComponent<CustomerAI>();

            group.Add(customer);
            customerList.Add(customer);
        }

        DiningTable table = FindAvailableTable(groupSize);

        if (table == null)
        {
            // Свободного стола нет — не отказываемся от группы сразу, а отправляем её в очередь.
            // Если в очереди уже слишком много групп (>= maxQueuedGroups) — тогда действительно
            // отказ, чтобы не накапливать бесконечную толпу. Иначе SendGroupToQueue создаёт
            // им маршрут к queueSpot и переключает в состояние WalkingToQueue.
            int maxQueuedGroups = Mathf.Max(1, maxCustomers / Mathf.Max(1, minGroupSize));
            if (queuedGroups.Count >= maxQueuedGroups)
            {
                Debug.Log($"[CustomerManager] Очередь переполнена ({queuedGroups.Count} групп), не спавним новую.");
                // Группа уже создана — удаляем её, чтобы не висела в воздухе без логики.
                foreach (CustomerAI c in group)
                {
                    if (c == null) continue;
                    customerList.Remove(c);
                    Destroy(c.gameObject);
                }
                return;
            }

            SendGroupToQueue(group);
            groupsSpawnedToday++;
            Debug.Log($"Группа из {groupSize} чел. отправлена в очередь (всего в очереди: {queuedGroups.Count}).");
            return;
        }

        table.OccupyTable(group);
        groupsSpawnedToday++;

        Debug.Log($"Создана группа из {groupSize} человек. Групп сегодня: {groupsSpawnedToday}/{dailyGroupTarget}.");
    }

    /// <summary>
    /// Отправляет уже заспавненную группу в очередь ожидания. Каждому клиенту задаётся
    /// точка назначения (с небольшим смещением, чтобы они не стояли в одной точке),
    /// переключается состояние на WalkingToQueue, и CustomerAI сам дальше работает с
    /// таймером терпения. Группа сохраняется в queuedGroups, чтобы DisbandQueuedGroup
    /// мог найти её по любому члену.
    /// </summary>
    private void SendGroupToQueue(List<CustomerAI> group)
    {
        if (group == null || group.Count == 0) return;

        Vector3 baseSpot = queueSpot != null ? queueSpot.position : spawnPoint.position;
        // Сдвигаем точку очереди для каждой новой группы, чтобы они не наклаывались друг на друга.
        // Используем queuedGroups.Count как индекс: 0-я группа стоит у базы, 1-я — на spacing дальше и т.д.
        Vector3 groupSpot = baseSpot + new Vector3(0f, 0f, queuedGroups.Count * queueGroupSpacing);

        foreach (CustomerAI customer in group)
        {
            if (customer == null) continue;
            customer.SetTargetQueueSpot(groupSpot);
        }

        queuedGroups.Add(new List<CustomerAI>(group));
    }

    /// <summary>
    /// Пытается посадить первую группу из очереди, если свободный стол появился.
    /// Вызывается когда стол освобождается (DiningTable.CleanTable после уборки посуды)
    /// и при регистрации нового стола (RegisterTable) — на случай, если он появится
    /// в фазе подготовки через магазин, и кто-то из очереди сразу сможет сесть.
    /// </summary>
    private void TryAssignQueuedGroupsToTables()
    {
        // Идём с конца, чтобы безопасно удалять из списка.
        for (int i = queuedGroups.Count - 1; i >= 0; i--)
        {
            List<CustomerAI> group = queuedGroups[i];
            if (group == null || group.Count == 0)
            {
                queuedGroups.RemoveAt(i);
                continue;
            }

            // Проверяем, что вся группа ещё в состоянии очереди (никто не ушёл).
            bool allStillQueued = true;
            foreach (CustomerAI c in group)
            {
                if (c == null) { allStillQueued = false; break; }
                CustomerAI.CustomerState s = c.GetState();
                if (s != CustomerAI.CustomerState.Queueing &&
                    s != CustomerAI.CustomerState.WalkingToQueue)
                {
                    allStillQueued = false;
                    break;
                }
            }
            if (!allStillQueued)
            {
                queuedGroups.RemoveAt(i);
                continue;
            }

            int groupSize = group.Count;
            DiningTable table = FindAvailableTable(groupSize);
            if (table == null) continue;

            // Нашли стол — сажаем. Нужно занять стулья до OccupyTable, иначе между
            // проверкой CanAccommodateGroup и самим OccupyTable другой спавнер
            // мог бы занять тот же стол.
            List<CustomerAI> groupCopy = new List<CustomerAI>(group);
            queuedGroups.RemoveAt(i);
            table.OccupyTable(groupCopy);

            Debug.Log($"[CustomerManager] Группа из очереди ({groupSize} чел.) посажена за стол '{table.name}'.");
        }
    }

    private DiningTable FindAvailableTable(int groupSize)
    {
        // Перебираем в обратном порядке, чтобы можно было безопасно удалить null-ссылки
        // (стол был Destroy'нут, но ещё не успел сняться с учёта из-за гонки OnDestroy).
        // Обратный перебор нужен, потому что удаление из списка во время foreach невозможно,
        // а во время обычного for с прямым порядком — смещает индексы.
        for (int i = allTables.Count - 1; i >= 0; i--)
        {
            DiningTable table = allTables[i];

            if (table == null)
            {
                // Стол был Destroy'нут, но ссылка осталась — чистим.
                allTables.RemoveAt(i);
                continue;
            }

            if (table.CanAccommodateGroup(groupSize))
            {
                return table;
            }
        }
        return null;
    }

    public void RemoveCustomer(CustomerAI customer)
    {
        if (customerList.Contains(customer))
        {
            customerList.Remove(customer);
        }
    }

    public int GetCustomerCount() => customerList.Count;

    public bool HasFreeTable(int groupSize) => FindAvailableTable(groupSize) != null;

    public List<CustomerAI> GetCustomers() => customerList;

    /// <summary>Возвращает снимок списка всех зарегистрированных столов (для UI/сохранений).</summary>
    public List<DiningTable> GetAllTables() => new List<DiningTable>(allTables);

    /// <summary>
    /// День выполнен, когда сегодняшняя норма групп заспавнена И в зале никого не осталось
    /// (все либо обслужены и ушли, либо ушли недовольными). Используется GameLoopManager'ом
    /// вместо таймера для завершения GamePlaying.
    /// </summary>
    public bool IsDailyWorkloadComplete() => groupsSpawnedToday >= dailyGroupTarget && customerList.Count == 0;

    public int GetGroupsSpawnedToday() => groupsSpawnedToday;
    public int GetDailyGroupTarget() => dailyGroupTarget;

    // Для карты "Щедрый день" — снижает, насколько быстро растёт дневная норма групп
    // по дням, не ниже нуля (отрицательный прирост означал бы, что норма со временем
    // сама уменьшается, что не было целью карты).
    public void ReduceDailyGroupTargetGrowth(int amount) =>
        dailyGroupTargetIncreasePerDay = Mathf.Max(0, dailyGroupTargetIncreasePerDay - amount);

    /// <summary>
    /// Увеличивает БАЗОВЫЙ максимальный размер группы (от которого считается maxGroupSize
    /// в ApplyDayDifficulty). Используется картой апгрейда, которая позволяет принимать
    /// более крупные группы клиентов.
    ///
    /// Важно: меняется именно baseMaxGroupSize, а не maxGroupSize напрямую — иначе
    /// при следующем ApplyDayDifficulty (на следующем дне) значение бы перетёрлось.
    /// После изменения сразу пересчитываем maxGroupSize через ApplyDayDifficulty,
    /// чтобы апгрейд подействовал немедленно, а не ждал следующего дня.
    /// </summary>
    public void IncreaseBaseMaxGroupSize(int amount)
    {
        if (amount == 0) return;
        baseMaxGroupSize = Mathf.Max(1, baseMaxGroupSize + amount);

        if (GameLoopManager.Instance != null)
        {
            ApplyDayDifficulty(GameLoopManager.Instance.GetCurrentDay());
        }

        Debug.Log($"[CustomerManager] Базовый макс. размер группы увеличен на {amount}. " +
                  $"Теперь: {baseMaxGroupSize} (с учётом дня: {maxGroupSize}).");
    }

    /// <summary>
    /// Распускает группу, в которой состоит caller — он уже успел получить штраф за
    /// истечение терпения (в LeaveQueueAngry), теперь по тому же принципу, что и у
    /// рассаженных групп в DiningTable, утягиваем за собой остальных, чтобы они не
    /// стояли в очереди в одиночестве без шанса быть обслуженными.
    ///
    /// Вызывается из CustomerAI.LeaveQueueAngry и передаёт «this» — конкретного клиента,
    /// у которого истекло терпение. Мы находим его группу в queuedGroups, удаляем её
    /// и вызываем LeaveQueue() на каждом оставшемся члене (не LeaveQueueAngry, чтобы
    /// не начислять штраф повторно — он уже списан с caller).
    /// </summary>
    public void DisbandQueuedGroup(CustomerAI caller)
    {
        if (caller == null) return;

        // Ищем группу, содержащую этого клиента.
        List<CustomerAI> groupToDisband = null;
        int foundIndex = -1;
        for (int i = 0; i < queuedGroups.Count; i++)
        {
            if (queuedGroups[i] == null) continue;
            if (queuedGroups[i].Contains(caller))
            {
                groupToDisband = queuedGroups[i];
                foundIndex = i;
                break;
            }
        }

        if (groupToDisband == null)
        {
            // caller не найден ни в одной очереди — возможно, он уже ушёл ранее. Это не ошибка,
            // просто выходим без действия.
            return;
        }

        // Убираем группу из списка очереди до обхода — иначе LeaveQueue мог бы
        // рекурсивно триггерить новые DisbandQueuedGroup.
        queuedGroups.RemoveAt(foundIndex);

        foreach (CustomerAI c in groupToDisband)
        {
            if (c == null) continue;
            if (c == caller)
            {
                // На самого caller тоже вызываем LeaveQueue — он должен переключить состояние
                // на Leaving и удалить себя из customerList. Штраф уже был начислен в
                // LeaveQueueAngry, поэтому тут именно LeaveQueue, а не LeaveQueueAngry.
                c.LeaveQueue();
            }
            else
            {
                // Остальных утягиваем за собой — без доп. штрафа, они же не сами потеряли терпение.
                c.LeaveQueue();
            }
        }

        Debug.Log($"[CustomerManager] Очередная группа распущена (caller: {caller.name}). Осталось в очереди: {queuedGroups.Count}.");
    }

    /// <summary>
    /// Уменьшает БАЗОВЫЙ дневной таргет групп (не меняя прироста по дням).
    /// Для карты «Большие компании» — группы становятся крупнее, но приходят реже.
    /// </summary>
    public void DecreaseBaseDailyGroupTarget(int amount)
    {
        if (amount == 0) return;
        baseDailyGroupTarget = Mathf.Max(1, baseDailyGroupTarget - amount);

        if (GameLoopManager.Instance != null)
        {
            ApplyDayDifficulty(GameLoopManager.Instance.GetCurrentDay());
        }

        Debug.Log($"[CustomerManager] Базовый дневной таргет групп уменьшен на {amount}. " +
                  $"Теперь: {baseDailyGroupTarget} (с учётом дня: {dailyGroupTarget}).");
    }

    /// <summary>
    /// Уменьшает БАЗОВЫЙ интервал спавна между группами.
    /// Для карты «Час пик» — группы приходят чаще, но компенсируется уменьшением maxCustomers.
    /// </summary>
    public void DecreaseBaseSpawnInterval(float amount)
    {
        if (Mathf.Approximately(amount, 0f)) return;
        baseSpawnInterval = Mathf.Max(minSpawnInterval, baseSpawnInterval - amount);

        if (GameLoopManager.Instance != null)
        {
            ApplyDayDifficulty(GameLoopManager.Instance.GetCurrentDay());
        }

        Debug.Log($"[CustomerManager] Базовый интервал спавна уменьшен на {amount:F1}с. " +
                  $"Теперь: {baseSpawnInterval:F1}с (с учётом дня: {spawnInterval:F1}с).");
    }

    /// <summary>
    /// Уменьшает БАЗОВЫЙ максимум одновременных клиентов (не меняя прироста по дням).
    /// Для карты «Час пик» — балансировка: чаще приходят, но меньше одновременно.
    /// </summary>
    public void DecreaseBaseMaxCustomers(int amount)
    {
        if (amount == 0) return;
        // Не позволяем опустить ниже minGroupSize — иначе спавн сломается,
        // потому что группа всегда состоит минимум из minGroupSize человек.
        baseMaxCustomers = Mathf.Max(Mathf.Max(1, minGroupSize), baseMaxCustomers - amount);

        if (GameLoopManager.Instance != null)
        {
            ApplyDayDifficulty(GameLoopManager.Instance.GetCurrentDay());
        }

        Debug.Log($"[CustomerManager] Базовый макс. клиентов уменьшен на {amount}. " +
                  $"Теперь: {baseMaxCustomers} (с учётом дня: {maxCustomers}).");
    }

    public float GetDailyProgressNormalized() => dailyGroupTarget > 0 ? (float)groupsSpawnedToday / dailyGroupTarget : 1f;
}
