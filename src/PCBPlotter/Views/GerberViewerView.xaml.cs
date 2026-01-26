using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PCBPlotter.Controls;
using PCBPlotter.Core.Models;
using PCBPlotter.ViewModels;

namespace PCBPlotter.Views
{
    /// <summary>
    /// Gerber viewer view
    /// </summary>
    public partial class GerberViewerView : UserControl
    {
        private bool _useOpenGL = false;
        // Preset colors for quick layer color selection
        private static readonly Color[] _presetColors = new[]
        {
            Color.FromRgb(255, 0, 0),      // Red
            Color.FromRgb(0, 255, 0),      // Green
            Color.FromRgb(0, 0, 255),      // Blue
            Color.FromRgb(255, 255, 0),    // Yellow
            Color.FromRgb(255, 0, 255),    // Magenta
            Color.FromRgb(0, 255, 255),    // Cyan
            Color.FromRgb(255, 128, 0),    // Orange
            Color.FromRgb(128, 0, 255),    // Purple
            Color.FromRgb(0, 128, 255),    // Sky blue
            Color.FromRgb(128, 255, 0),    // Lime
            Color.FromRgb(255, 128, 128),  // Light red
            Color.FromRgb(128, 255, 128),  // Light green
            Color.FromRgb(128, 128, 255),  // Light blue
            Color.FromRgb(255, 255, 128),  // Light yellow
            Color.FromRgb(255, 255, 255),  // White
            Color.FromRgb(128, 128, 128),  // Gray
        };

        public GerberViewerView()
        {
            InitializeComponent();

            GerberCanvas.CursorPositionChanged += OnCursorPositionChanged;
            GerberCanvas.SelectionRectCompleted += OnSelectionRectCompleted;
            GerberCanvas.Loaded += (s, e) =>
            {
                var vm = DataContext as GerberViewerViewModel;
                if (vm != null)
                {
                    vm.ZoomToFitWithViewport(GerberCanvas.ActualWidth, GerberCanvas.ActualHeight);
                }
            };

            // Wire up OpenGL canvas events for selection support
            OpenGLCanvas.CursorPositionChanged += OnCursorPositionChanged;
            OpenGLCanvas.SelectionRectCompleted += OnOpenGLSelectionRectCompleted;
            OpenGLCanvas.PointClicked += OnOpenGLPointClicked;

            // Subscribe to layer list selection changes
            LayerListBox.SelectionChanged += OnLayerListSelectionChanged;

            // Subscribe to data context changes to hook up visibility changed event
            DataContextChanged += OnDataContextChanged;

            // Handle keyboard shortcuts
            KeyDown += OnKeyDown;
            Focusable = true;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Unsubscribe from old view model
            var oldVm = e.OldValue as GerberViewerViewModel;
            if (oldVm != null)
            {
                oldVm.LayerVisibilityChanged -= OnLayerVisibilityChanged;
            }

            // Subscribe to new view model
            var newVm = e.NewValue as GerberViewerViewModel;
            if (newVm != null)
            {
                newVm.LayerVisibilityChanged += OnLayerVisibilityChanged;
            }
        }

