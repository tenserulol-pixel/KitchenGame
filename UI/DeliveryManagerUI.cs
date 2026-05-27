using UnityEngine;
using System.Collections;
using System.Collections.Generic;
public class DeliveryManagerUI : MonoBehaviour
{
[SerializeField] private Transform container;

[SerializeField] private Transform recipeTemplate;

private void Awake()
    {
        recipeTemplate.gameObject.SetActive(false);
    }
private void Start()
    {
        DeliveryManager.Instance.OnRecipeSpawned+=Instance_OnRecipeSpawned;
        DeliveryManager.Instance.OnRecipeCompleted+=Instance_OnRecipeCompleted;
        UpdateVisual();
    }
private void Instance_OnRecipeSpawned(object sender,System.EventArgs e)
    {
    UpdateVisual();
    }
private void Instance_OnRecipeCompleted(object sender,System.EventArgs e)
    {
    UpdateVisual();
    }
private void UpdateVisual()
    {
        foreach(Transform child in container)
        {
            if(child==recipeTemplate) continue;
            Destroy(child.gameObject);
        }
        foreach (RecipeSO recipeSO in DeliveryManager.Instance.GetWaitingRecipeSOList())
        {
            Transform recipeTransform =Instantiate(recipeTemplate,container);
            recipeTransform.gameObject.SetActive(true);
            recipeTransform.GetComponent<DeliveryManagerSingleUI>().SetRecipeSO(recipeSO);
        }
    }
}
