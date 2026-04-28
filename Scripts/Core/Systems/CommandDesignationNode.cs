using Godot;
using System.Collections.Generic;
using MetaFort.Core.Data;
using MetaFort.Core.EventBus;
using MetaFort.Core.EventBus.Events;
using MetaFort.Core.Items;
using MetaFort.Core.Spatial;
using TileData = MetaFort.Core.Spatial.TileData;

namespace MetaFort.Core.Systems
{
    [GlobalClass]
    public partial class CommandDesignationNode : Node
    {
        public sealed class DigDesignation
        {
            public GridPosition Target;
            public GridPosition PreviewCell;
            public DigTargetKind Kind;
        }

        public sealed class DemolishDesignation
        {
            public GridPosition Anchor;
            public string ItemId;
        }

        [Export]
        public NodePath CoreSourcePath { get; set; }

        [Export]
        public NodePath ItemSystemPath { get; set; }

        private IEventBus _eventBus;
        private IMapManager _mapManager;
        private ItemSystemNode _itemSystem;

        private readonly Dictionary<GridPosition, DigDesignation> _digDesignations = new Dictionary<GridPosition, DigDesignation>();
        private readonly Dictionary<GridPosition, DemolishDesignation> _demolishByAnchor = new Dictionary<GridPosition, DemolishDesignation>();
        private readonly Dictionary<GridPosition, GridPosition> _demolishOccupiedToAnchor = new Dictionary<GridPosition, GridPosition>();

        public override void _Ready()
        {
            MetaFort.GameEntry gameEntry = ResolveGameEntry();
            if (gameEntry == null || gameEntry.EventBus == null || gameEntry.MapManager == null)
            {
                GD.PrintErr("[CommandDesignationNode] Missing GameEntry dependencies.");
                SetProcess(false);
                return;
            }

            _eventBus = gameEntry.EventBus;
            _mapManager = gameEntry.MapManager;
            _itemSystem = GetNodeOrNull<ItemSystemNode>(ItemSystemPath);

            if (_itemSystem == null)
            {
                GD.PrintErr("[CommandDesignationNode] ItemSystemPath must point to ItemSystemNode.");
                SetProcess(false);
                return;
            }

            _eventBus.Subscribe<PlacedItemRemovedEvent>(OnPlacedItemRemoved);
        }

        public override void _ExitTree()
        {
            if (_eventBus != null)
            {
                _eventBus.Unsubscribe<PlacedItemRemovedEvent>(OnPlacedItemRemoved);
            }
        }

        public IEnumerable<DigDesignation> EnumerateDigDesignations()
        {
            return _digDesignations.Values;
        }

        public IEnumerable<DemolishDesignation> EnumerateDemolishDesignations()
        {
            return _demolishByAnchor.Values;
        }

        public bool TryGetDigDesignation(GridPosition target, out DigDesignation designation)
        {
            return _digDesignations.TryGetValue(target, out designation);
        }

        public bool TryGetDemolishDesignation(GridPosition target, out DemolishDesignation designation)
        {
            designation = null;
            if (!_demolishOccupiedToAnchor.TryGetValue(target, out GridPosition anchor))
            {
                return false;
            }

            return _demolishByAnchor.TryGetValue(anchor, out designation);
        }

        public bool TryResolveDigTarget(GridPosition clickedCell, out DigTargetResolution resolution)
        {
            resolution = default;
            if (!_mapManager.IsWithinBounds(clickedCell))
            {
                return false;
            }

            TileData currentTile = _mapManager.GetTile(clickedCell.X, clickedCell.Y, clickedCell.Z);
            if (currentTile.Type != TerrainType.Air)
            {
                resolution = new DigTargetResolution
                {
                    ResolvedTarget = clickedCell,
                    PreviewCell = clickedCell,
                    Kind = DigTargetKind.Wall
                };
                return true;
            }

            if (clickedCell.Z <= 0)
            {
                return false;
            }

            GridPosition floorCell = new GridPosition(clickedCell.X, clickedCell.Y, clickedCell.Z - 1);
            if (!_mapManager.IsWithinBounds(floorCell))
            {
                return false;
            }

            resolution = new DigTargetResolution
            {
                ResolvedTarget = floorCell,
                PreviewCell = clickedCell,
                Kind = DigTargetKind.Floor
            };
            return true;
        }

        public bool TryGetDigDesignationAtDisplayCell(GridPosition clickedCell, out DigDesignation designation, out DigTargetResolution resolution)
        {
            designation = null;
            if (!TryResolveDigTarget(clickedCell, out resolution))
            {
                return false;
            }

            return _digDesignations.TryGetValue(resolution.ResolvedTarget, out designation);
        }

