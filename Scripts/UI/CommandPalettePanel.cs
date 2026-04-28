using Godot;
using MetaFort.Core.EventBus;
using MetaFort.Core.EventBus.Events;

namespace MetaFort.UI
{
    public partial class CommandPalettePanel : Node
    {
        [Export]
        public NodePath EventBusSourcePath { get; set; }

        private IEventBus _eventBus;
        private CanvasLayer _layer;
        private MarginContainer _root;
        private PanelContainer _panel;
        private VBoxContainer _stack;
        private Label _modeLabel;

        public override void _Ready()
        {
            ResolveEventBus();
            if (_eventBus == null)
            {
                GD.PrintErr("[CommandPalettePanel] Missing EventBus.");
                return;
            }

            _eventBus.Subscribe<MapCursorModeChangedEvent>(OnModeChanged);
            BuildUi();
            UpdateModeLabel(MapCursorModeKind.None);
        }

        public override void _ExitTree()
        {
            if (_eventBus != null)
            {
                _eventBus.Unsubscribe<MapCursorModeChangedEvent>(OnModeChanged);
            }
        }

        private void ResolveEventBus()
        {
            if (EventBusSourcePath != null && !EventBusSourcePath.IsEmpty && GetNodeOrNull(EventBusSourcePath) is MetaFort.GameEntry gameEntry)
            {
                _eventBus = gameEntry.EventBus;
            }
        }

        private void BuildUi()
        {
            _layer = new CanvasLayer { Layer = 145 };
            _root = new MarginContainer();
            _root.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
            _root.OffsetRight = -18f;
            _root.OffsetBottom = -18f;
            _root.OffsetLeft = -230f;
            _root.OffsetTop = -220f;

            _panel = new PanelContainer();
            _stack = new VBoxContainer();
            _modeLabel = new Label();

            _stack.AddChild(new Label { Text = "Command Menu" });
            _stack.AddChild(_modeLabel);
            _stack.AddChild(CreateModeButton("Dig", MapCursorModeKind.DigDesignation, "cmd_dig", "Dig"));
            _stack.AddChild(CreateModeButton("Demolish", MapCursorModeKind.DemolishDesignation, "cmd_demolish", "Demolish"));
            _stack.AddChild(CreateModeButton("Cancel Designation", MapCursorModeKind.CancelDesignation, "cmd_cancel", "Cancel"));

            _panel.AddChild(_stack);
            _root.AddChild(_panel);
            _layer.AddChild(_root);
            AddChild(_layer);
        }

        private Button CreateModeButton(string text, MapCursorModeKind mode, string markerKey, string displayLabel)
        {
            Button button = new Button
            {
                Text = text,
                CustomMinimumSize = new Vector2(210f, 36f)
            };
            button.Pressed += () =>
            {
                if (_eventBus == null)
                {
                    return;
                }

                var evt = new MapCursorModeRequestEvent
                {
                    Mode = new MapCursorModeState
                    {
                        Kind = mode,
                        MarkerKey = markerKey,
                        DisplayLabel = displayLabel
                    }
                };
                _eventBus.Publish(ref evt);
            };
            return button;
        }

        private void OnModeChanged(ref MapCursorModeChangedEvent evt)
        {
            UpdateModeLabel(evt.Mode.Kind);
        }

        private void UpdateModeLabel(MapCursorModeKind mode)
        {
            _modeLabel.Text = mode switch
            {
                MapCursorModeKind.DigDesignation => "Mode: dig designation",
                MapCursorModeKind.DemolishDesignation => "Mode: demolish designation",
                MapCursorModeKind.CancelDesignation => "Mode: cancel designation",
                _ => "Mode: normal"
            };
        }
    }
}
