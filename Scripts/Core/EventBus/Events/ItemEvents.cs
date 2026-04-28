using Godot;
using MetaFort.Core.Spatial;

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
        Use,
        BuildBlueprint,
        DigDesignationWork,
        DemolishDesignationWork,
        CancelDigDesignation,
        CancelDemolishDesignation
    }

    public enum ConstructionBlueprintStatus
    {
        Planned,
        Assigned,
        Building,
        Cancelled,
        Completed
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
        public Vector3I ResolvedTarget;
        public string PayloadId;
        public DigTargetKind DigTargetKind;
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

    public struct ConstructionBlueprintPlacedEvent : IGameEvent
    {
        public int BlueprintId;
        public string ItemId;
        public GridPosition Anchor;
        public uint PlacedByActorId;
        public int Day;
        public int Hour;
    }

    public struct ConstructionBlueprintCancelledEvent : IGameEvent
    {
        public int BlueprintId;
        public string ItemId;
        public GridPosition Anchor;
    }

    public struct ConstructionBlueprintCommandEvent : IGameEvent
    {
        public uint ActorEntityId;
        public int BlueprintId;
        public GridPosition BlueprintAnchor;
    }

    public struct ConstructionBlueprintCompletedEvent : IGameEvent
    {
        public int BlueprintId;
        public string ItemId;
        public GridPosition Anchor;
        public uint BuiltByActorId;
    }

    public struct BuildPlannerItemSelectedEvent : IGameEvent
    {
        public string ItemId;
    }

    public struct BuildPlannerPlacementCancelledEvent : IGameEvent
    {
    }

    public enum ItemDamageSourceType
    {
        Weather,
        Lightning
    }

    public struct ItemConditionChangedEvent : IGameEvent
    {
        public string ItemId;
        public GridPosition Anchor;
        public float PreviousCondition;
        public float CurrentCondition;
        public float WearDelta;
        public float Wetness;
        public float TemperatureStress;
        public int Day;
        public int Hour;
        public bool IsBroken;
    }

    public struct ItemWeatherDamagedEvent : IGameEvent
    {
        public string ItemId;
        public GridPosition Anchor;
        public ItemDamageSourceType DamageSource;
        public float WearDelta;
        public float CurrentCondition;
        public float Wetness;
        public float TemperatureStress;
        public int Day;
        public int Hour;
    }

    public struct ItemBrokenEvent : IGameEvent
    {
        public string ItemId;
        public GridPosition Anchor;
        public ItemDamageSourceType DamageSource;
        public int Day;
        public int Hour;
    }
}
