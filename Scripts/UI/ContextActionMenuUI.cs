using Godot;
using MetaFort.Core.EventBus;
using MetaFort.Core.EventBus.Events;

namespace MetaFort.UI
{
    public partial class ContextActionMenuUI : Node
    {
        [Export]
        public NodePath EventBusSourcePath { get; set; }

        private IEventBus _eventBus;
        private CanvasLayer _layer;
        private PanelContainer _panel;
        private VBoxContainer _buttons;
        private bool _initialized;

        public override void _Ready()
        {
            if (_initialized)
            {
                EnsureUiBuilt();
                return;
            }

            if (EventBusSourcePath == null || EventBusSourcePath.IsEmpty)
            {
                GD.PrintErr($"[ContextActionMenuUI] Missing EventBusSourcePath on node '{GetPath()}'.");
                return;
            }

            Node source = GetNodeOrNull(EventBusSourcePath);
            if (source is not MetaFort.GameEntry gameEntry)
            {
                GD.PrintErr($"[ContextActionMenuUI] EventBusSourcePath '{EventBusSourcePath}' must point to a GameEntry node.");
                return;
            }

            if (gameEntry.EventBus == null)
            {
                GD.PrintErr($"[ContextActionMenuUI] GameEntry at '{EventBusSourcePath}' has no EventBus yet.");
                return;
            }

            Initialize(gameEntry.EventBus);
        }

        public override void _ExitTree()
        {
            if (_initialized && _eventBus != null)
            {
                _eventBus.Unsubscribe<ContextActionMenuRequestEvent>(OnMenuRequest);
            }

            _initialized = false;
            _eventBus = null;
        }

        public void Initialize(IEventBus eventBus)
        {
            if (_initialized) return;
            if (eventBus == null)
            {
                GD.PrintErr("[ContextActionMenuUI] Initialize failed because EventBus is null.");
                return;
            }

            _eventBus = eventBus;
            _eventBus.Subscribe<ContextActionMenuRequestEvent>(OnMenuRequest);
            EnsureUiBuilt();
            _initialized = true;
        }

        private void EnsureUiBuilt()
        {
            if (_layer != null) return;

            _layer = new CanvasLayer { Layer = 120 };
            _panel = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
            _panel.CustomMinimumSize = new Vector2(180, 1);
            _buttons = new VBoxContainer();
            _panel.AddChild(_buttons);
            _layer.AddChild(_panel);
            AddChild(_layer);
        }

        private void OnMenuRequest(ref ContextActionMenuRequestEvent evt)
        {
            foreach (Node child in _buttons.GetChildren())
            {
                child.QueueFree();
            }

            if (evt.Options == null || evt.Options.Length == 0)
            {
                _panel.Visible = false;
                return;
            }

            for (int i = 0; i < evt.Options.Length; i++)
            {
                var option = evt.Options[i];
                uint actorEntityId = evt.ActorEntityId;
                Button btn = new Button { Text = option.Label, CustomMinimumSize = new Vector2(170, 34) };
                btn.Pressed += () =>
                {
                    var selected = new ContextActionSelectedEvent
                    {
                        ActorEntityId = actorEntityId,
                        Selected = option
                    };
                    _eventBus.Publish(ref selected);
                    _panel.Visible = false;
                };
                _buttons.AddChild(btn);
            }

            Vector2 desiredPosition = evt.ScreenPosition;
            Vector2 menuSize = _panel.GetCombinedMinimumSize();
            Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
            desiredPosition.X = Mathf.Clamp(desiredPosition.X, 0, Mathf.Max(0, viewportSize.X - menuSize.X));
            desiredPosition.Y = Mathf.Clamp(desiredPosition.Y, 0, Mathf.Max(0, viewportSize.Y - menuSize.Y));

            _panel.Position = desiredPosition;
            _panel.Visible = true;
            GD.Print($"[ContextActionMenuUI] Show menu with {evt.Options.Length} options at {desiredPosition}");
        }
    }
}
