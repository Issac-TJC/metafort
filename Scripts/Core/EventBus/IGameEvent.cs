namespace MetaFort.Core.EventBus
{
    /// <summary>
    /// 游戏事件的基础接口，所有通过EventBus传递的事件都需要实现此接口
    /// 由于是纯数据驱动，事件本身也只是纯数据结构 (struct)
    /// </summary>
    public interface IGameEvent
    {
    }
}
