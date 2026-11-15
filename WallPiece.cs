using UnityEngine;

/// <summary>
/// ★ v1.1 (стены — только для редактора Unity) — маркер настенной мебели.
///
/// ЗАЧЕМ: обычная мебель ставится В ЦЕНТР КЛЕТКИ (GridPositioningSystem
/// регистрирует ячейку). Стена так выглядеть не может — она должна висеть
/// НА ГРАНИЦЕ МЕЖДУ КЛЕТКАМИ (на «ребре» сетки) и стыковаться торцами
/// с соседними стенами. Компонент-маркер говорит всей системе: «этот предмет —
/// стена, работай с ним по рёбрам, а не по ячейкам».
///
/// ★ ЧТО ИЗМЕНИЛО v1.1: весь API переведён с BaseCounter на Component.
/// BaseCounter на стене БОЛЬШЕ НЕ НУЖЕН и его можно удалить со стены:
/// он был нужен только старому игровому пайплайну (выбор лучом игрока,
/// T-перенос, покупка в лавке, сейв расстановки) — всё это для стен закрыто
/// (FurnitureMovingController v2.6+, ShopManager v1.6+, ShopUI v1.1+).
/// Теперь стена — ЧИСТО РЕДАКТОРСКИЙ объект: расстановка через GridSnapEditor v2.5
/// (он хватает стену по этому маркеру, без BaseCounter), в игре она — просто
/// статичная преграда с коллайдером. Вызовы со старыми аргументами BaseCounter
/// продолжают компилироваться: BaseCounter — это Component.
///
/// КАК ПОДКЛЮЧИТЬ: повесь WallPiece НА КОРЕНЬ префаба стены.
///
/// ТРЕБОВАНИЯ К ПРЕФАБУ СТЕНЫ:
/// - на корне WallPiece (маркер для редакторского снапа);
/// - BoxCollider на корне (стена должна блокировать игрока и луч):
///   size (2, 2.5, 0.2), center (0, 1.25, 0) для клетки 2×2 м;
/// - BaseCounter НЕ нужен: с ним стена попадала бы в выбор игрока, лавку
///   и сейв расстановки — без него стена просто часть декораций;
/// - геометрия — вытянутый бокс (длинная ось — вдоль стены), пивот где угодно:
///   центр геометрии вычисляется автоматически, стены с пивотом на торце
///   встают на ребро так же точно, как стены с пивотом в центре;
/// - поворот кратен 90° (0/90/180/270). ПЕРЕНОС В РЕДАКТОРЕ ПОВОРОТ НЕ МЕНЯЕТ:
///   снап предлагает только рёбра, параллельные текущей ориентации стены
///   (повернуть — инструментом Rotate, E).
///
/// СТЫКОВКА «К ДРУГ ДРУГУ»: рёбра сетки — сегменты длиной ровно в клетку.
/// Соседние коллинеарные рёбра делят торец, перпендикулярные встречаются в узле —
/// любая стыковка получается геометрически точной без отдельных «угловых»
/// префабов. Плюс магнит: если у ребра есть занятый коллинеарный сосед,
/// стена в редакторе тянется продолжить линию (см. GridSnapEditor).
/// </summary>
public class WallPiece : MonoBehaviour
{
    /// <summary>Является ли предмет стеной. Null-безопасно. Принимает Component:
    /// стены живут и без BaseCounter (v1.1).</summary>
    public static bool IsWall(Component counter)
    {
        return counter != null && counter.GetComponent<WallPiece>() != null;
    }

