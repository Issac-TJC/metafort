using Godot;
using MetaFort.Core.EventBus;
using MetaFort.Core.EventBus.Events;
using MetaFort.Core.Systems;

namespace MetaFort.UI
{
    public partial class StockpileHudPanel : Node
    {
        [Export]
        public NodePath EventBusSourcePath { get; set; }

        [Export]
        public NodePath StockpileSourcePath { get; set; }

        private IEventBus _eventBus;
        private PlayerStockpileNode _stockpile;
        private CanvasLayer _layer;
        private MarginContainer _root;
        private PanelContainer _panel;
        private VBoxContainer _rows;

        public override void _Ready()
        {
            ResolveDependencies();
            if (_eventBus == null || _stockpile == null)
            {
                GD.PrintErr("[StockpileHudPanel] Missing EventBus or Stockpile node.");
                return;
            }

            _eventBus.Subscribe<StockpileChangedEvent>(OnStockpileChanged);
            BuildUi();
            Refresh(_stockpile.GetDisplayEntries());
        }

        public override void _ExitTree()
        {
            if (_eventBus != null)
            {
                _eventBus.Unsubscribe<StockpileChangedEvent>(OnStockpileChanged);
            }
        }

        private void ResolveDependencies()
        {
            if (EventBusSourcePath != null && !EventBusSourcePath.IsEmpty && GetNodeOrNull(EventBusSourcePath) is MetaFort.GameEntry gameEntry)
            {
                _eventBus = gameEntry.EventBus;
            }

            if (StockpileSourcePath != null && !StockpileSourcePath.IsEmpty)
            {
                _stockpile = GetNodeOrNull<PlayerStockpileNode>(StockpileSourcePath);
            }
            else
            {
                _stockpile = GetNodeOrNull<PlayerStockpileNode>("../PlayerStockpileNode");
            }
        }

        private void BuildUi()
        {
            if (_layer != null)
            {
                return;
            }

            _layer = new CanvasLayer { Layer = 150 };
            _root = new MarginContainer();
            _root.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            _root.OffsetLeft = 18f;
            _root.OffsetTop = 18f;
            _root.OffsetRight = 220f;
            _root.OffsetBottom = 240f;

            _panel = new PanelContainer();
            _rows = new VBoxContainer();

            _panel.AddChild(_rows);
            _root.AddChild(_panel);
            _layer.AddChild(_root);
            AddChild(_layer);
        }

        private void OnStockpileChanged(ref StockpileChangedEvent evt)
        {
            Refresh(evt.Entries);
        }

        private void Refresh(StockpileEntryData[] entries)
        {
            if (_rows == null)
            {
                return;
            }

            foreach (Node child in _rows.GetChildren())
            {
                child.QueueFree();
            }

            if (entries == null)
            {
                return;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                Label label = new Label
                {
                    Text = $"{entries[i].Label} x {entries[i].Count}"
                };
                _rows.AddChild(label);
            }
        }
    }
}
