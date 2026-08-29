using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Persistent-менеджер сохранений. Единственный, кто имеет право читать/писать
/// сохранение игры. Все остальные системы ходят к нему с запросами: "дай текущее
/// сохранение", "сохрани вот это состояние", "есть ли сохранение вообще".
///
/// Persistent: создаётся в BootScene через BootStrapper, DontDestroyOnLoad.
///
/// Формат хранения: JSON в PlayerPrefs. Это простейший способ, который переживает
/// перезапуск Unity-приложения. Если в будущем понадобится несколько слотов сохранений
/// или большие объёмы данных — стоит перейти на файлы в Application.persistentDataPath.
/// Сейчас же PlayerPrefs достаточно, и API у него простой.
/// </summary>
public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    private const string SaveKey = "kitchen_game_save_v1";

    /// <summary>
    /// Текущее сохранение в памяти. Читается из PlayerPrefs в Awake, обновляется
    /// через Save(). Если null — сохранения нет, "Continue" в главном меню должен
    /// быть неактивен.
    /// </summary>
    private GameSaveData currentSave;

    public event EventHandler OnSaveChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        LoadSaveFromDisk();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>True, если есть сохранение, которое можно загрузить.</summary>
    public bool HasSave() => currentSave != null;

    /// <summary>Возвращает текущее сохранение (null, если его нет).</summary>
    public GameSaveData GetCurrentSave() => currentSave;

    /// <summary>
    /// Сохраняет переданное состояние в память и в PlayerPrefs.
    /// Не вызывает автоматического OnSaveChanged для самой сцены, которая данные
    /// предоставила — обычно эта сцена уже в согласованном состоянии. Если нужна
    /// подписка — вызывающий сам стреляет OnSaveChanged через NotifySaveChanged().
    /// </summary>
    public void Save(GameSaveData data)
    {
        currentSave = data;
        string json = JsonUtility.ToJson(data, prettyPrint: true);

        try
        {
            PlayerPrefs.SetString(SaveKey, json);
            PlayerPrefs.Save();
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveManager] Не удалось записать сохранение в PlayerPrefs: {e.Message}");
            return;
        }

        NotifySaveChanged();
    }

    /// <summary>
    /// Удаляет сохранение полностью. Используется при "Новой игре" из главного меню
    /// и при выходе в меню с явным решением не сохранять (если такая опция будет).
    /// </summary>
    public void ClearSave()
    {
        currentSave = null;
        PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.Save();
        NotifySaveChanged();
    }

    /// <summary>Перечитывает сохранение из PlayerPrefs. На случай внешней правки.</summary>
    public void LoadSaveFromDisk()
    {
        if (!PlayerPrefs.HasKey(SaveKey))
        {
            currentSave = null;
            return;
        }

        string json = PlayerPrefs.GetString(SaveKey, "");
        if (string.IsNullOrEmpty(json))
        {
            currentSave = null;
            return;
        }

        try
        {
            currentSave = JsonUtility.FromJson<GameSaveData>(json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveManager] Сохранение повреждено, не удалось распарсить JSON: {e.Message}");
            currentSave = null;
        }
    }

    private void NotifySaveChanged()
    {
        OnSaveChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Полное состояние игры в один момент времени. Сериализуется в JSON через JsonUtility.
///
/// ВАЖНО: JsonUtility не поддерживает Dictionary и polymorphism. Все коллекции —
/// List&lt;&gt; и массивы. Все типы полей — либо сериализуемые Unity (Vector3, int, string,
/// List&lt;T&gt;), либо [Serializable] классы с публичными полями.
///
/// При добавлении новых подсистем добавляем сюда поля. При изменении схемы старые
/// сохранения будут терять новые поля (JsonUtility выставит их в default) — это
/// приемлемо для раннего прототипа, для релиза стоит версионирование (поле schemaVersion).
/// </summary>
[Serializable]
public class GameSaveData
{
    /// <summary>Версия схемы сохранения. Поднимаем на 1 при ломающих изменениях.</summary>
    public int schemaVersion = 1;

    // === Прогресс игры ===
    public int currentDay = 1;
    public int totalGold = 0;

    // === Апгрейды ===
    /// <summary>Список cardName (string) из UpgradeCardSO. Сохраняем по имени, а не по
    /// ссылке — ScriptableObject'ы не сериализуются в JSON через JsonUtility напрямую,
    /// а имя достаточно уникально для нашего каталога карт.</summary>
    public List<string> ownedUpgradeCardNames = new List<string>();

    // === Расстановка столов и мебели ===
    /// <summary>Список всех купленных/перемещённых объектов мебели. При загрузке сцены
    /// GameScene должна пройти по этому списку и либо заспавнить новый объект (если его
    /// не было на старте), либо переместить существующий на сохранённую позицию.</summary>
    public List<PlacedObjectSaveData> placedObjects = new List<PlacedObjectSaveData>();

    // === Настройки (опционально — можно вынести в отдельный SettingsSaveData) ===
    public float masterVolume = 1f;
    public float musicVolume = 0.7f;
    public float sfxVolume = 1f;
}

/// <summary>
/// Один размещённый на сетке объект. itemId — идентификатор префаба (string), по
/// которому ShopManager или загрузчик сцены найдёт нужный префаб. Position и rotation
/// сохраняем раздельно по компонентам, потому что JsonUtility не сериализует Vector3
/// напрямую в читаемый JSON — он делает {"x":1,"y":2,"z":3}, что норм, но явные float
/// поля дают контроль над форматом. Здесь используем Vector3 — JsonUtility с ним
/// справляется корректно.
/// </summary>
[Serializable]
public class PlacedObjectSaveData
{
    public string itemId;
    public Vector3 position;
    public Quaternion rotation;
}

