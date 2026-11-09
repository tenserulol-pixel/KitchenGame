using UnityEngine;

/// <summary>
/// ★ v1.5 (фича «Полы по зонам»): одна строка меню полов — имя зоны, текущий
/// скин и кнопки «‹» / «›» для листания купленных скинов (включая «По умолчанию»).
///
/// Создаётся FloorDecorUI'ем инстанцированием шаблона. Кнопки подключаются
/// к FloorManager.CycleZoneSkin(zoneId, ±1) кодом, в инспекторе связывать ничего
/// не нужно — только назначить ссылки на тексты и кнопки в шаблоне.
///
/// Отдельный файл, потому что компонент вешается на префаб строки
/// (у Unity имя MonoBehaviour-файла должно совпадать с именем класса).
/// </summary>
public class FloorZoneRowUI : MonoBehaviour
{
    [Header("Ссылки на элементы строки")]
    [Tooltip("Текст с названием зоны. Например: «Кухня»")]
    [SerializeField] private TMPro.TextMeshProUGUI zoneNameText;

    [Tooltip("Текст с названием текущего скина. Например: «Обсидиановые плиты»")]
    [SerializeField] private TMPro.TextMeshProUGUI skinNameText;

    [Tooltip("Кнопка «предыдущий скин»")]
    [SerializeField] private UnityEngine.UI.Button prevButton;

    [Tooltip("Кнопка «следующий скин»")]
    [SerializeField] private UnityEngine.UI.Button nextButton;

    [Tooltip("Иконка текущего скина (опционально). Для «По умолчанию» показывается пусто")]
    [SerializeField] private UnityEngine.UI.Image skinIconImage;

    private FloorManager manager;
    private FloorManager.FloorZone zone;

    /// <summary>
    /// Настройка строки после инстанцирования: запоминаем менеджера и зону,
    /// подключаем кнопки, заполняем тексты. Кнопки перематываются listeners каждый раз,
    /// потому что шаблон переиспользуется, а зона у каждого инстанса своя.
    /// </summary>
    public void Setup(FloorManager floorManager, FloorManager.FloorZone floorZone)
    {
        manager = floorManager;
        zone = floorZone;

        if (prevButton != null)
        {
            prevButton.onClick.RemoveAllListeners();
            prevButton.onClick.AddListener(() =>
            {
                if (manager != null && zone != null)
                {
                    manager.CycleZoneSkin(zone.zoneId, -1);
                }
            });
        }

        if (nextButton != null)
        {
            nextButton.onClick.RemoveAllListeners();
            nextButton.onClick.AddListener(() =>
            {
                if (manager != null && zone != null)
                {
                    manager.CycleZoneSkin(zone.zoneId, +1);
                }
            });
        }

        Refresh();
    }

    /// <summary>
    /// Обновляет тексты строки под текущее состояние зоны. Вызывается из Setup
    /// и из FloorDecorUI при OnZoneSkinsChanged (неважно, какая зона сменилась —
    /// строк единицы, обновляем все).
    /// </summary>
    public void Refresh()
    {
        if (manager == null || zone == null) return;

        if (zoneNameText != null)
        {
            zoneNameText.text = string.IsNullOrEmpty(zone.displayName) ? zone.zoneId : zone.displayName;
        }

        if (skinNameText != null)
        {
            skinNameText.text = manager.GetSkinDisplayName(zone.zoneId);
        }

        if (skinIconImage != null)
        {
            Sprite icon = manager.GetSkinIcon(zone.zoneId);
            skinIconImage.sprite = icon;
            skinIconImage.enabled = icon != null;
        }
    }
}
