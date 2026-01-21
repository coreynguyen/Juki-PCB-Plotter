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
        public PlacementEditorView()
        {
            InitializeComponent();
        }

        private void PartNumberComboBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var comboBox = sender as ComboBox;
            if (comboBox == null) return;

            var placement = comboBox.DataContext as Placement;
            if (placement == null) return;

            var vm = DataContext as PlacementEditorViewModel;
            if (vm?.Project == null) return;

            // Get the typed text
            string typedText = comboBox.Text?.Trim();
            if (string.IsNullOrEmpty(typedText)) return;

            // Check if there's already a component with this part number
            var existingComponent = vm.Project.Components.FirstOrDefault(
                c => string.Equals(c.PartNumber, typedText, System.StringComparison.OrdinalIgnoreCase));

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
    }
}
