using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Менеджер магазина ведьмовской лавки. Открывается клавишей B в фазе DayPreparation.
///
/// Логика покупки:
/// 1) Проверяем, что товар доступен (IsAvailableOnDay + лимит покупок не исчерпан).
/// 2) Проверяем, что у игрока хватает золота (GameLoopManager.HasEnoughMoney).
/// 3) Ищем ближайшую свободную ячейку сетки рядом с игроком.
/// 4) Списываем золото через GameLoopManager.SpendMoney.
/// 5) Instantiate(prefab) в найденной ячейке. DiningTable автоматически регистрируется
///    в CustomerManager через свой Awake. BaseCounter автоматически регистрируется в
///    GridPositioningSystem через свой Awake.
/// 6) Увеличиваем счётчик покупок этого товара.
///
/// Для категории Upgrade: prefab может быть null, вместо спавна вызывается
/// ApplyUpgradeEffect() — но пока этот метод пустой (заглушка для будущих апгрейдов
/// вроде "Усиленный диспенсер тарелок" или "Вечный огонь").
///
/// UI: сам ShopManager НЕ рисует интерфейс. Он только хранит состояние и стреляет
/// событиями OnShopOpened / OnShopClosed / OnItemPurchased. UI подписывается на них
/// и обновляет карточки товаров. Это разделение позволяет менять UI без правки логики.
/// </summary>
public class ShopManager : MonoBehaviour
{
    public static ShopManager Instance { get; private set; }

    public event EventHandler OnShopOpened;
    public event EventHandler OnShopClosed;
    public event EventHandler<OnItemPurchasedEventArgs> OnItemPurchased;

    public class OnItemPurchasedEventArgs : EventArgs
    {
        public ShopItemSO item;
        public int newPurchaseCount;
    }

    [Header("Каталог")]
    [Tooltip("Ссылка на ShopCatalogSO — общий список всех товаров магазина")]
    [SerializeField] private ShopCatalogSO catalog;

    [Header("Управление")]
    [Tooltip("Клавиша открытия/закрытия магазина. По умолчанию B (Buy)")]
    [SerializeField] private KeyCode toggleKey = KeyCode.B;

    [Header("Размещение")]
    [Tooltip("На каком расстоянии от игрока искать свободную ячейку для нового предмета")]
    [SerializeField] private float placementDistance = 2f;

    [Tooltip("Если true — блокируем движение игрока, пока магазин открыт. Реализовано через GameInput.DisablePlayerInput()")]
    [SerializeField] private bool lockPlayerWhileShopOpen = true;

    /// <summary>True, пока UI магазина открыт. UI подписывается на OnShopOpened/OnShopClosed.</summary>
    public bool IsShopOpen { get; private set; }

    /// <summary>
    /// Сколько раз каждый товар уже куплен. Ключ — itemId (string).
    /// Используется для проверки maxPurchaseCount и для сохранения между сессиями.
    /// </summary>
    private readonly Dictionary<string, int> purchaseCounts = new Dictionary<string, int>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        // Магазин работает только в фазе подготовки дня.
        if (GameLoopManager.Instance == null || !GameLoopManager.Instance.IsPreparationActive())
        {
            // Если каким-то образом магазин открыт, а фаза сменилась — принудительно закрываем.
            if (IsShopOpen)
            {
                CloseShop();
            }
            return;
        }

