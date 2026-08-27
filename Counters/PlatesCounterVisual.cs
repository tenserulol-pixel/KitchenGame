using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

public class PlatesCounterVisual : MonoBehaviour
{
    [SerializeField] private Transform counterTopPoint;
    [SerializeField] private Transform plateVisualPrefab;
    [SerializeField] private PlatesCounter plateCounter;

    private List<GameObject> plateVisualGameObjectList;

    private void Awake()
    {
        plateVisualGameObjectList = new List<GameObject>();
    }

    // Подписка в OnEnable, а не в Start: PlatesCounter.Update в первом кадре уже
    // стреляет OnPlateSpawned (столько раз, сколько plateSpawnAmount), и если визуал
    // ещё не подписался (Start идёт после Awake, но событие стреляет в Update) —
    // стартовая стопка тарелок визуально не появится.
    // OnEnable гарантированно вызывается до первого Update этого же объекта.
    private void OnEnable()
    {
        if (plateCounter != null)
        {
            plateCounter.OnPlateSpawned += PlateCounter_OnPlateSpawned;
            plateCounter.OnPlateRemoved += PlateCounter_OnPlateRemoved;
        }
    }

    private void OnDisable()
    {
        if (plateCounter != null)
        {
            plateCounter.OnPlateSpawned -= PlateCounter_OnPlateSpawned;
            plateCounter.OnPlateRemoved -= PlateCounter_OnPlateRemoved;
        }
    }

    private void PlateCounter_OnPlateRemoved(object sender, System.EventArgs e)
    {
        // Защита от рассинхронизации: если событие пришло, а визуальный список уже пуст.
        if (plateVisualGameObjectList.Count == 0)
        {
            Debug.LogWarning($"[PlatesCounterVisual] '{name}': OnPlateRemoved пришёл, а визуальный список пуст.");
            return;
        }

        GameObject plateGameObject = plateVisualGameObjectList[plateVisualGameObjectList.Count - 1];
        plateVisualGameObjectList.Remove(plateGameObject);
        Destroy(plateGameObject);
    }

    private void PlateCounter_OnPlateSpawned(object sender, System.EventArgs e)
    {
        Transform plateVisualTransform = Instantiate(plateVisualPrefab, counterTopPoint);

        float plateOffsetY = .1f;
        plateVisualTransform.localPosition = new Vector3(0, plateOffsetY * plateVisualGameObjectList.Count, 0);
        plateVisualGameObjectList.Add(plateVisualTransform.gameObject);
    }
}