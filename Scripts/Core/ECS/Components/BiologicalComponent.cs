using System;
using MetaFort.Core.ECS;

namespace MetaFort.Core.ECS
{
    public enum Gender
    {
        None,
        Male,
        Female
    }

    /// <summary>
    /// 生理状态组件，纯数据结构
    /// </summary>
    public struct BiologicalComponent : IComponent
    {
        public Gender Gender;
        public float Libido;
        public float Hunger;
        public float Stamina;
        public float Sanity;

        // 初始化方法或默认值可在此添加，确保 0-100 的数值范围处理交由 System 来做
    }
}