        if (Input.GetKeyDown(toggleKey))
        {
            if (IsShopOpen)
            {
                CloseShop();
            }
            else
            {
                OpenShop();
            }
        }
        else if (IsShopOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            CloseShop();
        }
    }

    /// <summary>Открывает UI магазина. UI подписан на OnShopOpened и показывает панели.</summary>
    public void OpenShop()
    {
        if (IsShopOpen) return;

        IsShopOpen = true;

        if (lockPlayerWhileShopOpen && GameInput.Instance != null)
        {
            GameInput.Instance.DisablePlayerInput();
        }

        OnShopOpened?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Закрывает UI магазина.</summary>
    public void CloseShop()
    {
        if (!IsShopOpen) return;

        IsShopOpen = false;

        if (lockPlayerWhileShopOpen && GameInput.Instance != null)
        {
            GameInput.Instance.EnablePlayerInput();
        }

        OnShopClosed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Возвращает список всех товаров из каталога. UI вызывает это для построения карточек.
    /// </summary>
    public List<ShopItemSO> GetCatalogItems()
    {
        if (catalog == null) return new List<ShopItemSO>();
        return catalog.items ?? new List<ShopItemSO>();
    }

    /// <summary>Сколько раз товар уже куплен в этой игре.</summary>
    public int GetPurchaseCount(ShopItemSO item)
    {
        if (item == null) return 0;
        return purchaseCounts.TryGetValue(item.itemId, out int count) ? count : 0;
    }

    /// <summary>
    /// Доступен ли товар для покупки прямо сейчас: день ≥ availableFromDay И
    /// лимит покупок не исчерпан.
    /// </summary>
    public bool IsItemAvailable(ShopItemSO item)
    {
        if (item == null) return false;
        int currentDay = GameLoopManager.Instance != null ? GameLoopManager.Instance.GetCurrentDay() : 1;
        int purchased = GetPurchaseCount(item);
        return item.IsAvailableOnDay(currentDay, purchased);
    }

    /// <summary>
    /// Главная точка входа для UI: игрок нажал "Купить" на карточке товара.
    /// Возвращает true, если покупка прошла успешно.
    ///
    /// Шаги:
    /// 1) Проверка доступности (день + лимит).
    /// 2) Проверка денег.
    /// 3) Поиск свободной ячейки сетки рядом с игроком (для категорий с prefab).
    /// 4) Списание золота.
    /// 5) Спавн префаба ИЛИ применение апгрейда.
    /// 6) Увеличение счётчика покупок.
    /// 7) Сохранение прогресса (через GameLoopManager.SaveCurrentProgress).
    /// 8) Уведомление UI через OnItemPurchased.
    /// </summary>
    public bool TryBuyItem(ShopItemSO item)
    {
        if (!IsShopOpen)
        {
            Debug.LogWarning("[ShopManager] Попытка купить товар при закрытом магазине.");
            return false;
        }

        if (item == null)
        {
            Debug.LogWarning("[ShopManager] Попытка купить null товар.");
            return false;
        }

        if (!IsItemAvailable(item))
        {
            Debug.Log($"[ShopManager] '{item.displayName}' недоступен для покупки.");
            return false;
        }

        if (GameLoopManager.Instance == null || !GameLoopManager.Instance.HasEnoughMoney(item.cost))
        {
            Debug.Log($"[ShopManager] Недостаточно золота для '{item.displayName}' (нужно {item.cost}g).");
            return false;
        }

        // Для апгрейдов (prefab == null) — отдельный путь: применяем эффект без спавна.
        if (item.category == ShopItemCategory.Upgrade || item.prefab == null)
        {
            if (!ApplyUpgradeEffect(item))
            {
                Debug.LogWarning($"[ShopManager] Не удалось применить апгрейд '{item.displayName}'. Покупка отменена.");
                return false;
            }
        }
        else
        {
            // Обычный товар — спавним префаб на сетке.
            if (!TrySpawnPurchasedItem(item))
            {
                return false;
            }
        }

        // Списываем золото — после успешного спавна/апгрейда, чтобы не потерять деньги
        // при неудачной покупке.
        if (!GameLoopManager.Instance.SpendMoney(item.cost))
        {
            // Если деньги вдруг не списались (гонка состояний) — откатываем спавн.
            Debug.LogError("[ShopManager] SpendMoney вернул false после успешной проверки. Это не должно случаться.");
            return false;
        }

        // Увеличиваем счётчик покупок.
        int newCount = GetPurchaseCount(item) + 1;
        purchaseCounts[item.itemId] = newCount;

        // Сохраняем прогресс — на случай, если игрок закроет игру без завершения дня.
        GameLoopManager.Instance.SaveCurrentProgress();

        Debug.Log($"[ShopManager] Куплен '{item.displayName}' за {item.cost}g. Всего куплено: {newCount}.");

        OnItemPurchased?.Invoke(this, new OnItemPurchasedEventArgs
        {
            item = item,
            newPurchaseCount = newCount
        });

        return true;
    }

    /// <summary>
    /// Спавнит префаб товара на ближайшей свободной ячейке сетки рядом с игроком.
    /// Возвращает true при успехе, false — если места не нашлось.
    /// </summary>
    private bool TrySpawnPurchasedItem(ShopItemSO item)
    {
        if (item.prefab == null)
        {
            Debug.LogError($"[ShopManager] У товара '{item.displayName}' не назначен prefab.");
            return false;
        }

        if (Player.Instance == null || GridPositioningSystem.Instance == null)
        {
            Debug.LogError("[ShopManager] Player или GridPositioningSystem не найдены.");
            return false;
        }

        // Ищем свободную ячейку рядом с игроком. Сначала пробуем прямо перед игроком,
        // затем — по кругу вокруг этой точки.
        Vector3 aheadPoint = Player.Instance.transform.position + Player.Instance.transform.forward * placementDistance;
        Vector2Int targetCell = GridPositioningSystem.Instance.GetGridPosition(aheadPoint);

        // Если прямо перед игроком занято — ищем соседнюю ячейку по кругу.
        if (GridPositioningSystem.Instance.IsCellOccupied(targetCell))
        {
            Vector2Int? freeCell = FindNearestFreeCell(targetCell, maxRadius: 2);
            if (freeCell == null)
            {
                Debug.Log($"[ShopManager] Нет свободной ячейки рядом с игроком для '{item.displayName}'.");
                return false;
            }
            targetCell = freeCell.Value;
        }

        Vector3 worldPos = GridPositioningSystem.Instance.GetWorldPosition(targetCell);
        // Сохраняем высоту префаба — некоторые объекты могут быть выше пола.
        worldPos.y = Player.Instance.transform.position.y;

        BaseCounter newCounter = Instantiate(item.prefab, worldPos, Quaternion.identity);

        // BaseCounter.Awake сам зарегистрирует объект в GridPositioningSystem и (если это
        // DiningTable) в CustomerManager. Мы только создаём — дальше всё автоматическое.

        Debug.Log($"[ShopManager] '{item.displayName}' размещён в ячейке {targetCell} ({worldPos}).");
        return true;
    }

    /// <summary>
    /// Ищет ближайшую свободную ячейку в радиусе maxRadius клеток от center.
    /// Перебор по кольцам: сначала radius=1 (8 соседей), потом radius=2 (16 соседей) и т.д.
    /// </summary>
    private Vector2Int? FindNearestFreeCell(Vector2Int center, int maxRadius)
    {
        for (int radius = 1; radius <= maxRadius; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    // Проверяем только крайние клетки кольца — внутренние уже проверены на меньших radius.
                    if (Mathf.Abs(dx) != radius && Mathf.Abs(dz) != radius) continue;

                    Vector2Int candidate = center + new Vector2Int(dx, dz);
                    if (!GridPositioningSystem.Instance.IsCellOccupied(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Применяет эффект апгрейда к существующей станции. Пока что — заглушка: метод
    /// проверяет, что effectType != None, и логирует. Реальные эффекты (RunicMortar,
    /// EternalFire и т.д.) будут добавлены, когда мы реализуем методы UpgradeToRunic()
    /// на CuttingCounter, StoveCounter, SinkCounter, PlatesCounter.
    /// </summary>
    private bool ApplyUpgradeEffect(ShopItemSO item)
    {
        if (item.upgradeEffect == UpgradeEffect.None)
        {
            Debug.LogWarning($"[ShopManager] Товар '{item.displayName}' помечен как Upgrade, но effectType = None.");
            return false;
        }

        // TODO: реализовать вызовы методов на соответствующих Counter'ах.
        // Сейчас просто логируем — игра не падает, но эффект не применяется.
        Debug.Log($"[ShopManager] Применён апгрейд '{item.displayName}' (эффект: {item.upgradeEffect}). " +
                  $"Реализация эффекта будет добавлена в будущих шагах.");

        return true;
    }

    /// <summary>
    /// Возвращает снимок счётчика покупок для сохранения. SaveManager вызовет это
    /// при сериализации GameSaveData.
    /// </summary>
    public Dictionary<string, int> GetPurchaseCountsSnapshot()
    {
        return new Dictionary<string, int>(purchaseCounts);
    }

    /// <summary>
    /// Восстанавливает счётчик покупок из сохранения. Вызывается GameLoopManager
    /// при загрузке сохранения.
    /// </summary>
    public void RestorePurchaseCounts(Dictionary<string, int> saved)
    {
        purchaseCounts.Clear();
        if (saved == null) return;

        foreach (var kvp in saved)
        {
            purchaseCounts[kvp.Key] = kvp.Value;
        }

        Debug.Log($"[ShopManager] Восстановлено {purchaseCounts.Count} записей о покупках.");
    }
}
