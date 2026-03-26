using MetaFort.Core.EventBus;

namespace MetaFort.Core.ECS
{
    /// <summary>
    /// 系统基类接口，负责处理拥有特定组件的实体的无状态逻辑
    /// </summary>
    public interface ISystem
    {
        /// <summary>
        /// 系统初始化，注入 EntityManager 和 EventBus，允许系统进行事件注册
        /// </summary>
        void Initialize(IEntityManager entityManager, IEventBus eventBus);

        /// <summary>
        /// 系统的逻辑更新帧
        /// </summary>
        void Update(double deltaTime);
    }
}
