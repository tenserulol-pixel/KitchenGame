using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Котёл ведьмы — варит зелья и блюда через перемешивание.
///
/// Механика:
/// 1) Игрок кладёт ингредиенты через E (по одному за раз). Каждый ингредиент добавляется в список currentIngredients.
/// 2) Когда текущий список совпадает с inputs какого-то рецепта → переходим в ReadyToStir.
/// 3) Игрок зажимает F (InteractAlternate) — SetStirringState(true) вызывается из Player.Update().
///    Состояние Stirring, прогресс stirring растёт.
/// 4) При достижении stirringRequired → выходим в Done, спавним выходное блюдо поверх ингредиентов.
/// 5) Если игрок отпустил F — прогресс сохраняется, состояние ReadyToStir (можно продолжить).
/// 6) Если у рецепта есть burningTimerMax > 0, и игрок не забрал блюдо за это время — Burned.
///
/// Поддержка нескольких ингредиентов:
/// - Сравнение списков через HashSet (неупорядоченное сравнение, чтобы [Грибы, Коренья] == [Коренья, Грибы]).
/// - Сначала проверяем рецепты с БОЛЬШИМ количеством inputs (приоритет сложным рецептам над простыми).
/// </summary>
public class StoveCounter : BaseCounter, IHasProgress
{
    public event EventHandler<IHasProgress.OnProgressChangedEventArgs> OnProgressChanged;
    public event EventHandler<OnStateChangedEventArgs> OnStateChanged;

    public class OnStateChangedEventArgs : EventArgs
    {
        public State state;
    }

    public enum State
    {
        Empty,           // Котёл пуст, можно класть ингредиенты
        Loading,        // Часть ингредиентов положена, ждём остальных
        ReadyToStir,    // Все ингредиенты на месте, можно перемешивать
        Stirring,       // Игрок зажал F, идёт перемешивание
        Brewing,        // Автоматическая варка после перемешивания — прогресс растёт сам
        Done,           // Блюдо готово, ждёт когда заберут
        Burned,         // Блюдо сгорело
    }

    [SerializeField] private FryingRecipeSO[] brewingRecipeSOarray;
    [SerializeField] private BurningRecipeSO[] burningRecipeSOarray;

    // Состояние
    private State state = State.Empty;
    private List<KitchenObjectSO> currentIngredients = new List<KitchenObjectSO>();
    private FryingRecipeSO activeRecipe;
    private BurningRecipeSO burningRecipe;

    // Прогресс
    private float stirringProgress = 0f;   // для Stirring (накапливается при зажатии F)
    private float brewingTimer = 0f;       // для Brewing (тикает сам после завершения перемешивания)
    private float burningTimer = 0f;      // для Done (отсчёт до сжигания)

    private bool isPlayerStirring = false;

    private void Start()
    {
        state = State.Empty;
        NotifyStateChanged();
    }

    private void Update()
    {
        switch (state)
        {
            case State.Empty:
            case State.Loading:
                // Ничего не делаем — ждём, пока игрок положит ингредиенты
                break;

            case State.ReadyToStir:
                // Если игрок зажал F (флаг установлен через SetStirringState) — переходим в Stirring
                if (isPlayerStirring)
                {
                    state = State.Stirring;
                    NotifyStateChanged();
                }
                break;

            case State.Stirring:
                // Прогресс перемешивания растёт только если игрок держит F
                if (isPlayerStirring && activeRecipe != null)
                {
                    stirringProgress += Time.deltaTime;
                    NotifyProgressChanged(stirringProgress / activeRecipe.stirringRequired);

                    if (stirringProgress >= activeRecipe.stirringRequired)
                    {
                        // Перемешивание завершено — переходим к варке (если есть brewingTimerMax)
                        // или сразу к готовому блюду (если brewingTimerMax == 0).
                        StartBrewingOrComplete();
                    }
                }
                // Если отпустил — остаёмся в Stirring, прогресс сохраняется
                break;

            case State.Brewing:
                // Автоматическая варка — прогресс растёт без участия игрока.
                // Игрок может отойти и заниматься другими делами.
                if (activeRecipe != null && activeRecipe.brewingTimerMax > 0f)
                {
                    brewingTimer += Time.deltaTime;
                    NotifyProgressChanged(brewingTimer / activeRecipe.brewingTimerMax);

                    if (brewingTimer >= activeRecipe.brewingTimerMax)
                    {
                        // Варка завершена — спавним выходное блюдо
                        CompleteRecipe();
                    }
                }
                else
                {
                    // Если brewingTimerMax == 0 (ошибка конфигурации) — сразу завершаем
                    CompleteRecipe();
                }
                break;

            case State.Done:
                // Если у рецепта есть burningTimerMax — тикает таймер сжигания
                if (burningRecipe != null && burningRecipe.burningTimerMax > 0f)
                {
                    burningTimer += Time.deltaTime;
                    NotifyProgressChanged(burningTimer / burningRecipe.burningTimerMax);

                    if (burningTimer >= burningRecipe.burningTimerMax)
                    {
                        // Сгорело
                        BurnDish();
                    }
                }
                break;

            case State.Burned:
                // Ничего не делаем — ждём, пока заберут
                break;
        }
    }

