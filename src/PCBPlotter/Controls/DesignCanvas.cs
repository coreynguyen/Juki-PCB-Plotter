using System;
using System.Collections.Generic;
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

        #region Dependency Properties

        public static readonly DependencyProperty ZoomProperty =
            DependencyProperty.Register("Zoom", typeof(double), typeof(DesignCanvas),
                new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender, OnZoomChanged));

        public static readonly DependencyProperty PanXProperty =
            DependencyProperty.Register("PanX", typeof(double), typeof(DesignCanvas),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty PanYProperty =
            DependencyProperty.Register("PanY", typeof(double), typeof(DesignCanvas),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ShowGridProperty =
            DependencyProperty.Register("ShowGrid", typeof(bool), typeof(DesignCanvas),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty GridSpacingProperty =
            DependencyProperty.Register("GridSpacing", typeof(double), typeof(DesignCanvas),
                new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty BackgroundColorProperty =
            DependencyProperty.Register("BackgroundColor", typeof(Color), typeof(DesignCanvas),
                new FrameworkPropertyMetadata(Color.FromRgb(30, 30, 30), FrameworkPropertyMetadataOptions.AffectsRender));

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

            Loaded += (s, e) => InvalidateVisual();
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

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            // Render background
            RenderBackground();

            // Render selection overlay if selecting
            if (_isSelecting)
            {
                RenderSelectionOverlay();
            }
        }

        private void RenderBackground()
        {
            using (var dc = _backgroundVisual.RenderOpen())
            {
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

        private void RenderSelectionOverlay()
        {
            using (var dc = _overlayVisual.RenderOpen())
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

            _isPanning = true;
            _panStart = e.GetPosition(this);
            _lastMousePosition = _panStart;
            CaptureMouse();
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonUp(e);

            _isPanning = false;
            ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
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
    }
}
