using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
[CreateAssetMenu()]
public class BurningRecipeSO : ScriptableObject
{
public KitchenObjectSO input;
public KitchenObjectSO output; 
public float burningTimerMax;
}
