using UnityEngine;
using System;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "Brew_", menuName = "KitchenGame/Frying Recipe SO")]
public class FryingRecipeSO : ScriptableObject
{
    [Header("Ингредиенты (вход)")]
    [Tooltip("Список ингредиентов, которые нужно положить в котёл для этого рецепта. " +
             "Например, для грибного супа: [Грибы, Коренья]. Каждый ингредиент кладётся игроком через E.")]
    public List<KitchenObjectSO> inputs = new List<KitchenObjectSO>();

    [Header("Результат (выход)")]
    public KitchenObjectSO output;

    [Header("Перемешивание (этап 1)")]
    [Tooltip("Сколько секунд нужно держать клавишу F (InteractAlternate) для перемешивания. " +
             "Например, 2.0 = 2 секунды активного перемешивания.")]
    public float stirringRequired = 2f;

    [Header("Варка (этап 2, автоматическая)")]
    [Tooltip("Сколько секунд котёл варит блюдо ПОСЛЕ завершения перемешивания. " +
             "Игрок может отойти и заниматься другими делами — варка идёт сама. " +
             "Если 0 — после перемешивания блюдо сразу готово (без варки).")]
    public float brewingTimerMax = 3f;

    [Header("Риск сжигания (после варки)")]
    [Tooltip("Сколько секунд после завершения варки у игрока есть, чтобы забрать блюдо. " +
             "Если 0 — сжигание отключено для этого рецепта. Если > 0 — по истечении блюдо сгорит.")]
    public float burningTimerMax = 0f;
}
