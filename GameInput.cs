using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

public class GameInput : MonoBehaviour
{
    // Добавляем Instance, чтобы Player мог легко обращаться к вводу
    public static GameInput Instance { get; private set; }

    public event EventHandler OnInteractAction;
    public event EventHandler OnInteractAlternateAction;
    private PlayerInput playerInput;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        playerInput = new PlayerInput();
        playerInput.Player.Enable();
        playerInput.Player.Interact.performed += Interact_performed;
        playerInput.Player.InteractAlternate.performed += InteractAlternate_performed;
    }

    private void InteractAlternate_performed(UnityEngine.InputSystem.InputAction.CallbackContext obj) {
        OnInteractAlternateAction?.Invoke(this, EventArgs.Empty);
    }

    private void Interact_performed(UnityEngine.InputSystem.InputAction.CallbackContext obj) {
        if(OnInteractAction != null) {
            OnInteractAction(this, EventArgs.Empty);
        }
    }

    public Vector2 GetMovementVectorNormalized() {
        Vector2 inputVector = playerInput.Player.Move.ReadValue<Vector2>();   
        inputVector = inputVector.normalized;
        return inputVector;
    }

    // ВАЖНО: Этот метод проверяет, зажата ли кнопка в данный кадр
    public bool IsInteractAlternatePressed() {
        if (playerInput != null && playerInput.Player.InteractAlternate != null) {
            return playerInput.Player.InteractAlternate.IsPressed();
        }
        return false;
    }
}