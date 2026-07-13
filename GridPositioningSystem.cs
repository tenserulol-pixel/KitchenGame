using UnityEngine;
using System.Collections.Generic;

public class GridPositioningSystem : MonoBehaviour
{
    // Одиночка (Singleton) для легкого доступа из других скриптов (например, из менеджера строительства)
    public static GridPositioningSystem Instance { get; private set; }

    [Header("Настройки сетки")]
    [SerializeField] private float cellSize = 2f; // Размер одной ячейки (например, 2x2 метра)
    [SerializeField] private Vector3 gridOrigin = Vector3.zero; // Начало координат сетки в мировом пространстве
    [SerializeField] private Vector2Int gridBounds = new Vector2Int(10, 10); // Размеры сетки для визуализации в редакторе

    // Словарь занятых ячеек. Хранит координаты и ссылку на объект, который её занимает
    private Dictionary<Vector2Int, BaseCounter> occupiedCells = new Dictionary<Vector2Int, BaseCounter>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Конвертирует мировую позицию в координаты ячейки сетки (X, Z).
    /// </summary>
    public Vector2Int GetGridPosition(Vector3 worldPosition)
    {
        int x = Mathf.FloorToInt((worldPosition.x - gridOrigin.x) / cellSize);
        int z = Mathf.FloorToInt((worldPosition.z - gridOrigin.z) / cellSize);
        return new Vector2Int(x, z);
    }

    /// <summary>
    /// Получает центральную мировую позицию для заданной ячейки сетки.
    /// </summary>
    public Vector3 GetWorldPosition(Vector2Int gridPosition)
    {
        float x = gridPosition.x * cellSize + (cellSize / 2f) + gridOrigin.x;
        float z = gridPosition.y * cellSize + (cellSize / 2f) + gridOrigin.z;
        return new Vector3(x, gridOrigin.y, z); // Используем базовую высоту сетки
    }

    /// <summary>
    /// Проверяет, свободна ли выбранная ячейка.
    /// </summary>
    public bool IsCellOccupied(Vector2Int gridPosition)
    {
        return occupiedCells.ContainsKey(gridPosition);
    }

    /// <summary>
    /// Регистрирует объект на его текущей позиции и выравнивает по сетке.
    /// Полезно для объектов, которые изначально расставлены на сцене в редакторе Unity.
    /// </summary>
    public void RegisterCounterAtCurrentPosition(BaseCounter counter)
    {
        Vector2Int gridPos = GetGridPosition(counter.transform.position);

        if (IsCellOccupied(gridPos))
        {
            // Проверяем: если ячейку занял тот же самый объект, игнорируем ошибку
            if (occupiedCells[gridPos] == counter) return;

            Debug.LogWarning($"[GridSystem] Конфликт! Ячейка {gridPos} уже занята объектом '{occupiedCells[gridPos].gameObject.name}'. Не удалось зарегистрировать '{counter.gameObject.name}'");
            return;
        }

        // Выравниваем объект строго по центру ячейки
        Vector3 alignedPosition = GetWorldPosition(gridPos);
        alignedPosition.y = counter.transform.position.y; // Сохраняем исходную высоту стола
        counter.transform.position = alignedPosition;

        // Бронируем ячейку за этим столом
        occupiedCells[gridPos] = counter;
        Debug.Log($"[GridSystem] '{counter.gameObject.name}' автоматически выровнен и зарегистрирован в ячейке {gridPos}");
    }

    /// <summary>
    /// Пытается разместить счетчик/стол на сетке (например, при покупке стола).
    /// </summary>
    /// <returns>True, если размещение успешно. False, если ячейка занята.</returns>
    public bool TryPlaceCounter(BaseCounter counter, Vector3 targetWorldPosition)
    {
        Vector2Int gridPos = GetGridPosition(targetWorldPosition);

        if (IsCellOccupied(gridPos))
        {
            Debug.LogWarning($"[GridSystem] Не удалось разместить! Ячейка {gridPos} уже занята объектом: {occupiedCells[gridPos].gameObject.name}");
            return false;
        }

        // Выравниваем объект по центру сетки
        Vector3 alignedPosition = GetWorldPosition(gridPos);
        
        // Корректируем высоту под объект
        alignedPosition.y = counter.transform.position.y; 
        counter.transform.position = alignedPosition;

        // Регистрируем объект в словаре
        occupiedCells[gridPos] = counter;
        Debug.Log($"[GridSystem] Объект {counter.gameObject.name} успешно размещен в ячейке {gridPos}");
        return true;
    }

    /// <summary>
    /// Удаляет стол с сетки по его координатам.
    /// </summary>
    public void RemoveCounter(Vector2Int gridPosition)
    {
        if (occupiedCells.ContainsKey(gridPosition))
        {
            occupiedCells.Remove(gridPosition);
            Debug.Log($"[GridSystem] Ячейка {gridPosition} теперь свободна.");
        }
    }

    /// <summary>
    /// Находит и удаляет переданный стол из реестра занятых ячеек сетки.
    /// </summary>
    public void RemoveCounter(BaseCounter counter)
    {
        Vector2Int keyToRemove = Vector2Int.zero;
        bool found = false;

        foreach (var kvp in occupiedCells)
        {
            if (kvp.Value == counter)
            {
                keyToRemove = kvp.Key;
                found = true;
                break;
            }
        }

        if (found)
        {
            occupiedCells.Remove(keyToRemove);
            Debug.Log($"[GridSystem] Объект {counter.gameObject.name} удален. Ячейка {keyToRemove} теперь свободна.");
        }
    }

    /// <summary>
    /// Отрисовка сетки в редакторе Unity для удобного проектирования уровней.
    /// </summary>
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0f, 1f, 1f, 0.3f); // Полупрозрачный голубой цвет

        for (int x = 0; x < gridBounds.x; x++)
        {
            for (int z = 0; z < gridBounds.y; z++)
            {
                Vector2Int gridPos = new Vector2Int(x, z);
                Vector3 cellCenter = GetWorldPosition(gridPos);
                
                // Рисуем границы ячейки плоским кубом на полу
                Gizmos.DrawWireCube(cellCenter, new Vector3(cellSize, 0.05f, cellSize));
            }
        }
    }
}