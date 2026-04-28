using MetaFort.Core.Spatial;

namespace MetaFort.Core.Enemy
{
    public interface IResourceRaidTarget
    {
        bool TryExtract(string itemId, int requested, out int extracted);
    }

    public interface IEnemyTargetProvider
    {
        bool TryGetTarget(uint enemyEntityId, out string targetKind, out uint targetEntityId, out GridPosition targetPosition);
    }
}
