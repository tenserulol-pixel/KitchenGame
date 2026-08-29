using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// Управляет главным меню: показ/скрытие панелей, реакция на кнопки,
/// проверка наличия сохранения для активации кнопки "Продолжить".
///
/// Логика кнопок:
/// - "Новая игра": очищает сохранение через SaveManager и грузит GameScene.
///   Если у игрока было сохранение — спрашивает подтверждение через диалог.
/// - "Продолжить": просто грузит GameScene. SaveManager сохранит состояние в GameScene
///   через GameLoopManager при первом сохранении между днями.
/// - "Настройки": открывает панель настроек (пока что заглушка — реальные настройки
///   сделаем на P3). Escape возвращает в главное меню.
/// - "Выход": спрашивает подтверждение через диалог, затем Application.Quit().
///
/// Не persistent: создаётся в MainMenuScene, уничтожается при переходе в GameScene.
/// </summary>
public class MainMenuManager : MonoBehaviour
{
    [Header("Кнопки главного меню")]
    [SerializeField] private Button newGameButton;
    [SerializeField] private Button continueButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button quitButton;

    [Header("Панели UI")]
    [Tooltip("Корневая панель главного меню — то, что видно при старте сцены.")]
    [SerializeField] private GameObject mainMenuPanel;
    [Tooltip("Панель настроек — открывается при нажатии на кнопку Settings.")]
    [SerializeField] private GameObject settingsPanel;
    [Tooltip("Диалог подтверждения (универсальный: для новой игры поверх сохранения и для выхода).")]
    [SerializeField] private ConfirmationDialog confirmationDialog;

    [Header("Тексты диалогов")]
    [SerializeField] private string newGameOverwriteTitle = "Начать новую игру?";
    [SerializeField] private string newGameOverwriteMessage =
        "У вас есть несохранённый прогресс. Начать новую игру — это удалит текущее сохранение. Продолжить?";
    [SerializeField] private string quitConfirmationTitle = "Выйти из игры?";
    [SerializeField] private string quitConfirmationMessage = "Вы уверены?";

    /// <summary>
    /// True, пока показан диалог подтверждения. На это время кнопки главного меню
    /// блокируются — иначе можно нажать несколько кнопок подряд и запустить несколько
    /// корутин перехода между сценами, что приведёт к гонкам состояний.
    /// </summary>
    private bool isDialogActive = false;

    private void Start()
    {
        // Включаем стартовую панель — главное меню.
        ShowMainMenuPanel();

        // Continue активен только если есть сохранение.
        RefreshContinueButtonInteractable();

        // Подписка на изменения сохранения — если по какой-то причине сохранение
        // изменилось, пока меню открыто (маловероятно, но возможно при асинхронных операциях),
        // обновим состояние кнопки.
        if (SaveManager.Instance != null)
        {
            SaveManager.Instance.OnSaveChanged += SaveManager_OnSaveChanged;
        }

        // Подписываемся на кнопки.
        if (newGameButton != null)
        {
            newGameButton.onClick.AddListener(OnNewGameClicked);
        }
        if (continueButton != null)
        {
            continueButton.onClick.AddListener(OnContinueClicked);
        }
        if (settingsButton != null)
        {
            settingsButton.onClick.AddListener(OnSettingsClicked);
        }
        if (quitButton != null)
        {
            quitButton.onClick.AddListener(OnQuitClicked);
        }

        // Если диалог подтверждения есть — настраиваем его на блокировку ввода в меню.
        if (confirmationDialog != null)
        {
            confirmationDialog.OnConfirmed += HandleDialogConfirmed;
            confirmationDialog.OnCancelled += HandleDialogCancelled;
            confirmationDialog.gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (SaveManager.Instance != null)
        {
            SaveManager.Instance.OnSaveChanged -= SaveManager_OnSaveChanged;
        }

        // Отписываемся от кнопок — UnityButton.onClick сохраняет ссылки делегатов,
        // и если MainMenuManager уничтожен, а кнопки остались (что невозможно при
        // выгрузке сцены, но всё же), подписки повисли бы в воздухе.
        if (newGameButton != null) newGameButton.onClick.RemoveListener(OnNewGameClicked);
        if (continueButton != null) continueButton.onClick.RemoveListener(OnContinueClicked);
        if (settingsButton != null) settingsButton.onClick.RemoveListener(OnSettingsClicked);
        if (quitButton != null) quitButton.onClick.RemoveListener(OnQuitClicked);

        if (confirmationDialog != null)
        {
            confirmationDialog.OnConfirmed -= HandleDialogConfirmed;
            confirmationDialog.OnCancelled -= HandleDialogCancelled;
        }
    }

    private void Update()
    {
        // Escape закрывает открытую панель или выходит из игры.
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (isDialogActive)
            {
                // Диалог сам обработает Escape внутри себя.
                return;
            }

            if (settingsPanel != null && settingsPanel.activeSelf)
            {
                ShowMainMenuPanel();
                return;
            }

            // Если ни одна вложенная панель не открыта — это попытка выйти.
            OnQuitClicked();
        }
    }

