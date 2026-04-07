using System;
using System.Collections.Generic;
using Godot;
using MetaFort.Core.Spatial;

namespace MetaFort.Core.Items
{
    public readonly struct ItemInteractionContext
    {
        public uint ActorEntityId { get; init; }
        public GridPosition Target { get; init; }
        public GridPosition Anchor { get; init; }
        public ItemDefinition Definition { get; init; }
        public ItemSystemNode.PlacedItemRecord Record { get; init; }
    }

    public interface IItemBehavior
    {
        string BehaviorId { get; }
        bool TryUse(in ItemInteractionContext context, out string message);
    }

    public static class ItemBehaviorRegistry
    {
        private static readonly Dictionary<string, IItemBehavior> Behaviors = new Dictionary<string, IItemBehavior>(StringComparer.OrdinalIgnoreCase);
        private static bool _builtInsRegistered;

        public static void EnsureBuiltInsRegistered()
        {
            if (_builtInsRegistered)
            {
                return;
            }

            Register(new DebugBellItemBehavior());
            Register(new LadderItemBehavior());
            _builtInsRegistered = true;
        }

        public static void Register(IItemBehavior behavior)
        {
            if (behavior == null || string.IsNullOrWhiteSpace(behavior.BehaviorId))
            {
                throw new ArgumentException("Item behavior must provide a stable BehaviorId.");
            }

            Behaviors[behavior.BehaviorId] = behavior;
        }

        public static bool TryGet(string behaviorId, out IItemBehavior behavior)
        {
            if (string.IsNullOrWhiteSpace(behaviorId))
            {
                behavior = null;
                return false;
            }

            return Behaviors.TryGetValue(behaviorId, out behavior);
        }

        private sealed class DebugBellItemBehavior : IItemBehavior
        {
            public string BehaviorId => "DebugBellBehavior";

            public bool TryUse(in ItemInteractionContext context, out string message)
            {
                GD.Print($"[DebugBellBehavior] Ring Ring! actor={context.ActorEntityId} at {context.Target}, anchor={context.Anchor}.");
                message = $"Used {context.Definition.displayName}.";
                return true;
            }
        }

        private sealed class LadderItemBehavior : IItemBehavior
        {
            public string BehaviorId => "LadderBehavior";

            public bool TryUse(in ItemInteractionContext context, out string message)
            {
                GD.Print($"[LadderBehavior] Future hook: actor={context.ActorEntityId} can request cross-Z traversal at anchor={context.Anchor}.");
                message = $"Used {context.Definition.displayName}.";
                return true;
            }
        }
    }
}
