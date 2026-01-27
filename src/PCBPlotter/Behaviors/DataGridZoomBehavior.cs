using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PCBPlotter.Behaviors
{
    /// <summary>
    /// Attached behavior that enables Ctrl+Scroll zoom for DataGrid controls
    /// </summary>
    public static class DataGridZoomBehavior
    {
        public static readonly DependencyProperty EnableZoomProperty =
            DependencyProperty.RegisterAttached(
                "EnableZoom",
                typeof(bool),
                typeof(DataGridZoomBehavior),
                new PropertyMetadata(false, OnEnableZoomChanged));

        public static bool GetEnableZoom(DependencyObject obj)
        {
            return (bool)obj.GetValue(EnableZoomProperty);
        }

        public static void SetEnableZoom(DependencyObject obj, bool value)
        {
            obj.SetValue(EnableZoomProperty, value);
        }

        public static readonly DependencyProperty ZoomLevelProperty =
            DependencyProperty.RegisterAttached(
                "ZoomLevel",
                typeof(double),
                typeof(DataGridZoomBehavior),
                new PropertyMetadata(1.0, OnZoomLevelChanged));

        public static double GetZoomLevel(DependencyObject obj)
        {
            return (double)obj.GetValue(ZoomLevelProperty);
        }

        public static void SetZoomLevel(DependencyObject obj, double value)
        {
            obj.SetValue(ZoomLevelProperty, value);
        }

        private static void OnEnableZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var dataGrid = d as DataGrid;
            if (dataGrid == null) return;

            if ((bool)e.NewValue)
            {
                dataGrid.PreviewMouseWheel += DataGrid_PreviewMouseWheel;

                // Apply initial transform
                if (dataGrid.LayoutTransform == null || !(dataGrid.LayoutTransform is ScaleTransform))
                {
                    dataGrid.LayoutTransform = new ScaleTransform(1.0, 1.0);
                }
            }
            else
            {
                dataGrid.PreviewMouseWheel -= DataGrid_PreviewMouseWheel;
            }
        }

        private static void DataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control)
                return;

            var dataGrid = sender as DataGrid;
            if (dataGrid == null) return;

            e.Handled = true;

            double currentZoom = GetZoomLevel(dataGrid);
            double delta = e.Delta > 0 ? 0.1 : -0.1;
            double newZoom = currentZoom + delta;

            // Clamp zoom level between 0.5 and 2.0
            newZoom = System.Math.Max(0.5, System.Math.Min(2.0, newZoom));

            SetZoomLevel(dataGrid, newZoom);
        }

        private static void OnZoomLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var dataGrid = d as DataGrid;
            if (dataGrid == null) return;

            double zoom = (double)e.NewValue;

            var transform = dataGrid.LayoutTransform as ScaleTransform;
            if (transform == null)
            {
                transform = new ScaleTransform(zoom, zoom);
                dataGrid.LayoutTransform = transform;
            }
            else
            {
                transform.ScaleX = zoom;
                transform.ScaleY = zoom;
            }
        }
    }
}
