using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PCBPlotter.ViewModels;

namespace PCBPlotter.Views
{
    /// <summary>
    /// Output plot view (main design canvas)
    /// </summary>
    public partial class OutputPlotView : UserControl
    {
        private bool _isRecallingSet = false;
        private bool _isSyncing = false;
        private System.Action<PCBPlotter.Core.Events.SelectionChangedEvent> _selectionHandler;

        public OutputPlotView()
        {
            InitializeComponent();

            // Wire up canvas events
            DesignCanvas.CursorPositionChanged += OnCursorPositionChanged;
            DesignCanvas.SelectionRectCompleted += OnSelectionRectCompleted;
            DesignCanvas.PointClicked += OnPointClicked;
            DesignCanvas.SizeChanged += OnCanvasSizeChanged;
            DesignCanvas.SelectionChanged += OnCanvasSelectionChanged;

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Subscribe to selection changed events from other tabs
            _selectionHandler = OnExternalSelectionChanged;
            PCBPlotter.Core.Events.EventAggregator.Instance.Subscribe(_selectionHandler);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_selectionHandler != null)
            {
                PCBPlotter.Core.Events.EventAggregator.Instance.Unsubscribe(_selectionHandler);
            }
        }

        private void OnExternalSelectionChanged(PCBPlotter.Core.Events.SelectionChangedEvent evt)
        {
            var vm = DataContext as OutputPlotViewModel;
            if (vm == null || evt.Source == vm) return;

            // Update canvas selection to match external selection
            _isSyncing = true;
            try
            {
                // Clear current selection
                if (vm.Project?.Placements != null)
                {
                    foreach (var p in vm.Project.Placements)
                    {
                        p.IsSelected = false;
                    }
                }

                // Apply new selection
                vm.SelectedPlacements.Clear();
                foreach (var p in evt.SelectedPlacements)
                {
                    p.IsSelected = true;
                    vm.SelectedPlacements.Add(p);
                }

                // Redraw canvas
                DesignCanvas.InvalidateVisual();
            }
            finally
            {
                _isSyncing = false;
            }
        }

        private void OnCanvasSelectionChanged(object sender, System.Collections.Generic.List<PCBPlotter.Core.Models.Placement> selected)
        {
            if (_isSyncing) return;

            var vm = DataContext as OutputPlotViewModel;
            if (vm == null) return;

            // Update view model's selection
            vm.SelectedPlacements.Clear();
            foreach (var p in selected)
            {
                vm.SelectedPlacements.Add(p);
            }

            // Publish selection changed event for other tabs to sync
            PCBPlotter.Core.Events.EventAggregator.Instance.Publish(
                new PCBPlotter.Core.Events.SelectionChangedEvent
                {
                    SelectedPlacements = selected,
                    Source = vm
                });
        }

        private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e)
        {
            var vm = DataContext as OutputPlotViewModel;
            if (vm != null)
            {
                vm.ViewportWidth = e.NewSize.Width;
                vm.ViewportHeight = e.NewSize.Height;
            }
        }

        private void OnCursorPositionChanged(object sender, Point worldPos)
        {
            var vm = DataContext as OutputPlotViewModel;
            if (vm != null)
            {
                vm.CursorPosition = worldPos;
            }
        }

        private void OnSelectionRectCompleted(object sender, Rect worldRect)
        {
            var vm = DataContext as OutputPlotViewModel;
            if (vm != null)
            {
                bool addToSelection = System.Windows.Input.Keyboard.Modifiers.HasFlag(
                    System.Windows.Input.ModifierKeys.Shift);
                vm.SelectPlacementsInRect(worldRect, addToSelection);
            }
        }

        private void OnPointClicked(object sender, Point worldPos)
        {
            var vm = DataContext as OutputPlotViewModel;
            if (vm != null)
            {
                // TODO: Hit test for placements at clicked point
            }
        }

        private void SelectionSetComboBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var comboBox = sender as ComboBox;
                var vm = DataContext as OutputPlotViewModel;
                if (comboBox == null || vm == null) return;

                string name = comboBox.Text?.Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    // Create or update selection set with current selection
                    vm.CreateOrUpdateSelectionSet(name);
                    e.Handled = true;
                }
            }
        }

        private void SelectionSetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRecallingSet) return;

            var comboBox = sender as ComboBox;
            var vm = DataContext as OutputPlotViewModel;
            if (comboBox == null || vm == null) return;

            string selectedName = comboBox.SelectedItem as string;
            if (!string.IsNullOrEmpty(selectedName))
            {
                _isRecallingSet = true;
                try
                {
                    // Recall the selection set
                    var placements = vm.RecallSelectionSet(selectedName);
                    if (placements != null && placements.Count > 0)
                    {
                        // Clear current selection and select the set
                        foreach (var p in vm.SelectedPlacements)
                        {
                            p.IsSelected = false;
                        }
                        vm.SelectedPlacements.Clear();

                        foreach (var p in placements)
                        {
                            p.IsSelected = true;
                            vm.SelectedPlacements.Add(p);
                        }

                        // Publish selection changed event
                        PCBPlotter.Core.Events.EventAggregator.Instance.Publish(
                            new PCBPlotter.Core.Events.SelectionChangedEvent
                            {
                                SelectedPlacements = placements,
                                Source = vm
                            });

                        DesignCanvas.InvalidateVisual();
                    }
                }
                finally
                {
                    _isRecallingSet = false;
                }
            }
        }
    }
}
