using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ★ v1.5 (фича «Полы по зонам»): UI-панель меню полов. Подписывается на события
/// FloorManager: OnFloorPanelOpened → показываем панель и строим строки зон,
/// OnFloorPanelClosed → скрываем, OnZoneSkinsChanged → обновляем тексты строк.
///
/// Структура UI (собирается в Unity, по образцу ShopUI):
/// - корневой GameObject панели с этим компонентом (ОСТАВЬ АКТИВНЫМ в сцене:
///   при hideOnStart=true панель сама скроется в Start; если выключить её в
///   инспекторе — Start не выполнится, подписки на события не случится, и
///   панель никогда не откроется)
/// - дочерний Container (Transform с VerticalLayoutGroup) — куда создаются строки
/// - дочерний RowTemplate (SetActive=false) — префаб строки с FloorZoneRowUI
/// - дочерний CloseButton (Button) — альтернатива клавише F/Escape
/// - дочерний HintText (TMP, опционально) — подсказка «Новые полы — в лавке (B)»
///
/// Блокировку ввода и модальность ведёт FloorManager (как ShopManager для магазина) —
/// здесь только визуальная часть.
/// </summary>
public class FloorDecorUI : MonoBehaviour
{
    [Header("Ссылки на UI элементы")]
    [Tooltip("Контейнер для строк зон. Обычно GameObject с VerticalLayoutGroup")]
    [SerializeField] private Transform rowsContainer;

    [Tooltip("Шаблон строки зоны (FloorZoneRowUI). Должен быть SetActive(false) в сцене")]
    [SerializeField] private FloorZoneRowUI rowTemplate;

    [Tooltip("Кнопка закрытия меню (опционально — F/Escape тоже работают)")]
    [SerializeField] private UnityEngine.UI.Button closeButton;

    [Tooltip("Подсказка внизу панели (опционально). Например: «Новые полы — в лавке (B)»")]
    [SerializeField] private TMPro.TextMeshProUGUI hintText;

    [Tooltip("Если true — панель скрывается при старте. Если false — нужно вручную SetActive(false) в сцене")]
    [SerializeField] private bool hideOnStart = true;

    // Все созданные строки — чистятся при каждой сборке (тот же приём, что в ShopUI).
    private readonly List<FloorZoneRowUI> spawnedRows = new List<FloorZoneRowUI>();

    private void Start()
    {
        if (hideOnStart)
        {
            gameObject.SetActive(false);
        }

        // Шаблон нужен только для Instantiate — держим его скрытым.
        if (rowTemplate != null)
        {
            rowTemplate.gameObject.SetActive(false);
        }

        if (FloorManager.Instance != null)
        {
            FloorManager.Instance.OnFloorPanelOpened += FloorManager_OnFloorPanelOpened;
            FloorManager.Instance.OnFloorPanelClosed += FloorManager_OnFloorPanelClosed;
            FloorManager.Instance.OnZoneSkinsChanged += FloorManager_OnZoneSkinsChanged;
        }

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(() =>
            {
                if (FloorManager.Instance != null)
                {
                    FloorManager.Instance.ClosePanel();
                }
            });
        }
    }

    private void OnDestroy()
    {
        if (FloorManager.Instance != null)
        {
            FloorManager.Instance.OnFloorPanelOpened -= FloorManager_OnFloorPanelOpened;
            FloorManager.Instance.OnFloorPanelClosed -= FloorManager_OnFloorPanelClosed;
            FloorManager.Instance.OnZoneSkinsChanged -= FloorManager_OnZoneSkinsChanged;
        }

        if (closeButton != null)
        {
            closeButton.onClick.RemoveAllListeners();
        }
    }

    // ===== Обработчики событий FloorManager =====

    private void FloorManager_OnFloorPanelOpened(object sender, System.EventArgs e)
    {
        gameObject.SetActive(true);
        BuildRows();
    }

    private void FloorManager_OnFloorPanelClosed(object sender, System.EventArgs e)
    {
        gameObject.SetActive(false);
    }

    private void FloorManager_OnZoneSkinsChanged(object sender, System.EventArgs e)
    {
        // Сменился скин на любой зоне — обновляем тексты всех строк без пересоздания.
        RefreshAllRows();
    }

    // ===== Визуальная часть =====

    /// <summary>
    /// Строит строку на каждую зону FloorManager. Старые строки уничтожаются —
    /// это проще, чем diff, а зон в сцене единицы (тот же подход, что в ShopUI).
    /// </summary>
    private void BuildRows()
    {
        foreach (FloorZoneRowUI row in spawnedRows)
        {
            if (row != null && row != rowTemplate)
            {
                Destroy(row.gameObject);
            }
        }
        spawnedRows.Clear();

        if (FloorManager.Instance == null || rowTemplate == null || rowsContainer == null)
        {
            Debug.LogWarning("[FloorDecorUI] Не все ссылки назначены — FloorManager, rowTemplate или rowsContainer null.");
            return;
        }

        List<FloorManager.FloorZone> zones = FloorManager.Instance.GetZones();
        if (zones == null || zones.Count == 0)
        {
            Debug.LogWarning("[FloorDecorUI] В FloorManager не размечена ни одна зона — заполните список zones в инспекторе.");
            return;
        }

        foreach (FloorManager.FloorZone zone in zones)
        {
            FloorZoneRowUI rowInstance = Instantiate(rowTemplate, rowsContainer);
            rowInstance.gameObject.SetActive(true);
            rowInstance.Setup(FloorManager.Instance, zone);
            spawnedRows.Add(rowInstance);
        }

        Debug.Log($"[FloorDecorUI] Создано строк зон: {spawnedRows.Count}.");
    }

    private void RefreshAllRows()
    {
        foreach (FloorZoneRowUI row in spawnedRows)
        {
            if (row != null)
            {
                row.Refresh();
            }
        }
    }
}
