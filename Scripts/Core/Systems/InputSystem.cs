using Godot;
using MetaFort.Core.EventBus;
using MetaFort.UI;

namespace MetaFort.Core.Systems
{
    public partial class InputSystem : Node
    {
        private IEventBus _eventBus;

        public void Initialize(IEventBus eventBus)
        {
            _eventBus = eventBus;
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (@event is InputEventKey escapeEvent && escapeEvent.Pressed && !escapeEvent.Echo && escapeEvent.Keycode == Key.Escape)
            {
                var toggleEvent = new TogglePauseMenuEvent();
                _eventBus.Publish(ref toggleEvent);
                GetTree().Root.SetInputAsHandled(); 
            }
        }
    }
}
