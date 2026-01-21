using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        private bool _isSelectingSet = false;

        public PlacementEditorView()
        {
            InitializeComponent();
        }

        private void SelectionSetComboBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var comboBox = sender as ComboBox;
                var vm = DataContext as PlacementEditorViewModel;
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
            if (_isSelectingSet) return;

            var comboBox = sender as ComboBox;
            var vm = DataContext as PlacementEditorViewModel;
            if (comboBox == null || vm == null) return;

            string selectedName = comboBox.SelectedItem as string;
            if (!string.IsNullOrEmpty(selectedName))
            {
                _isSelectingSet = true;
                try
                {
                    // Recall the selection set
                    var placements = vm.RecallSelectionSet(selectedName);
                    if (placements != null && placements.Count > 0)
                    {
                        PlacementsGrid.SelectedItems.Clear();
                        foreach (var p in placements)
                        {
                            PlacementsGrid.SelectedItems.Add(p);
                        }
                    }
                }
                finally
                {
                    _isSelectingSet = false;
                }
            }
        }

        private void PlacementsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var vm = DataContext as PlacementEditorViewModel;
            if (vm == null) return;

            // Sync the DataGrid's selected items with the ViewModel's SelectedPlacements
            vm.SelectedPlacements.Clear();
            foreach (Placement item in PlacementsGrid.SelectedItems)
            {
                if (item != null)
                {
                    vm.SelectedPlacements.Add(item);
                }
            }
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
