using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PCBPlotter.Core.Models;
using PCBPlotter.ViewModels;
using Component = PCBPlotter.Core.Models.Component;

namespace PCBPlotter.Views
{
    /// <summary>
    /// Placement editor view
    /// </summary>
    public partial class PlacementEditorView : UserControl
    {
        private bool _isSyncing = false;
        private System.Action<PCBPlotter.Core.Events.SelectionChangedEvent> _selectionHandler;

        public PlacementEditorView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Subscribe to selection changed events from other tabs
            _selectionHandler = OnExternalSelectionChanged;
            PCBPlotter.Core.Events.EventAggregator.Instance
                .Subscribe(_selectionHandler);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_selectionHandler != null)
            {
                PCBPlotter.Core.Events.EventAggregator.Instance
                    .Unsubscribe(_selectionHandler);
            }
        }

        private void OnExternalSelectionChanged(PCBPlotter.Core.Events.SelectionChangedEvent e)
        {
            var vm = DataContext as PlacementEditorViewModel;
            if (vm == null || e.Source == vm) return;

            // Update DataGrid selection to match external selection
            _isSyncing = true;
            try
            {
                PlacementsGrid.SelectedItems.Clear();
                foreach (var p in e.SelectedPlacements)
                {
                    PlacementsGrid.SelectedItems.Add(p);
                }
            }
            finally
            {
                _isSyncing = false;
            }
        }

        private void PlacementsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncing) return;

            var vm = DataContext as PlacementEditorViewModel;
            if (vm == null) return;

            // Sync the DataGrid's selected items with the ViewModel's SelectedPlacements
            vm.SelectedPlacements.Clear();
            var selectedList = new System.Collections.Generic.List<Placement>();
            foreach (Placement item in PlacementsGrid.SelectedItems)
            {
                if (item != null)
                {
                    vm.SelectedPlacements.Add(item);
                    item.IsSelected = true;
                    selectedList.Add(item);
                }
            }

            // Update unselected items
            if (vm.Project?.Placements != null)
            {
                foreach (var p in vm.Project.Placements)
                {
                    if (!selectedList.Contains(p))
                    {
                        p.IsSelected = false;
                    }
                }
            }

            // Publish selection changed event for other tabs to sync
            PCBPlotter.Core.Events.EventAggregator.Instance.Publish(
                new PCBPlotter.Core.Events.SelectionChangedEvent
                {
                    SelectedPlacements = selectedList,
                    Source = vm
                });
        }

        private void PartNumberComboBox_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                var comboBox = sender as ComboBox;
                if (comboBox == null) return;

                var placement = comboBox.DataContext as Placement;
                if (placement == null) return;

                var vm = DataContext as PlacementEditorViewModel;
                if (vm?.Project?.Components == null) return;

                // Get the typed text
                string typedText = comboBox.Text?.Trim();
                if (string.IsNullOrEmpty(typedText)) return;

                // If the text matches the current component, no change needed
                if (placement.Component != null &&
                    string.Equals(placement.Component.PartNumber, typedText, System.StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                // Check if there's already a component with this part number
                var existingComponent = vm.Project.Components.FirstOrDefault(
                    c => c != null && string.Equals(c.PartNumber, typedText, System.StringComparison.OrdinalIgnoreCase));

                if (existingComponent != null)
                {
                    // Assign existing component
                    placement.Component = existingComponent;
                }
                else
                {
                    // Create new component with this part number
                    var newComponent = new Component
                    {
                        PartNumber = typedText
                    };
                    vm.Project.Components.Add(newComponent);
                    placement.Component = newComponent;
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error in PartNumberComboBox_LostFocus: " + ex.Message);
            }
        }
    }
}
