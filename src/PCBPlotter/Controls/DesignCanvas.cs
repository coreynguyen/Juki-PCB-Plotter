using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PCBPlotter.Core.Models;
using PCBPlotter.Core.Rendering;
using PCBPlotter.Services;

namespace PCBPlotter.Controls
{
    /// <summary>
    /// High-performance 2D canvas for rendering PCB layouts and Gerber data
    /// </summary>
    public class DesignCanvas : FrameworkElement
    {
        private WpfDrawingRenderer _renderer;
        private DrawingVisual _backgroundVisual;
        private DrawingVisual _contentVisual;
        private DrawingVisual _overlayVisual;
        private List<Visual> _visuals;

        private Point _lastMousePosition;
        private Point _panStart;
        private bool _isPanning;
        private bool _isSelecting;
        private Point _selectionStart;
        private Rect _selectionRect;

        // Cached brushes and pens for placement rendering
        private SolidColorBrush _placementFillBrush;
        private SolidColorBrush _placementSelectedBrush;
        private SolidColorBrush _placementErrorBrush;
        private SolidColorBrush _labelBrush;
        private Pen _placementOutlinePen;
        private Pen _placementSelectedPen;
        private Pen _pin1Pen;
        private Typeface _labelTypeface;

        #region Dependency Properties

        public static readonly DependencyProperty ZoomProperty =
            DependencyProperty.Register("Zoom", typeof(double), typeof(DesignCanvas),
                new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender, OnZoomChanged));

