using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

public class CuttingCounter : BaseCounter, IHasProgress
{
    public event EventHandler<IHasProgress.OnProgressChangedEventArgs> OnProgressChanged;
    public event EventHandler OnCut; 

    [SerializeField] private CuttingRecipeSO[] cuttingRecipeSOarray; 
    
    private float cuttingProgress; // Используем float для плавного удержания
    private bool isPlayerCutting = false;

    private void Update()
    {
        // Проверяем, зажата ли кнопка и лежит ли на столе объект
        if (isPlayerCutting && HasKitchenObject())
        {
            // Проверяем, можно ли этот объект вообще резать дальше
            if (HasRecipeWithInput(GetKitchenObject().GetKitchenObjectSo()))
            {
                CuttingRecipeSO cuttingRecipeSO = GetCuttingRecipeSOWithInput(GetKitchenObject().GetKitchenObjectSo());
                
                // Плавно увеличиваем прогресс
                cuttingProgress += Time.deltaTime;

                // Отправляем текущий прогресс в полоску UI
                OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs
                {
                    progressNormalized = cuttingProgress / cuttingRecipeSO.cuttingProgressMax
                });  

                // Если время удержания закончилось — объект нарезался!
                if (cuttingProgress >= cuttingRecipeSO.cuttingProgressMax)
                {
                    KitchenObjectSO outputKitchenObjectSO = GetOutputForInput(GetKitchenObject().GetKitchenObjectSo());
                    
                    GetKitchenObject().DestroySelf();
                    KitchenObject.SpawnKitchenObject(outputKitchenObjectSO, this);   
                    
                    // КРИТИЧЕСКИЙ МОМЕНТ: Сбрасываем резку и обнуляем прогресс
                    isPlayerCutting = false;
                    cuttingProgress = 0f;

                    // Принудительно отправляем 0f (или 1f), чтобы ProgressBarUI вызвал свой метод Hide()
                    OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs
                    {
                        progressNormalized = 0f
                    });
                }
            }
            else
            {
                // Если объект на столе ЕСТЬ, но его больше НЕЛЬЗЯ нарезать (например, он уже нарезан)
                // Сбрасываем состояние резки и прячем UI
                isPlayerCutting = false;
                cuttingProgress = 0f;
                OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs { progressNormalized = 0f });
            }
        }
    }

    public override void Interact(Player player)
    {
        if (!HasKitchenObject())
        {
            if (player.HasKitchenObject())
            {
                if (HasRecipeWithInput(player.GetKitchenObject().GetKitchenObjectSo()))
                {
                    player.GetKitchenObject().SetKitchenObjectParent(this);
                    cuttingProgress = 0f; // Сброс прогресса под новый предмет
                    
                    CuttingRecipeSO cuttingRecipeSO = GetCuttingRecipeSOWithInput(GetKitchenObject().GetKitchenObjectSo());
                    OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs
                    {
                        progressNormalized = cuttingProgress / cuttingRecipeSO.cuttingProgressMax
                    });   
                }
            }
        }
        else
        {
            // Если на столе уже что-то лежит и игрок нажимает Е
            if (player.HasKitchenObject())
            {
                if (player.GetKitchenObject().TryGetPlate(out PlateKitchenObject plateKitchenObject))
                {
                    if (plateKitchenObject.AddIngredient(GetKitchenObject().GetKitchenObjectSo()))
                    {
                        GetKitchenObject().DestroySelf(); 
                        
                        // Забрали ингредиент на тарелку — прячем UI
                        isPlayerCutting = false;
                        cuttingProgress = 0f;
                        OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs { progressNormalized = 0f });
                    }
                }
            }
            else
            {
                // Если у игрока пустые руки — он забирает предмет со стола
                GetKitchenObject().SetKitchenObjectParent(player);
                
                // Предмет забрали — полностью сбрасываем и прячем полоску
                isPlayerCutting = false;
                cuttingProgress = 0f;
                OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs
                {
                    progressNormalized = 0f
                });
            }
        }
    }

    // Вызывается из Player.cs каждый кадр
    public void SetCuttingState(bool isCutting)
    {
        // Начинаем резать только если зажата кнопка И на столе лежит то, что можно нарезать
        if (isCutting && HasKitchenObject() && HasRecipeWithInput(GetKitchenObject().GetKitchenObjectSo()))
        {
            if (!isPlayerCutting)
            {
                OnCut?.Invoke(this, EventArgs.Empty); // Эвент для анимации ножа
            }
            isPlayerCutting = true;
        }
        else
        {
            // Если кнопку отпустили (или условия не подходят), плавно гасим процесс
            isPlayerCutting = false;

            // Если игрок просто отошел или отпустил кнопку на полпути, 
            // мы НЕ сбрасываем cuttingProgress в 0, чтобы сохранить прогресс нарезки,
            // но если нарезка завершена, UI должен скрыться.
            if (!HasKitchenObject() || !HasRecipeWithInput(GetKitchenObject().GetKitchenObjectSo()))
            {
                cuttingProgress = 0f;
                OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs { progressNormalized = 0f });
            }
        }
    }

    private bool HasRecipeWithInput(KitchenObjectSO inputkitchenObjectSO)
    {
        CuttingRecipeSO cuttingRecipeSO = GetCuttingRecipeSOWithInput(inputkitchenObjectSO);
        return cuttingRecipeSO != null;
    }

    private KitchenObjectSO GetOutputForInput(KitchenObjectSO inputkitchenObjectSO)
    {
        CuttingRecipeSO cuttingRecipeSO = GetCuttingRecipeSOWithInput(inputkitchenObjectSO);
        return cuttingRecipeSO != null ? cuttingRecipeSO.output : null;
    }

    private CuttingRecipeSO GetCuttingRecipeSOWithInput(KitchenObjectSO inputkitchenObjectSO)
    {
        foreach (CuttingRecipeSO cuttingRecipeSO in cuttingRecipeSOarray)
        {
            if (cuttingRecipeSO.input == inputkitchenObjectSO)
            {
                return cuttingRecipeSO;
            }
        }
        return null; 
    }
}