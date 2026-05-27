using UnityEngine;
using System;

public class StoveCounterVisual : MonoBehaviour
{
[SerializeField] private StoveCounter stoveCounter;
[SerializeField] private GameObject stoveGameObject;
[SerializeField] private GameObject particlesGameObject;

private void Start()
    {
        stoveCounter.OnStateChanged+=StoveCounter_OnStateChanged;
    }
    private void StoveCounter_OnStateChanged(object sender,StoveCounter.OnStateChangedEventArgs e)
    {
         Debug.Log("OnStateChanged triggered! State: " + e.state);
        bool showVisual=e.state==StoveCounter.State.Frying ||e.state==StoveCounter.State.Fried;
        Debug.Log("Show visual: " + showVisual);
        stoveGameObject.SetActive(showVisual);
        particlesGameObject.SetActive(showVisual);
    }

}
