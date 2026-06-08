using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public class DeliveryManagerSingleUI : MonoBehaviour
{
    [Header("UI Элементы карточки заказа")]
    [SerializeField] private TextMeshProUGUI recipeNameText;
    [SerializeField] private Transform iconContainer;
    [SerializeField] private Transform iconTemplate;

    private void Awake()
    {
        // Выключаем шаблон при старте, чтобы он не висел пустым
        if (iconTemplate != null)
        {
            iconTemplate.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Заполняет карточку интерфейса данными из ScriptableObject рецепта
    /// </summary>
    public void SetRecipeSO(RecipeSO recipeSO)
    {
        // -------------------------------------------------------------
        // БЛОК ЗАЩИТЫ ОТ NULL (Выполняется в первую очередь)
        // -------------------------------------------------------------
        
        // 1. Проверяем, передан ли вообще рецепт
        if (recipeSO == null) 
        {
            Debug.LogError($"[DeliveryManagerSingleUI] Метод SetRecipeSO на объекте {gameObject.name} вызван с пустым рецептом (null)!");
            return;
        }

        // 2. Проверяем, назначен ли контейнер для иконок в инспекторе префаба
        if (iconContainer == null) 
        {
            Debug.LogError($"[DeliveryManagerSingleUI] На префабе {gameObject.name} не назначена ссылка на 'Icon Container' в инспекторе!", gameObject);
            return; 
        }

        // 3. Проверяем, есть ли список ингредиентов внутри ScriptableObject рецепта
        if (recipeSO.kitchenObjectSOList == null) 
        {
            Debug.LogError($"[DeliveryManagerSingleUI] У рецепта {recipeSO.RecipeName} отсутствует или пуст список kitchenObjectSOList!");
            return;
        }

        // -------------------------------------------------------------
        // ОСНОВНАЯ ЛОГИКА ОТОБРАЖЕНИЯ
        // -------------------------------------------------------------

        // Устанавливаем текст названия (используем свойство с большой буквы RecipeName)
        if (recipeNameText != null) 
        {
            recipeNameText.text = recipeSO.RecipeName;
        }

        // Очищаем старые иконки от предыдущих заказов (кроме самого шаблона)
        foreach (Transform child in iconContainer)
        {
            if (child == iconTemplate) continue;
            Destroy(child.gameObject);
        }

        // Создаем новые иконки ингредиентов для этого блюда
        foreach (KitchenObjectSO kitchenObjectSO in recipeSO.kitchenObjectSOList)
        {
            if (iconTemplate == null) break;

            // Спавним копию шаблона иконки внутри контейнера
            Transform iconTransform = Instantiate(iconTemplate, iconContainer);
            
            if (iconTransform != null)
            {
                iconTransform.gameObject.SetActive(true);
                
                // Безопасно ищем компонент Image на созданном объекте
                if (iconTransform.TryGetComponent<Image>(out Image ingredientImage)) 
                {
                    // Проверяем, задана ли картинка у самого ингредиента в его ScriptableObject
                    if (kitchenObjectSO != null && kitchenObjectSO.sprite != null) 
                    {
                        ingredientImage.sprite = kitchenObjectSO.sprite;
                    }
                    else 
                    {
                        Debug.LogWarning($"[DeliveryManagerSingleUI] У ингредиента {kitchenObjectSO?.name} не назначен Sprite в ScriptableObject!");
                    }
                }
                else 
                {
                    // Если Image на шаблоне вдруг не окажется, Unity выдаст эту ошибку вместо вылета всей игры
                    Debug.LogError($"[DeliveryManagerSingleUI] На объекте шаблона иконки '{iconTransform.name}' внутри префаба не найден компонент UI Image!", iconTransform);
                }
            }
        }
    }
}