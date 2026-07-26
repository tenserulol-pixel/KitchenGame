using TMPro;
using UnityEngine;

public class MoneyUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI moneyText;

    private void Start()
    {
        UpdateVisual();

        MoneyManager.Instance.OnMoneyChanged += MoneyChanged;
    }

    private void OnDestroy()
    {
        MoneyManager.Instance.OnMoneyChanged -= MoneyChanged;
    }

    private void MoneyChanged(object sender, System.EventArgs e)
    {
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        moneyText.text = "" + MoneyManager.Instance.GetMoney();
    }
}