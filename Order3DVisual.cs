using UnityEngine;
using System.Collections.Generic;

public class Order3DVisual : MonoBehaviour
{
    [Header("Настройки отображения 3D еды")]
    [SerializeField] private float itemSpacing = 0.4f; // Расстояние между модельками продуктов
    [SerializeField] private float globalScale = 0.3f;   // Насколько уменьшить модельки над головой
    [SerializeField] private float rotationSpeed = 50f;  // Скорость вращения продуктов

    private List<GameObject> spawnedModels = new List<GameObject>();

    /// <summary>
    /// Этот метод заменяет SetRecipeSO. Он спавнит 3D-модели ингредиентов в ряд.
    /// </summary>
    public void SetRecipe3D(RecipeSO recipeSO)
    {
        // Очищаем старые модельки, если они были
        ClearOldModels();

        if (recipeSO == null || recipeSO.kitchenObjectSOList == null) return;

        List<KitchenObjectSO> ingredients = recipeSO.kitchenObjectSOList;
        int count = ingredients.Count;

        // Вычисляем начальную точку смещения, чтобы весь ряд был строго по центру над головой
        float startX = -((count - 1) * itemSpacing) / 2f;

        for (int i = 0; i < count; i++)
        {
            KitchenObjectSO ingredient = ingredients[i];
            
            if (ingredient == null || ingredient.prefab == null) continue;

            // 1. Спавним 3D модель продукта как дочерний объект к этому контейнеру
            Transform modelTransform = Instantiate(ingredient.prefab, transform);
            GameObject modelGameObject = modelTransform.gameObject;

            // 2. Выстраиваем их в горизонтальный ряд по оси X
            float posX = startX + (i * itemSpacing);
            modelTransform.localPosition = new Vector3(posX, 0f, 0f);

            // 3. Уменьшаем модельку, чтобы она не была размером с самого клиента
            modelTransform.localScale = Vector3.one * globalScale;

            // 4. Отключаем компоненты физики/скриптов на копиях, чтобы они не падали и не улетали
            if (modelGameObject.TryGetComponent<Collider>(out var col)) col.enabled = false;
            if (modelGameObject.TryGetComponent<Rigidbody>(out var rb)) rb.isKinematic = true;

            spawnedModels.Add(modelGameObject);
        }
    }

    private void Update()
    {
        // Заставляем все ингредиенты плавно крутиться для красоты
        foreach (GameObject model in spawnedModels)
        {
            if (model != null)
            {
                model.transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);
            }
        }
    }

    private void ClearOldModels()
    {
        foreach (GameObject model in spawnedModels)
        {
            if (model != null) Destroy(model);
        }
        spawnedModels.Clear();
    }
}