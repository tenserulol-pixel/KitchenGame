using System;
using UnityEngine;

public class MoneyManager : MonoBehaviour
{
    public static MoneyManager Instance { get; private set; }

    public event EventHandler OnMoneyChanged;

    [SerializeField] private int startingMoney = 100;

    private int money;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        money = startingMoney;
    }

    public int GetMoney()
    {
        return money;
    }

    public void AddMoney(int amount)
    {
        money += amount;

        Debug.Log($"Получено: {amount}$");

        OnMoneyChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool SpendMoney(int amount)
    {
        if (money < amount)
        {
            return false;
        }

        money -= amount;

        Debug.Log($"Потрачено: {amount}$");

        OnMoneyChanged?.Invoke(this, EventArgs.Empty);

        return true;
    }

    public bool HasEnoughMoney(int amount)
    {
        return money >= amount;
    }
}