using System.Windows.Controls;
using PCBPlotter.ViewModels;

namespace PCBPlotter.Views
{
    /// <summary>
    /// Component/Package editor view
    /// </summary>
    public partial class ComponentEditorView : UserControl
    {
        public ComponentEditorView()
        {
            InitializeComponent();

            // Wire up canvas selection events
            PackageCanvas.GraphicSelectionChanged += OnGraphicSelectionChanged;
            PackageCanvas.PinSelectionChanged += OnPinSelectionChanged;
        }

        private void OnGraphicSelectionChanged(object sender, int graphicIndex)
        {
            var vm = DataContext as ComponentEditorViewModel;
            if (vm?.SelectedPackage == null) return;

            if (graphicIndex >= 0 && graphicIndex < vm.SelectedPackage.Graphics.Count)
            {
                vm.SelectedGraphic = vm.SelectedPackage.Graphics[graphicIndex];
            }
            else
            {
                vm.SelectedGraphic = null;
            }
        }

        private void OnPinSelectionChanged(object sender, int pinIndex)
        {
            var vm = DataContext as ComponentEditorViewModel;
            if (vm == null) return;

            // For now, we don't have SelectedPin in the VM
            // Could be extended to support pin property editing
            vm.SelectedGraphic = null;
        }
    }
}