    // ===== Обработчики кнопок =====

    private void OnNewGameClicked()
    {
        if (isDialogActive) return;

        // Если есть сохранение — спрашиваем подтверждение перед затиранием.
        if (SaveManager.Instance != null && SaveManager.Instance.HasSave())
        {
            ShowConfirmationDialog(
                newGameOverwriteTitle,
                newGameOverwriteMessage,
                confirmAction: () =>
                {
                    SaveManager.Instance.ClearSave();
                    LoadGameScene();
                });
            return;
        }

        // Сохранения нет — просто стартуем.
        LoadGameScene();
    }

    private void OnContinueClicked()
    {
        if (isDialogActive) return;

        if (SaveManager.Instance == null || !SaveManager.Instance.HasSave())
        {
            // Кнопка должна быть неинтерактивной, но на всякий случай.
            Debug.LogWarning("[MainMenuManager] Continue нажат, но сохранения нет.");
            return;
        }

        LoadGameScene();
    }

    private void OnSettingsClicked()
    {
        if (isDialogActive) return;
        ShowSettingsPanel();
    }

    private void OnQuitClicked()
    {
        if (isDialogActive) return;

        ShowConfirmationDialog(
            quitConfirmationTitle,
            quitConfirmationMessage,
            confirmAction: () =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            });
    }

    // ===== Внутренние методы =====

    private void LoadGameScene()
    {
        if (SceneLoader.Instance == null)
        {
            Debug.LogError("[MainMenuManager] SceneLoader не найден — BootStrapper не отработал?");
            return;
        }

        SceneLoader.Instance.LoadGameScene(showLoadingScreen: true);
    }

    /// <summary>
    /// Публичная обёртка над ShowMainMenuPanel — нужна для назначения на OnClick кнопки
    /// "Назад" в Settings панели через инспектор. Unity OnClick не видит приватные методы.
    /// </summary>
    public void BackToMainMenuFromSettings()
    {
        if (isDialogActive) return;
        ShowMainMenuPanel();
    }

    private void ShowMainMenuPanel()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        if (settingsPanel != null) settingsPanel.SetActive(false);
    }

    private void ShowSettingsPanel()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(true);
    }

    private void RefreshContinueButtonInteractable()
    {
        if (continueButton == null) return;

        bool hasSave = SaveManager.Instance != null && SaveManager.Instance.HasSave();
        continueButton.interactable = hasSave;
    }

    private void SaveManager_OnSaveChanged(object sender, System.EventArgs e)
    {
        RefreshContinueButtonInteractable();
    }

    // ===== Диалог подтверждения =====

    private void ShowConfirmationDialog(string title, string message, System.Action confirmAction)
    {
        if (confirmationDialog == null)
        {
            // Диалог не настроен — выполняем подтверждение сразу. Это не идеально
            // (пользователь не успеет передумать), но лучше, чем блокировать игру.
            Debug.LogWarning("[MainMenuManager] ConfirmationDialog не назначен — подтверждаю сразу.");
            confirmAction?.Invoke();
            return;
        }

        isDialogActive = true;
        confirmationDialog.Show(title, message, confirmAction);
    }

    private void HandleDialogConfirmed(object sender, System.EventArgs e)
    {
        isDialogActive = false;
        // Сам confirmAction выполняется внутри диалога в момент нажатия "ОК" —
        // здесь только снимаем флаг блокировки.
    }

    private void HandleDialogCancelled(object sender, System.EventArgs e)
    {
        isDialogActive = false;
    }
}