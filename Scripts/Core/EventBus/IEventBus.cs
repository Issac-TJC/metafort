using System;

namespace MetaFort.Core.EventBus
{
    /// <summary>
    /// 事件处理委托，通过 ref 传递 struct，从根源上杜绝任何装箱 (Boxing) 和值拷贝开销
    /// </summary>
    public delegate void GameEventHandler<T>(ref T gameEvent) where T : struct, IGameEvent;

    public interface IEventBus
    {
        void Subscribe<T>(GameEventHandler<T> handler) where T : struct, IGameEvent;
        
        void Unsubscribe<T>(GameEventHandler<T> handler) where T : struct, IGameEvent;
        
        void Publish<T>(ref T gameEvent) where T : struct, IGameEvent;
    }
}
