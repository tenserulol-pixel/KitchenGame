using UnityEngine;

/// <summary>
/// Толкание физических объектов телом (двери на петлях, ящики, табуретки).
/// Вешается на игрока — в тот же объект, где лежит Player.
/// Специально под систему движения KitchenGame: игрок двигается через
/// Physics.CapsuleCast + transform.position (CharacterController НЕТ),
/// поэтому скрипт сам делает капсульный каст по направлению движения
/// с той же геометрией, что HandleMovement в Player.cs, и прикладывает
/// импульс в точку контакта, пока игрок «давит» в объект движением.
/// </summary>
[RequireComponent(typeof(Player))]
public class BodyPusher : MonoBehaviour
{
    [Header("Геометрия капсулы (должна совпадать с Player)")]
    [Tooltip("Должно совпадать с playerRadius в Player.cs")]
    [SerializeField] private float pushRadius = 0.7f;

    [Tooltip("Должно совпадать с playerHeight в Player.cs")]
    [SerializeField] private float pushHeight = 2f;

    [Header("Сила толчка")]
    [Tooltip("Импульс за одно касание. Больше — дверь распахивается резче")]
    [SerializeField] private float pushPower = 8f;

    [Tooltip("Пауза между толчками (сек). Защита от накопления импульса каждый кадр")]
    [SerializeField] private float pushInterval = 0.05f;

    [Tooltip("Запас каста за капсулу (м)")]
    [SerializeField] private float pushProbeDistance = 0.12f;

    [Header("Слои")]
    [Tooltip("Что можно толкать. По умолчанию — всё")]
    [SerializeField] private LayerMask pushableLayers = ~0;

    [Header("Отладка")]
    [SerializeField] private bool debugLogs = false;

    private float lastPushTime = -999f;

    private void Update()
    {
        // Тот же гейт, что и в Player.Update: вне игры и вне подготовки не толкаем
        if (GameLoopManager.Instance != null &&
            !GameLoopManager.Instance.IsGamePlaying() &&
            !GameLoopManager.Instance.IsPreparationActive())
        {
            return;
        }

        if (GameInput.Instance == null) return;

        // Направление, куда игрок идёт — туда и толкаем
        Vector2 inputVector = GameInput.Instance.GetMovementVectorNormalized();
        Vector3 moveDir = new Vector3(inputVector.x, 0f, inputVector.y);
        if (moveDir.sqrMagnitude < 0.001f) return;

        // Капсульный каст с той же геометрией, что в Player.HandleMovement
        RaycastHit hit;
        bool touched = Physics.CapsuleCast(
            transform.position,
            transform.position + Vector3.up * pushHeight,
            pushRadius,
            moveDir,
            out hit,
            pushProbeDistance,
            pushableLayers,
            QueryTriggerInteraction.Ignore
        );

        if (!touched) return;

        PushRigidbody(hit, moveDir);
    }

    private void PushRigidbody(RaycastHit hit, Vector3 moveDir)
    {
        // 1. У того, во что упёрлись, должно быть физическое тело
        Rigidbody body = hit.rigidbody;

        if (body == null || body.isKinematic) return;

        // Своё (иерархия игрока) не толкаем
        if (body.transform == transform || body.transform.IsChildOf(transform)) return;

        // 2. Кулдаун — иначе импульс копится каждый кадр и дверь улетает
        if (Time.time - lastPushTime < pushInterval) return;
        lastPushTime = Time.time;

        // 3. Толкаем по горизонтали туда, куда идём
        Vector3 pushDir = new Vector3(moveDir.x, 0f, moveDir.z).normalized;

        // 4. AddForceAtPosition: сила в точке контакта создаёт крутящий момент —
        //    дверь честно распахивается от того края, куда упёрся игрок
        body.AddForceAtPosition(pushDir * pushPower, hit.point, ForceMode.Impulse);

        if (debugLogs)
            Debug.Log($"[Толчок телом] '{body.name}', точка: {hit.point}, направление: {pushDir}");
    }
}