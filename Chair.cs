using UnityEngine;

public class Chair : MonoBehaviour
{
    private CustomerAI currentCustomer;

    public bool IsEmpty()
    {
        return currentCustomer == null;
    }

    public bool IsOccupied()
    {
        return currentCustomer != null;
    }

    public void SetCustomer(CustomerAI customer)
    {
        currentCustomer = customer;
    }

    public void ClearCustomer()
    {
        currentCustomer = null;
    }

    public CustomerAI GetCustomer()
    {
        return currentCustomer;
    }

    public Vector3 GetPosition()
    {
        return transform.position;
    }
}