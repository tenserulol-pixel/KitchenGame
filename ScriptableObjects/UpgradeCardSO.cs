using UnityEngine;

// Небольшой, сознательно ограниченный набор эффектов для первой версии — все три
// трогают простое поле на Player/GameLoopManager, а не общий ScriptableObject-рецепт.
// Скорость резки/варки (CuttingRecipeSO/FryingRecipeSO) сюда пока не входит —
// это отдельная задача, поскольку эти значения общие на все счётчики сразу.
public enum UpgradeEffectType
{
    MoveSpeed,
    InteractDistance,
    AngryCustomerTolerance,
}

[CreateAssetMenu]
public class UpgradeCardSO : ScriptableObject
{
    public string cardName;
    [TextArea] public string description;
    public UpgradeEffectType effectType;
    public float value;
}
