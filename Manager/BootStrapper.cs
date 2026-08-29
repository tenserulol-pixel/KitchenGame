using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Единственная точка входа в игру. Должен жить на пустом GameObject в BootScene,
/// которая идёт первой в Build Settings (index 0).
///
/// Задачи:
/// 1) Создать persistent-менеджеры (GameInput, SaveManager, SceneLoader) — они не должны
///    лежать в MainMenuScene или GameScene, иначе при переходе между сценами они бы
///    уничтожались и теряли свои подписки/состояния.
/// 2) Дождаться отработки Awake у всех persistent-менеджеров (один кадр).
/// 3) Перейти в MainMenuScene. После этого BootScene выгружается — но persistent-менеджеры
///    остаются (DontDestroyOnLoad).
///
/// Сам BootStrapper НЕ persistent — после загрузки MainMenuScene он больше не нужен.
/// </summary>
public class BootStrapper : MonoBehaviour
{
    [Header("Префабы persistent-менеджеров")]
    [Tooltip("Создаются через Instantiate, а не лежат в сцене — так мы гарантируем,\nчто их ровно по одному экземпляру на всю игру, и они не теряются при перезагрузке сцен.\n\nВАЖНО: назначьте все три префаба, иначе соответствующий менеджер не будет создан.")]
    [SerializeField] private GameInput gameInputPrefab;
    [SerializeField] private SaveManager saveManagerPrefab;
    [SerializeField] private SceneLoader sceneLoaderPrefab;

    [Header("Сцены")]
    [Tooltip("Имя сцены главного меню, в которую переходим после инициализации.")]
    [SerializeField] private string mainMenuSceneName = "MainMenuScene";

    private IEnumerator Start()
    {
        // 1) Создаём persistent-менеджеры. Instantiate сразу вызывает Awake у созданного
        //    объекта, так что после Instantiate мы гарантированно имеем живые Instance.
        InstantiatePersistentManagers();

        // 2) Ждём конец кадра, чтобы все Awake/Start у созданных менеджеров отработали.
        //    Особенно важно для SaveManager — он в Awake читает PlayerPrefs, и его Instance
        //    должен быть готов, прежде чем кто-то к нему обратится.
        yield return null;

        // 3) Переходим в главное меню. SceneLoader теперь жив и обработает переход
        //    с экраном загрузки и событиями, но для самого первого перехода достаточно
        //    обычного LoadSceneAsync — UI ещё не готов, экран загрузки показывать некуда.
        SceneManager.LoadSceneAsync(mainMenuSceneName);
    }

    private void InstantiatePersistentManagers()
    {
        if (gameInputPrefab != null && GameInput.Instance == null)
        {
            var gameInput = Instantiate(gameInputPrefab);
            DontDestroyOnLoad(gameInput.gameObject);
            // GameInput.Awake сам выставит Instance.
        }
        else if (GameInput.Instance == null)
        {
            Debug.LogError("[BootStrapper] GameInput prefab не назначен в инспекторе BootStrapper!");
        }

        if (saveManagerPrefab != null && SaveManager.Instance == null)
        {
            var saveManager = Instantiate(saveManagerPrefab);
            DontDestroyOnLoad(saveManager.gameObject);
        }
        else if (SaveManager.Instance == null)
        {
            Debug.LogError("[BootStrapper] SaveManager prefab не назначен в инспекторе BootStrapper!");
        }

        if (sceneLoaderPrefab != null && SceneLoader.Instance == null)
        {
            var sceneLoader = Instantiate(sceneLoaderPrefab);
            DontDestroyOnLoad(sceneLoader.gameObject);
        }
        else if (SceneLoader.Instance == null)
        {
            Debug.LogError("[BootStrapper] SceneLoader prefab не назначен в инспекторе BootStrapper!");
        }
    }
}