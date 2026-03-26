using System;
using System.Collections.Generic;

namespace MetaFort.Core.EventBus
{
    /// <summary>
    /// 零装箱的高性能事件总线。内部将 Delegate 安全缓存。
    /// 可以使用 ref 传递结构体，且强转时直接利用明确泛型，完全避免隐式 object 类型的强转装箱（Box）。
    /// </summary>
    public class EventBus : IEventBus
    {
        private readonly Dictionary<Type, Delegate> _subscribers = new Dictionary<Type, Delegate>();

        public void Subscribe<T>(GameEventHandler<T> handler) where T : struct, IGameEvent
        {
            Type eventType = typeof(T);
            if (_subscribers.TryGetValue(eventType, out Delegate currentDel))
            {
                _subscribers[eventType] = Delegate.Combine(currentDel, handler);
            }
            else
            {
                _subscribers[eventType] = handler;
            }
        }

        public void Unsubscribe<T>(GameEventHandler<T> handler) where T : struct, IGameEvent
        {
            Type eventType = typeof(T);
            if (_subscribers.TryGetValue(eventType, out Delegate currentDel))
            {
                Delegate newDel = Delegate.Remove(currentDel, handler);
                if (newDel == null)
                    _subscribers.Remove(eventType);
                else
                    _subscribers[eventType] = newDel;
            }
        }

        public void Publish<T>(ref T gameEvent) where T : struct, IGameEvent
        {
            Type eventType = typeof(T);
            if (_subscribers.TryGetValue(eventType, out Delegate del))
            {
                // (GameEventHandler<T>)的强转不会导致泛型 struct 发生装箱，非常干净
                var typedDelegate = (GameEventHandler<T>)del;
                typedDelegate?.Invoke(ref gameEvent);
            }
        }
    }
}
