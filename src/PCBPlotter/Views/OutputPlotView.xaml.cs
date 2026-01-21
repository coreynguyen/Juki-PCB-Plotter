using System.Windows;
using System.Windows.Controls;
using PCBPlotter.ViewModels;

namespace PCBPlotter.Views
{
    /// <summary>
    /// Output plot view (main design canvas)
    /// </summary>
    public partial class OutputPlotView : UserControl
    {
        public OutputPlotView()
        {
            InitializeComponent();

            // Wire up canvas events
            DesignCanvas.CursorPositionChanged += OnCursorPositionChanged;
            DesignCanvas.SelectionRectCompleted += OnSelectionRectCompleted;
            DesignCanvas.PointClicked += OnPointClicked;
            DesignCanvas.SizeChanged += OnCanvasSizeChanged;
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
    }
}
