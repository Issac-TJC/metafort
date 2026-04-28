using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using MetaFort.Core.EventBus;
using MetaFort.Core.EventBus.Events;
using MetaFort.Core.Items;

namespace MetaFort.UI
{
    public partial class BuildingPlannerPanel : Node
    {
        [Export]
        public string PlannerTitle { get; set; } = "Build Planner";

        [Export]
        public NodePath EventBusSourcePath { get; set; }

        private CanvasLayer _layer;
        private MarginContainer _root;
        private PanelContainer _panel;
        private VBoxContainer _stack;
        private Button _toggleButton;
        private Label _modeLabel;
        private HBoxContainer _categoryRow;
        private VBoxContainer _itemList;
        private Button _cancelButton;
        private IEventBus _eventBus;

        private readonly Dictionary<string, List<ItemDefinition>> _buildablesByCategory = new Dictionary<string, List<ItemDefinition>>();
        private string _selectedCategory = string.Empty;
        private string _activeItemId = string.Empty;
        private bool _placementActive;
        private bool _isExpanded;

        public override void _Ready()
        {
            ResolveEventBus();
            SubscribeEvents();
            BuildCatalog();
            BuildUi();
            RefreshCategoryButtons();
            RefreshItemButtons();
            SetExpanded(false);
            UpdateModeLabel();
        }

        public override void _ExitTree()
        {
            if (_eventBus != null)
            {
                _eventBus.Unsubscribe<MapCursorModeChangedEvent>(OnCursorModeChanged);
            }
        }

        public void SetPlacementState(bool isActive, string itemId)
        {
            _placementActive = isActive;
            _activeItemId = itemId ?? string.Empty;
            if (!_placementActive)
            {
                SetExpanded(false);
            }
            UpdateModeLabel();
            RefreshItemButtons();
        }

        private void BuildCatalog()
        {
            _buildablesByCategory.Clear();
            foreach (ItemDefinition item in ItemConfigManager.GetBuildableItems())
            {
                string category = item.ResolveBuildCategory();
                if (!_buildablesByCategory.TryGetValue(category, out List<ItemDefinition> list))
                {
                    list = new List<ItemDefinition>();
                    _buildablesByCategory[category] = list;
                }

                list.Add(item);
            }

            if (string.IsNullOrEmpty(_selectedCategory) && _buildablesByCategory.Count > 0)
            {
                _selectedCategory = _buildablesByCategory.Keys.OrderBy(key => key).First();
            }
        }

        private void BuildUi()
        {
            _layer = new CanvasLayer { Layer = 140 };
            AddChild(_layer);

            _root = new MarginContainer();
            _root.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
            _root.OffsetLeft = 18f;
            _root.OffsetBottom = -18f;
            _root.OffsetTop = -220f;
            _root.OffsetRight = 250f;
            _layer.AddChild(_root);

            _panel = new PanelContainer();
            _root.AddChild(_panel);

            _stack = new VBoxContainer();
            _panel.AddChild(_stack);

            _toggleButton = new Button { Text = PlannerTitle, CustomMinimumSize = new Vector2(220, 40) };
            _toggleButton.Pressed += TogglePlanner;
            _stack.AddChild(_toggleButton);

            _modeLabel = new Label();
            _stack.AddChild(_modeLabel);

            _categoryRow = new HBoxContainer();
            _stack.AddChild(_categoryRow);

            _itemList = new VBoxContainer();
            _stack.AddChild(_itemList);

            _cancelButton = new Button { Text = "Cancel Placement", Visible = false };
            _cancelButton.Pressed += () =>
            {
                PublishCursorModeRequest(new MapCursorModeState
                {
                    Kind = MapCursorModeKind.None
                });
            };
            _stack.AddChild(_cancelButton);
        }

        private void TogglePlanner()
        {
            SetExpanded(!_isExpanded);
        }

        private void UpdateModeLabel()
        {
            if (_placementActive && ItemConfigManager.TryGetItem(_activeItemId, out ItemDefinition active))
            {
                _modeLabel.Text = $"Mode: placing {active.ResolvePlannerLabel()} blueprint";
            }
            else
            {
                _modeLabel.Text = "Mode: normal";
            }

            _modeLabel.Visible = _placementActive;
            _cancelButton.Visible = _placementActive;
        }

        private void RefreshCategoryButtons()
        {
            foreach (Node child in _categoryRow.GetChildren())
            {
                child.QueueFree();
            }

            foreach (string category in _buildablesByCategory.Keys.OrderBy(key => key))
            {
                string capture = category;
                Button button = new Button
                {
                    Text = capture,
                    ToggleMode = true,
                    ButtonPressed = capture == _selectedCategory,
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
                };
                button.Pressed += () =>
                {
                    _selectedCategory = capture;
                    RefreshCategoryButtons();
                    RefreshItemButtons();
                };
                _categoryRow.AddChild(button);
            }
        }

        private void RefreshItemButtons()
        {
            foreach (Node child in _itemList.GetChildren())
            {
                child.QueueFree();
            }

            if (!_buildablesByCategory.TryGetValue(_selectedCategory, out List<ItemDefinition> items))
            {
                return;
            }

            foreach (ItemDefinition item in items.OrderBy(value => value.ResolvePlannerLabel()))
            {
                string itemId = item.id;
                bool selected = itemId == _activeItemId && _placementActive;
                Button button = new Button
                {
                    Text = selected ? $"{item.ResolvePlannerLabel()} (Active)" : item.ResolvePlannerLabel(),
                    ToggleMode = false,
                    CustomMinimumSize = new Vector2(220, 34)
                };
                button.Pressed += () =>
                {
                    SetExpanded(false);
                    PublishCursorModeRequest(new MapCursorModeState
                    {
                        Kind = MapCursorModeKind.BuildBlueprint,
                        ItemId = itemId,
                        DisplayLabel = item.ResolvePlannerLabel()
                    });
                };
                _itemList.AddChild(button);
            }
        }

        private void SetExpanded(bool expanded)
        {
            _isExpanded = expanded;
            _categoryRow.Visible = expanded;
            _itemList.Visible = expanded;
        }

        private void ResolveEventBus()
        {
            if (EventBusSourcePath == null || EventBusSourcePath.IsEmpty)
            {
                return;
            }

            if (GetNodeOrNull(EventBusSourcePath) is MetaFort.GameEntry gameEntry)
            {
                _eventBus = gameEntry.EventBus;
            }
        }

        private void SubscribeEvents()
        {
            if (_eventBus == null)
            {
                return;
            }

            _eventBus.Subscribe<MapCursorModeChangedEvent>(OnCursorModeChanged);
        }

        private void PublishCursorModeRequest(MapCursorModeState mode)
        {
            if (_eventBus == null)
            {
                GD.PrintErr("[BuildingPlannerPanel] EventBus is missing. Cursor mode request was not published.");
                return;
            }

            var evt = new MapCursorModeRequestEvent { Mode = mode };
            _eventBus.Publish(ref evt);
        }

        private void OnCursorModeChanged(ref MapCursorModeChangedEvent evt)
        {
            bool isBuildMode = evt.Mode.Kind == MapCursorModeKind.BuildBlueprint;
            SetPlacementState(isBuildMode, isBuildMode ? evt.Mode.ItemId : string.Empty);
        }
    }
}
