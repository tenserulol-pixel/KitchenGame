using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
public class PlateCompleteVisual : MonoBehaviour
{
[Serializable]
public struct KitchenObjectSO_GameObject
    {
        public KitchenObjectSO kitchenObjectSO;
        public GameObject gameObject;
    }
[SerializeField] private PlateKitchenObject plateKitchenObject;
[SerializeField] private List<KitchenObjectSO_GameObject> kitchenObjectSOGameObjectList;

private void Start()
    {
        plateKitchenObject.OnIngredientAdded+=PlateKitchenObject_OnIngredientAdded;

    }
private void PlateKitchenObject_OnIngredientAdded(object sender,PlateKitchenObject.OnIngredientAddedEventArgs e)
    {
        foreach(KitchenObjectSO_GameObject kitchenObjectSOGameObject in kitchenObjectSOGameObjectList)
        {
            if (kitchenObjectSOGameObject.kitchenObjectSO == e.kitchenObjectSO)
            {
                kitchenObjectSOGameObject.gameObject.SetActive(true);
            }
        }
        //e.kitchenObjectSO
    }
}
