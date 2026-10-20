#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Диагностический инструмент для GridPositioningSystem. Меню: Tools → Grid System.
/// Файл ДОЛЖЕН лежать в папке Assets/Editor/ — тогда он не попадёт в билд игры.
///
/// Закрывает три последние возможные причины «объект есть, а игра его не видит»:
/// 1) Сцена не сохранена — BootScene → SceneLoader грузит GameScene С ДИСКА,
///    то есть сохранённую версию, а не то, что сейчас на экране.
/// 2) Два файла сцен с одинаковым именем — SceneLoader грузит по ИМЕНИ из
///    Build Settings, и может грузиться не тот GameScene, который ты правил.
/// 3) Missing Script — объект в сцене есть, но компонент потерял ссылку на
///    файл скрипта (классика после перемещения/пересоздания файла скрипта).
/// </summary>
public static class GridSystemDebugTools
{
    private const string MenuRoot = "Tools/Grid System/";

    // ─────────────────────────────────────────────────────────────
    // 1) Проверка ОТКРЫТОЙ сцены — самый быстрый способ понять,
    //    почему игра не видит сетку, которую ты видишь в Hierarchy.
    // ─────────────────────────────────────────────────────────────
    [MenuItem(MenuRoot + "1) Проверить сетку в ОТКРЫТОЙ сцене")]
    public static void CheckOpenScene()
    {
        Scene scene = SceneManager.GetActiveScene();

        Debug.Log($"[GridCheck] Проверяю открытую сцену: '{scene.name}' ({scene.path})");

        GridPositioningSystem[] grids = Object.FindObjectsByType<GridPositioningSystem>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (grids != null && grids.Length > 0)
        {
            foreach (GridPositioningSystem grid in grids)
            {
                string parent = grid.transform.parent != null ? grid.transform.parent.name : "(без родителя)";
                Debug.Log($"[GridCheck] ✓ Сетка найдена: объект '{grid.name}', родитель: {parent}, " +
                          $"активен: {grid.gameObject.activeInHierarchy}. В ЭТОЙ сцене всё в порядке.", grid);
            }
        }
        else
        {
            Debug.LogError("[GridCheck] ✗ В открытой сцене объекта с компонентом GridPositioningSystem НЕТ. " +
                           "Если ты ВИДИШЬ такой объект в Hierarchy — открой его Inspector: скорее всего, " +
                           "компонент отображается как 'Missing (Mono Script)' (потеряна ссылка на файл скрипта). " +
                           "Тогда: right click по компоненту → Remove Component → добавь GridPositioningSystem заново.");
        }

        // Missing Script: объект есть, а скрипт «слетел». Компонент хранит ссылку
        // на файл по GUID — перемещение/пересоздание файла её рвёт.
        int missingTotal = 0;
        GameObject[] allObjects = Object.FindObjectsByType<GameObject>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (GameObject go in allObjects)
        {
            int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            if (missing <= 0) continue;

            missingTotal += missing;
            if (go.name.ToLower().Contains("grid"))
            {
                Debug.LogWarning($"[GridCheck] ⚠ Объект '{go.name}' содержит Missing Script ({missing} шт.)! " +
                                 "Right click по компоненту → Remove Component → добавь GridPositioningSystem заново.", go);
            }
        }

        if (missingTotal > 0)
        {
            Debug.LogWarning($"[GridCheck] Всего Missing Script в сцене: {missingTotal} — такие компоненты мертвы, игра их не видит.");
        }

        // НЕСОХРАНЁННОСТЬ — причина №1 «объект есть, а игра его не видит»:
        // BootScene → SceneLoader грузит GameScene с диска, то есть сохранённую версию.
        if (scene.isDirty)
        {
            Debug.LogWarning("[GridCheck] ⚠⚠ СЦЕНА НЕ СОХРАНЕНА (звёздочка * рядом с именем сцены в Hierarchy)! " +
                             "Нажми Ctrl+S ПРЯМО СЕЙЧАС — иначе при запуске через BootScene будет играть старая " +
                             "версия сцены с диска, без твоих последних объектов (включая сетку).");
        }
        else
        {
            Debug.Log("[GridCheck] Сцена сохранена (isDirty = false) — на диске лежит то, что ты видишь в Hierarchy.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 2) Полная проверка: открывает каждую сцену из Build Settings
    //    (СОХРАНЁННЫЕ версии на диске!) и ищет сетку в каждой.
    //    Ловит ситуацию «два файла GameScene в разных папках» —
    //    SceneLoader грузит по имени, и может грузиться не тот файл.
    // ─────────────────────────────────────────────────────────────
    [MenuItem(MenuRoot + "2) Проверить сетку во ВСЕХ сценах Build Settings")]
    public static void CheckAllBuildScenes()
    {
        EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;

        if (buildScenes == null || buildScenes.Length == 0)
        {
            Debug.LogError("[GridCheck] В Build Settings нет ни одной сцены!");
            return;
        }

        string originalPath = SceneManager.GetActiveScene().path;

        // Перед открытием других сцен даём сохранить текущую — иначе её правки могут потеряться.
        if (SceneManager.GetActiveScene().isDirty)
        {
            bool save = EditorUtility.DisplayDialog(
                "GridCheck: сохранить сцену?",
                "Текущая сцена '" + SceneManager.GetActiveScene().name + "' имеет несохранённые изменения.\n\n" +
                "Сохранить перед проверкой? (Рекомендую — иначе изменения могут потеряться при открытии других сцен.)",
                "Сохранить", "Не сохранять");

            if (save)
            {
                EditorSceneManager.SaveOpenScenes();
            }
        }

        Debug.Log($"[GridCheck] Сканирую {buildScenes.Length} сцен из Build Settings — СОХРАНЁННЫЕ версии на диске...");

        foreach (EditorBuildSettingsScene buildScene in buildScenes)
        {
            if (!buildScene.enabled)
            {
                Debug.Log($"[GridCheck] — '{buildScene.path}' выключена в Build Settings, пропускаю.");
                continue;
            }

            if (!File.Exists(buildScene.path))
            {
                Debug.LogError($"[GridCheck] ✗ '{buildScene.path}' указана в Build Settings, но ФАЙЛА НЕ СУЩЕСТВУЕТ! " +
                               "Удали её из Build Settings или добавь правильную сцену.");
                continue;
            }

            EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Single);

            GridPositioningSystem[] grids = Object.FindObjectsByType<GridPositioningSystem>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (grids != null && grids.Length > 0)
            {
                foreach (GridPositioningSystem grid in grids)
                {
                    Debug.Log($"[GridCheck] ✓ '{buildScene.path}' — сетка ЕСТЬ (объект '{grid.name}', " +
                              $"активен: {grid.gameObject.activeInHierarchy}).");
                }
            }
            else
            {
                Debug.LogWarning($"[GridCheck] ✗ '{buildScene.path}' — в СОХРАНЁННОЙ сцене сетки НЕТ.");
            }
        }

        if (!string.IsNullOrEmpty(originalPath) && File.Exists(originalPath))
        {
            EditorSceneManager.OpenScene(originalPath, OpenSceneMode.Single);
            Debug.Log($"[GridCheck] Вернул открытую сцену: '{originalPath}'.");
        }

        Debug.Log("[GridCheck] Итог: сетка должна быть ровно в ОДНОЙ игровой сцене (GameScene). " +
                  "Если нашлась в BootScene/MainMenuScene — перенеси её в GameScene.");
    }

    // ─────────────────────────────────────────────────────────────
    // 3) Быстрое создание сетки в активной сцене — не собирать объект
    //    вручную. Создаёт, выделяет и сохраняет сцену.
    // ─────────────────────────────────────────────────────────────
    [MenuItem(MenuRoot + "3) Создать сетку в активной сцене")]
    public static void CreateGridInOpenScene()
    {
        GridPositioningSystem[] existing = Object.FindObjectsByType<GridPositioningSystem>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (existing != null && existing.Length > 0)
        {
            Debug.LogWarning($"[GridCheck] В сцене уже есть GridPositioningSystem: '{existing[0].name}'. Ничего не создаю.", existing[0]);
            return;
        }

        GameObject go = new GameObject("GridPositioningSystem");
        go.AddComponent<GridPositioningSystem>();
        Undo.RegisterCreatedObjectUndo(go, "Create GridPositioningSystem");
        Selection.activeGameObject = go;

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        bool saved = EditorSceneManager.SaveOpenScenes();

        Debug.Log($"[GridCheck] ✓ Создал объект 'GridPositioningSystem' в сцене '{SceneManager.GetActiveScene().name}' " +
                  $"и {(saved ? "сохранил сцену" : "НЕ смог сохранить — нажми Ctrl+S вручную")}. " +
                  "Настрой cellSize/gridOrigin под комнату (гизмо сетки видно в Scene view).", go);
    }

    // ─────────────────────────────────────────────────────────────
    // 4) Решающая проверка ВО ВРЕМЯ ИГРЫ. Запусти игру, дождись
    //    warning'а «НЕ НАЙДЕН» и выбери этот пункт, НЕ выходя из Play Mode.
    //    Он печатает пути РЕАЛЬНО загруженных сцен: если путь отличается
    //    от 'Assets/Scenes/GameScene.unity' — в Build Settings лежит
    //    другой файл с тем же именем, и грузится именно он.
    // ─────────────────────────────────────────────────────────────
    [MenuItem(MenuRoot + "4) Проверить сетку ПРЯМО СЕЙЧАС (в Play Mode)")]
    public static void CheckPlayModeNow()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[GridCheck] Сначала запусти игру (Play), затем выбери этот пункт, НЕ выходя из Play Mode.");
            return;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene s = SceneManager.GetSceneAt(i);
            if (i > 0) sb.Append(", ");
            sb.Append("'").Append(s.name).Append("' (").Append(s.path).Append(")");
            if (!s.isLoaded) sb.Append(" [выгружена]");
        }

        GridPositioningSystem[] grids = Object.FindObjectsByType<GridPositioningSystem>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (grids != null && grids.Length > 0)
        {
            foreach (GridPositioningSystem grid in grids)
            {
                Debug.Log($"[GridCheck] В Play Mode сетка ЕСТЬ: '{grid.name}', активна: {grid.gameObject.activeInHierarchy}, " +
                          $"сцена объекта: '{grid.gameObject.scene.path}'. Загруженные сцены: {sb}", grid);
            }
        }
        else
        {
            Debug.LogError("[GridCheck] В Play Mode сетки НЕТ. Загруженные сцены: " + sb + ". " +
                           "СРАВНИ путь с 'Assets/Scenes/GameScene.unity' (та, что прошла проверку №1). " +
                           "Пути разные → в Build Settings лежит ДРУГОЙ файл GameScene — открой File → Build Settings " +
                           "и замени его на правильный. Пути одинаковые → сетку кто-то удаляет при старте — пришли мне этот лог.");
        }
    }
}
#endif
