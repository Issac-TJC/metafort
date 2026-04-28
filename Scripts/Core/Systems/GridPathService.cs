using System;
using System.Collections.Generic;
using MetaFort.Core.ECS;
using MetaFort.Core.Enemy;
using MetaFort.Core.Spatial;

namespace MetaFort.Core.Systems
{
    public interface IPathQueryService
    {
        Queue<GridPosition> CalculateLayeredPath(IEntityManager entityManager, IMapManager mapManager, GridPosition start, GridPosition target);
    }

    public sealed class GridPathService : IPathQueryService
    {
        public Queue<GridPosition> CalculateLayeredPath(IEntityManager entityManager, IMapManager mapManager, GridPosition start, GridPosition target)
        {
            Queue<GridPosition> fullPath = new Queue<GridPosition>();
            if (start.Z == target.Z)
            {
                return SimpleAStar(start, target);
            }

            GridPosition? closestStair = FindClosestStair(entityManager, start.X, start.Y, start.Z);
            if (!closestStair.HasValue)
            {
                return fullPath;
            }

            Queue<GridPosition> partialPath = SimpleAStar(start, closestStair.Value);
            foreach (GridPosition node in partialPath)
            {
                fullPath.Enqueue(node);
            }

            int nextZ = start.Z < target.Z ? start.Z + 1 : start.Z - 1;
            GridPosition climbingPosition = new GridPosition(closestStair.Value.X, closestStair.Value.Y, nextZ);
            fullPath.Enqueue(climbingPosition);

            Queue<GridPosition> remainingPath = CalculateLayeredPath(entityManager, mapManager, climbingPosition, target);
            foreach (GridPosition node in remainingPath)
            {
                fullPath.Enqueue(node);
            }

            return fullPath;
        }

        private GridPosition? FindClosestStair(IEntityManager entityManager, int sx, int sy, int sz)
        {
            GridPosition? closest = FindClosestTaggedPosition<TempStairComponent>(entityManager, sx, sy, sz);
            if (closest.HasValue)
            {
                return closest;
            }

            return FindClosestTaggedPosition<EnemyNavigationAnchorComponent>(entityManager, sx, sy, sz);
        }

        private GridPosition? FindClosestTaggedPosition<T>(IEntityManager entityManager, int sx, int sy, int sz) where T : struct, IComponent
        {
            int count = entityManager.GetComponentCount<T>();
            if (count == 0)
            {
                return null;
            }

            ReadOnlySpan<uint> entityIds = entityManager.GetDenseEntityIds<T>();
            GridPosition? closest = null;
            float minDistance = float.MaxValue;

            for (int i = 0; i < entityIds.Length; i++)
            {
                uint entityId = entityIds[i];
                if (!entityManager.HasComponent<MetaFort.Core.ECS.PositionComponent>(entityId))
                {
                    continue;
                }

                ref MetaFort.Core.ECS.PositionComponent position = ref entityManager.GetComponent<MetaFort.Core.ECS.PositionComponent>(entityId);
                if ((int)position.Z != sz)
                {
                    continue;
                }

                float distance = (position.X - sx) * (position.X - sx) + (position.Y - sy) * (position.Y - sy);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closest = new GridPosition((int)position.X, (int)position.Y, (int)position.Z);
                }
            }

            return closest;
        }

        private Queue<GridPosition> SimpleAStar(GridPosition start, GridPosition target)
        {
            Queue<GridPosition> path = new Queue<GridPosition>();
            int cx = start.X;
            int cy = start.Y;
            int iterations = 0;

            while ((cx != target.X || cy != target.Y) && iterations < 1000)
            {
                iterations++;
                int dx = Math.Sign(target.X - cx);
                int dy = Math.Sign(target.Y - cy);

                if (cx != target.X)
                {
                    cx += dx;
                }
                else if (cy != target.Y)
                {
                    cy += dy;
                }

                path.Enqueue(new GridPosition(cx, cy, start.Z));
            }

            return path;
        }
    }
}
