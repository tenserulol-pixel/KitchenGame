using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class BaseCounter : MonoBehaviour, IKitchenObjectParent
{
    [SerializeField] private Transform counterTopPoint;

    private KitchenObject kitchenObject;

    protected virtual void Awake()
    {
        // Каждому столу при рождении/старте игры даем команду выровняться по сетке и зарегистрироваться.
        // Это сработает автоматически для всех наследников (StoveCounter, CuttingCounter, DiningTable и т.д.)
        if (GridPositioningSystem.Instance != null)
        {
            GridPositioningSystem.Instance.RegisterCounterAtCurrentPosition(this);
        }
    }

    public virtual void InteractAlternate(Player player)
    {

    }
    public virtual void Interact(Player player)
    {

    }
    public Transform GetKitchenObjectFollowTransform()
    {
        return counterTopPoint;
    }
    
    public void SetKitchenObject(KitchenObject kitchenObject)
    {
        this.kitchenObject = kitchenObject;
    }

    public KitchenObject GetKitchenObject()
    {
        return this.kitchenObject;
    }

    public void ClearKitchenObject()
    {
        kitchenObject = null;
    }

    public bool HasKitchenObject()
    {
        return kitchenObject != null;
    }
}