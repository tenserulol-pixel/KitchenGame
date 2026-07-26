using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
[CreateAssetMenu()]
public class RecipeSO : ScriptableObject
{
public List<KitchenObjectSO> kitchenObjectSOList;
public string RecipeName;
public int Cost;
}
