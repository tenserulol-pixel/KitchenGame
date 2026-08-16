using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Самый дешёвый уровень вариативности планировки — комната (стены, NavMesh) остаётся
/// той же самой каждый забег, но при старте случайно решается, какие из перечисленных
/// прилавков начинают на своих обычных, "спроектированных" местах, а какие — в резерве.
/// Резервные прилавки не спрятаны — они физически стоят в отведённом месте, игрок сам
/// расставляет их в фазе подготовки через уже существующий FurnitureMovingController (T).
///
/// Специально не трогает DiningTable — столы регистрируются отдельно в CustomerManager,
/// смешивать их сюда означало бы городить дополнительную осторожность ради одной фичи.
/// </summary>
public class StartingLayoutRandomizer : MonoBehaviour
{
    [Tooltip("Прилавки, которые МОГУТ уйти в резерв за этот забег — не включай сюда всё подряд, чтобы кухня не осталась совсем пустой")]
    [SerializeField] private List<BaseCounter> randomizableCounters;

    [Tooltip("Сколько из перечисленных прилавков уходит в резерв за один забег")]
    [SerializeField] private int reserveCount = 2;

    [Tooltip("Куда переносятся прилавки, ушедшие в резерв — нужен хотя бы один слот на каждый возможный резервный прилавок")]
    [SerializeField] private List<Transform> reserveSlots;

    private void Start()
    {
        // Start(), а не Awake() — чтобы все BaseCounter.Awake() (регистрация в сетке на
        // родных местах) уже точно отработали к этому моменту, иначе TryPlaceCounter ниже
        // рисковал бы конфликтовать с ещё не завершившейся начальной регистрацией.
        if (randomizableCounters == null || randomizableCounters.Count == 0) return;
        if (reserveSlots == null || reserveSlots.Count == 0) return;

        List<BaseCounter> pool = new List<BaseCounter>(randomizableCounters);

        // Перемешиваем — тот же приём, что уже используется в UpgradeManager.OfferDraft()
        for (int i = 0; i < pool.Count; i++)
        {
            int swapIndex = Random.Range(i, pool.Count);
            (pool[i], pool[swapIndex]) = (pool[swapIndex], pool[i]);
        }

        int actualReserveCount = Mathf.Min(reserveCount, pool.Count, reserveSlots.Count);

        for (int i = 0; i < actualReserveCount; i++)
        {
            MoveToReserve(pool[i], reserveSlots[i]);
        }
    }

    private void MoveToReserve(BaseCounter counter, Transform slot)
    {
        if (counter == null || slot == null) return;

        if (GridPositioningSystem.Instance != null)
        {
            GridPositioningSystem.Instance.TryPlaceCounter(counter, slot.position);
        }
        else
        {
            counter.transform.position = slot.position;
        }

        Debug.Log($"[StartingLayoutRandomizer] '{counter.name}' начинает в резерве — расставь через T в фазе подготовки.");
    }
}