    public override void Interact(Player player)
    {
        // === ИГРОК КЛАДЁТ ИЛИ ЗАБИРАЕТ ===
        //
        // Логика Interact (клавиша E):
        // - Если котёл в Empty/Loading и у игрока есть ингредиент → добавить в currentIngredients
        // - Если котёл в Done и у игрока пустые руки → забрать выходное блюдо
        // - Если котёл в Done и у игрока тарелка → добавить блюдо в тарелку
        // - Если котёл в Burned и у игрока пустые руки → забрать сожжённое
        // - Если котёл в Stirring — Interact игнорируется (нельзя мешать процесс)

        if (state == State.Stirring)
        {
            // Нельзя мешать перемешиванию
            return;
        }

        if (state == State.Empty || state == State.Loading)
        {
            // Котёл ждёт ингредиенты
            if (player.HasKitchenObject())
            {
                KitchenObjectSO playerObject = player.GetKitchenObject().GetKitchenObjectSo();

                // Тарелку нельзя класть в котёл
                if (player.GetKitchenObject().TryGetPlate(out _))
                {
                    return;
                }

                // Проверяем, совместим ли этот ингредиент с каким-то рецептом при текущей загрузке
                if (!CanIngredientBePartOfRecipe(playerObject))
                {
                    Debug.Log($"[StoveCounter] '{playerObject.objectName}' не подходит ни для одного рецепта в текущей загрузке.");
                    return;
                }

                // Добавляем ингредиент в список
                currentIngredients.Add(playerObject);
                player.GetKitchenObject().DestroySelf();

                // Проверяем, собран ли полный рецепт
                FryingRecipeSO matchedRecipe = FindMatchingRecipe();
                if (matchedRecipe != null)
                {
                    activeRecipe = matchedRecipe;
                    state = State.ReadyToStir;
                    stirringProgress = 0f;

                    // Подготавливаем burning recipe для выходного блюда
                    burningRecipe = GetBurningRecipeSOWithInput(activeRecipe.output);
                    NotifyStateChanged();
                    NotifyProgressChanged(0f);
                    Debug.Log($"[StoveCounter] Рецепт '{activeRecipe.output.objectName}' готов к перемешиванию.");
                }
                else
                {
                    // Ждём ещё ингредиентов
                    state = State.Loading;
                    NotifyStateChanged();
                    Debug.Log($"[StoveCounter] Добавлен '{playerObject.objectName}'. Всего в котле: {currentIngredients.Count}.");
                }
            }
            return;
        }

        if (state == State.Done)
        {
            // Блюдо готово — забираем
            if (!player.HasKitchenObject())
            {
                // Игрок с пустыми руками — забирает блюдо целиком
                // Блюдо хранится как child на counterTopPoint — нужно его перенести игроку
                if (HasKitchenObject())
                {
                    GetKitchenObject().SetKitchenObjectParent(player);
                }

                // Сброс котла
                ResetCauldron();
            }
            else
            {
                // Игрок держит тарелку — добавляем блюдо в тарелку
                if (player.GetKitchenObject().TryGetPlate(out PlateKitchenObject plate))
                {
                    if (HasKitchenObject() && plate.AddIngredient(GetKitchenObject().GetKitchenObjectSo()))
                    {
                        GetKitchenObject().DestroySelf();
                        ResetCauldron();
                    }
                }
            }
            return;
        }

        if (state == State.Burned)
        {
            // Сожжённое блюдо — игрок может забрать и выбросить в Бездну
            if (!player.HasKitchenObject())
            {
                if (HasKitchenObject())
                {
                    GetKitchenObject().SetKitchenObjectParent(player);
                }
                ResetCauldron();
            }
            return;
        }
    }

