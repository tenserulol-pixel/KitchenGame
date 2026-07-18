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

    [Header("Tables")]
    [SerializeField] private List<DiningTable> allTables = new List<DiningTable>();

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
        int groupSize = Random.Range(minGroupSize, maxGroupSize + 1);

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
                Random.Range(-0.5f, 0.5f),
                0f,
                Random.Range(-0.5f, 0.5f)
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

        Debug.Log($"Создана группа из {groupSize} человек.");
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
}