        private void OnLayerVisibilityChanged()
        {
            // Refresh the canvas when layer visibility changes
            GerberCanvas.InvalidateGerberCache();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            var vm = DataContext as GerberViewerViewModel;
            if (vm == null) return;

            // Page Up / [ = Step layer up
            if (e.Key == Key.PageUp || e.Key == Key.OemOpenBrackets)
            {
                vm.StepLayerUpCommand.Execute(null);
                e.Handled = true;
            }
            // Page Down / ] = Step layer down
            else if (e.Key == Key.PageDown || e.Key == Key.OemCloseBrackets)
            {
                vm.StepLayerDownCommand.Execute(null);
                e.Handled = true;
            }
            // I = Invert layers
            else if (e.Key == Key.I && Keyboard.Modifiers == ModifierKeys.None)
            {
                vm.InvertLayersCommand.Execute(null);
                e.Handled = true;
            }
            // A = Show all layers
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Shift)
            {
                vm.ShowAllLayersCommand.Execute(null);
                e.Handled = true;
            }
            // H = Hide all layers
            else if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.None)
            {
                vm.HideAllLayersCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void OnLayerListSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var listBox = sender as ListBox;
            if (listBox == null) return;

            var selectedLayer = listBox.SelectedItem as GerberLayer;
            if (selectedLayer != null)
            {
                // Activate the selected layer on the canvas
                GerberCanvas.SetActiveGerberLayer(selectedLayer);
            }
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
                bool addToSelection = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                vm.SelectPrimitivesInRect(worldRect, addToSelection);
            }
        }

        private void OnOpenGLSelectionRectCompleted(object sender, Rect worldRect)
        {
            // Forward selection to view model (same as CPU canvas)
            var vm = DataContext as GerberViewerViewModel;
            if (vm != null)
            {
                bool addToSelection = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                vm.SelectPrimitivesInRect(worldRect, addToSelection);
            }
        }

        private void OnOpenGLPointClicked(object sender, Point worldPos)
        {
            // Forward click to view model for hit testing
            var vm = DataContext as GerberViewerViewModel;
            if (vm != null)
            {
                bool addToSelection = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
                // Calculate hit radius in world units (5 pixels converted to world space)
                // This matches the CPU canvas behavior which uses hitRadius / Zoom
                double hitRadiusWorld = 5.0 / OpenGLCanvas.Zoom;
                vm.SelectPrimitiveAtPoint(worldPos, addToSelection, hitRadiusWorld);
            }
        }

        private void ColorBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var border = sender as FrameworkElement;
            if (border == null) return;

            var layer = border.DataContext as GerberLayer;
            if (layer == null) return;

            // Create a popup menu with color presets
            var contextMenu = new ContextMenu();

            // Add preset colors as grid
            var grid = new System.Windows.Controls.Primitives.UniformGrid
            {
                Columns = 4,
                Width = 130,
                Height = 130
            };

            foreach (var color in _presetColors)
            {
                var colorRect = new Border
                {
                    Width = 28,
                    Height = 28,
                    Margin = new Thickness(2),
                    Background = new SolidColorBrush(color),
                    BorderBrush = new SolidColorBrush(Colors.Gray),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand
                };

                Color capturedColor = color;
                colorRect.MouseLeftButtonDown += (s, args) =>
                {
                    layer.Color = capturedColor;
                    contextMenu.IsOpen = false;
                    GerberCanvas.InvalidateGerberCache();
                };

                grid.Children.Add(colorRect);
            }

            var menuItem = new MenuItem { Header = grid, StaysOpenOnClick = true };
            contextMenu.Items.Add(menuItem);

            contextMenu.PlacementTarget = border;
            contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            contextMenu.IsOpen = true;

            e.Handled = true;
        }

        private void RendererComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;

            var comboBox = sender as ComboBox;
            if (comboBox == null) return;

            bool useOpenGL = comboBox.SelectedIndex == 1;

            if (useOpenGL != _useOpenGL)
            {
                _useOpenGL = useOpenGL;
                SwitchRenderer(useOpenGL);
            }
        }

        private void SwitchRenderer(bool useOpenGL)
        {
            if (useOpenGL)
            {
                // Switch to OpenGL
                GerberCanvas.Visibility = Visibility.Collapsed;
                OpenGLCanvas.Visibility = Visibility.Visible;

                // Apply screen blend setting
                OpenGLCanvas.UseScreenBlend = ScreenBlendCheckBox?.IsChecked ?? true;
            }
            else
            {
                // Switch to WPF
                OpenGLCanvas.Visibility = Visibility.Collapsed;
                GerberCanvas.Visibility = Visibility.Visible;

                // Apply screen blend setting
                GerberCanvas.UseScreenBlend = ScreenBlendCheckBox?.IsChecked ?? true;
            }
        }

        private void ScreenBlendCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            var checkBox = sender as CheckBox;
            if (checkBox == null) return;

            bool useScreenBlend = checkBox.IsChecked ?? true;

            // Guard against null during InitializeComponent
            if (_useOpenGL)
            {
                if (OpenGLCanvas != null)
                    OpenGLCanvas.UseScreenBlend = useScreenBlend;
            }
            else
            {
                if (GerberCanvas != null)
                    GerberCanvas.UseScreenBlend = useScreenBlend;
            }
        }

        /// <summary>
        /// Gets GPU information for display in settings or diagnostics
        /// </summary>
        public static GpuInfo GetGpuInfo()
        {
            return OpenGLCanvas.GetGpuInfo();
        }
    }
}
