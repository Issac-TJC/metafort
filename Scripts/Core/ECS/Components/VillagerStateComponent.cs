using MetaFort.Core.ECS;

namespace MetaFort.Core.ECS
{
    public enum VillagerAction
    {
        Idle,
        Moving,
        Digging,
        Building
    }

    /// <summary>
    /// 小人状态组件，保存当前高级动作与目标
    /// </summary>
    public struct VillagerStateComponent : IComponent
    {
        public VillagerAction CurrentAction;
        
        // 目标网格坐标
        public int TargetX;
        public int TargetY;
        public int TargetZ;
    }
}
