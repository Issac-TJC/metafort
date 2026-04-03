using Godot;
using MetaFort.Core.EventBus;
using MetaFort.Core.EventBus.Events;

namespace MetaFort.UI
{
    public partial class ContextActionMenuUI : Node
    {
        private IEventBus _eventBus;
        private CanvasLayer _layer;
        private PanelContainer _panel;
        private VBoxContainer _buttons;

        public void Initialize(IEventBus eventBus)
        {
            _eventBus = eventBus;
            _eventBus.Subscribe<ContextActionMenuRequestEvent>(OnMenuRequest);
            BuildUI();
        }

        private void BuildUI()
        {
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
                Button btn = new Button { Text = option.Label, CustomMinimumSize = new Vector2(170, 34) };
                btn.Pressed += () =>
                {
                    var selected = new ContextActionSelectedEvent
                    {
                        ActorEntityId = evt.ActorEntityId,
                        Selected = option
                    };
                    _eventBus.Publish(ref selected);
                    _panel.Visible = false;
                };
                _buttons.AddChild(btn);
            }

            _panel.Position = evt.ScreenPosition;
            _panel.Visible = true;
            GD.Print($"[ContextActionMenuUI] Show menu with {evt.Options.Length} options at {evt.ScreenPosition}");
        }
    }
}
