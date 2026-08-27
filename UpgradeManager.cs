using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Раз в день (кроме первого — OnDayChanged стреляет со 2-го) предлагает cardsPerDraft
/// случайных, ещё не полученных карт улучшений. Пока нет отдельного UI под выбор —
/// предложенные карты пишутся в Debug.Log, а взять карту можно клавишами 1/2/3
/// (по порядку, как показаны в логе), пока фаза подготовки активна.
///
/// Эффекты применяются через центральный switch в ApplyEffect() — по образцу
/// "enum + применятор", который обсуждали. Осознанно просто на старте: три эффекта,
/// каждый трогает одно простое поле на Player/GameLoopManager.
/// </summary>
public class UpgradeManager : MonoBehaviour
{
    public static UpgradeManager Instance { get; private set; }

    [SerializeField] private UpgradeCardListSO upgradeCardListSO;
    [SerializeField] private int cardsPerDraft = 3;

    private readonly List<UpgradeCardSO> ownedCards = new List<UpgradeCardSO>();
    private readonly List<UpgradeCardSO> currentOffer = new List<UpgradeCardSO>();

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (GameLoopManager.Instance != null)
        {
            GameLoopManager.Instance.OnDayChanged += GameLoopManager_OnDayChanged;
        }
    }

    private void OnDestroy()
    {
        if (GameLoopManager.Instance != null)
        {
            GameLoopManager.Instance.OnDayChanged -= GameLoopManager_OnDayChanged;
        }
    }

    private void GameLoopManager_OnDayChanged(object sender, EventArgs e)
    {
        OfferDraft();
    }

    private void Update()
    {
        // Пока нет UI-выбора — временный ввод цифрами, только пока есть что предложить
        // и мы в фазе подготовки (та же гейтовка, что и у FurnitureMovingController).
        if (currentOffer.Count == 0) return;
        if (GameLoopManager.Instance == null || !GameLoopManager.Instance.IsPreparationActive()) return;

        for (int i = 0; i < currentOffer.Count && i < 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                PickCard(i);
                break;
            }
        }
    }

    private void OfferDraft()
    {
        currentOffer.Clear();

        if (upgradeCardListSO == null || upgradeCardListSO.upgradeCardSOList == null) return;

        List<UpgradeCardSO> pool = new List<UpgradeCardSO>();
        foreach (UpgradeCardSO card in upgradeCardListSO.upgradeCardSOList)
        {
            if (card != null && !ownedCards.Contains(card))
            {
                pool.Add(card);
            }
        }

        // Перемешиваем весь доступный пул и берём первые cardsPerDraft — так карты
        // внутри одного предложения гарантированно не повторяются.
        for (int i = 0; i < pool.Count; i++)
        {
            int swapIndex = UnityEngine.Random.Range(i, pool.Count);
            (pool[i], pool[swapIndex]) = (pool[swapIndex], pool[i]);
        }

        int offerCount = Mathf.Min(cardsPerDraft, pool.Count);
        for (int i = 0; i < offerCount; i++)
        {
            currentOffer.Add(pool[i]);
        }

        if (currentOffer.Count == 0)
        {
            Debug.Log("[UpgradeManager] Свободных карт для предложения больше нет.");
            return;
        }

        string log = "[UpgradeManager] Новые карты дня:\n";
        for (int i = 0; i < currentOffer.Count; i++)
        {
            log += $"  {i + 1}) {currentOffer[i].cardName} — {currentOffer[i].description}\n";
        }
        log += "Выбери клавишей с соответствующей цифрой, пока идёт подготовка.";
        Debug.Log(log);
    }

    private void PickCard(int index)
    {
        if (index < 0 || index >= currentOffer.Count) return;

        UpgradeCardSO picked = currentOffer[index];
        ApplyEffect(picked);
        ownedCards.Add(picked);
        currentOffer.Clear();

        Debug.Log($"[UpgradeManager] Взята карта: {picked.cardName}.");
    }

    private void ApplyEffect(UpgradeCardSO card)
    {
        switch (card.effectType)
        {
            case UpgradeEffectType.MoveSpeed:
                if (Player.Instance != null) Player.Instance.IncreaseMoveSpeed(card.value);
                break;

            case UpgradeEffectType.InteractDistance:
                if (Player.Instance != null) Player.Instance.IncreaseInteractDistance(card.value);
                break;

            case UpgradeEffectType.AngryCustomerTolerance:
                if (GameLoopManager.Instance != null)
                    GameLoopManager.Instance.IncreaseAngryCustomerTolerance(Mathf.RoundToInt(card.value));
                break;

            case UpgradeEffectType.UnstableMagic:
                if (Player.Instance != null)
                {
                    Player.Instance.IncreaseCuttingSpeedMultiplier(card.value);
                    Player.Instance.IncreaseCuttingRuinChance(card.secondaryValue);
                }
                break;

            case UpgradeEffectType.PenaltyReduction:
                if (GameLoopManager.Instance != null)
                    GameLoopManager.Instance.ReducePenaltyPerOrder(Mathf.RoundToInt(card.value));
                break;

            case UpgradeEffectType.BonusGold:
                if (GameLoopManager.Instance != null)
                    GameLoopManager.Instance.AddBonusGold(Mathf.RoundToInt(card.value));
                break;

            case UpgradeEffectType.SlowerQuotaGrowth:
                if (CustomerManager.Instance != null)
                    CustomerManager.Instance.ReduceDailyGroupTargetGrowth(Mathf.RoundToInt(card.value));
                break;

            case UpgradeEffectType.DarkDeal:
                if (Player.Instance != null) Player.Instance.IncreaseRecipeCostMultiplier(card.value);
                if (GameLoopManager.Instance != null)
                    GameLoopManager.Instance.IncreaseAngryCustomerTolerance(Mathf.RoundToInt(card.secondaryValue));
                break;

            case UpgradeEffectType.BiggerRarerGroups:
                if (CustomerManager.Instance != null)
                {
                    CustomerManager.Instance.IncreaseBaseMaxGroupSize(Mathf.RoundToInt(card.value));
                    CustomerManager.Instance.DecreaseBaseDailyGroupTarget(Mathf.RoundToInt(card.secondaryValue));
                }
                break;

            case UpgradeEffectType.RushHour:
                if (CustomerManager.Instance != null)
                {
                    CustomerManager.Instance.DecreaseBaseSpawnInterval(card.value);
                    CustomerManager.Instance.DecreaseBaseMaxCustomers(Mathf.RoundToInt(card.secondaryValue));
                }
                break;
        }
    }

    public List<UpgradeCardSO> GetOwnedCards() => ownedCards;
    public List<UpgradeCardSO> GetCurrentOffer() => currentOffer;
}
