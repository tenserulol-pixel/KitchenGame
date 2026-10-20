using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ★ НОВЫЙ ФАЙЛ (v1.0) — мост между сеткой (GridPositioningSystem) и сохранением
/// (SaveManager). Закрывает BUG-4: раньше placedObjects в GameSaveData всегда
/// оставался пустым, и сохранение теряло всю переставленную и купленную мебель.
///
/// КАК ЭТО РАБОТАЕТ.
///
/// Сохранение (CollectPlacedObjects):
///   Берём снимок реестра занятых ячеек сетки — это ВСЯ мебель, которая сейчас
///   стоит на кухне (и авторская из сцены, и купленная в магазине). Для каждого
///   объекта пишем: имя (без «(Clone)»), itemId (если найден в каталоге магазина),
///   позицию и поворот.
///
/// Загрузка (ApplySavedLayout):
///   Вызывается через кадр после старта GameScene (см. GameLoopManager.Start) —
///   гарантированно ПОСЛЕ всех Awake/BaseCounter-регистраций и ПОСЛЕ
///   StartingLayoutRandomizer (он в своём Start уводит прилавки в резерв).
///   Поэтому на ПРОДОЛЖЕНИИ игры сохранённая расстановка побеждает случайную,
///   а на НОВОЙ игре (сохранения нет) метод просто ничего не делает.
///
///   Сопоставление «запись сохранения ↔ объект сцены» — ПО ИМЕНИ:
///   1) Нашёлся живой объект сцены с таким именем → телепортируем его на
///      сохранённое место (ячейки освобождаются заранее, см. ниже).
///   2) Не нашёлся, но у записи есть itemId → это купленный предмет: спавним
///      префаб из каталога магазина на сохранённом месте.
///   3) Не нашёлся и itemId пуст → объект переименован/удалён дизайнером —
///      запись пропускается с явным Warning.
///
///   Почему два прохода: если переносить мебель по очереди, промежуточные
///   конфликты гарантированы (стол А хочет в ячейку стола Б, который ещё не
///   ушёл со своего места). Сначала мы освобождаем ячейки ВСЕЙ переносимой
///   мебели, потом расставляем — конфликтов почти не остаётся, а редкий
///   остаточный (ячейку держит объект, которого нет в сохранении) решается
///   переносом на ближайшую свободную ячейку.
///
/// ИЗВЕСТНОЕ ОГРАНИЧЕНИЕ: если в сцене и в каталоге магазина есть предметы
/// с ОДИНАКОВЫМ именем (например, авторская «StoveCounter» и купленный
/// «StoveCounter (Clone)»), при загрузке позиции одинаковых предметов могут
/// обменяться местами. Для игрока это незаметно — предметы идентичны.
///
/// Стулья DiningTable отдельно не сохраняются: они дети стола
/// (DiningTable.Awake делает SetParent) и переезжают/поворачиваются вместе с ним.
/// </summary>
public static class FurnitureSaveUtility
{
    private const string CloneSuffix = "(Clone)";

    /// <summary>Кандидат на загрузку: запись сохранения + найденный объект сцены.</summary>
    private class MatchedEntry
    {
        public PlacedObjectSaveData data;
        public BaseCounter counter;
    }

    // ============================================================
    //  СОХРАНЕНИЕ
    // ============================================================

    /// <summary>
    /// Собирает текущую расстановку мебели со сетки для GameSaveData.placedObjects.
    /// Вызывается из GameLoopManager.SaveCurrentProgress().
    /// </summary>
    public static List<PlacedObjectSaveData> CollectPlacedObjects()
    {
        List<PlacedObjectSaveData> result = new List<PlacedObjectSaveData>();

        GridPositioningSystem grid = GridPositioningSystem.ResolveInstance();
        if (grid == null)
        {
            Debug.LogWarning("[FurnitureSave] GridPositioningSystem не найден — " +
                             "расстановка мебели в сохранение НЕ попала.");
            return result;
        }

        foreach (KeyValuePair<Vector2Int, BaseCounter> kvp in grid.GetOccupiedCellsSnapshot())
        {
            BaseCounter counter = kvp.Value;
            if (counter == null) continue;

            result.Add(new PlacedObjectSaveData
            {
                objectName = StripCloneSuffix(counter.gameObject.name),
                itemId = FindItemIdForCounter(counter),
                position = counter.transform.position,
                rotation = counter.transform.rotation
            });
        }

        Debug.Log($"[FurnitureSave] В сохранение записано объектов мебели: {result.Count}.");
        return result;
    }

