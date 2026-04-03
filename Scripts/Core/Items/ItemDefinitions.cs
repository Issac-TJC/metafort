using System.Collections.Generic;

namespace MetaFort.Core.Items
{
    [System.Flags]
    public enum PlacementRuleFlags
    {
        None = 0,
        RequireWalkableGround = 1 << 0,
        SupportCrossZ = 1 << 1,
        NeedsAdjacentSolid = 1 << 2,
        AllowOnAir = 1 << 3
    }

    public class ItemMaterialRequirement
    {
        public string itemId { get; set; }
        public int count { get; set; }
    }

    public class OccupiedCellOffset
    {
        public int x { get; set; }
        public int y { get; set; }
        public int z { get; set; }
    }

    public class ItemDefinition
    {
        public string id { get; set; }
        public string displayName { get; set; }
        public int maxDurability { get; set; }
        public int hardness { get; set; }
        public bool blocksVision { get; set; }
        public bool blocksMovement { get; set; }
        public int footprintX { get; set; } = 1;
        public int footprintY { get; set; } = 1;
        public int placementFlags { get; set; }
        public List<OccupiedCellOffset> occupiedOffsets { get; set; } = new List<OccupiedCellOffset>();
        public List<ItemMaterialRequirement> requiredMaterials { get; set; } = new List<ItemMaterialRequirement>();
        public string texturePath { get; set; }
        public string interactionScript { get; set; }

        public PlacementRuleFlags GetPlacementFlags() => (PlacementRuleFlags)placementFlags;
    }

    public class ItemConfigRoot
    {
        public List<ItemDefinition> items { get; set; } = new List<ItemDefinition>();
    }
}
