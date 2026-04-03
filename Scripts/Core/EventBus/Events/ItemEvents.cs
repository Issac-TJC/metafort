using Godot;

namespace MetaFort.Core.EventBus.Events
{
    public enum ItemCommandType
    {
        Craft,
        Place,
        Use,
        Remove
    }

    public enum ContextActionType
    {
        Move,
        Craft,
        Place,
        Use
    }

    public struct ItemCommandEvent : IGameEvent
    {
        public ItemCommandType Type;
        public uint ActorEntityId;
        public string ItemId;
        public Vector3I Target;
    }

    public struct ItemCommandResultEvent : IGameEvent
    {
        public bool Success;
        public string Message;
        public uint ActorEntityId;
        public string ItemId;
        public Vector3I Target;
    }

    public struct ContextActionOption
    {
        public ContextActionType Type;
        public string Label;
        public string ItemId;
        public Vector3I Target;
    }

    public struct ContextActionMenuRequestEvent : IGameEvent
    {
        public uint ActorEntityId;
        public Vector2 ScreenPosition;
        public ContextActionOption[] Options;
    }

    public struct ContextActionSelectedEvent : IGameEvent
    {
        public uint ActorEntityId;
        public ContextActionOption Selected;
    }
}
