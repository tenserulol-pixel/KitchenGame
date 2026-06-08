using UnityEngine;
using System.Collections.Generic;

public class CustomerManager : MonoBehaviour
{
    public static CustomerManager Instance { get; private set; }

    [Header("Настройки спавна")]
    [SerializeField] private GameObject customerPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private float spawnInterval = 15f;

    [Header("Окружение")]
    [SerializeField] private List<DiningTable> allTables = new List<DiningTable>();

    private float spawnTimer;

    private void Awake()
    {
        Instance = this;
    }

    private void Update()
    {
        spawnTimer += Time.deltaTime;
        if (spawnTimer >= spawnInterval)
        {
            spawnTimer = 0f;
            TrySpawnCustomerGroup();
        }
    }

    private void TrySpawnCustomerGroup()
    {
        // 1. Генерируем случайный размер группы от 1 до 4 человек
        int groupSize = Random.Range(2, 2); 

        // 2. Ищем стол, который может принять группу целиком
        DiningTable targetTable = null;
        foreach (DiningTable table in allTables)
        {
            if (table.CanAccommodateGroup(groupSize))
            {
                targetTable = table;
                break;
            }
        }

        // Если свободный стол под этот размер группы найден
        if (targetTable != null)
        {
            List<CustomerAI> newGroup = new List<CustomerAI>();

            // 3. Спавним нужное количество людей в одной точке
            for (int i = 0; i < groupSize; i++)
            {
                // Небольшое смещение при спавне, чтобы префабы не появлялись ровно друг в друге
                Vector3 spawnOffset = new Vector3(Random.Range(-0.5f, 0.5f), 0f, Random.Range(-0.5f, 0.5f));
                GameObject customerObj = Instantiate(customerPrefab, spawnPoint.position + spawnOffset, Quaternion.identity);
                
                CustomerAI customerAI = customerObj.GetComponent<CustomerAI>();
                newGroup.Add(customerAI);
            }

            // 4. Помещаем всю группу за выбранный стол
            targetTable.OccupyTable(newGroup);
            Debug.Log($"Появилась группа из {groupSize} чел. и направляется к столу {targetTable.gameObject.name}");
        }
        else
        {
            Debug.Log($"Нет свободных столов для группы из {groupSize} человек. Ждем...");
        }
    }
}