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

[Tooltip("true (по умолчанию) — блюдо подаётся клиенту только на тарелке. " +
         "false — блюдо можно подать «голыми руками», без тарелки (зелья, напитки и т.п.). " +
         "Подача без тарелки работает только для рецептов ровно с ОДНИМ ингредиентом: " +
         "сам предмет в руках и есть блюдо. На тарелке такой рецепт тоже принимается.")]
public bool needsPlate = true;
}
