using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Физическая лужа на полу — появляется в свободной соседней ячейке, когда карта
/// "Нестабильная магия" портит ингредиент на CuttingCounter (см. CuttingCounter.TrySpawnPuddle).
///
/// Наследует BaseCounter не потому что лужа — прилавок, а чтобы бесплатно получить:
///  - регистрацию/снятие с учёта в GridPositioningSystem (Awake/OnDestroy у BaseCounter
///    уже это делают, лужа не сможет появиться поверх другого объекта);
///  - выбор через обычный рейкаст игрока и уже готовый паттерн "держать кнопку — прогресс
///    растёт" (тот же способ, что у резки и мойки), вместо того чтобы изобретать новую
///    систему взаимодействия с нуля.
///
/// Управление: держать F (InteractAlternate) — убирает; E ничего не делает,
/// InteractAlternate тоже не переопределён — прогресс двигается только через
/// SetCleaningState(), вызываемый из Player.Update() каждый кадр, ровно как у
/// CuttingCounter.SetCuttingState()/SinkCounter.SetWashingState().
/// </summary>
public class Puddle : BaseCounter, IHasProgress
{
    // Все существующие лужи — чтобы Player.cs мог каждый кадр проверить, не стоит ли
    // игрок рядом с одной из них, не обходя всю сцену через FindObjectsOfType.
    public static readonly List<Puddle> ActivePuddles = new List<Puddle>();

    public event EventHandler<IHasProgress.OnProgressChangedEventArgs> OnProgressChanged;

    [Header("Уборка (держать F)")]
    [SerializeField] private float cleanTimeMax = 2f;

    [Header("Замедление игрока рядом")]
    [Tooltip("На каком расстоянии от лужи игрок считается 'стоящим в ней'")]
    [SerializeField] private float slowRadius = 0.6f;
    [Tooltip("Множитель скорости игрока внутри радиуса — 0.5 значит вдвое медленнее")]
    [SerializeField] private float slowMultiplier = 0.5f;

    private float cleanProgress = 0f;
    private bool isBeingCleaned = false;

    protected override void Awake()
    {
        // ВАЖНО: обязательно base.Awake() — тот самый баг, что уже ловили на DiningTable:
        // без него BaseCounter.Awake() (регистрация в сетке) молча не выполнится.
        base.Awake();
        ActivePuddles.Add(this);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        ActivePuddles.Remove(this);
    }

    private void Update()
    {
        if (isBeingCleaned)
        {
            cleanProgress += Time.deltaTime;

            OnProgressChanged?.Invoke(this, new IHasProgress.OnProgressChangedEventArgs
            {
                progressNormalized = cleanProgress / cleanTimeMax
            });

            if (cleanProgress >= cleanTimeMax)
            {
                Destroy(gameObject); // OnDestroy сам освободит ячейку и уберёт из ActivePuddles
            }
        }
    }

    /// <summary>
    /// Вызывается из Player.Update() каждый кадр, пока зажата альтернативная кнопка (F) —
    /// тем же способом, что уже используют CuttingCounter/SinkCounter для своих действий.
    /// </summary>
    public void SetCleaningState(bool isHeld)
    {
        isBeingCleaned = isHeld;
    }

    public float GetSlowRadius() => slowRadius;
    public float GetSlowMultiplier() => slowMultiplier;
}
