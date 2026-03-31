using MetaFort.Core.ECS;

namespace MetaFort.Core.ECS
{
    /// <summary>
    /// 测试用临时组件，表示一个可以在Z轴跨层的梯子
    /// </summary>
    public struct TempStairComponent : IComponent
    {
        // 可以记录该梯子通向哪些层，目前简单假定可以通向 Z+1 和 Z-1
    }
}
