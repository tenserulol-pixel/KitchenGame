using UnityEngine;

/// <summary>
/// Подсвечивает стул, когда он сейчас является целью для переключателя на E у DiningTable —
/// тем же принципом, что SelectedCounterVisual для обычных прилавков, но здесь нет
/// собственного выделения через рейкаст: у стульев его никогда не было, и заводить
/// отдельный рейкаст ради одной подсветки не нужно — цель и так уже вычисляется в
/// DiningTable через GetNearestFreeChair(), просто раньше это никак не показывалось.
///
/// Расположить на том же объекте, что и Chair (нужен GetComponent<Chair>()), внутри
/// иерархии DiningTable (нужен GetComponentInParent<DiningTable>() — chairs уже
/// становятся дочерними у стола в DiningTable.Awake()).
/// </summary>
public class ChairHighlightVisual : MonoBehaviour
{
    [SerializeField] private GameObject visualGameObject;

    private Chair chair;
    private DiningTable parentTable;

    private void Start()
    {
        // Start(), а не Awake() — важно дождаться, пока DiningTable.Awake() успеет
        // сделать SetParent() для стульев; Unity гарантирует, что все Awake() в сцене
        // отработают раньше любого Start(), поэтому к этому моменту иерархия уже верна.
        chair = GetComponent<Chair>();
        parentTable = GetComponentInParent<DiningTable>();

        // Раньше отсутствие любого из трёх ниже просто молча ничего не подсвечивало —
        // теперь сразу видно в консоли при старте сцены, что именно не настроено и на каком объекте.
        if (chair == null)
        {
            Debug.LogWarning($"[ChairHighlightVisual] На '{name}' нет компонента Chair — " +
                              "повесь ChairHighlightVisual на тот же объект, где висит Chair, а не на дочерний/соседний.");
        }

        if (parentTable == null)
        {
            Debug.LogWarning($"[ChairHighlightVisual] На '{name}' не нашёлся DiningTable среди родителей — " +
                              "объект должен быть где-то внутри иерархии стола (дочерним, на любом уровне).");
        }

        if (visualGameObject == null)
        {
            Debug.LogWarning($"[ChairHighlightVisual] На '{name}' не назначен visualGameObject в инспекторе — " +
                              "подсвечивать нечего, даже если остальное настроено верно.");
        }
    }

    private void Update()
    {
        if (visualGameObject == null || chair == null || parentTable == null) return;

        visualGameObject.SetActive(IsCurrentRemoveTarget());
    }

    private bool IsCurrentRemoveTarget()
    {
        if (GameLoopManager.Instance == null || !GameLoopManager.Instance.IsPreparationActive()) return false;
        if (Player.Instance == null || Player.Instance.HasKitchenObject()) return false;
        if (Player.Instance.GetSelectedCounter() != parentTable) return false;

        return parentTable.IsRemoveTarget(chair, Player.Instance.transform.position);
    }
}