    /// <summary>
    /// Мировой центр геометрии стены — точка, которая должна садиться на середину
    /// ребра. Принимает Component: стены живут и без BaseCounter (v1.1).
    /// Источники (по приоритету, как в FurnitureMovingController.CaptureWallBox):
    /// 1) самый объёмный BoxCollider в детях (size/center не зависят от enabled —
    ///    работает и у призрака с выключенными коллайдерами);
    /// 2) суммарные bounds всех коллайдеров;
    /// 3) суммарные bounds рендереров.
    /// </summary>
    public static bool TryGetGeometryCenter(Component wall, out Vector3 center)
    {
        if (wall == null)
        {
            center = default;
            return false;
        }

        // 1) Самый объёмный BoxCollider.
        BoxCollider[] boxes = wall.GetComponentsInChildren<BoxCollider>();

        BoxCollider bestBox = null;
        float bestVolume = 0f;

        foreach (BoxCollider box in boxes)
        {
            if (box == null) continue;

            Vector3 worldSize = Vector3.Scale(box.size, box.transform.lossyScale);
            float volume = worldSize.x * worldSize.y * worldSize.z;

            if (box.size != Vector3.zero && volume > bestVolume)
            {
                bestVolume = volume;
                bestBox = box;
            }
        }

        if (bestBox != null)
        {
            center = bestBox.transform.TransformPoint(bestBox.center);
            return true;
        }

        // 2) Суммарные bounds коллайдеров (валидны, только если включены).
        Collider[] colliders = wall.GetComponentsInChildren<Collider>();

        if (colliders != null && colliders.Length > 0 && colliders[0].enabled)
        {
            Bounds bounds = colliders[0].bounds;

            for (int i = 1; i < colliders.Length; i++)
            {
                if (colliders[i] == null) continue;
                bounds.Encapsulate(colliders[i].bounds);
            }

            center = bounds.center;
            return bounds.size.sqrMagnitude > 0.0001f;
        }

        // 3) Рендереры (у стены совсем нет коллайдеров).
        Renderer[] renderers = wall.GetComponentsInChildren<Renderer>();

        if (renderers != null && renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;

            for (int i = 1; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                bounds.Encapsulate(renderers[i].bounds);
            }

            center = bounds.center;
            return bounds.size.sqrMagnitude > 0.0001f;
        }

        center = default;
        return false;
    }

    /// <summary>
    /// Мировые границы геометрии стены — по ним определяется «длинная ось»
    /// (куда стена смотрит) и, соответственно, подходящее ребро.
    /// Принимает Component: стены живут и без BaseCounter (v1.1).
    /// BoxCollider-источник не зависит от enabled — как и в TryGetGeometryCenter.
    /// </summary>
    public static bool TryGetGeometryBounds(Component wall, out Bounds bounds)
    {
        if (wall == null)
        {
            bounds = default;
            return false;
        }

        // 1) BoxCollider — не зависит от enabled.
        BoxCollider[] boxes = wall.GetComponentsInChildren<BoxCollider>();

        BoxCollider bestBox = null;
        float bestVolume = 0f;

        foreach (BoxCollider box in boxes)
        {
            if (box == null) continue;

            Vector3 worldSize = Vector3.Scale(box.size, box.transform.lossyScale);
            float volume = worldSize.x * worldSize.y * worldSize.z;

            if (box.size != Vector3.zero && volume > bestVolume)
            {
                bestVolume = volume;
                bestBox = box;
            }
        }

        if (bestBox != null)
        {
            Vector3 worldCenter = bestBox.transform.TransformPoint(bestBox.center);
            Vector3 worldSize = Vector3.Scale(bestBox.size, bestBox.transform.lossyScale);

            bounds = new Bounds(worldCenter, worldSize);
            return true;
        }

        // 2) Коллайдеры bounds (валидны, только если включены).
        Collider[] colliders = wall.GetComponentsInChildren<Collider>();

        if (colliders != null && colliders.Length > 0 && colliders[0].enabled)
        {
            bounds = colliders[0].bounds;

            for (int i = 1; i < colliders.Length; i++)
            {
                if (colliders[i] == null) continue;
                bounds.Encapsulate(colliders[i].bounds);
            }

            return bounds.size.sqrMagnitude > 0.0001f;
        }

        // 3) Рендереры.
        Renderer[] renderers = wall.GetComponentsInChildren<Renderer>();

        if (renderers != null && renderers.Length > 0)
        {
            bounds = renderers[0].bounds;

            for (int i = 1; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds.size.sqrMagnitude > 0.0001f;
        }

        bounds = default;
        return false;
    }
}