        public static readonly DependencyProperty PanXProperty =
            DependencyProperty.Register("PanX", typeof(double), typeof(DesignCanvas),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnPanChanged));

        public static readonly DependencyProperty PanYProperty =
            DependencyProperty.Register("PanY", typeof(double), typeof(DesignCanvas),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnPanChanged));

        public static readonly DependencyProperty ShowGridProperty =
            DependencyProperty.Register("ShowGrid", typeof(bool), typeof(DesignCanvas),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty GridSpacingProperty =
            DependencyProperty.Register("GridSpacing", typeof(double), typeof(DesignCanvas),
                new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty BackgroundColorProperty =
            DependencyProperty.Register("BackgroundColor", typeof(Color), typeof(DesignCanvas),
                new FrameworkPropertyMetadata(Color.FromRgb(30, 30, 30), FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty PlacementsProperty =
            DependencyProperty.Register("Placements", typeof(ObservableCollection<Placement>), typeof(DesignCanvas),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnPlacementsChanged));

        public static readonly DependencyProperty FiducialsProperty =
            DependencyProperty.Register("Fiducials", typeof(ObservableCollection<Fiducial>), typeof(DesignCanvas),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ShowLabelsProperty =
            DependencyProperty.Register("ShowLabels", typeof(bool), typeof(DesignCanvas),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ViewSideProperty =
            DependencyProperty.Register("ViewSide", typeof(BoardSide), typeof(DesignCanvas),
                new FrameworkPropertyMetadata(BoardSide.Top, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Zoom
        {
            get { return (double)GetValue(ZoomProperty); }
            set { SetValue(ZoomProperty, value); }
        }

        public double PanX
        {
            get { return (double)GetValue(PanXProperty); }
            set { SetValue(PanXProperty, value); }
        }

        public double PanY
        {
            get { return (double)GetValue(PanYProperty); }
            set { SetValue(PanYProperty, value); }
        }

        public bool ShowGrid
        {
            get { return (bool)GetValue(ShowGridProperty); }
            set { SetValue(ShowGridProperty, value); }
        }

        public double GridSpacing
        {
            get { return (double)GetValue(GridSpacingProperty); }
            set { SetValue(GridSpacingProperty, value); }
        }

        public Color BackgroundColor
        {
            get { return (Color)GetValue(BackgroundColorProperty); }
            set { SetValue(BackgroundColorProperty, value); }
        }

        public ObservableCollection<Placement> Placements
        {
            get { return (ObservableCollection<Placement>)GetValue(PlacementsProperty); }
            set { SetValue(PlacementsProperty, value); }
        }

        public ObservableCollection<Fiducial> Fiducials
        {
            get { return (ObservableCollection<Fiducial>)GetValue(FiducialsProperty); }
            set { SetValue(FiducialsProperty, value); }
        }

        public bool ShowLabels
        {
            get { return (bool)GetValue(ShowLabelsProperty); }
            set { SetValue(ShowLabelsProperty, value); }
        }

        public BoardSide ViewSide
        {
            get { return (BoardSide)GetValue(ViewSideProperty); }
            set { SetValue(ViewSideProperty, value); }
        }

        #endregion

        #region Events

        public event EventHandler<Point> CursorPositionChanged;
        public event EventHandler<Rect> SelectionRectCompleted;
        public event EventHandler<Point> PointClicked;

        #endregion

        public DesignCanvas()
        {
            _renderer = new WpfDrawingRenderer();
            _visuals = new List<Visual>();

            _backgroundVisual = new DrawingVisual();
            _contentVisual = new DrawingVisual();
            _overlayVisual = new DrawingVisual();

            _visuals.Add(_backgroundVisual);
            _visuals.Add(_contentVisual);
            _visuals.Add(_overlayVisual);

            ClipToBounds = true;
            Focusable = true;

            InitializeBrushesAndPens();

            Loaded += (s, e) => InvalidateVisual();
        }

        private void InitializeBrushesAndPens()
        {
            // Create and freeze brushes for better performance
            _placementFillBrush = new SolidColorBrush(Color.FromArgb(200, 60, 60, 70));
            _placementFillBrush.Freeze();

            _placementSelectedBrush = new SolidColorBrush(Color.FromArgb(200, 0, 120, 200));
            _placementSelectedBrush.Freeze();

            _placementErrorBrush = new SolidColorBrush(Color.FromArgb(200, 200, 60, 60));
            _placementErrorBrush.Freeze();

            _labelBrush = new SolidColorBrush(Colors.White);
            _labelBrush.Freeze();

            // Create and freeze pens
            _placementOutlinePen = new Pen(new SolidColorBrush(Color.FromRgb(150, 150, 160)), 1.5);
            _placementOutlinePen.Freeze();

            _placementSelectedPen = new Pen(new SolidColorBrush(Color.FromRgb(0, 180, 255)), 2);
            _placementSelectedPen.Freeze();

            _pin1Pen = new Pen(new SolidColorBrush(Color.FromRgb(255, 100, 100)), 2);
            _pin1Pen.Freeze();

            _labelTypeface = new Typeface("Segoe UI");
        }

        private static void OnPlacementsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var canvas = d as DesignCanvas;
            if (canvas == null) return;

            // Unsubscribe from old collection
            var oldCollection = e.OldValue as ObservableCollection<Placement>;
            if (oldCollection != null)
            {
                oldCollection.CollectionChanged -= canvas.OnPlacementsCollectionChanged;
            }

            // Subscribe to new collection
            var newCollection = e.NewValue as ObservableCollection<Placement>;
            if (newCollection != null)
            {
                newCollection.CollectionChanged += canvas.OnPlacementsCollectionChanged;
            }

            canvas.InvalidateVisual();
        }

        private void OnPlacementsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            InvalidateVisual();
        }

        #region Visual Tree

        protected override int VisualChildrenCount
        {
            get { return _visuals.Count; }
        }

        protected override Visual GetVisualChild(int index)
        {
            return _visuals[index];
        }

        #endregion

        #region Rendering

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            // Draw background
            dc.DrawRectangle(new SolidColorBrush(BackgroundColor), null,
                new Rect(0, 0, ActualWidth, ActualHeight));

            // Draw grid if enabled
            if (ShowGrid && Zoom > 0.1)
            {
                RenderGrid(dc);
            }

            // Draw origin crosshair
            RenderOrigin(dc);

            // Render placements and content
            RenderPlacementsDirect(dc);

            // Render selection overlay if selecting
            if (_isSelecting)
            {
                RenderSelectionOverlayDirect(dc);
            }
        }

        private void RenderGrid(DrawingContext dc)
        {
            var gridPen = new Pen(new SolidColorBrush(RenderColors.Grid), 1);
            gridPen.Freeze();
            var majorGridPen = new Pen(new SolidColorBrush(RenderColors.GridMajor), 1);
            majorGridPen.Freeze();

            double spacing = GridSpacing * Zoom;

            // Don't render grid if spacing is too small
            if (spacing < 5) return;

            // Calculate visible range
            double startX = (-PanX % spacing) - spacing;
            double startY = (-PanY % spacing) - spacing;

            int majorInterval = 10;
            int lineIndexX = (int)Math.Floor(-PanX / spacing);
            int lineIndexY = (int)Math.Floor(-PanY / spacing);

            // Vertical lines
            for (double x = startX; x < ActualWidth + spacing; x += spacing)
            {
                var pen = (lineIndexX % majorInterval == 0) ? majorGridPen : gridPen;
                dc.DrawLine(pen, new Point(x, 0), new Point(x, ActualHeight));
                lineIndexX++;
            }

            // Horizontal lines
            for (double y = startY; y < ActualHeight + spacing; y += spacing)
            {
                var pen = (lineIndexY % majorInterval == 0) ? majorGridPen : gridPen;
                dc.DrawLine(pen, new Point(0, y), new Point(ActualWidth, y));
                lineIndexY++;
            }
        }

        private void RenderOrigin(DrawingContext dc)
        {
            // Draw origin at (0,0) world coordinates
            Point screenOrigin = WorldToScreen(new Point(0, 0));

            var xAxisPen = new Pen(new SolidColorBrush(RenderColors.AxisX), 2);
            xAxisPen.Freeze();
            var yAxisPen = new Pen(new SolidColorBrush(RenderColors.AxisY), 2);
            yAxisPen.Freeze();

            double axisLength = 50;

            // X axis (red)
            dc.DrawLine(xAxisPen, screenOrigin, new Point(screenOrigin.X + axisLength, screenOrigin.Y));

            // Y axis (green) - inverted because screen Y is inverted
            dc.DrawLine(yAxisPen, screenOrigin, new Point(screenOrigin.X, screenOrigin.Y - axisLength));

            // Origin marker
            dc.DrawEllipse(new SolidColorBrush(RenderColors.OriginMarker), null, screenOrigin, 4, 4);
        }

        private void RenderSelectionOverlayDirect(DrawingContext dc)
        {
            if (_selectionRect.Width > 0 && _selectionRect.Height > 0)
            {
                var fillBrush = new SolidColorBrush(RenderColors.SelectionBox);
                fillBrush.Freeze();
                var strokePen = new Pen(new SolidColorBrush(RenderColors.SelectionBorder), 1);
                strokePen.DashStyle = DashStyles.Dash;
                strokePen.Freeze();

                dc.DrawRectangle(fillBrush, strokePen, _selectionRect);
            }
        }

        private void RenderPlacementsDirect(DrawingContext dc)
        {
            if (Placements == null || Placements.Count == 0)
                return;

            // Constants for placement rendering
            double placementSize = 2.0; // Base size in mm

            foreach (var placement in Placements)
            {
                // Skip placements on the wrong side
                if (placement.Side != ViewSide)
                    continue;

                // Convert world position to screen
                Point screenPos = WorldToScreen(placement.Position);

                // Calculate size based on zoom
                double size = placementSize * Zoom;
                double halfSize = size / 2;

                // Choose brush/pen based on state
                SolidColorBrush fillBrush = _placementFillBrush;
                Pen outlinePen = _placementOutlinePen;

                if (placement.IsSelected)
                {
                    fillBrush = _placementSelectedBrush;
                    outlinePen = _placementSelectedPen;
                }
                else if (placement.Status != PlacementStatus.Valid)
                {
                    fillBrush = _placementErrorBrush;
                }

                // Draw placement marker (rounded rectangle for modern look)
                var placementRect = new Rect(
                    screenPos.X - halfSize,
                    screenPos.Y - halfSize,
                    size,
                    size
                );
                dc.DrawRoundedRectangle(fillBrush, outlinePen, placementRect, 2, 2);

                // Draw pin 1 indicator (small circle at top-left)
                double pin1Size = Math.Max(2, size * 0.2);
                double pin1Offset = halfSize * 0.6;
                Point pin1Pos = new Point(
                    screenPos.X - pin1Offset,
                    screenPos.Y - pin1Offset
                );
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(255, 100, 100)), null, pin1Pos, pin1Size, pin1Size);

                // Draw center crosshair
                double crossSize = Math.Max(2, size * 0.3);
                var crossPen = new Pen(new SolidColorBrush(Colors.White), 1);
                crossPen.Freeze();
                dc.DrawLine(crossPen,
                    new Point(screenPos.X - crossSize, screenPos.Y),
                    new Point(screenPos.X + crossSize, screenPos.Y));
                dc.DrawLine(crossPen,
                    new Point(screenPos.X, screenPos.Y - crossSize),
                    new Point(screenPos.X, screenPos.Y + crossSize));

                // Draw reference label if enabled
                if (ShowLabels && !string.IsNullOrEmpty(placement.Reference))
                {
                    double fontSize = Math.Max(8, Math.Min(14, 10 * Zoom));
                    var formattedText = new FormattedText(
                        placement.Reference,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        _labelTypeface,
                        fontSize,
                        _labelBrush,
                        VisualTreeHelper.GetDpi(this).PixelsPerDip
                    );

                    // Position label below the placement
                    Point labelPos = new Point(
                        screenPos.X - formattedText.Width / 2,
                        screenPos.Y + halfSize + 2
                    );

                    // Draw label background for readability
                    var labelBgRect = new Rect(
                        labelPos.X - 2,
                        labelPos.Y - 1,
                        formattedText.Width + 4,
                        formattedText.Height + 2
                    );
                    var labelBgBrush = new SolidColorBrush(Color.FromArgb(180, 30, 30, 35));
                    labelBgBrush.Freeze();
                    dc.DrawRoundedRectangle(labelBgBrush, null, labelBgRect, 2, 2);

                    dc.DrawText(formattedText, labelPos);
                }
            }

            // Render fiducials
            if (Fiducials != null)
            {
                foreach (var fiducial in Fiducials)
                {
                    if (fiducial.Side != ViewSide)
                        continue;

                    Point screenPos = WorldToScreen(new Point(fiducial.X, fiducial.Y));
                    double fidSize = 3.0 * Zoom;

                    // Draw fiducial as a diamond shape
                    var fidBrush = new SolidColorBrush(Color.FromRgb(255, 0, 255));
                    fidBrush.Freeze();
                    var fidPen = new Pen(fidBrush, 2);
                    fidPen.Freeze();

                    // Draw diamond
                    var diamondGeometry = new StreamGeometry();
                    using (var ctx = diamondGeometry.Open())
                    {
                        ctx.BeginFigure(new Point(screenPos.X, screenPos.Y - fidSize), true, true);
                        ctx.LineTo(new Point(screenPos.X + fidSize, screenPos.Y), true, false);
                        ctx.LineTo(new Point(screenPos.X, screenPos.Y + fidSize), true, false);
                        ctx.LineTo(new Point(screenPos.X - fidSize, screenPos.Y), true, false);
                    }
                    diamondGeometry.Freeze();
                    dc.DrawGeometry(null, fidPen, diamondGeometry);

                    // Draw center dot
                    dc.DrawEllipse(fidBrush, null, screenPos, 2, 2);

                    // Draw label if enabled
                    if (ShowLabels && !string.IsNullOrEmpty(fiducial.Name))
                    {
                        double fontSize = Math.Max(8, Math.Min(12, 9 * Zoom));
                        var formattedText = new FormattedText(
                            fiducial.Name,
                            CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            _labelTypeface,
                            fontSize,
                            fidBrush,
                            VisualTreeHelper.GetDpi(this).PixelsPerDip
                        );
                        dc.DrawText(formattedText, new Point(screenPos.X + fidSize + 2, screenPos.Y - fontSize / 2));
                    }
                }
            }
        }

        public void RenderContent(Action<WpfDrawingRenderer, RenderContext> renderAction, RenderContext context)
        {
            using (var dc = _contentVisual.RenderOpen())
            {
                _renderer.Initialize(null);
                _renderer.BeginFrame();

                // Set up transform for world to screen conversion
                var transform = new Matrix();
                transform.Scale(context.Zoom, -context.Zoom); // Flip Y
                transform.Translate(context.PanX, context.ViewportHeight - context.PanY);
                _renderer.SetTransform(transform);

                renderAction(_renderer, context);

                _renderer.EndFrame();

                // Draw the renderer's output
                dc.DrawDrawing(_renderer.DrawingGroup);
            }
        }

        #endregion

        #region Coordinate Conversion

        public Point WorldToScreen(Point world)
        {
            return new Point(
                world.X * Zoom + PanX,
                ActualHeight - (world.Y * Zoom + PanY)
            );
        }

        public Point ScreenToWorld(Point screen)
        {
            return new Point(
                (screen.X - PanX) / Zoom,
                (ActualHeight - screen.Y - PanY) / Zoom
            );
        }

        #endregion

        #region Mouse Handling

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            Point mousePos = e.GetPosition(this);
            Point worldPos = ScreenToWorld(mousePos);

            CursorPositionChanged?.Invoke(this, worldPos);

            // Check if right button is pressed but panning hasn't started yet
            if (e.RightButton == MouseButtonState.Pressed && !_isPanning)
            {
                StartPanningIfNeeded(mousePos);
            }

            if (_isPanning)
            {
                double deltaX = mousePos.X - _lastMousePosition.X;
                double deltaY = mousePos.Y - _lastMousePosition.Y;

                PanX += deltaX;
                PanY -= deltaY; // Invert because screen Y is inverted

                InvalidateVisual();
            }
            else if (_isSelecting)
            {
                _selectionRect = new Rect(
                    Math.Min(_selectionStart.X, mousePos.X),
                    Math.Min(_selectionStart.Y, mousePos.Y),
                    Math.Abs(mousePos.X - _selectionStart.X),
                    Math.Abs(mousePos.Y - _selectionStart.Y)
                );
                InvalidateVisual();
            }

            _lastMousePosition = mousePos;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Focus();

            Point mousePos = e.GetPosition(this);

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                // Start selection rectangle
                _isSelecting = true;
                _selectionStart = mousePos;
                _selectionRect = new Rect(mousePos, new Size(0, 0));
                CaptureMouse();
            }
            else
            {
                // Point click
                PointClicked?.Invoke(this, ScreenToWorld(mousePos));
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);

            if (_isSelecting)
            {
                _isSelecting = false;
                ReleaseMouseCapture();

                if (_selectionRect.Width > 5 && _selectionRect.Height > 5)
                {
                    // Convert selection rect to world coordinates
                    Point topLeft = ScreenToWorld(new Point(_selectionRect.Left, _selectionRect.Top));
                    Point bottomRight = ScreenToWorld(new Point(_selectionRect.Right, _selectionRect.Bottom));

                    Rect worldRect = new Rect(
                        Math.Min(topLeft.X, bottomRight.X),
                        Math.Min(topLeft.Y, bottomRight.Y),
                        Math.Abs(bottomRight.X - topLeft.X),
                        Math.Abs(bottomRight.Y - topLeft.Y)
                    );

                    SelectionRectCompleted?.Invoke(this, worldRect);
                }

                _selectionRect = Rect.Empty;
                InvalidateVisual();
            }
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonDown(e);
            Focus();

            // Store start position for potential panning, but don't capture mouse yet
            _panStart = e.GetPosition(this);
            _lastMousePosition = _panStart;
            _isPanning = false;
            // Don't capture mouse - this allows context menu to show
        }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonUp(e);

            if (_isPanning)
            {
                _isPanning = false;
                ReleaseMouseCapture();
                Cursor = Cursors.Arrow;
                e.Handled = true; // Prevent context menu after panning
            }
            // If not panning, don't handle - let context menu show
        }

        private void StartPanningIfNeeded(Point currentPos)
        {
            // Start panning after 3 pixels of movement
            double dist = Math.Sqrt(Math.Pow(currentPos.X - _panStart.X, 2) +
                                   Math.Pow(currentPos.Y - _panStart.Y, 2));
            if (dist > 3 && !_isPanning)
            {
                _isPanning = true;
                CaptureMouse(); // Only capture when actually panning
                Cursor = Cursors.Hand;
            }
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);

            Point mousePos = e.GetPosition(this);
            Point worldBefore = ScreenToWorld(mousePos);

            // Zoom
            double zoomFactor = e.Delta > 0 ? 1.2 : 1.0 / 1.2;
            double newZoom = Zoom * zoomFactor;
            newZoom = Math.Max(0.01, Math.Min(100, newZoom));

            Zoom = newZoom;

            // Adjust pan to keep mouse position stable
            Point worldAfter = ScreenToWorld(mousePos);
            PanX += (worldAfter.X - worldBefore.X) * Zoom;
            PanY += (worldAfter.Y - worldBefore.Y) * Zoom;

            InvalidateVisual();
        }

        #endregion

        #region Keyboard Handling

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            double panAmount = 50 / Zoom;

            switch (e.Key)
            {
                case Key.Left:
                    PanX += panAmount * Zoom;
                    InvalidateVisual();
                    e.Handled = true;
                    break;
                case Key.Right:
                    PanX -= panAmount * Zoom;
                    InvalidateVisual();
                    e.Handled = true;
                    break;
                case Key.Up:
                    PanY -= panAmount * Zoom;
                    InvalidateVisual();
                    e.Handled = true;
                    break;
                case Key.Down:
                    PanY += panAmount * Zoom;
                    InvalidateVisual();
                    e.Handled = true;
                    break;
                case Key.Home:
                    // Reset view
                    Zoom = 1.0;
                    PanX = ActualWidth / 2;
                    PanY = ActualHeight / 2;
                    InvalidateVisual();
                    e.Handled = true;
                    break;
            }
        }

        #endregion

        private static void OnZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var canvas = d as DesignCanvas;
            canvas?.InvalidateVisual();
        }

        private static void OnPanChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var canvas = d as DesignCanvas;
            canvas?.InvalidateVisual();
        }
    }
}