    /// <summary>«StoveCounter (Clone)» → «StoveCounter».</summary>
    private static string StripCloneSuffix(string objectName)
    {
        if (string.IsNullOrEmpty(objectName)) return objectName;

        int cloneIndex = objectName.LastIndexOf(CloneSuffix);
        if (cloneIndex < 0) return objectName;

        string stripped = objectName.Substring(0, cloneIndex);
        return stripped.TrimEnd();
    }

    /// <summary>
    /// Ищет itemId каталога магазина по имени префаба. Купленные объекты —
    /// инстансы префабов, их имя всегда «<имя префаба> (Clone)».
    /// Для авторской мебели сцены itemId останется пустым — при загрузке такие
    /// записи сопоставляются с объектами сцены по имени.
    /// </summary>
    private static string FindItemIdForCounter(BaseCounter counter)
    {
        if (ShopManager.Instance == null) return string.Empty;

        string prefabName = StripCloneSuffix(counter.gameObject.name);

        foreach (ShopItemSO item in ShopManager.Instance.GetCatalogItems())
        {
            if (item == null || item.prefab == null) continue;
            if (item.prefab.name == prefabName) return item.itemId;
        }

        return string.Empty;
    }

    // ============================================================
    //  ЗАГРУЗКА
    // ============================================================

