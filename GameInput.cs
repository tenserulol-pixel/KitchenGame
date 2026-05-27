using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
public class GameInput : MonoBehaviour
{
    public event EventHandler OnInteractAction;
    public event EventHandler OnInteractAlternateAction;
    private PlayerInput playerInput;
    private void Awake(){
        playerInput =new PlayerInput();
        playerInput.Player.Enable();
        playerInput.Player.Interact.performed+=Interact_performed;
        playerInput.Player.InteractAlternate.performed+=InteractAlternate_performed;

    }
    private void InteractAlternate_performed(UnityEngine.InputSystem.InputAction.CallbackContext obj){
        OnInteractAlternateAction?.Invoke(this,EventArgs.Empty);
    }
    private void Interact_performed(UnityEngine.InputSystem.InputAction.CallbackContext obj){
        if(OnInteractAction!=null){
         OnInteractAction(this,EventArgs.Empty);
         }
    }
    public Vector2 GetMovementVectorNormalized(){

        Vector2 inputVector=playerInput.Player.Move.ReadValue<Vector2>();   
        inputVector =inputVector.normalized;
        return inputVector;
    }
}