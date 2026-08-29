using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Persistent-менеджер переходов между сценами.
///
/// Зачем отдельный класс, если есть SceneManager.LoadSceneAsync?
/// 1) Единая точка: все переходы в игре идут через SceneLoader.Instance.LoadScene(...),
///    а не раскиданы по коду. Если захотим добавить экран загрузки, fade-анимацию
///    или событие до/после загрузки — правим только здесь.
/// 2) События OnSceneLoadStarted / OnSceneLoadCompleted: на них подписываются
///    AudioManager (тихнуть музыку), SaveManager (сохранить состояние), UI (показать лоадер).
/// 3) Защита от двойного перехода: если уже идёт загрузка, повторный вызов LoadScene
///    игнорируется — иначе два LoadSceneAsync ломают друг другу состояние.
///
/// Persistent: создаётся в BootScene через BootStrapper, помечается DontDestroyOnLoad,
/// переживает все переходы между сценами.
/// </summary>
public class SceneLoader : MonoBehaviour
{
    public static SceneLoader Instance { get; private set; }

    public event EventHandler<SceneLoadEventArgs> OnSceneLoadStarted;
    public event EventHandler<SceneLoadEventArgs> OnSceneLoadCompleted;

    public class SceneLoadEventArgs : EventArgs
    {
        public string sceneName;
    }

    [Header("Имена сцен")]
    [Tooltip("Используется как fallback, если имя не передано явно в LoadScene.")]
    [SerializeField] private string mainMenuSceneName = "MainMenuScene";
    [SerializeField] private string gameSceneName = "GameScene";

    /// <summary>
    /// True, пока идёт асинхронная загрузка сцены. На это время блокируются повторные
    /// вызовы LoadScene, иначе два LoadSceneAsync ломают друг другу состояние Unity.
    /// </summary>
    public bool IsLoading { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // DontDestroyOnLoad дублирует вызов из BootStrapper — на случай, если менеджер
        // случайно положили прямо в сцену, а не создали через префаб.
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Запускает асинхронную загрузку сцены по имени. Если уже идёт загрузка —
    /// тихо игнорирует (защиту от двойного клика по кнопке "Играть").
    ///
    /// showLoadingScreen: если true, через события можно показать UI загрузки.
    /// Сейчас сам SceneLoader UI не показывает — он только стреляет событиями.
    /// Подписчик (LoadingScreenUI в Canvas) решает, что рисовать.
    /// </summary>
    public void LoadScene(string sceneName, bool showLoadingScreen = true)
    {
        if (IsLoading)
        {
            Debug.LogWarning($"[SceneLoader] Уже грузится сцена, игнорирую повторный запрос на '{sceneName}'.");
            return;
        }

        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError("[SceneLoader] Имя сцены пустое, не могу загрузить.");
            return;
        }

        StartCoroutine(LoadSceneAsync(sceneName, showLoadingScreen));
    }

    /// <summary>Переход в главное меню по умолчанию.</summary>
    public void LoadMainMenu(bool showLoadingScreen = true) =>
        LoadScene(mainMenuSceneName, showLoadingScreen);

    /// <summary>Переход в игровую сцену по умолчанию.</summary>
    public void LoadGameScene(bool showLoadingScreen = true) =>
        LoadScene(gameSceneName, showLoadingScreen);

    private IEnumerator LoadSceneAsync(string sceneName, bool showLoadingScreen)
    {
        IsLoading = true;

        OnSceneLoadStarted?.Invoke(this, new SceneLoadEventArgs { sceneName = sceneName });

        // Минимальная пауза в один кадр: даём UI-подписчикам (LoadingScreen) отрисовать
        // себя до того, как начнётся тяжёлая загрузка сцены. Иначе OnSceneLoadStarted
        // и фактический старт загрузки происходят в одном кадре, и UI не успевает
        // появиться — экран загрузки моргает пустым.
        yield return null;

        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName);
        // allowSceneActivation=false дал бы ручной контроль над моментом активации сцены,
        // но нам это не нужно — пусть Unity сама переключает, когда загрузка завершится.

        while (operation != null && !operation.isDone)
        {
            // Здесь можно было бы публиковать прогресс (operation.progress) для прогресс-бара.
            // Сейчас намеренно не делаем, чтобы не раздувать API — добавим, когда будет UI.
            yield return null;
        }

        // Ждём конец кадра после переключения: новый GameScene только что создался,
        // и его Awake/Start отрабатывают в этом же кадре после LoadSceneAsync.
        // Лучше дать им один кадр, прежде чем стрелять OnSceneLoadCompleted — иначе
        // подписчики могут обратиться к менеджерам на новой сцене, которые ещё не готовы.
        yield return null;

        OnSceneLoadCompleted?.Invoke(this, new SceneLoadEventArgs { sceneName = sceneName });

        IsLoading = false;
    }
}