    /// <summary>
    /// Применяет сохранённую расстановку к свежезагруженной GameScene.
    /// Вызывается из GameLoopManager через кадр после старта сцены.
    /// </summary>
    public static void ApplySavedLayout()
    {
        if (SaveManager.Instance == null || !SaveManager.Instance.HasSave())
        {
            return; // Новая игра — расставляем как задумано дизайнером.
        }

        GameSaveData save = SaveManager.Instance.GetCurrentSave();
        if (save == null || save.placedObjects == null || save.placedObjects.Count == 0)
        {
            Debug.Log("[FurnitureSave] В сохранении нет данных о мебели — применяю авторскую расстановку.");
            return;
        }

        GridPositioningSystem grid = GridPositioningSystem.ResolveInstance();
        if (grid == null)
        {
            Debug.LogError("[FurnitureSave] GridPositioningSystem не найден — " +
                           "сохранённую расстановку применить нельзя.");
            return;
        }

        // ── 1. Индекс активной мебели сцены по имени ──
        BaseCounter[] sceneCounters = Object.FindObjectsByType<BaseCounter>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        Dictionary<string, List<BaseCounter>> sceneByName = new Dictionary<string, List<BaseCounter>>();
        HashSet<BaseCounter> claimed = new HashSet<BaseCounter>();

        foreach (BaseCounter counter in sceneCounters)
        {
            if (counter == null) continue;

            string key = StripCloneSuffix(counter.gameObject.name);
            if (!sceneByName.TryGetValue(key, out List<BaseCounter> list))
            {
                list = new List<BaseCounter>();
                sceneByName[key] = list;
            }
            list.Add(counter);
        }

        // ── 2. Разбор записей сохранения: перенос / спавн / пропуск ──
        List<MatchedEntry> matched = new List<MatchedEntry>();
        List<PlacedObjectSaveData> toSpawn = new List<PlacedObjectSaveData>();
        int skipped = 0;

        foreach (PlacedObjectSaveData entry in save.placedObjects)
        {
            if (entry == null)
            {
                skipped++;
                continue;
            }

            bool hasName = !string.IsNullOrEmpty(entry.objectName);
            bool hasItemId = !string.IsNullOrEmpty(entry.itemId);

            BaseCounter sceneObject = null;

            if (hasName && sceneByName.TryGetValue(entry.objectName, out List<BaseCounter> candidates))
            {
                // Первая незанятая корзина с этим именем — так мы корректно
                // обрабатываем и дубликаты имён (несколько «ClearCounter»).
                sceneObject = candidates.Find(c => c != null && !claimed.Contains(c));
            }

            if (sceneObject != null)
            {
                claimed.Add(sceneObject);
                matched.Add(new MatchedEntry { data = entry, counter = sceneObject });
            }
            else if (hasItemId)
            {
                // В свежей сцене такого объекта нет — значит, это купленный
                // предмет прошлой сессии. Спавним из каталога.
                toSpawn.Add(entry);
            }
            else
            {
                skipped++;
                Debug.LogWarning("[FurnitureSave] Запись сохранения '" + entry.objectName +
                                 "' не нашла объект в сцене (переименован? удалён?) — пропущена.");
            }
        }

        // ── 3. Освобождаем ячейки ВСЕЙ переносимой мебели одним махом ──
        foreach (MatchedEntry m in matched)
        {
            grid.RemoveCounter(m.counter);
        }

        // ── 4. Телепорт + поворот ──
        foreach (MatchedEntry m in matched)
        {
            m.counter.transform.position = m.data.position;
            m.counter.transform.rotation = m.data.rotation;
        }

        // ── 5. Регистрация с разрешением остаточных конфликтов ──
        // Ячейку мог удержать объект, которого нет в сохранении
        // (дизайнер добавил новую мебель после записи сохранения).
        int moved = 0;

        foreach (MatchedEntry m in matched)
        {
            Vector2Int targetCell = grid.GetGridPosition(m.counter.transform.position);

            if (!grid.IsCellFreeFor(targetCell, m.counter))
            {
                Vector2Int? freeCell = FindNearestFreeCell(grid, targetCell);

                if (freeCell == null)
                {
                    Debug.LogError("[FurnitureSave] Для '" + m.counter.gameObject.name +
                                   "' нет свободных ячеек рядом — объект остался на авторском месте.");
                    continue;
                }

                Vector3 adjusted = grid.GetWorldPosition(freeCell.Value);
                adjusted.y = m.counter.transform.position.y;
                m.counter.transform.position = adjusted;

                Debug.LogWarning("[FurnitureSave] '" + m.counter.gameObject.name +
                                 "': сохранённая ячейка занята — поставлен в соседнюю " + freeCell.Value + ".");
            }

            // RegisterCounterAtCurrentPosition выровняет по центру ячейки
            // и забронирует её (свою старую мы уже освободили в шаге 3).
            grid.RegisterCounterAtCurrentPosition(m.counter);
            moved++;
        }

        // ── 6. Спавн купленных предметов прошлой сессии ──
        int spawned = 0;

        foreach (PlacedObjectSaveData entry in toSpawn)
        {
            BaseCounter prefab = FindPrefabByItemId(entry.itemId);

            if (prefab == null)
            {
                skipped++;
                Debug.LogWarning("[FurnitureSave] itemId '" + entry.itemId +
                                 "' не найден в каталоге магазина — купленный предмет не восстановлен.");
                continue;
            }

            // Awake инстанса сам зарегистрирует его в сетке
            // (RegisterCounterAtCurrentPosition) на месте спавна.
            Object.Instantiate(prefab, entry.position, entry.rotation);
            spawned++;
        }

        Debug.Log($"[FurnitureSave] Расстановка восстановлена: перенесено {moved}, " +
                  $"заспавнено {spawned}, пропущено {skipped}.");
    }

    /// <summary>
    /// Поиск ближайшей свободной ячейки по кольцам (radius 1 → 3).
    /// Локальная копия логики ShopManager — утилита не должна зависеть
    /// от того, есть ли магазин в сцене.
    /// </summary>
    private static Vector2Int? FindNearestFreeCell(GridPositioningSystem grid, Vector2Int center)
    {
        const int maxRadius = 3;

        for (int radius = 1; radius <= maxRadius; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    if (Mathf.Abs(dx) != radius && Mathf.Abs(dz) != radius) continue;

                    Vector2Int candidate = center + new Vector2Int(dx, dz);

                    if (!grid.IsCellOccupied(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Префаб по itemId из каталога магазина (null — если каталог/товар не найден).</summary>
    private static BaseCounter FindPrefabByItemId(string itemId)
    {
        if (ShopManager.Instance == null) return null;

        foreach (ShopItemSO item in ShopManager.Instance.GetCatalogItems())
        {
            if (item != null && item.itemId == itemId) return item.prefab;
        }

        return null;
    }
}
