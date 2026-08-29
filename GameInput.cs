using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Persistent-обёртка над Input System.
///
/// Persistent: создаётся в BootScene через BootStrapper, DontDestroyOnLoad.
/// Это значит, что подписки Player.cs на OnInteractAction не теряются при переходе
/// MainMenu → GameScene (Player создаётся заново в GameScene, в его Start подписка
/// снова срабатывает, и всё работает).
///
/// Action Maps: сейчас включён только "Player", потому что UI в главном меню использует
/// стандартную EventSystem (Standalone Input Module), а не наш GameInput. Когда в меню
/// понадобится навигация с геймпада через GameInput — добавить Action Map "UI" и
/// переключаться через EnableUIInput() / EnablePlayerInput().
/// </summary>
public class GameInput : MonoBehaviour
{
    public static GameInput Instance { get; private set; }

    public event EventHandler OnInteractAction;
    public event EventHandler OnInteractAlternateAction;

    private PlayerInput playerInput;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        playerInput = new PlayerInput();
        playerInput.Player.Enable();
        playerInput.Player.Interact.performed += Interact_performed;
        playerInput.Player.InteractAlternate.performed += InteractAlternate_performed;
    }

    private void OnDestroy()
    {
        // На всякий случай отписываемся — если когда-нибудь GameInput будет уничтожаться
        // не через Destroy(gameObject) другого дубля, а через выгрузку сцены.
        if (playerInput != null)
        {
            playerInput.Player.Interact.performed -= Interact_performed;
            playerInput.Player.InteractAlternate.performed -= InteractAlternate_performed;
            playerInput.Dispose();
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void InteractAlternate_performed(UnityEngine.InputSystem.InputAction.CallbackContext obj)
    {
        OnInteractAlternateAction?.Invoke(this, EventArgs.Empty);
    }

    private void Interact_performed(UnityEngine.InputSystem.InputAction.CallbackContext obj)
    {
        OnInteractAction?.Invoke(this, EventArgs.Empty);
    }

    public Vector2 GetMovementVectorNormalized()
    {
        if (playerInput == null) return Vector2.zero;
        Vector2 inputVector = playerInput.Player.Move.ReadValue<Vector2>();
        inputVector = inputVector.normalized;
        return inputVector;
    }

    // ВАЖНО: Этот метод проверяет, зажата ли кнопка в данный кадр
    public bool IsInteractAlternatePressed()
    {
        if (playerInput != null && playerInput.Player.InteractAlternate != null)
        {
            return playerInput.Player.InteractAlternate.IsPressed();
        }
        return false;
    }

    /// <summary>
    /// Включает Action Map "Player" — управление в игре (WASD, E, F).
    /// Вызывается при загрузке GameScene.
    /// </summary>
    public void EnablePlayerInput()
    {
        if (playerInput != null)
        {
            playerInput.Player.Enable();
        }
    }

    /// <summary>
    /// Выключает Action Map "Player" — например, в главном меню или при открытии паузы.
    /// </summary>
    public void DisablePlayerInput()
    {
        if (playerInput != null)
        {
            playerInput.Player.Disable();
        }
    }
}
