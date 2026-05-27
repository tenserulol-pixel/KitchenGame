using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
[CreateAssetMenu()]
public class CuttingRecipeSO : ScriptableObject
{
public KitchenObjectSO input;
public KitchenObjectSO output; 
public int cuttingProgressMax;
}
