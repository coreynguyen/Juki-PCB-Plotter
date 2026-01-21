using System.Windows;
using System.Windows.Controls;
using PCBPlotter.ViewModels;

namespace PCBPlotter.Views
{
    /// <summary>
    /// Gerber viewer view
    /// </summary>
    public partial class GerberViewerView : UserControl
    {
        public GerberViewerView()
        {
            InitializeComponent();

            GerberCanvas.CursorPositionChanged += OnCursorPositionChanged;
            GerberCanvas.SelectionRectCompleted += OnSelectionRectCompleted;
        }

        private void OnCursorPositionChanged(object sender, Point worldPos)
        {
            var vm = DataContext as GerberViewerViewModel;
            if (vm != null)
            {
                vm.CursorPosition = worldPos;
            }
        }

        private void OnSelectionRectCompleted(object sender, Rect worldRect)
        {
            var vm = DataContext as GerberViewerViewModel;
            if (vm != null)
            {
                bool addToSelection = System.Windows.Input.Keyboard.Modifiers.HasFlag(
                    System.Windows.Input.ModifierKeys.Shift);
                vm.SelectPrimitivesInRect(worldRect, addToSelection);
            }
        }
    }
}
