using UnityEngine;

[RequireComponent(typeof(Grid))]
public class GridPlacement : MonoBehaviour
{
    // Синглтон для быстрого доступа из других скриптов (например, системы постройки)
    public static GridPlacement Instance { get; private set; }

    private Grid unityGrid;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        unityGrid = GetComponent<Grid>();
    }

    /// <summary>
    /// Принимает любую неровную позицию в 3D-мире и возвращает точный МИРОВОЙ ЦЕНТР ближайшей ячейки сетки.
    /// Идеально подходит для объектов с пивотом строго посередине.
    /// </summary>
    public Vector3 SnapToGridCenter(Vector3 worldPosition)
    {
        if (unityGrid == null) unityGrid = GetComponent<Grid>();

        // 1. Переводим координаты клика или игрока в целочисленный индекс ячейки (например: X:2, Z:-1)
        Vector3Int cellPosition = unityGrid.WorldToCell(worldPosition);

        // 2. Получаем строгие координаты МИРОВОГО ЦЕНТРА этой ячейки
        Vector3 snappedPosition = unityGrid.GetCellCenterWorld(cellPosition);

        // 3. Удерживаем объект на уровне пола (высота Y = 0)
        snappedPosition.y = 0f; 

        return snappedPosition;
    }

    /// <summary>
    /// Проверяет, свободна ли клетка (нет ли там уже другого стола или стены)
    /// </summary>
    /// <param name="targetGridPosition">Мировой центр ячейки, полученный из SnapToGridCenter</param>
    public bool IsCellFree(Vector3 targetGridPosition, float cellSize = 2f)
    {
        // Создаем невидимую коробку проверки чуть меньше размера самой клетки (например, 1.9м при клетке 2м),
        // чтобы не задевать соседние клетки из-за погрешностей округления.
        float halfSize = (cellSize * 0.95f) / 2f;
        Vector3 checkHalfExtents = new Vector3(halfSize, 1f, halfSize);

        // Проверяем физическое пересечение с коллайдерами
        Collider[] colliders = Physics.OverlapBox(targetGridPosition + Vector3.up * 0.5f, checkHalfExtents, Quaternion.identity);
        
        foreach (Collider col in colliders)
        {
            // Если в этой точке найден другой стол (или объект с вашим базовым классом прилавков/стен)
            if (col.GetComponent<DiningTable>() != null)
            {
                return false; // Клетка занята!
            }
        }
        return true; // Клетка абсолютно свободна
    }
}