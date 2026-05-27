using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;
public class PlateIconSingleUI : MonoBehaviour
{
    [SerializeField] private Image image;
    public void SetKitchenObjectSO(KitchenObjectSO kitchenObjectSO)
    {
        image.sprite=kitchenObjectSO.sprite;
    }
}
