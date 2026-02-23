using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
        private bool _useOpenGL = true;
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
            GerberCanvas.GerberSelectionChanged += OnCpuGerberSelectionChanged;
            GerberCanvas.Loaded += OnCanvasLoaded;
            GerberCanvas.SizeChanged += OnCanvasSizeChanged;

            // Wire up OpenGL canvas events for selection support
            OpenGLCanvas.CursorPositionChanged += OnCursorPositionChanged;
            OpenGLCanvas.SelectionRectCompleted += OnOpenGLSelectionRectCompleted;
            OpenGLCanvas.PointClicked += OnOpenGLPointClicked;
            OpenGLCanvas.Loaded += OnCanvasLoaded;
            OpenGLCanvas.SizeChanged += OnCanvasSizeChanged;

            // Alt+Click in viewport picks a layer
            GerberCanvas.LayerPicked += OnLayerPicked;
            OpenGLCanvas.LayerPicked += OnLayerPicked;

            // Right-click creates placement from selection
            GerberCanvas.MouseRightButtonUp += OnCanvasRightClick;
            OpenGLCanvas.RightClicked += OnOpenGLCanvasRightClick;

            // Force refresh when the view becomes visible (e.g. switching to Gerber tab)
            // IMPORTANT: Do NOT call InvalidateGerberCache() here - that clears quadtrees
            // which causes large layers (>2000 primitives) to disappear until rebuilt async.
            // Just call InvalidateVisual() to trigger a repaint with existing caches.
            IsVisibleChanged += (s, e) =>
            {
                if ((bool)e.NewValue)
                {
                    // Immediate invalidation for cases where layout is already done
                    GerberCanvas.InvalidateVisual();
                    if (OpenGLCanvas.Visibility == Visibility.Visible)
                    {
                        // CRITICAL: Invalidate static GPU buffers when tab becomes visible.
                        // WindowsFormsHost + GLControl can have GL context issues after being hidden,
                        // causing static buffers (line bodies, polygons) to become invalid while
                        // instanced geometry (circles) still works since it's re-uploaded each frame.
                        OpenGLCanvas.InvalidateStaticGpuBuffers();
                    }

                    // Deferred invalidation to catch cases where layout hasn't completed yet
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                    {
                        GerberCanvas.InvalidateVisual();
                        if (OpenGLCanvas.Visibility == Visibility.Visible)
                            OpenGLCanvas.Invalidate();
                    }));

                    // Extra deferred invalidation at Render priority for WindowsFormsHost
                    // which may need additional time to become active after tab switch
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
                    {
                        if (OpenGLCanvas.Visibility == Visibility.Visible)
                            OpenGLCanvas.Invalidate();
                    }));
                }
            };

            // Subscribe to double-click on layer list to set active layer
            LayerListBox.MouseDoubleClick += OnLayerListDoubleClick;

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
                oldVm.ActiveLayerChanged -= OnActiveLayerChanged;
                oldVm.RequestLayerListSelection -= OnRequestLayerListSelection;
                oldVm.CanvasRefreshRequested -= OnCanvasRefreshRequested;
                oldVm.RequestLayerFlash -= OnRequestLayerFlash;
            }

            // Subscribe to new view model
            var newVm = e.NewValue as GerberViewerViewModel;
            if (newVm != null)
            {
                newVm.LayerVisibilityChanged += OnLayerVisibilityChanged;
                newVm.ActiveLayerChanged += OnActiveLayerChanged;
                newVm.RequestLayerListSelection += OnRequestLayerListSelection;
                newVm.CanvasRefreshRequested += OnCanvasRefreshRequested;
                newVm.RequestLayerFlash += OnRequestLayerFlash;
            }
        }

        private void OnLayerVisibilityChanged()
        {
            // Refresh both canvases when layer visibility changes
            // IMPORTANT: Only invalidate visual, NOT the gerber cache.
            // InvalidateGerberCache() clears quadtrees, which causes large layers
            // (>2000 primitives) to disappear until async rebuild completes.
            // Visibility changes don't require quadtree rebuilds - just a repaint.
            GerberCanvas.InvalidateVisual();
            if (OpenGLCanvas.Visibility == Visibility.Visible)
                OpenGLCanvas.Invalidate();
        }

        private void OnCanvasLoaded(object sender, RoutedEventArgs e)
        {
            UpdateViewportSize();
            // Initial zoom fit when canvas is first loaded
            var vm = DataContext as GerberViewerViewModel;
            if (vm != null)
            {
                var (width, height) = GetActiveCanvasSize();
                if (width > 0 && height > 0)
                {
                    vm.ZoomToFitWithViewport(width, height);
                }
            }
        }

        private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateViewportSize();
        }

        private void UpdateViewportSize()
        {
            var vm = DataContext as GerberViewerViewModel;
            if (vm == null) return;

            var (width, height) = GetActiveCanvasSize();
            if (width > 0 && height > 0)
            {
                vm.UpdateViewportSize(width, height);
            }
        }

        private (double width, double height) GetActiveCanvasSize()
        {
            // Use whichever canvas is currently visible
            if (_useOpenGL && OpenGLCanvas.Visibility == Visibility.Visible)
            {
                return (OpenGLCanvas.ActualWidth, OpenGLCanvas.ActualHeight);
            }
            else if (GerberCanvas.Visibility == Visibility.Visible)
            {
                return (GerberCanvas.ActualWidth, GerberCanvas.ActualHeight);
            }
            // Fallback to OpenGL canvas dimensions even if collapsed (for initial load)
            return (OpenGLCanvas.ActualWidth, OpenGLCanvas.ActualHeight);
        }

        private void OnActiveLayerChanged(GerberLayer layer)
        {
            // Update the canvas active layer
            if (layer != null)
            {
                GerberCanvas.SetActiveGerberLayer(layer);
            }
        }

        private void OnRequestLayerListSelection(GerberLayer layer)
        {
            // Programmatically select a layer in the ListBox without triggering active-layer logic
            if (layer != null)
            {
                LayerListBox.SelectedItem = layer;
                LayerListBox.ScrollIntoView(layer);
            }
        }

        private void OnCanvasRefreshRequested()
        {
            // Refresh both canvases to show consumed region overlays
            // Only invalidate visual, not the full cache - consumed regions
            // don't change the geometry, just the overlay rendering.
            GerberCanvas.InvalidateVisual();
            if (OpenGLCanvas.Visibility == Visibility.Visible)
                OpenGLCanvas.Invalidate();
        }

        /// <summary>
        /// Flash the active layer 3 times to indicate which layer is now active
        /// </summary>
        private void OnRequestLayerFlash(GerberLayer layer)
        {
            if (layer == null) return;

            // Remember original visibility state
            bool originalVisibility = layer.IsVisible;

            // Flash counter (3 flashes = 6 toggles: off-on-off-on-off-on)
            int flashCount = 0;
            const int totalFlashes = 6;

            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };

            timer.Tick += (s, args) =>
            {
                flashCount++;

                if (flashCount >= totalFlashes)
                {
                    // Restore original visibility and stop
                    layer.IsVisible = originalVisibility;
                    timer.Stop();
                    RefreshCanvas();
                    return;
                }

                // Toggle visibility
                layer.IsVisible = !layer.IsVisible;
                RefreshCanvas();
            };

            // Start with layer hidden (first flash off)
            layer.IsVisible = false;
            RefreshCanvas();
            timer.Start();
        }

        private void RefreshCanvas()
        {
            // Only invalidate visual for repaint - don't clear quadtrees/caches
            GerberCanvas.InvalidateVisual();
            if (OpenGLCanvas.Visibility == Visibility.Visible)
                OpenGLCanvas.Invalidate();
        }

        /// <summary>
        /// Double-click on layer list sets the active layer
        /// </summary>
        private void OnLayerListDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var listBox = sender as ListBox;
            if (listBox == null) return;

            // Find the layer item that was double-clicked
            var element = e.OriginalSource as FrameworkElement;
            while (element != null && !(element is ListBoxItem))
            {
                element = VisualTreeHelper.GetParent(element) as FrameworkElement;
            }

            if (element is ListBoxItem item)
            {
                var layer = item.DataContext as GerberLayer;
                if (layer != null)
                {
                    var vm = DataContext as GerberViewerViewModel;
                    if (vm != null)
                    {
                        vm.SetActiveLayer(layer);
                    }
                }
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            var vm = DataContext as GerberViewerViewModel;
            if (vm == null) return;

            // F2 = Rename selected layer
            if (e.Key == Key.F2)
            {
                vm.RenameLayerCommand.Execute(null);
                e.Handled = true;
            }
            // Space = Toggle visibility of selected layers
            else if (e.Key == Key.Space)
            {
                var selectedLayers = LayerListBox.SelectedItems.Cast<GerberLayer>().ToList();
                if (selectedLayers.Count > 0)
                {
                    vm.ToggleSelectedLayersVisibility(selectedLayers);
                    e.Handled = true;
                }
            }
            // Page Up / [ = Step layer up
            else if (e.Key == Key.PageUp || e.Key == Key.OemOpenBrackets)
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
            // Delete = Remove selected layers
            else if (e.Key == Key.Delete)
            {
                var selectedLayers = LayerListBox.SelectedItems.Cast<GerberLayer>().ToList();
                if (selectedLayers.Count > 0)
                {
                    vm.RemoveSelectedLayers(selectedLayers);
                    e.Handled = true;
                }
            }
            // Enter = Add selection to output (create placement + package)
            else if (e.Key == Key.Return || e.Key == Key.Enter)
            {
                if (vm.SelectedPrimitives != null && vm.SelectedPrimitives.Count > 0)
                {
                    vm.AddSelectionToOutputCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }

        /// <summary>
        /// Alt+Click on visibility checkbox: toggle all on / all off (ZBrush style)
        /// </summary>
        private void VisibilityCheckBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
                return; // Let normal click through

            var vm = DataContext as GerberViewerViewModel;
            if (vm == null) return;

            e.Handled = true; // Prevent normal checkbox toggle

            var layers = vm.Project?.GerberLayers;
            if (layers == null || layers.Count == 0) return;

            // Determine: if any layer is currently visible, turn all off; otherwise turn all on
            bool anyVisible = layers.Any(l => l.IsVisible);
            foreach (var layer in layers)
            {
                layer.IsVisible = !anyVisible;
            }

            vm.NotifyLayerVisibilityChanged();
        }

        /// <summary>
        /// CPU canvas fires this when gerber primitives are selected/deselected.
        /// Sync the VM's SelectedPrimitives so commands like "Add to Output" work.
        /// </summary>
        private void OnCpuGerberSelectionChanged(object sender, List<GerberPrimitive> selected)
        {
            var vm = DataContext as GerberViewerViewModel;
            if (vm == null) return;

            vm.SelectedPrimitives.Clear();
            if (selected != null)
            {
                foreach (var prim in selected)
                    vm.SelectedPrimitives.Add(prim);
            }
        }

        private void OnLayerPicked(object sender, GerberLayer layer)
        {
            var vm = DataContext as GerberViewerViewModel;
            if (vm != null && layer != null)
            {
                vm.SetActiveLayer(layer);
                // Also select it in the list
                LayerListBox.SelectedItem = layer;
                LayerListBox.ScrollIntoView(layer);
            }
        }

        /// <summary>
        /// Right-click on CPU canvas directly creates placement from selection (same as Enter key).
        /// </summary>
        private void OnCanvasRightClick(object sender, MouseButtonEventArgs e)
        {
            var vm = DataContext as GerberViewerViewModel;
            if (vm == null) return;

            if (vm.SelectedPrimitives != null && vm.SelectedPrimitives.Count > 0)
            {
                vm.AddSelectionToOutputCommand.Execute(null);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Right-click on OpenGL canvas directly creates placement from selection (same as Enter key).
        /// </summary>
        private void OnOpenGLCanvasRightClick(object sender, Point worldPos)
        {
            var vm = DataContext as GerberViewerViewModel;
            if (vm == null) return;

            if (vm.SelectedPrimitives != null && vm.SelectedPrimitives.Count > 0)
            {
                vm.AddSelectionToOutputCommand.Execute(null);
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
                bool isCtrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
                bool isAlt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
                bool isShift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

                // Ctrl+Alt = deselect, Ctrl or Shift = add to selection
                bool removeFromSelection = isCtrl && isAlt;
                bool addToSelection = (isCtrl || isShift) && !removeFromSelection;

                vm.SelectPrimitivesInRect(worldRect, addToSelection, removeFromSelection);
            }
        }

        private void OnOpenGLSelectionRectCompleted(object sender, Controls.SelectionRectEventArgs e)
        {
            // Forward selection to view model (same as CPU canvas)
            var vm = DataContext as GerberViewerViewModel;
            if (vm != null)
            {
                // Use modifier keys from the WinForms event (WPF Keyboard.Modifiers doesn't work with WindowsFormsHost)
                // Ctrl+Alt = deselect, Ctrl or Shift = add to selection
                bool removeFromSelection = e.IsCtrlPressed && e.IsAltPressed;
                bool addToSelection = (e.IsCtrlPressed || e.IsShiftPressed) && !removeFromSelection;

                vm.SelectPrimitivesInRect(e.WorldRect, addToSelection, removeFromSelection);
            }
        }

        private void OnOpenGLPointClicked(object sender, Controls.PointClickedEventArgs e)
        {
            // Forward click to view model for hit testing
            var vm = DataContext as GerberViewerViewModel;
            if (vm != null)
            {
                // Use modifier keys from the WinForms event (WPF Keyboard.Modifiers doesn't work with WindowsFormsHost)
                // Ctrl+Alt = deselect, Ctrl or Shift = add to selection
                bool removeFromSelection = e.IsCtrlPressed && e.IsAltPressed;
                bool addToSelection = (e.IsCtrlPressed || e.IsShiftPressed) && !removeFromSelection;

                // Calculate hit radius in world units (5 pixels converted to world space)
                // This matches the CPU canvas behavior which uses hitRadius / Zoom
                double hitRadiusWorld = 5.0 / OpenGLCanvas.Zoom;
                vm.SelectPrimitiveAtPoint(e.WorldPosition, addToSelection, hitRadiusWorld, removeFromSelection);
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
                    // Only invalidate visual - color changes don't require quadtree rebuilds
                    GerberCanvas.InvalidateVisual();
                    if (OpenGLCanvas.Visibility == Visibility.Visible)
                        OpenGLCanvas.Invalidate();
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
                try
                {
                    // Switch to OpenGL
                    GerberCanvas.Visibility = Visibility.Collapsed;
                    OpenGLCanvas.Visibility = Visibility.Visible;

                    // CRITICAL: Invalidate static GPU buffers when switching to GPU mode.
                    // Same issue as tab switch - static buffers may be invalid after being hidden.
                    OpenGLCanvas.InvalidateStaticGpuBuffers();

                    // Apply screen blend setting
                    OpenGLCanvas.UseScreenBlend = ScreenBlendCheckBox?.IsChecked ?? true;
                }
                catch (Exception ex)
                {
                    // GPU initialization failed - fall back to CPU renderer
                    System.Diagnostics.Debug.WriteLine("OpenGL init failed, falling back to CPU: " + ex.Message);
                    _useOpenGL = false;
                    OpenGLCanvas.Visibility = Visibility.Collapsed;
                    GerberCanvas.Visibility = Visibility.Visible;
                    GerberCanvas.UseScreenBlend = ScreenBlendCheckBox?.IsChecked ?? true;

                    // Update the ComboBox to reflect the fallback
                    RendererComboBox.SelectionChanged -= RendererComboBox_SelectionChanged;
                    RendererComboBox.SelectedIndex = 0;
                    RendererComboBox.SelectionChanged += RendererComboBox_SelectionChanged;
                }
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
