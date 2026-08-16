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

    // Столы больше не назначаются вручную в инспекторе: список заполняется автоматически
    // в Awake() через FindObjectsOfType, чтобы новый стол на сцене не забыли сюда добавить.
    private List<DiningTable> allTables = new List<DiningTable>();

    private readonly List<CustomerAI> customerList = new List<CustomerAI>();

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

        allTables = new List<DiningTable>(FindObjectsOfType<DiningTable>());
    }

    private void Start()
    {
        if (GameLoopManager.Instance != null)
        {
            GameLoopManager.Instance.OnDayChanged += GameLoopManager_OnDayChanged;
        }
    }

    private void OnDestroy()
    {
        if (GameLoopManager.Instance != null)
        {
            GameLoopManager.Instance.OnDayChanged -= GameLoopManager_OnDayChanged;
        }
    }

    private void GameLoopManager_OnDayChanged(object sender, EventArgs e)
    {
        ApplyDayDifficulty(GameLoopManager.Instance.GetCurrentDay());
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
                  $"норма групп на день {dailyGroupTarget}.");
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

        DiningTable table = FindAvailableTable(groupSize);

        if (table == null)
        {
            Debug.Log($"Нет стола для группы из {groupSize} человек.");
            return;
        }

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

        table.OccupyTable(group);
        groupsSpawnedToday++;

        Debug.Log($"Создана группа из {groupSize} человек. Групп сегодня: {groupsSpawnedToday}/{dailyGroupTarget}.");
    }

    private DiningTable FindAvailableTable(int groupSize)
    {
        foreach (DiningTable table in allTables)
        {
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
    public float GetDailyProgressNormalized() => dailyGroupTarget > 0 ? (float)groupsSpawnedToday / dailyGroupTarget : 1f;
}