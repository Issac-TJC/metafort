using System.Collections.Generic;
using Godot;

namespace MetaFort.Core.EventBus.Events
{
    public struct VisionUpdatedEvent : IGameEvent
    {
        public int ZLevel;
        public List<Vector2I> NewlyVisibleCoords;
        public List<Vector2I> NewlyExploredCoords;
        public List<Vector2I> NewlyHiddenCoords;
    }
}
