using MetaFort.Core.Items;
using MetaFort.Core.Spatial;

namespace MetaFort.Core.EventBus.Events
{
    public enum MapCursorModeKind
    {
        None,
        BuildBlueprint,
        DigDesignation,
        DemolishDesignation,
        CancelDesignation
    }

    public enum PlannerCommandMode
    {
        None,
        Dig,
        Demolish
    }

    public enum DigTargetKind
    {
        None,
        Wall,
        Floor
    }

    public struct MapCursorModeState
    {
        public MapCursorModeKind Kind;
        public string ItemId;
        public string MarkerKey;
        public string DisplayLabel;
    }

    public struct MapCursorModeRequestEvent : IGameEvent
    {
        public MapCursorModeState Mode;
    }

    public struct MapCursorModeChangedEvent : IGameEvent
    {
        public MapCursorModeState Mode;
    }

    public struct DigTargetResolution
    {
        public GridPosition ResolvedTarget;
        public GridPosition PreviewCell;
        public DigTargetKind Kind;
    }

    public enum VillagerWorkType
    {
        Build,
        Dig,
        Demolish
    }

    public struct SimulationSpeedChangedEvent : IGameEvent
    {
        public float PreviousTimeScale;
        public float CurrentTimeScale;
    }

    public struct StockpileEntryData
    {
        public string ItemId;
        public string Label;
        public int Count;
        public int Order;
    }

    public struct StockpileChangedEvent : IGameEvent
    {
        public StockpileEntryData[] Entries;
    }

    public struct CommandModeChangedEvent : IGameEvent
    {
        public PlannerCommandMode Mode;
        public string MarkerKey;
    }

    public struct DigDesignationChangedEvent : IGameEvent
    {
        public GridPosition Target;
        public GridPosition PreviewCell;
        public bool IsActive;
        public DigTargetKind Kind;
    }

    public struct DemolishDesignationChangedEvent : IGameEvent
    {
        public GridPosition Anchor;
        public string ItemId;
        public bool IsActive;
    }

    public struct VillagerWorkRequestEvent : IGameEvent
    {
        public uint ActorEntityId;
        public VillagerWorkType WorkType;
        public GridPosition Target;
        public GridPosition ResolvedTarget;
        public DigTargetKind DigTargetKind;
        public string PayloadId;
    }

    public struct PlacedItemRemovedEvent : IGameEvent
    {
        public string ItemId;
        public GridPosition Anchor;
        public uint RemovedByActorId;
    }

    public struct PlacedItemAddedEvent : IGameEvent
    {
        public string ItemId;
        public GridPosition Anchor;
        public uint OwnerEntityId;
        public bool IsBroken;
    }
}
