using System;

namespace MetaFort.Core.ECS
{
    public interface IEntityManager
    {
        uint CreateEntity();
        void DestroyEntity(uint entityId);
        bool IsAlive(uint entityId);

        void AddComponent<T>(uint entityId, in T component) where T : struct, IComponent;
        ref T GetComponent<T>(uint entityId) where T : struct, IComponent;
        bool HasComponent<T>(uint entityId) where T : struct, IComponent;
        void RemoveComponent<T>(uint entityId) where T : struct, IComponent;

        /// <summary>
        /// 获取当前拥有该组件的实体注册总量
        /// </summary>
        int GetComponentCount<T>() where T : struct, IComponent;

        /// <summary>
        /// O(1) 获取包含了所有有效实体ID（包含世代的高8位）的密集内存切片 (Span)
        /// 此 API 允许无状态 System 以最高速执行内存数组 Join。
        /// </summary>
        ReadOnlySpan<uint> GetDenseEntityIds<T>() where T : struct, IComponent;
    }
}
