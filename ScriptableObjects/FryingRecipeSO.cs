using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
[CreateAssetMenu()]
public class FryingRecipeSO : ScriptableObject
{
public KitchenObjectSO input;
public KitchenObjectSO output; 
public float fryingTimerMax;
}
