using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ★ v1.5 (фича «Полы по зонам») — Вариант 3: пол разбит на зоны (кухня /
/// обеденный зал / вход), у каждой зоны свой материал. Скины покупаются в лавке
/// (ShopItemSO с категорией FloorSkin, prefab = null) и назначаются зонам через
/// простое меню (FloorDecorUI): кнопки «‹» и «›» листают купленные скины.
///
/// ЗОНЫ: список zones заполняется в инспекторе. zoneId — уникальная строка
/// («kitchen», «hall», «entrance») — по ней зона сохраняется в сейв.
/// renderers — массив рендереров: одна зона может состоять из нескольких кусков
/// пола. При применении скина ВСЕ слоты материалов каждого рендерера заменяются
/// материалом скина; «По умолчанию» возвращает исходные материалы, снятые в Awake.
///
/// МОДАЛЬНОСТЬ: панель полов открывается клавишей F в фазе подготовки дня
/// (настраивается toggleKey). Полностью повторяет паттерн ShopManager:
/// PushUiInputLock (Enter не начнёт день поверх панели, перенос мебели замрёт)
/// + GameInput.DisablePlayerInput. Если открыт магазин — панель полов закроется
/// (магазин — старший модальный экран).
///
/// СОХРАНЕНИЕ: GameLoopManager.SaveCurrentProgress забирает GetFloorSaveData()
/// (список zoneId → skinItemId), RestoreFromSave вызывается при старте сцены.
/// Скин сохраняется по skinItemId (строка) — тот же приём, что у карт и мебели.
/// «По умолчанию» в сейв не пишется (пустая запись лишняя).
///
/// Сам FloorManager НЕ рисует интерфейс — только хранит состояние и стреляет
/// событиями OnFloorPanelOpened / OnFloorPanelClosed / OnZoneSkinsChanged.
/// </summary>
public class FloorManager : MonoBehaviour
{
    public static FloorManager Instance { get; private set; }

    public event EventHandler OnFloorPanelOpened;
    public event EventHandler OnFloorPanelClosed;
    public event EventHandler OnZoneSkinsChanged;

    /// <summary>
    /// Одна зона пола. zoneId — уникальный строковый идентификатор для сейва;
    /// displayName — человеческое имя для меню; renderers — куски пола этой зоны.
    /// </summary>
    [Serializable]
    public class FloorZone
    {
        [Tooltip("Уникальный id зоны для сохранения. Например: kitchen, hall, entrance")]
        public string zoneId;

        [Tooltip("Название в меню полов. Например: «Кухня»")]
        public string displayName;

        [Tooltip("Рендереры пола этой зоны (одна зона может состоять из нескольких кусков)")]
        public Renderer[] renderers;
    }

    [Header("Зоны")]
    [Tooltip("Все зоны пола в сцене. zoneId должен быть уникальным — по нему работает сейв")]
    [SerializeField] private List<FloorZone> zones = new List<FloorZone>();

    [Header("Каталог скинов")]
    [Tooltip("Все FloorSkinSO игры. Скин считается купленным, если в магазине куплен " +
             "товар ShopItemSO с itemId == skin.skinId")]
    [SerializeField] private List<FloorSkinSO> skinCatalog = new List<FloorSkinSO>();

    [Header("Управление")]
    [Tooltip("Клавиша открытия/закрытия меню полов. По умолчанию F (Floor)")]
    [SerializeField] private KeyCode toggleKey = KeyCode.F;

    /// <summary>True, пока меню полов открыто. UI подписывается на события.</summary>
    public bool IsPanelOpen { get; private set; }

    /// <summary>
    /// Текущее состояние зон: zoneId → skinItemId. Пустая строка = «По умолчанию».
    /// Заполняется циклом скинов (CycleZoneSkin) и восстановлением сейва.
    /// </summary>
    private readonly Dictionary<string, string> zoneSkins = new Dictionary<string, string>();

    /// <summary>
    /// Исходные материалы всех рендереров (сняты в Awake, ДО любых применений скинов).
    /// Ключ — Renderer, значение — копия массива sharedMaterials. «По умолчанию»
    /// возвращает именно их.
    /// </summary>
    private readonly Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();

    // ★ паттерн ShopManager: держим ли мы сейчас PushUiInputLock — для строгой парности Push/Pop.
    private bool uiLockHeld;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        CaptureOriginalMaterials();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        // Страховка парности: если объект уничтожили при открытой панели,
        // блокировка ввода обязана сняться.
        ReleaseUiLock();
    }

    private void Update()
    {
        // Меню полов работает только в фазе подготовки дня — как магазин.
        if (GameLoopManager.Instance == null || !GameLoopManager.Instance.IsPreparationActive())
        {
            if (IsPanelOpen)
            {
                ClosePanel();
            }
            return;
        }

        // Магазин — старший экран: если он открылся, нашу панель закрываем,
        // чтобы два модальных экрана не висели одновременно.
        if (IsPanelOpen && ShopManager.Instance != null && ShopManager.Instance.IsShopOpen)
        {
            ClosePanel();
            return;
        }

        if (Input.GetKeyDown(toggleKey))
        {
            if (IsPanelOpen)
            {
                ClosePanel();
            }
            else
            {
                OpenPanel();
            }
        }
        else if (IsPanelOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            ClosePanel();
        }
    }

    // ===== Инициализация =====

    /// <summary>
    /// Снимает исходные материалы всех зон в Awake — до любых применений скинов.
    /// Заодно валидирует разметку: пустые/дублирующиеся zoneId ломали бы сейв.
    /// </summary>
    private void CaptureOriginalMaterials()
    {
        HashSet<string> seenIds = new HashSet<string>();

        foreach (FloorZone zone in zones)
        {
            if (zone == null) continue;

            if (string.IsNullOrEmpty(zone.zoneId))
            {
                Debug.LogError($"[FloorManager] Зона «{zone.displayName}» без zoneId — пропущена. " +
                               "Назначь каждой зоне уникальный id (он используется в сохранении).");
                continue;
            }

            if (!seenIds.Add(zone.zoneId))
            {
                Debug.LogError($"[FloorManager] Дублирующийся zoneId «{zone.zoneId}» — вторая зона пропущена. " +
                               "Сейв хранит состояние по zoneId, дубли недопустимы.");
                continue;
            }

            if (zone.renderers == null || zone.renderers.Length == 0)
            {
                Debug.LogWarning($"[FloorManager] Зона «{zone.displayName}» ({zone.zoneId}) без рендереров — скины на неё не повесятся.");
                continue;
            }

            foreach (Renderer renderer in zone.renderers)
            {
                if (renderer == null) continue;

                Material[] mats = renderer.sharedMaterials;
                Material[] copy = new Material[mats.Length];
                Array.Copy(mats, copy, mats.Length);
                originalMaterials[renderer] = copy;
            }
        }
    }

    // ===== Модальность (паттерн ShopManager) =====

    /// <summary>Открывает меню полов. Вызывается по toggleKey или из UI-кнопки.</summary>
    public void OpenPanel()
    {
        if (IsPanelOpen) return;

        // Не открываемся поверх магазина (и вообще вне подготовки дня — гейт в Update).
        if (ShopManager.Instance != null && ShopManager.Instance.IsShopOpen) return;

        IsPanelOpen = true;

        if (GameInput.Instance != null)
        {
            GameInput.Instance.DisablePlayerInput();
        }

        AcquireUiLock();

        OnFloorPanelOpened?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Закрывает меню полов. Вызывается по toggleKey/Escape, из UI-кнопки и страховками.</summary>
    public void ClosePanel()
    {
        if (!IsPanelOpen) return;

        IsPanelOpen = false;

        if (GameInput.Instance != null)
        {
            GameInput.Instance.EnablePlayerInput();
        }

        ReleaseUiLock();

        OnFloorPanelClosed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Для UI-кнопки «Полы»: OnClick → FloorManager.TogglePanel.</summary>
    public void TogglePanel()
    {
        if (IsPanelOpen) ClosePanel(); else OpenPanel();
    }

    private void AcquireUiLock()
    {
        if (uiLockHeld) return;

        if (GameLoopManager.Instance != null)
        {
            GameLoopManager.Instance.PushUiInputLock();
            uiLockHeld = true;
        }
    }

    private void ReleaseUiLock()
    {
        if (!uiLockHeld) return;

        if (GameLoopManager.Instance != null)
        {
            GameLoopManager.Instance.PopUiInputLock();
        }

        uiLockHeld = false;
    }

    // ===== Запросы для UI =====

    /// <summary>Список зон для построения строк меню. Null-зоны отфильтрованы.</summary>
    public List<FloorZone> GetZones()
    {
        List<FloorZone> valid = new List<FloorZone>();
        foreach (FloorZone zone in zones)
        {
            if (zone != null && !string.IsNullOrEmpty(zone.zoneId))
            {
                valid.Add(zone);
            }
        }
        return valid;
    }

    /// <summary>
    /// Купленные скины — те, чей skinId совпадает с itemId купленного товара лавки.
    /// Порядок совпадает с порядком skinCatalog — «‹» и «›» листают его стабильно.
    /// </summary>
    public List<FloorSkinSO> GetOwnedSkins()
    {
        List<FloorSkinSO> owned = new List<FloorSkinSO>();

        foreach (FloorSkinSO skin in skinCatalog)
        {
            if (skin == null) continue;

            if (ShopManager.Instance != null && ShopManager.Instance.GetPurchaseCountById(skin.skinId) > 0)
            {
                owned.Add(skin);
            }
        }

        return owned;
    }

    /// <summary>Текущий skinItemId зоны ("" или неизвестный id = «По умолчанию»).</summary>
    public string GetCurrentSkinId(string zoneId)
    {
        return zoneSkins.TryGetValue(zoneId, out string skinId) ? skinId : "";
    }

    /// <summary>Имя текущего скина зоны для UI: «По умолчанию» или displayName скина.</summary>
    public string GetSkinDisplayName(string zoneId)
    {
        string skinId = GetCurrentSkinId(zoneId);
        FloorSkinSO skin = FindSkinById(skinId);
        return skin != null ? skin.displayName : "По умолчанию";
    }

    /// <summary>Иконка текущего скина зоны для UI (null = «По умолчанию» — иконки нет).</summary>
    public Sprite GetSkinIcon(string zoneId)
    {
        FloorSkinSO skin = FindSkinById(GetCurrentSkinId(zoneId));
        return skin != null ? skin.icon : null;
    }

    /// <summary>Поиск скина в каталоге по skinItemId (строке из сейва).</summary>
    public FloorSkinSO FindSkinById(string skinId)
    {
        if (string.IsNullOrEmpty(skinId)) return null;

        foreach (FloorSkinSO skin in skinCatalog)
        {
            if (skin != null && skin.skinId == skinId)
            {
                return skin;
            }
        }
        return null;
    }

    // ===== Смена скина =====

    /// <summary>
    /// Листание скина зоны: direction = +1 (следующий) или -1 (предыдущий).
    /// Варианты для зоны: [«По умолчанию»] + купленные скины (в порядке каталога),
    /// циклично. Применяет материал, сохраняет прогресс и стреляет OnZoneSkinsChanged.
    /// Возвращает true, если скин сменился.
    /// </summary>
    public bool CycleZoneSkin(string zoneId, int direction)
    {
        FloorZone zone = FindZoneById(zoneId);
        if (zone == null) return false;

        List<FloorSkinSO> owned = GetOwnedSkins();

        // Полный круг вариантов: 0 = «По умолчанию», 1..N = купленные скины.
        int total = owned.Count + 1;
        string currentId = GetCurrentSkinId(zoneId);
        int currentIndex = 0;
        for (int i = 0; i < owned.Count; i++)
        {
            if (owned[i].skinId == currentId)
            {
                currentIndex = i + 1;
                break;
            }
        }

        int nextIndex = ((currentIndex + direction) % total + total) % total;

        if (nextIndex == 0)
        {
            return ApplySkinToZone(zoneId, null);
        }

        return ApplySkinToZone(zoneId, owned[nextIndex - 1]);
    }

    /// <summary>
    /// Применить скин к зоне. skin = null — вернуть «По умолчанию».
    /// Меняет материалы, пишет состояние, сохраняет прогресс (золото/день/мебель
    /// и всё остальное переезжают в сейв одним вызовом SaveCurrentProgress).
    /// </summary>
    public bool ApplySkinToZone(string zoneId, FloorSkinSO skin)
    {
        FloorZone zone = FindZoneById(zoneId);
        if (zone == null) return false;

        if (skin != null && FindSkinById(skin.skinId) == null)
        {
            Debug.LogWarning($"[FloorManager] Скин «{skin.displayName}» не из каталога FloorManager — отклонён.");
            return false;
        }

        zoneSkins[zoneId] = skin != null ? skin.skinId : "";
        ApplySkinMaterials(zone, skin);

        // Сохраняем сразу: выход в любой момент не потеряет выбор пола.
        if (GameLoopManager.Instance != null)
        {
            GameLoopManager.Instance.SaveCurrentProgress();
        }

        OnZoneSkinsChanged?.Invoke(this, EventArgs.Empty);

        Debug.Log($"[FloorManager] Зона «{zone.displayName}»: {GetSkinDisplayName(zoneId)}.");
        return true;
    }

    /// <summary>
    /// Подмена материалов рендереров зоны: все слоты каждого рендерера получают
    /// материал скина (зона станет целиком из нового пола), null — исходные материалы.
    /// Использую sharedMaterials (а не material), чтобы не плодить инстансы материалов
    /// на каждый свап: мы только назначаем готовые ассеты, свойства не правим.
    /// </summary>
    private void ApplySkinMaterials(FloorZone zone, FloorSkinSO skin)
    {
        if (zone.renderers == null) return;

        foreach (Renderer renderer in zone.renderers)
        {
            if (renderer == null) continue;

            if (skin == null)
            {
                if (originalMaterials.TryGetValue(renderer, out Material[] original))
                {
                    renderer.sharedMaterials = original;
                }
                continue;
            }

            if (skin.floorMaterial == null)
            {
                Debug.LogWarning($"[FloorManager] У скина «{skin.displayName}» не назначен floorMaterial — зона «{zone.displayName}» не изменится.");
                continue;
            }

            int slotCount = renderer.sharedMaterials.Length;
            Material[] newMats = new Material[slotCount];
            for (int i = 0; i < slotCount; i++)
            {
                newMats[i] = skin.floorMaterial;
            }
            renderer.sharedMaterials = newMats;
        }
    }

    private FloorZone FindZoneById(string zoneId)
    {
        if (string.IsNullOrEmpty(zoneId)) return null;

        foreach (FloorZone zone in zones)
        {
            if (zone != null && zone.zoneId == zoneId)
            {
                return zone;
            }
        }
        return null;
    }

    // ===== Сохранение / восстановление =====

    /// <summary>
    /// Снимок состояния зон для GameSaveData.floorZones. Зоны «По умолчанию»
    /// не пишутся — их отсутствие в сейве и означает дефолт (короткий JSON).
    /// </summary>
    public List<FloorZoneSkinSaveData> GetFloorSaveData()
    {
        List<FloorZoneSkinSaveData> list = new List<FloorZoneSkinSaveData>();

        foreach (KeyValuePair<string, string> kvp in zoneSkins)
        {
            if (string.IsNullOrEmpty(kvp.Value)) continue;

            list.Add(new FloorZoneSkinSaveData { zoneId = kvp.Key, skinItemId = kvp.Value });
        }

        return list;
    }

    /// <summary>
    /// Восстановление зон из сейва (вызывается GameLoopManager при старте сцены).
    /// Старые сейвы без поля floorZones дают пустой список — все зоны остаются дефолтными.
    /// Скин, которого нет в каталоге (удалили ассет), молча откатывает зону к дефолту.
    /// </summary>
    public void RestoreFromSave(List<FloorZoneSkinSaveData> saved)
    {
        zoneSkins.Clear();

        // Сначала все зоны — к исходным материалам (на случай повторного вызова).
        foreach (FloorZone zone in zones)
        {
            if (zone != null && !string.IsNullOrEmpty(zone.zoneId))
            {
                ApplySkinMaterials(zone, null);
            }
        }

        if (saved == null || saved.Count == 0)
        {
            Debug.Log("[FloorManager] В сохранении нет записей о зонах пола — все зоны по умолчанию.");
            return;
        }

        int applied = 0;

        foreach (FloorZoneSkinSaveData entry in saved)
        {
            if (entry == null || string.IsNullOrEmpty(entry.zoneId)) continue;

            FloorSkinSO skin = FindSkinById(entry.skinItemId);
            if (skin == null)
            {
                Debug.LogWarning($"[FloorManager] Зона «{entry.zoneId}»: скин «{entry.skinItemId}» не найден в каталоге — зона остаётся по умолчанию.");
                continue;
            }

            zoneSkins[entry.zoneId] = entry.skinItemId;
            ApplySkinMaterials(FindZoneById(entry.zoneId), skin);
            applied++;
        }

        Debug.Log($"[FloorManager] Восстановлено скинов на зонах: {applied}.");

        OnZoneSkinsChanged?.Invoke(this, EventArgs.Empty);
    }
}
