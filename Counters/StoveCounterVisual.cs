using UnityEngine;
using System;

public class StoveCounterVisual : MonoBehaviour
{
    [SerializeField] private StoveCounter stoveCounter;
    [Tooltip("Огонь под котлом — виден во время перемешивания (Stirring) и когда блюдо готово (Done)")]
    [SerializeField] private GameObject stoveGameObject;
    [Tooltip("Пар над котлом — виден во время перемешивания (Stirring) и когда блюдо готово (Done)")]
    [SerializeField] private GameObject particlesGameObject;

    private void Start()
    {
        if (stoveCounter != null)
        {
            stoveCounter.OnStateChanged += StoveCounter_OnStateChanged;
        }
        else
        {
            Debug.LogError($"[StoveCounterVisual] '{name}': stoveCounter не назначен!");
        }
    }

    private void OnDestroy()
    {
        if (stoveCounter != null)
        {
            stoveCounter.OnStateChanged -= StoveCounter_OnStateChanged;
        }
    }

    private void StoveCounter_OnStateChanged(object sender, StoveCounter.OnStateChangedEventArgs e)
    {
        // Огонь и пар показываем во время:
        // - Stirring (игрок активно перемешивает)
        // - Brewing (автоматическая варка после перемешивания)
        // - Done (блюдо готово, ещё тёплое)
        bool showVisual = e.state == StoveCounter.State.Stirring
                       || e.state == StoveCounter.State.Brewing
                       || e.state == StoveCounter.State.Done;
        if (stoveGameObject != null) stoveGameObject.SetActive(showVisual);
        if (particlesGameObject != null) particlesGameObject.SetActive(showVisual);
    }
}