        public bool TryPlaceDigDesignation(GridPosition clickedCell, out string failureReason)
        {
            failureReason = string.Empty;
            if (!TryResolveDigTarget(clickedCell, out DigTargetResolution resolution))
            {
                failureReason = "Target is out of bounds.";
                return false;
            }

            TileData tile = _mapManager.GetTile(resolution.ResolvedTarget.X, resolution.ResolvedTarget.Y, resolution.ResolvedTarget.Z);
            if (!ConfigManager.TryGetTerrainTypeConfig((ushort)tile.Type, out TerrainTypeConfig terrainConfig) || terrainConfig == null || !terrainConfig.canDig)
            {
                failureReason = "This tile cannot be designated for digging.";
                return false;
            }

            if (_digDesignations.ContainsKey(resolution.ResolvedTarget))
            {
                RemoveDigDesignation(resolution.ResolvedTarget);
                return true;
            }

            _digDesignations[resolution.ResolvedTarget] = new DigDesignation
            {
                Target = resolution.ResolvedTarget,
                PreviewCell = resolution.PreviewCell,
                Kind = resolution.Kind
            };

            var evt = new DigDesignationChangedEvent
            {
                Target = resolution.ResolvedTarget,
                PreviewCell = resolution.PreviewCell,
                IsActive = true,
                Kind = resolution.Kind
            };
            _eventBus.Publish(ref evt);
            return true;
        }

        public bool TryPlaceDemolishDesignation(GridPosition target, out string failureReason)
        {
            failureReason = string.Empty;
            if (!_itemSystem.TryGetPlacedItemAnchor(target, out GridPosition anchor))
            {
                failureReason = "Only placed buildings can be marked for demolition.";
                return false;
            }

            if (_demolishByAnchor.ContainsKey(anchor))
            {
                RemoveDemolishDesignation(anchor);
                return true;
            }

            if (!_itemSystem.TryGetPlacedItemRecord(anchor, out ItemSystemNode.PlacedItemRecord record))
            {
                failureReason = "Placed item record is missing.";
                return false;
            }

            DemolishDesignation designation = new DemolishDesignation
            {
                Anchor = anchor,
                ItemId = record.ItemId
            };

            _demolishByAnchor[anchor] = designation;
            foreach (GridPosition occupied in _itemSystem.GetOccupiedCellsForItem(record.ItemId, anchor))
            {
                _demolishOccupiedToAnchor[occupied] = anchor;
            }

            var evt = new DemolishDesignationChangedEvent
            {
                Anchor = anchor,
                ItemId = record.ItemId,
                IsActive = true
            };
            _eventBus.Publish(ref evt);
            return true;
        }

        public void RemoveDigDesignation(GridPosition target)
        {
            if (!_digDesignations.TryGetValue(target, out DigDesignation designation))
            {
                return;
            }

            _digDesignations.Remove(target);

            var evt = new DigDesignationChangedEvent
            {
                Target = target,
                PreviewCell = designation.PreviewCell,
                IsActive = false,
                Kind = DigTargetKind.None
            };
            _eventBus.Publish(ref evt);
        }

        public void RemoveDemolishDesignation(GridPosition anchor)
        {
            if (!_demolishByAnchor.TryGetValue(anchor, out DemolishDesignation designation))
            {
                return;
            }

            _demolishByAnchor.Remove(anchor);

            List<GridPosition> occupied = new List<GridPosition>();
            foreach (var kv in _demolishOccupiedToAnchor)
            {
                if (kv.Value.Equals(anchor))
                {
                    occupied.Add(kv.Key);
                }
            }

            for (int i = 0; i < occupied.Count; i++)
            {
                _demolishOccupiedToAnchor.Remove(occupied[i]);
            }

            var evt = new DemolishDesignationChangedEvent
            {
                Anchor = anchor,
                ItemId = designation.ItemId,
                IsActive = false
            };
            _eventBus.Publish(ref evt);
        }

        private void OnPlacedItemRemoved(ref PlacedItemRemovedEvent evt)
        {
            RemoveDemolishDesignation(evt.Anchor);
        }

        private MetaFort.GameEntry ResolveGameEntry()
        {
            if (CoreSourcePath != null && !CoreSourcePath.IsEmpty)
            {
                return GetNodeOrNull<MetaFort.GameEntry>(CoreSourcePath);
            }

            return GetNodeOrNull<MetaFort.GameEntry>("..") ?? MetaFort.GameEntry.Instance;
        }
    }
}