    /// <summary>
    /// Вызывается из Player.Update() каждый кадр с текущим состоянием кнопки F.
    /// Если true и котёл в ReadyToStir → переходим в Stirring.
    /// Если false и котёл в Stirring → прогресс сохраняется, но не растёт.
    /// </summary>
    public void SetStirringState(bool isHeld)
    {
        isPlayerStirring = isHeld;
    }

    // === Внутренние методы ===

    /// <summary>
    /// Проверяет, может ли указанный ингредиент быть частью какого-то рецепта, учитывая
    /// уже загруженные ингредиенты в currentIngredients. Важно, чтобы игрок не клал
    /// ингредиенты, которые точно не приведут к готовому блюду.
    /// </summary>
    private bool CanIngredientBePartOfRecipe(KitchenObjectSO candidate)
    {
        // Защита от null: если массив рецептов не назначен в инспекторе — сообщаем и блокируем
        if (brewingRecipeSOarray == null || brewingRecipeSOarray.Length == 0)
        {
            Debug.LogWarning($"[StoveCounter] '{name}': brewingRecipeSOarray не назначен или пуст. Котёл не будет работать.");
            return false;
        }

        // Создаём гипотетический список: текущие + новый
        List<KitchenObjectSO> hypothetical = new List<KitchenObjectSO>(currentIngredients);
        hypothetical.Add(candidate);

        foreach (FryingRecipeSO recipe in brewingRecipeSOarray)
        {
            if (recipe == null) continue;
            if (recipe.inputs == null) continue;

            if (IsSubset(hypothetical, recipe.inputs))
            {
                // Гипотетический список содержит подмножество inputs этого рецепта
                // (то есть каждый ингредиент из hypothetical есть в inputs рецепта)
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Ищет рецепт, чьи inputs точно совпадают с currentIngredients (как множества).
    /// Возвращает null, если полного совпадения нет.
    /// </summary>
    private FryingRecipeSO FindMatchingRecipe()
    {
        if (brewingRecipeSOarray == null || brewingRecipeSOarray.Length == 0)
        {
            return null;
        }

        // Приоритет: рецепты с большим количеством inputs (сложные рецепты важнее простых)
        List<FryingRecipeSO> sortedRecipes = new List<FryingRecipeSO>(brewingRecipeSOarray);
        sortedRecipes.Sort((a, b) =>
        {
            int aCount = (a != null && a.inputs != null) ? a.inputs.Count : 0;
            int bCount = (b != null && b.inputs != null) ? b.inputs.Count : 0;
            return bCount.CompareTo(aCount);
        });

        foreach (FryingRecipeSO recipe in sortedRecipes)
        {
            if (recipe == null) continue;
            if (recipe.inputs == null) continue;
            if (recipe.inputs.Count != currentIngredients.Count) continue;

            bool allMatch = true;
            List<KitchenObjectSO> recipeInputsCopy = new List<KitchenObjectSO>(recipe.inputs);

            foreach (KitchenObjectSO ingredient in currentIngredients)
            {
                int index = recipeInputsCopy.FindIndex(x => x == ingredient);
                if (index < 0)
                {
                    allMatch = false;
                    break;
                }
                recipeInputsCopy.RemoveAt(index);
            }

            if (allMatch) return recipe;
        }
        return null;
    }

    /// <summary>
    /// Проверяет, является ли hypothetical подмножеством targetSet (каждый элемент
    /// hypothetical присутствует в targetSet, с учётом кратности).
    /// </summary>
    private bool IsSubset(List<KitchenObjectSO> hypothetical, List<KitchenObjectSO> targetSet)
    {
        if (hypothetical.Count > targetSet.Count) return false;

        List<KitchenObjectSO> targetCopy = new List<KitchenObjectSO>(targetSet);
        foreach (KitchenObjectSO item in hypothetical)
        {
            int index = targetCopy.FindIndex(x => x == item);
            if (index < 0) return false;
            targetCopy.RemoveAt(index);
        }
        return true;
    }

    /// <summary>
    /// Вызывается после завершения перемешивания. Если у рецепта есть brewingTimerMax > 0 —
    /// переходим в состояние Brewing (автоматическая варка). Если brewingTimerMax == 0 —
    /// сразу завершаем рецепт (старое поведение, без этапа варки).
    /// </summary>
    private void StartBrewingOrComplete()
    {
        if (activeRecipe != null && activeRecipe.brewingTimerMax > 0f)
        {
            // Переходим к варке — прогресс бар покажет 0, будет расти до 1.
            brewingTimer = 0f;
            state = State.Brewing;
            NotifyStateChanged();
            NotifyProgressChanged(0f);
            Debug.Log($"[StoveCounter] Перемешивание завершено, началась варка ({activeRecipe.brewingTimerMax} сек).");
        }
        else
        {
            // Без этапа варки — сразу готово
            CompleteRecipe();
        }
    }

    /// <summary>
    /// Завершает рецепт — спавнит выходное блюдо и сбрасывает все прогресс-таймеры.
    /// </summary>
    private void CompleteRecipe()
    {
        if (activeRecipe == null || activeRecipe.output == null) return;

        // Спавним выходное блюдо как KitchenObject на counterTopPoint
        KitchenObject.SpawnKitchenObject(activeRecipe.output, this);

        // Очищаем список ингредиентов (они "поглощены" выходным блюдом)
        currentIngredients.Clear();
        stirringProgress = 0f;
        brewingTimer = 0f;
        burningTimer = 0f;
        state = State.Done;
        NotifyStateChanged();

        // Прогресс-бар: 0 (если есть burningTimerMax — переключится на сжигание в Update)
        NotifyProgressChanged(burningRecipe != null && burningRecipe.burningTimerMax > 0f ? 0f : 1f);

        Debug.Log($"[StoveCounter] Приготовлено '{activeRecipe.output.objectName}'.");
    }

    /// <summary>
    /// Сжигает блюдо — заменяет выходное блюдо на сожжённое, если есть burning recipe.
    /// </summary>
    private void BurnDish()
    {
        if (burningRecipe != null && burningRecipe.output != null && HasKitchenObject())
        {
            GetKitchenObject().DestroySelf();
            KitchenObject.SpawnKitchenObject(burningRecipe.output, this);
        }
        state = State.Burned;
        burningTimer = 0f;
        NotifyStateChanged();
        NotifyProgressChanged(0f);
        Debug.Log("[StoveCounter] Блюдо сгорело!");
    }

    /// <summary>
    /// Полный сброс котла к начальному состоянию Empty.
    /// </summary>
    private void ResetCauldron()
    {
        currentIngredients.Clear();
        activeRecipe = null;
        burningRecipe = null;
        stirringProgress = 0f;
        brewingTimer = 0f;
        burningTimer = 0f;
        state = State.Empty;
        NotifyStateChanged();
        NotifyProgressChanged(0f);
    }

    private BurningRecipeSO GetBurningRecipeSOWithInput(KitchenObjectSO input)
    {
        if (burningRecipeSOarray == null || burningRecipeSOarray.Length == 0)
        {
            return null;
        }

        foreach (BurningRecipeSO burning in burningRecipeSOarray)
        {
            if (burning == null) continue;
            if (burning.input == input) return burning;
        }
        return null;
    }

    private void NotifyStateChanged()
    {
        OnStateChanged?.Invoke(this, new OnStateChangedEventArgs { state = state });
    }

    private void NotifyProgressChanged(float normalized)
    {
        OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs
        {
            progressNormalized = Mathf.Clamp01(normalized)
        });
    }

    public State GetState() => state;
}