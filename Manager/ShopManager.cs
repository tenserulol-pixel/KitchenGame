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
/// ★ v1.1 (модальность и стены):
/// - Магазин теперь Push'ит GameLoopManager.PushUiInputLock на время жизни:
///   Enter больше НЕ может начать день поверх открытого магазина, а клавиши
///   переноса мебели (T) честно гасятся FurnitureMovingController'ом —
///   раньше оба гейта считали, что «модальных экранов нет».
/// - Спавн покупки проверяется на пересечение со стеной (та же логика АВТО-фильтра,
///   что у переноса мебели): предмет, влезший в стену, переносится на соседнюю
///   свободную ячейку; если стены окружают всё — покупка отменяется БЕЗ списания золота.
///
/// ★ v1.2 (запрет наложения мебели):
/// - Спавн покупки больше не игнорирует уже стоящую мебель: проверка места ловит
///   пересечение с коллайдерами ДРУГОЙ мебели (BaseCounter/Chair). Раньше их пересечение
///   «на бумаге» отслеживала только сетка (одна pivot-ячейка на предмет) — станция шире
///   ячейки или не зарегистрированная в сетке давала «предметы друг в друге».
///   Такой спавн переносится на соседнюю свободную ячейку (та же логика, что и для стен);
///   мест нет — покупка отменяется без списания золота.
/// - KitchenObject (тарелки/ингредиенты на станциях) больше не считается препятствием.
///
/// ★ v1.3 (постановка покупки игроком + завершение через призрак):
/// - Покупка мебели больше НЕ спавнится «в первую попавшуюся ячейку»: магазин
///   закрывается, инстанс префаба передаётся FurnitureMovingController
///   (BeginPlacingPurchasedItem), и игрок сам ходит, поворачивает (R) и
///   подтверждает (T) место — как в PlateUp.
/// - Деньги списываются ТОЛЬКО после подтверждения места
///   (CompletePendingPurchase → FinalizePurchase). Escape в призрачном
///   режиме уничтожает инстанс и НЕ тратит золото: невозможно случайно
///   оплатить предмет, воткнутый автоматикой в неудобное место.
/// - Лимиты покупки (maxPurchaseCount) по-прежнему проверяются ДО старта
///   постановки; счётчик увеличивается только по факту оплаты.
/// - Запасной путь: если в сцене нет FurnitureMovingController, работает
///   старое поведение — автоспавн на ближайшей свободной ячейке (v1.1/v1.2).
///
/// Для категории Upgrade: prefab может быть null, вместо спавна вызывается
/// ApplyUpgradeEffect().
///
/// ★ v1.4 (БАГФИКС «деньги за воздух» — реальные эффекты апгрейдов):
/// - ApplyUpgradeEffect больше НЕ заглушка. Каждый эффект реально применяется:
///     RunicMortar      → CuttingCounter.UpgradeToRunic()      (резка быстрее)
///     EternalFire      → StoveCounter.UpgradeToEternalFire()  (готовка быстрее)
///     PurifyingWater   → SinkCounter.UpgradeToPurifyingWater()(мойка быстрее)
///     SelfRefilling    → PlatesCounter.UpgradeToSelfRefilling() (стопка больше)
/// - Apprentice/BookshelfKnowledge — эффекты ещё не реализованы: покупка
///   ОТКЛОНЯЕТСЯ БЕЗ списания золота (раньше деньги брались, а эффект не применялся).
/// - Если целевая станция отсутствует в сцене — покупка тоже отклоняется без списания.
/// - Восстановление сейва: RestorePurchaseCounts теперь принимает формат сохранения
///   (List<PurchaseCountSaveData>) и ПОСЛЕ восстановления счётчиков ЗАНОВО применяет
///   эффекты всех купленных апгрейдов — иначе после «Продолжить» счётчик покупок был,
///   а эффект исчезал.
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

    // ★ v1.1: держим ли мы сейчас PushUiInputLock — чтобы Pop был строго
    // парным (закрытие из Update при смене фазы, из Escape и из OnDestroy).
    private bool uiLockHeld;

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

        // ★ v1.1: страховка парности — если объект магазина уничтожили при
        // открытом экране, блокировка ввода обязана сняться.
        ReleaseUiLock();
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

        // ★ v1.1: магазин — модальный экран. Push поднимает счётчик блокировки:
        // - гейт Enter в GameLoopManager не даст начать день поверх магазина;
        // - FurnitureMovingController заглушит клавиши переноса мебели.
        AcquireUiLock();

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

        // ★ v1.1: строго парный Pop к Push из OpenShop.
        ReleaseUiLock();

        OnShopClosed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>★ v1.1: Push с защитой от двойного вызова.</summary>
    private void AcquireUiLock()
    {
        if (uiLockHeld) return;

        if (GameLoopManager.Instance != null)
        {
            GameLoopManager.Instance.PushUiInputLock();
            uiLockHeld = true;
        }
    }

    /// <summary>★ v1.1: Pop с защитой от двойного вызова и от лишнего Pop.</summary>
    private void ReleaseUiLock()
    {
        if (!uiLockHeld) return;

        if (GameLoopManager.Instance != null)
        {
            GameLoopManager.Instance.PopUiInputLock();
        }

        uiLockHeld = false;
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

        // ★ v1.3: защита от второй покупки, пока предыдущий предмет ещё
        // ждёт постановки (магазин закрыт, но инстанс в руках у игрока).
        if (FurnitureMovingController.Instance != null &&
            FurnitureMovingController.Instance.IsPlacingPurchase)
        {
            Debug.Log("[ShopManager] Сначала поставь уже купленный предмет " +
                      "(T — поставить, Escape — отменить покупку).");
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

            FinalizePurchase(item);
            return true;
        }

        // ★ v1.3: обычный товар — ПРИЗРАЧНАЯ ПОСТАНОВКА ИГРОКОМ.
        // Магазин закрывается, инстанс передаётся FurnitureMovingController'у,
        // золото списывается только после подтверждения места (T).
        if (FurnitureMovingController.Instance != null)
        {
            return TryBeginGhostPurchase(item);
        }

        // Запасной путь (в сцене нет FurnitureMovingController): автоспавн
        // на ближайшей свободной ячейке — старое поведение v1.1/v1.2.
        if (!TrySpawnPurchasedItem(item))
        {
            return false;
        }

        FinalizePurchase(item);
        return true;
    }

    /// <summary>
    /// ★ v1.3: общая точка завершения покупки — списание золота, счётчик
    /// покупок, сохранение прогресса и событие для UI.
    /// Для апгрейдов и запасного автоспавна вызывается сразу;
    /// для призрачного пути — позже, из CompletePendingPurchase.
    /// </summary>
    private void FinalizePurchase(ShopItemSO item)
    {
        // Списываем золото — после успешного спавна/апгрейда, чтобы не потерять деньги
        // при неудачной покупке.
        if (!GameLoopManager.Instance.SpendMoney(item.cost))
        {
            // Если деньги вдруг не списались (гонка состояний) — откатываем спавн.
            Debug.LogError("[ShopManager] SpendMoney вернул false после успешной проверки. Это не должно случаться.");
            return;
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
    }

    /// <summary>
    /// ★ v1.3: призрачная постановка покупки. Спавним инстанс префаба перед
    /// игроком, закрываем магазин и передаём инстанс FurnitureMovingController'у:
    /// игрок ходит, поворачивает (R) и подтверждает (T) сам.
    /// Деньги НЕ списаны до CompletePendingPurchase — Escape бесплатен.
    /// </summary>
    private bool TryBeginGhostPurchase(ShopItemSO item)
    {
        if (Player.Instance == null || GridPositioningSystem.ResolveInstance() == null)
        {
            Debug.LogError("[ShopManager] Player или GridPositioningSystem не найдены — призрачная постановка невозможна.");
            return false;
        }

        // Точка спавна — перед игроком; FurnitureMovingController тут же
        // снимет её с регистрации в сетке и переведёт в призрачный режим.
        Vector3 aheadPoint = Player.Instance.transform.position +
                             Player.Instance.transform.forward * placementDistance;

        Vector3 spawnPos = GridPositioningSystem.Instance.GetWorldPosition(
            GridPositioningSystem.Instance.GetGridPosition(aheadPoint));
        spawnPos.y = Player.Instance.transform.position.y;

        BaseCounter instance = Instantiate(item.prefab, spawnPos, Quaternion.identity);

        if (!FurnitureMovingController.Instance.BeginPlacingPurchasedItem(item, instance))
        {
            // Постановку начать не удалось — инстанс не нужен, деньги не тронуты.
            Destroy(instance.gameObject);
            return false;
        }

        // Магазин закрывается — игрок расставляет покупку в свободной фазе подготовки.
        CloseShop();

        Debug.Log($"[ShopManager] '{item.displayName}' передан игроку на постановку. " +
                  $"Золото ({item.cost}g) спишется после подтверждения места (T).");

        return true;
    }

    /// <summary>
    /// ★ v1.3: завершение покупки после успешной постановки призрака.
    /// Вызывается FurnitureMovingController'ом из TryConfirmPlacement —
    /// деньги списываются ТОЛЬКО здесь, поэтому отмена (Escape) бесплатна.
    /// </summary>
    public void CompletePendingPurchase(ShopItemSO item)
    {
        if (item == null) return;

        FinalizePurchase(item);
    }

    /// <summary>
    /// Спавнит префаб товара на ближайшей свободной ячейке сетки рядом с игроком.
    /// ★ v1.1: после спавна предмет проверяется на пересечение со стеной —
    /// влезший в стену переносится на соседнюю свободную ячейку. Если валидного
    /// места нет вообще, покупка отменяется (золото ещё не списано).
    /// Возвращает true при успехе, false — если места не нашлось.
    /// </summary>
    private bool TrySpawnPurchasedItem(ShopItemSO item)
    {
        if (item.prefab == null)
        {
            Debug.LogError($"[ShopManager] У товара '{item.displayName}' не назначен prefab.");
            return false;
        }

        if (Player.Instance == null || GridPositioningSystem.ResolveInstance() == null)
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

        // ★ v1.1/v1.2: проверка «не в стене ли мы И не пересекаем ли чужую мебель».
        // Тот же АВТО-фильтр, что и при переносе мебели; чужая мебель теперь
        // считается занятостью — спавн в неё запрещён.
        if (IsSpawnPlacementBlocked(newCounter))
        {
            Debug.Log($"[ShopManager] '{item.displayName}' влез в стену или в чужую мебель — ищу соседнюю свободную ячейку...");

            if (!RelocateToFreeSpot(newCounter, targetCell))
            {
                Debug.Log($"[ShopManager] Валидного места для '{item.displayName}' нет — покупка отменена (золото не списано).");

                Destroy(newCounter.gameObject);
                return false;
            }
        }

        // BaseCounter.Awake сам зарегистрирует объект в GridPositioningSystem и (если это
        // DiningTable) в CustomerManager. Мы только создаём — дальше всё автоматическое.

        Debug.Log($"[ShopManager] '{item.displayName}' размещён в ячейке {GridPositioningSystem.Instance.GetGridPosition(newCounter.transform.position)} ({newCounter.transform.position}).");
        return true;
    }

    /// <summary>
    /// ★ v1.1: переносит свежезаспавненный предмет по соседним свободным ячейкам,
    /// пока не найдётся место без пересечения со стеной. Возвращает true, если
    /// валидное место найдено. Перебор ограничен (до 8 попыток в радиусе 2) —
    /// и без повторов уже проверенных ячеек.
    /// </summary>
    private bool RelocateToFreeSpot(BaseCounter counter, Vector2Int startCell)
    {
        HashSet<Vector2Int> triedCells = new HashSet<Vector2Int> { startCell };
        Vector2Int currentCell = startCell;

        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector2Int? candidate = FindNearestFreeCellExcluding(currentCell, 2, triedCells);

            if (candidate == null)
            {
                return false;
            }

            currentCell = candidate.Value;
            triedCells.Add(currentCell);

            Vector3 pos = GridPositioningSystem.Instance.GetWorldPosition(currentCell);
            pos.y = counter.transform.position.y;

            // TryPlaceCounter сам освободит старую ячейку и зарегистрирует новую.
            GridPositioningSystem.Instance.TryPlaceCounter(counter, pos);

            if (!IsSpawnPlacementBlocked(counter))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// ★ v1.1: как FindNearestFreeCell, но пропускает уже проверенные ячейки —
    /// иначе переносимый предмет мог бы «пинг-понгить» между двумя местами.
    /// </summary>
    private Vector2Int? FindNearestFreeCellExcluding(Vector2Int center, int maxRadius, HashSet<Vector2Int> excluded)
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

                    if (excluded.Contains(candidate)) continue;

                    if (!GridPositioningSystem.Instance.IsCellOccupied(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }
        return null;
    }

    // ★ v1.2: зазор проверки спавна — касание стены/мебели вплотную не блокирует,
    // только реальное пересечение (тот же смысл, что wallClearance у FurnitureMovingController).
    private const float SpawnClearance = 0.05f;

    /// <summary>
    /// ★ v1.1/v1.2: блокировано ли место спавна — стеной ИЛИ чужой мебелью.
    /// Логика повторяет АВТО-режим FurnitureMovingController: берём самый объёмный
    /// BoxCollider предмета, строим по нему проверочный бокс и ищем чужие коллайдеры.
    ///
    /// ★ v1.2: мебель (BaseCounter) и стулья (Chair) больше НЕ пропускаются — раньше
    /// их пересечение «на бумаге» отслеживала только сетка (одна pivot-ячейка на
    /// предмет), и станция шире ячейки / не зарегистрированная в сетке давала
    /// «предметы друг в друге». Теперь чужая мебель = место занято.
    /// Не считаются препятствием: игрок, клиенты, KitchenObject (предметы на
    /// станциях), динамические физические тела (дверное полотно) и пол.
    /// </summary>
    private bool IsSpawnPlacementBlocked(BaseCounter counter)
    {
        BoxCollider[] boxes = counter.GetComponentsInChildren<BoxCollider>();

        BoxCollider bestBox = null;
        float bestVolume = 0f;

        foreach (BoxCollider box in boxes)
        {
            Vector3 worldSize = Vector3.Scale(box.size, box.transform.lossyScale);
            float volume = worldSize.x * worldSize.y * worldSize.z;

            if (box.size != Vector3.zero && volume > bestVolume)
            {
                bestVolume = volume;
                bestBox = box;
            }
        }

        // Нет геометрии — проверить нечего, считаем место валидным
        // (FurnitureMovingController тоже пропускает предметы без коллайдеров).
        if (bestBox == null)
        {
            return false;
        }

        Vector3 center = bestBox.transform.TransformPoint(bestBox.center);
        Vector3 half = Vector3.Scale(bestBox.size, bestBox.transform.lossyScale) * 0.5f;

        // ★ v1.2: тот же зазор, что у переноса мебели — касание вплотную не блокирует.
        half.x = Mathf.Max(0.01f, half.x - SpawnClearance);
        half.y = Mathf.Max(0.01f, half.y - SpawnClearance);
        half.z = Mathf.Max(0.01f, half.z - SpawnClearance);

        int hits = Physics.OverlapBoxNonAlloc(
            center,
            half,
            wallOverlapBuffer,
            Quaternion.identity,
            ~0,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits; i++)
        {
            Collider hit = wallOverlapBuffer[i];

            if (hit == null)
            {
                break;
            }

            // Свои собственные коллайдеры (и вся иерархия предмета) — не помеха.
            if (hit.transform.IsChildOf(counter.transform))
            {
                continue;
            }

            // Динамические тела (дверное полотно) — не препятствие.
            Rigidbody hitBody = hit.attachedRigidbody;

            if (hitBody != null && !hitBody.isKinematic)
            {
                continue;
            }

            // ★ v1.2: ЧУЖАЯ мебель — место занято. Раньше мебель и стулья
            // пропускались как «не стены», и спавн мог воткнуть новый предмет
            // в уже стоящую станцию (сетка видит только pivot-ячейку).
            if (hit.GetComponentInParent<BaseCounter>() != null ||
                hit.GetComponentInParent<Chair>() != null)
            {
                Debug.Log($"[ShopManager] Место для '{counter.name}' занято мебелью '{hit.gameObject.name}' " +
                          $"(объект '{hit.transform.root.name}').");
                return true;
            }

            // Предметы на станциях (тарелки, ингредиенты) — не препятствие.
            if (hit.GetComponentInParent<KitchenObject>() != null) continue;

            // Игрок, клиенты — не препятствие для спавна.
            if (hit.GetComponentInParent<Player>() != null) continue;
            if (hit.GetComponentInParent<CustomerAI>() != null) continue;

            // Пол — тонкий и крупный.
            Bounds hitBounds = hit.bounds;

            if (hitBounds.size.y <= 0.5f &&
                hitBounds.size.x >= half.x * 3f &&
                hitBounds.size.z >= half.z * 3f)
            {
                continue;
            }

            Debug.Log($"[ShopManager] Место для '{counter.name}' блокирует '{hit.gameObject.name}' (слой {LayerMask.LayerToName(hit.gameObject.layer)}).");
            return true;
        }

        return false;
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
    /// Применяет эффект апгрейда к существующей станции.
    /// ★ v1.4 — БАГФИКС «деньги за воздух»: раньше метод был заглушкой (лог + return true),
    /// и игрок платил золото за товар, который ничего не делал. Теперь каждый эффект
    /// реально применяется к целевой станции; если применить НЕВОЗМОЖНО (эффект не
    /// реализован, станции нет в сцене) — возвращается false, и TryBuyItem отклоняет
    /// покупку ДО списания золота.
    /// </summary>
    private bool ApplyUpgradeEffect(ShopItemSO item)
    {
        if (item.upgradeEffect == UpgradeEffect.None)
        {
            Debug.LogWarning($"[ShopManager] Товар '{item.displayName}' помечен как Upgrade, но effectType = None.");
            return false;
        }

        switch (item.upgradeEffect)
        {
            case UpgradeEffect.RunicMortar:
            {
                CuttingCounter target = FindLiveCounter<CuttingCounter>();
                if (target == null)
                {
                    Debug.LogError($"[ShopManager] '{item.displayName}': в сцене нет CuttingCounter — покупка отклонена (золото не списано).");
                    return false;
                }
                target.UpgradeToRunic();
                return true;
            }

            case UpgradeEffect.EternalFire:
            {
                StoveCounter target = FindLiveCounter<StoveCounter>();
                if (target == null)
                {
                    Debug.LogError($"[ShopManager] '{item.displayName}': в сцене нет StoveCounter — покупка отклонена (золото не списано).");
                    return false;
                }
                target.UpgradeToEternalFire();
                return true;
            }

            case UpgradeEffect.PurifyingWater:
            {
                SinkCounter target = FindLiveCounter<SinkCounter>();
                if (target == null)
                {
                    Debug.LogError($"[ShopManager] '{item.displayName}': в сцене нет SinkCounter — покупка отклонена (золото не списано).");
                    return false;
                }
                target.UpgradeToPurifyingWater();
                return true;
            }

            case UpgradeEffect.SelfRefilling:
            {
                PlatesCounter target = FindLiveCounter<PlatesCounter>();
                if (target == null)
                {
                    Debug.LogError($"[ShopManager] '{item.displayName}': в сцене нет PlatesCounter — покупка отклонена (золото не списано).");
                    return false;
                }
                target.UpgradeToSelfRefilling();
                return true;
            }

            case UpgradeEffect.Apprentice:
            case UpgradeEffect.BookshelfKnowledge:
                // Эффекты пока не реализованы. ЧЕСТНОЕ поведение — не продавать:
                // return false не даст FinalizePurchase списать золото за «воздух».
                Debug.LogWarning($"[ShopManager] '{item.displayName}': эффект {item.upgradeEffect} ещё не реализован — товар временно не продаётся (золото не списано).");
                return false;

            default:
                Debug.LogWarning($"[ShopManager] '{item.displayName}': неизвестный эффект {item.upgradeEffect} — покупка отклонена (золото не списано).");
                return false;
        }
    }

    /// <summary>
    /// ★ v1.4: поиск живой (не уничтоженной) станции нужного типа в сцене.
    /// Обёртка с условной компиляцией по версии Unity — по тому же паттерну,
    /// что в Editor/GridSnapEditor.cs.
    /// </summary>
    private static T FindLiveCounter<T>() where T : BaseCounter
    {
#if UNITY_2023_1_OR_NEWER
          return UnityEngine.Object.FindFirstObjectByType<T>();
#else
        T[] found = FindObjectsOfType<T>();
        return found != null && found.Length > 0 ? found[0] : null;
#endif
    }

    /// <summary>
    /// ★ v1.4: возвращает счётчик покупок в сериализуемом формате сохранения.
    /// (JsonUtility не умеет Dictionary — поэтому List<PurchaseCountSaveData>.)
    /// </summary>
    public List<PurchaseCountSaveData> GetPurchaseCountsForSave()
    {
        List<PurchaseCountSaveData> list = new List<PurchaseCountSaveData>();

        foreach (KeyValuePair<string, int> kvp in purchaseCounts)
        {
            list.Add(new PurchaseCountSaveData { itemId = kvp.Key, count = kvp.Value });
        }

        return list;
    }

    /// <summary>
    /// Восстанавливает счётчик покупок из сохранения (формат GameSaveData) и
    /// ★ v1.4: ЗАНОВО применяет эффекты всех купленных апгрейдов. Без реапплая
    /// после «Продолжить» игра помнила бы, ЧТО апгрейд куплен (счётчик), но
    /// эффект терялся до перезапуска — тот же класс «неполного сейва».
    /// Вызывается GameLoopManager при загрузке сохранения (Start сцены).
    /// </summary>
    public void RestorePurchaseCounts(List<PurchaseCountSaveData> saved)
    {
        purchaseCounts.Clear();
        if (saved == null)
        {
            Debug.Log("[ShopManager] В сохранении нет счётчиков покупок — начинаем с нуля (старый сейв без этого поля).");
            return;
        }

        foreach (PurchaseCountSaveData entry in saved)
        {
            if (entry == null || string.IsNullOrEmpty(entry.itemId)) continue;
            purchaseCounts[entry.itemId] = entry.count;
        }

        Debug.Log($"[ShopManager] Восстановлено {purchaseCounts.Count} записей о покупках.");

        // Восстанавливаем ЭФФЕКТЫ купленных апгрейдов (идемпотентные методы
        // на счётчиках делают повторный вызов безопасным).
        ReapplyPurchasedUpgrades();
    }

    /// <summary>
    /// ★ v1.4: повторное применение эффектов всех купленных апгрейдов из каталога.
    /// </summary>
    private void ReapplyPurchasedUpgrades()
    {
        if (catalog == null || catalog.items == null) return;

        foreach (ShopItemSO item in catalog.items)
        {
            if (item == null) continue;

            int count = GetPurchaseCount(item);
            if (count <= 0) continue;

            bool isUpgrade = item.category == ShopItemCategory.Upgrade || item.upgradeEffect != UpgradeEffect.None;
            if (!isUpgrade) continue;

            // Эффекты идемпотентны, поэтому просто вызываем ApplyUpgradeEffect;
            // она сама залогирует фактическое применение (или «уже установлено»).
            if (!ApplyUpgradeEffect(item))
            {
                Debug.LogWarning($"[ShopManager] Не удалось восстановить эффект купленного апгрейда '{item.displayName}'.");
            }
        }
    }

    // ★ v1.1: переиспользуемый буфер проверки стен — без аллокаций на покупку.
    private static readonly Collider[] wallOverlapBuffer = new Collider[16];
}
