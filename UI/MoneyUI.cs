using TMPro;
using UnityEngine;

public class MoneyUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI moneyText;

    private void Start()
    {
        UpdateVisual(GameLoopManager.Instance != null ? GameLoopManager.Instance.GetTotalGold() : 0);

        if (GameLoopManager.Instance != null)
        {
            GameLoopManager.Instance.OnGoldChanged += GameLoopManager_OnGoldChanged;
        }
    }

    private void OnDestroy()
    {
        if (GameLoopManager.Instance != null)
        {
            GameLoopManager.Instance.OnGoldChanged -= GameLoopManager_OnGoldChanged;
        }
    }

    private void GameLoopManager_OnGoldChanged(object sender, GameLoopManager.OnGoldChangedEventArgs e)
    {
        UpdateVisual(e.currentTotalGold);
    }

    private void UpdateVisual(int amount)
    {
        moneyText.text = "" + amount;
    }
}
