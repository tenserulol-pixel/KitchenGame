using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Универсальный диалог подтверждения. Используется в MainMenuManager для подтверждения
/// "Новой игры" поверх сохранения и для подтверждения выхода из игры.
///
/// Шаблон использования:
/// <code>
/// dialog.Show("Заголовок", "Сообщение?", () => { /* что делать при ОК */ });
/// </code>
///
/// События OnConfirmed / OnCancelled позволяют вызывающему коду узнать, чем закончился
/// диалог — мы используем это, чтобы снять блокировку ввода на родительской панели.
///
/// UI должен быть устроен так:
/// - корневой GameObject с этим компонентом
/// - дочерние TMP_Text: titleLabel, messageLabel
/// - дочерние Button: confirmButton, cancelButton
/// - фоновый Image (затемнение) — кнопка с невидимым Image во весь экран, чтобы клики
///   мимо диалога считались отменой
///
/// По умолчанию диалог скрыт (SetActive(false) в Start), показывается через Show().
/// </summary>
public class ConfirmationDialog : MonoBehaviour
{
    public event EventHandler OnConfirmed;
    public event EventHandler OnCancelled;

    [Header("UI элементы")]
    [SerializeField] private TextMeshProUGUI titleLabel;
    [SerializeField] private TextMeshProUGUI messageLabel;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;
    [Tooltip("Кнопка/Panel во весь экран позади диалога — клик по ней = отмена. Может быть null.")]
    [SerializeField] private Button backgroundButton;

    /// <summary>
    /// Действие, которое выполнится при подтверждении. Сохраняем как поле, чтобы
    /// передать лямбду из вызывающего кода в обработчик кнопки.
    /// </summary>
    private Action pendingConfirmAction;

    private void Awake()
    {
        // Подписки на кнопки — один раз, в Awake. Show/Hide только меняет видимость.
        if (confirmButton != null)
        {
            confirmButton.onClick.AddListener(OnConfirmClicked);
        }
        if (cancelButton != null)
        {
            cancelButton.onClick.AddListener(OnCancelClicked);
        }
        if (backgroundButton != null)
        {
            backgroundButton.onClick.AddListener(OnCancelClicked);
        }
    }

    private void OnDestroy()
    {
        if (confirmButton != null) confirmButton.onClick.RemoveListener(OnConfirmClicked);
        if (cancelButton != null) cancelButton.onClick.RemoveListener(OnCancelClicked);
        if (backgroundButton != null) backgroundButton.onClick.RemoveListener(OnCancelClicked);
    }

    /// <summary>
    /// Показывает диалог с заданным заголовком и сообщением. confirmAction выполнится,
    /// если пользователь нажмёт "ОК" (или Enter). При отмене (Esc / кнопка "Отмена" /
    /// клик мимо) — действие не выполняется, стреляет OnCancelled.
    /// </summary>
    public void Show(string title, string message, Action confirmAction)
    {
        if (titleLabel != null)
        {
            titleLabel.text = title;
        }

        if (messageLabel != null)
        {
            messageLabel.text = message;
        }

        pendingConfirmAction = confirmAction;

        gameObject.SetActive(true);

        // Фокусируем кнопку подтверждения, чтобы Enter работал сразу.
        if (confirmButton != null)
        {
            confirmButton.Select();
        }
    }

    /// <summary>
    /// Скрывает диалог без выполнения действия. Не стреляет OnCancelled — это сделано
    /// для случая, когда внешний код сам хочет скрыть диалог (например, при переходе
    /// на другую сцену). Для пользовательской отмены используется OnCancelClicked.
    /// </summary>
    public void Hide()
    {
        pendingConfirmAction = null;
        gameObject.SetActive(false);
    }

    private void Update()
    {
        // Enter = подтвердить, Escape = отменить. Button.Select() выше гарантирует,
        // что кнопка получает клавиатурный ввод, но дублируем на случай, если фокус
        // потерян (например, игрок кликнул мимо кнопки — фокус ушёл в Canvas).
        if (gameObject.activeSelf)
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                OnConfirmClicked();
            }
            else if (Input.GetKeyDown(KeyCode.Escape))
            {
                OnCancelClicked();
            }
        }
    }

    private void OnConfirmClicked()
    {
        Action action = pendingConfirmAction;
        pendingConfirmAction = null;

        gameObject.SetActive(false);

        action?.Invoke();
        OnConfirmed?.Invoke(this, EventArgs.Empty);
    }

    private void OnCancelClicked()
    {
        pendingConfirmAction = null;

        gameObject.SetActive(false);

        OnCancelled?.Invoke(this, EventArgs.Empty);
    }
}
