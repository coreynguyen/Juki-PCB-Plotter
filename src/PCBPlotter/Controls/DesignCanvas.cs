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
                new FrameworkPropertyMetadata(20.0, FrameworkPropertyMetadataOptions.AffectsRender, OnZoomChanged));

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

        public static readonly DependencyProperty ShowPackageGraphicsProperty =
            DependencyProperty.Register("ShowPackageGraphics", typeof(bool), typeof(DesignCanvas),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ViewSideProperty =
            DependencyProperty.Register("ViewSide", typeof(BoardSide), typeof(DesignCanvas),
                new FrameworkPropertyMetadata(BoardSide.Top, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty BoardProperty =
            DependencyProperty.Register("Board", typeof(BoardDefinition), typeof(DesignCanvas),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty SelectedPackageProperty =
            DependencyProperty.Register("SelectedPackage", typeof(Package), typeof(DesignCanvas),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSelectedPackageChanged));

        private static void OnSelectedPackageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var canvas = d as DesignCanvas;
            if (canvas != null && e.NewValue != null)
            {
                // Auto-fit to the new package after layout is updated
                canvas.Dispatcher.BeginInvoke(new Action(() => canvas.ZoomToFitPackage()),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

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

        public bool ShowPackageGraphics
        {
            get { return (bool)GetValue(ShowPackageGraphicsProperty); }
            set { SetValue(ShowPackageGraphicsProperty, value); }
        }

        public BoardSide ViewSide
        {
            get { return (BoardSide)GetValue(ViewSideProperty); }
            set { SetValue(ViewSideProperty, value); }
        }

        public BoardDefinition Board
        {
            get { return (BoardDefinition)GetValue(BoardProperty); }
            set { SetValue(BoardProperty, value); }
        }

        public Package SelectedPackage
        {
            get { return (Package)GetValue(SelectedPackageProperty); }
            set { SetValue(SelectedPackageProperty, value); }
        }

        // Package editing state
        private int _selectedGraphicIndex = -1;
        private int _selectedPinIndex = -1;
        private bool _isDraggingGraphic;
        private Point _dragStartWorld;

        public int SelectedGraphicIndex
        {
            get { return _selectedGraphicIndex; }
            set
            {
                if (_selectedGraphicIndex != value)
                {
                    _selectedGraphicIndex = value;
                    _selectedPinIndex = -1; // Clear pin selection
                    InvalidateVisual();
                    GraphicSelectionChanged?.Invoke(this, value);
                }
            }
        }

        public int SelectedPinIndex
        {
            get { return _selectedPinIndex; }
            set
            {
                if (_selectedPinIndex != value)
                {
                    _selectedPinIndex = value;
                    _selectedGraphicIndex = -1; // Clear graphic selection
                    InvalidateVisual();
                    PinSelectionChanged?.Invoke(this, value);
                }
            }
        }

        #endregion

        #region Events

        public event EventHandler<Point> CursorPositionChanged;
        public event EventHandler<Rect> SelectionRectCompleted;
        public event EventHandler<Point> PointClicked;
        public event EventHandler<List<Placement>> SelectionChanged;
        public event EventHandler<int> GraphicSelectionChanged;
        public event EventHandler<int> PinSelectionChanged;
        public event EventHandler<Point> GraphicMoved;

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

        /// <summary>
        /// Zoom and pan to fit all placements in the viewport
        /// </summary>
        public void ZoomToFitPlacements()
        {
            if (Placements == null || Placements.Count == 0)
            {
                // No placements, center on origin with default zoom
                Zoom = 20.0;
                PanX = ActualWidth / 2;
                PanY = ActualHeight / 2;
                return;
            }

            // Calculate bounding box of all placements
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var p in Placements)
            {
                double size = p.Package != null ? Math.Max(p.Package.Width, p.Package.Length) : 2;
                minX = Math.Min(minX, p.X - size);
                maxX = Math.Max(maxX, p.X + size);
                minY = Math.Min(minY, p.Y - size);
                maxY = Math.Max(maxY, p.Y + size);
            }

            ZoomToFit(new Rect(minX, minY, maxX - minX, maxY - minY));
        }

        /// <summary>
        /// Zoom and pan to fit the selected package in the viewport
        /// </summary>
        public void ZoomToFitPackage()
        {
            var package = SelectedPackage;
            if (package == null)
            {
                // No package, center on origin
                Zoom = 50.0;
                PanX = ActualWidth / 2;
                PanY = ActualHeight / 2;
                return;
            }

            // Calculate bounding box from package dimensions and graphics
            double halfW = Math.Max(package.Width / 2, 1);
            double halfH = Math.Max(package.Length / 2, 1);

            // Expand bounds based on graphics and pins
            foreach (var g in package.Graphics)
            {
                halfW = Math.Max(halfW, Math.Abs(g.X) + g.Width);
                halfH = Math.Max(halfH, Math.Abs(g.Y) + g.Height);
            }
            foreach (var pin in package.Pins)
            {
                halfW = Math.Max(halfW, Math.Abs(pin.X) + pin.Width);
                halfH = Math.Max(halfH, Math.Abs(pin.Y) + pin.Height);
            }

            // Add margin
            halfW *= 1.2;
            halfH *= 1.2;

            ZoomToFit(new Rect(-halfW, -halfH, halfW * 2, halfH * 2));
        }

        /// <summary>
        /// Zoom and pan to fit the specified bounds in the viewport
        /// </summary>
        public void ZoomToFit(Rect worldBounds)
        {
            if (ActualWidth <= 0 || ActualHeight <= 0)
                return;

            if (worldBounds.Width <= 0 || worldBounds.Height <= 0)
            {
                Zoom = 20.0;
                PanX = ActualWidth / 2;
                PanY = ActualHeight / 2;
                return;
            }

            // Calculate zoom to fit bounds with margin
            double margin = 40; // pixels
            double availableWidth = ActualWidth - margin * 2;
            double availableHeight = ActualHeight - margin * 2;

            double zoomX = availableWidth / worldBounds.Width;
            double zoomY = availableHeight / worldBounds.Height;
            Zoom = Math.Min(zoomX, zoomY);

            // Center the bounds
            double centerX = worldBounds.X + worldBounds.Width / 2;
            double centerY = worldBounds.Y + worldBounds.Height / 2;

            PanX = ActualWidth / 2 - centerX * Zoom;
            PanY = ActualHeight / 2 + centerY * Zoom; // Flip Y

            InvalidateVisual();
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

            // Draw board outline if defined
            RenderBoardArea(dc);

            // Render selected package in editor mode (for Component tab)
            if (SelectedPackage != null)
            {
                RenderSelectedPackageDirect(dc);
            }

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

            double screenSpacing = GridSpacing * Zoom;

            // Don't render grid if spacing is too small
            if (screenSpacing < 5) return;

            int majorInterval = 10;

            // Find the world coordinate range visible on screen
            Point worldTopLeft = ScreenToWorld(new Point(0, 0));
            Point worldBottomRight = ScreenToWorld(new Point(ActualWidth, ActualHeight));

            // Calculate first grid line in world coordinates
            double worldMinX = Math.Min(worldTopLeft.X, worldBottomRight.X);
            double worldMaxX = Math.Max(worldTopLeft.X, worldBottomRight.X);
            double worldMinY = Math.Min(worldTopLeft.Y, worldBottomRight.Y);
            double worldMaxY = Math.Max(worldTopLeft.Y, worldBottomRight.Y);

            int startIndexX = (int)Math.Floor(worldMinX / GridSpacing);
            int endIndexX = (int)Math.Ceiling(worldMaxX / GridSpacing);
            int startIndexY = (int)Math.Floor(worldMinY / GridSpacing);
            int endIndexY = (int)Math.Ceiling(worldMaxY / GridSpacing);

            // Draw vertical lines (constant X in world space)
            for (int i = startIndexX; i <= endIndexX; i++)
            {
                double worldX = i * GridSpacing;
                Point screenTop = WorldToScreen(new Point(worldX, worldMaxY));
                Point screenBottom = WorldToScreen(new Point(worldX, worldMinY));

                var pen = (i % majorInterval == 0) ? majorGridPen : gridPen;
                dc.DrawLine(pen, new Point(screenTop.X, 0), new Point(screenTop.X, ActualHeight));
            }

            // Draw horizontal lines (constant Y in world space)
            for (int i = startIndexY; i <= endIndexY; i++)
            {
                double worldY = i * GridSpacing;
                Point screenLeft = WorldToScreen(new Point(worldMinX, worldY));

                var pen = (i % majorInterval == 0) ? majorGridPen : gridPen;
                dc.DrawLine(pen, new Point(0, screenLeft.Y), new Point(ActualWidth, screenLeft.Y));
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

        private void RenderBoardArea(DrawingContext dc)
        {
            if (Board == null || Board.Width <= 0 || Board.Height <= 0)
                return;

            // Board outline pen
            var boardPen = new Pen(new SolidColorBrush(Color.FromRgb(255, 200, 0)), 2);
            boardPen.DashStyle = DashStyles.Dash;
            boardPen.Freeze();

            var boardFillBrush = new SolidColorBrush(Color.FromArgb(20, 255, 200, 0));
            boardFillBrush.Freeze();

            // Convert board corners to screen coordinates
            double originX = Board.Origin.X;
            double originY = Board.Origin.Y;

            Point topLeft = WorldToScreen(new Point(originX, originY + Board.Height));
            Point topRight = WorldToScreen(new Point(originX + Board.Width, originY + Board.Height));
            Point bottomLeft = WorldToScreen(new Point(originX, originY));
            Point bottomRight = WorldToScreen(new Point(originX + Board.Width, originY));

            // Draw board rectangle
            var boardRect = new Rect(
                Math.Min(topLeft.X, bottomRight.X),
                Math.Min(topLeft.Y, bottomRight.Y),
                Math.Abs(topRight.X - topLeft.X),
                Math.Abs(bottomLeft.Y - topLeft.Y)
            );

            dc.DrawRectangle(boardFillBrush, boardPen, boardRect);

            // Draw dimension labels
            var labelBrush = new SolidColorBrush(Color.FromRgb(255, 200, 0));
            labelBrush.Freeze();

            double fontSize = 10;
            var typeface = new Typeface("Segoe UI");

            // Width label (bottom)
            var widthText = new FormattedText(
                string.Format("{0:F1}mm", Board.Width),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                labelBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip
            );
            dc.DrawText(widthText, new Point(
                (bottomLeft.X + bottomRight.X) / 2 - widthText.Width / 2,
                bottomLeft.Y + 4
            ));

            // Height label (right side)
            var heightText = new FormattedText(
                string.Format("{0:F1}mm", Board.Height),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                labelBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip
            );

            // Draw vertically on right side
            dc.PushTransform(new RotateTransform(-90, topRight.X + 4 + heightText.Height / 2,
                (topRight.Y + bottomRight.Y) / 2));
            dc.DrawText(heightText, new Point(
                topRight.X + 4,
                (topRight.Y + bottomRight.Y) / 2 - heightText.Width / 2
            ));
            dc.Pop();
        }

        private void RenderSelectedPackageDirect(DrawingContext dc)
        {
            var package = SelectedPackage;
            if (package == null) return;

            Point screenPos = WorldToScreen(new Point(0, 0)); // Package is at origin

            // Create transform to world coordinates
            var transform = new TransformGroup();
            transform.Children.Add(new ScaleTransform(Zoom, -Zoom)); // Flip Y for world coordinates
            transform.Children.Add(new TranslateTransform(screenPos.X, screenPos.Y));

            dc.PushTransform(transform);

            try
            {
                // Body outline brush/pen
                var bodyBrush = new SolidColorBrush(Color.FromArgb(40, 100, 150, 200));
                bodyBrush.Freeze();
                var bodyPen = new Pen(new SolidColorBrush(Color.FromRgb(100, 150, 200)), 0.05);
                bodyPen.Freeze();

                // Selected item pen (highlighted)
                var selectedPen = new Pen(new SolidColorBrush(Color.FromRgb(0, 200, 255)), 0.08);
                selectedPen.Freeze();

                // Render package graphics
                for (int i = 0; i < package.Graphics.Count; i++)
                {
                    var graphic = package.Graphics[i];
                    bool isSelected = (i == _selectedGraphicIndex);
                    RenderGraphicShape(dc, graphic, bodyBrush, isSelected ? selectedPen : bodyPen);

                    // Draw selection handles if selected
                    if (isSelected)
                    {
                        DrawSelectionHandles(dc, graphic);
                    }
                }

                // Render pins
                var pinBrush = new SolidColorBrush(Color.FromRgb(200, 180, 100));
                pinBrush.Freeze();
                var pinPen = new Pen(new SolidColorBrush(Color.FromRgb(150, 130, 80)), 0.02);
                pinPen.Freeze();
                var selectedPinBrush = new SolidColorBrush(Color.FromRgb(100, 220, 255));
                selectedPinBrush.Freeze();
                var pin1Brush = new SolidColorBrush(Color.FromRgb(255, 100, 100));
                pin1Brush.Freeze();

                for (int i = 0; i < package.Pins.Count; i++)
                {
                    var pin = package.Pins[i];
                    bool isSelected = (i == _selectedPinIndex);
                    var currentBrush = isSelected ? selectedPinBrush : pinBrush;
                    var currentPen = isSelected ? selectedPen : pinPen;

                    double x = pin.X - pin.Width / 2;
                    double y = pin.Y - pin.Height / 2;
                    var pinRect = new Rect(x, y, pin.Width, pin.Height);

                    switch (pin.Shape)
                    {
                        case PinShape.Circle:
                            dc.DrawEllipse(currentBrush, currentPen, new Point(pin.X, pin.Y), pin.Width / 2, pin.Height / 2);
                            break;
                        case PinShape.Oval:
                            dc.DrawRoundedRectangle(currentBrush, currentPen, pinRect, pin.Width / 2, pin.Height / 2);
                            break;
                        case PinShape.RoundedRectangle:
                            dc.DrawRoundedRectangle(currentBrush, currentPen, pinRect, pin.Width * 0.2, pin.Height * 0.2);
                            break;
                        default: // Rectangle
                            dc.DrawRectangle(currentBrush, currentPen, pinRect);
                            break;
                    }

                    // Pin 1 indicator
                    if (pin.Number == 1)
                    {
                        dc.DrawEllipse(pin1Brush, null, new Point(pin.X, pin.Y), pin.Width * 0.15, pin.Height * 0.15);
                    }
                }

                // Draw origin marker at package origin
                var originPen = new Pen(new SolidColorBrush(Colors.Cyan), 0.02);
                originPen.Freeze();
                double originSize = Math.Max(package.Width, package.Length) * 0.1;
                if (originSize < 0.2) originSize = 0.5;
                dc.DrawLine(originPen, new Point(-originSize, 0), new Point(originSize, 0));
                dc.DrawLine(originPen, new Point(0, -originSize), new Point(0, originSize));
            }
            finally
            {
                dc.Pop();
            }
        }

        private void DrawSelectionHandles(DrawingContext dc, PackageGraphic g)
        {
            var handleBrush = new SolidColorBrush(Colors.White);
            handleBrush.Freeze();
            var handlePen = new Pen(new SolidColorBrush(Color.FromRgb(0, 150, 255)), 0.02);
            handlePen.Freeze();

            double handleSize = 0.08;

            // Get corners based on shape type
            switch (g.ShapeType)
            {
                case GraphicShapeType.Rectangle:
                case GraphicShapeType.RoundedRectangle:
                    // Draw handles at corners
                    dc.DrawRectangle(handleBrush, handlePen, new Rect(g.X - handleSize, g.Y - handleSize, handleSize * 2, handleSize * 2));
                    dc.DrawRectangle(handleBrush, handlePen, new Rect(g.X + g.Width - handleSize, g.Y - handleSize, handleSize * 2, handleSize * 2));
                    dc.DrawRectangle(handleBrush, handlePen, new Rect(g.X - handleSize, g.Y + g.Height - handleSize, handleSize * 2, handleSize * 2));
                    dc.DrawRectangle(handleBrush, handlePen, new Rect(g.X + g.Width - handleSize, g.Y + g.Height - handleSize, handleSize * 2, handleSize * 2));
                    break;

                case GraphicShapeType.Circle:
                case GraphicShapeType.Ellipse:
                    // Draw handles at cardinal points
                    dc.DrawRectangle(handleBrush, handlePen, new Rect(g.X - g.Width / 2 - handleSize, g.Y - handleSize, handleSize * 2, handleSize * 2));
                    dc.DrawRectangle(handleBrush, handlePen, new Rect(g.X + g.Width / 2 - handleSize, g.Y - handleSize, handleSize * 2, handleSize * 2));
                    dc.DrawRectangle(handleBrush, handlePen, new Rect(g.X - handleSize, g.Y - g.Height / 2 - handleSize, handleSize * 2, handleSize * 2));
                    dc.DrawRectangle(handleBrush, handlePen, new Rect(g.X - handleSize, g.Y + g.Height / 2 - handleSize, handleSize * 2, handleSize * 2));
                    break;
            }
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

            // Fixed screen size for placement icons (in pixels)
            double iconSize = 12.0;
            double halfSize = iconSize / 2;

            // Viewport bounds for culling (with margin)
            double margin = 50;
            Rect viewport = new Rect(-margin, -margin, ActualWidth + margin * 2, ActualHeight + margin * 2);

            // Only render package details at zoom levels where they're visible
            bool renderPackageDetails = ShowPackageGraphics && Zoom > 0.5;

            // Pre-cache pin1 brush
            var pin1Brush = new SolidColorBrush(Color.FromRgb(255, 100, 100));
            pin1Brush.Freeze();

            foreach (var placement in Placements)
            {
                // Skip placements on the wrong side
                if (placement.Side != ViewSide)
                    continue;

                // Convert world position to screen
                Point screenPos = WorldToScreen(placement.Position);

                // Viewport culling - skip if outside visible area
                if (!viewport.Contains(screenPos))
                    continue;

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

                // Draw package graphics if enabled, zoom is sufficient, and package available
                bool drewPackage = false;
                if (renderPackageDetails && placement.Package != null &&
                    (placement.Package.Graphics.Count > 0 || placement.Package.Pins.Count > 0))
                {
                    drewPackage = RenderPackageGraphics(dc, placement, fillBrush, outlinePen);
                }

                // Fall back to simple marker if no package graphics or zoom too low
                if (!drewPackage)
                {
                    // Draw placement marker (rounded rectangle - fixed screen size)
                    var placementRect = new Rect(
                        screenPos.X - halfSize,
                        screenPos.Y - halfSize,
                        iconSize,
                        iconSize
                    );
                    dc.DrawRoundedRectangle(fillBrush, outlinePen, placementRect, 2, 2);

                    // Draw pin 1 indicator (small circle at top-left)
                    double pin1Size = 2;
                    double pin1Offset = halfSize * 0.6;
                    Point pin1Pos = new Point(
                        screenPos.X - pin1Offset,
                        screenPos.Y - pin1Offset
                    );
                    dc.DrawEllipse(pin1Brush, null, pin1Pos, pin1Size, pin1Size);
                }

                // Draw center crosshair
                double crossSize = 3;
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
                    double fontSize = 10; // Fixed font size
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
                double fidSize = 8; // Fixed screen size

                foreach (var fiducial in Fiducials)
                {
                    if (fiducial.Side != ViewSide)
                        continue;

                    Point screenPos = WorldToScreen(new Point(fiducial.X, fiducial.Y));

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
                        double fontSize = 9; // Fixed font size
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

        private bool RenderPackageGraphics(DrawingContext dc, Placement placement, SolidColorBrush fillBrush, Pen outlinePen)
        {
            var package = placement.Package;
            if (package == null) return false;

            Point screenPos = WorldToScreen(placement.Position);
            double scale = Zoom;

            // Create transform for placement position and rotation
            var transform = new TransformGroup();

            // Rotate around origin
            if (placement.Rotation != 0)
            {
                transform.Children.Add(new RotateTransform(placement.Rotation));
            }

            // Scale to screen coordinates
            transform.Children.Add(new ScaleTransform(scale, -scale)); // Flip Y

            // Translate to screen position
            transform.Children.Add(new TranslateTransform(screenPos.X, screenPos.Y));

            dc.PushTransform(transform);

            try
            {
                // Render package graphics (body outline, etc.)
                foreach (var graphic in package.Graphics)
                {
                    RenderGraphicShape(dc, graphic, fillBrush, outlinePen);
                }

                // Render pins
                var pinBrush = new SolidColorBrush(Color.FromRgb(200, 180, 100));
                pinBrush.Freeze();
                var pinPen = new Pen(new SolidColorBrush(Color.FromRgb(150, 130, 80)), 0.05);
                pinPen.Freeze();

                foreach (var pin in package.Pins)
                {
                    double x = pin.X - pin.Width / 2;
                    double y = pin.Y - pin.Height / 2;
                    var pinRect = new Rect(x, y, pin.Width, pin.Height);

                    switch (pin.Shape)
                    {
                        case PinShape.Circle:
                            dc.DrawEllipse(pinBrush, pinPen, new Point(pin.X, pin.Y), pin.Width / 2, pin.Height / 2);
                            break;
                        case PinShape.Oval:
                            dc.DrawRoundedRectangle(pinBrush, pinPen, pinRect, pin.Width / 2, pin.Height / 2);
                            break;
                        case PinShape.RoundedRectangle:
                            dc.DrawRoundedRectangle(pinBrush, pinPen, pinRect, pin.Width * 0.2, pin.Height * 0.2);
                            break;
                        default: // Rectangle
                            dc.DrawRectangle(pinBrush, pinPen, pinRect);
                            break;
                    }

                    // Draw pin 1 indicator
                    if (pin.Number == 1)
                    {
                        var pin1Brush = new SolidColorBrush(Color.FromRgb(255, 100, 100));
                        pin1Brush.Freeze();
                        dc.DrawEllipse(pin1Brush, null, new Point(pin.X, pin.Y), pin.Width * 0.2, pin.Height * 0.2);
                    }
                }

                return package.Graphics.Count > 0 || package.Pins.Count > 0;
            }
            finally
            {
                dc.Pop();
            }
        }

        private void RenderGraphicShape(DrawingContext dc, PackageGraphic graphic, SolidColorBrush fillBrush, Pen outlinePen)
        {
            var graphicBrush = graphic.IsFilled ? fillBrush : null;
            var graphicPen = new Pen(outlinePen.Brush, graphic.StrokeThickness > 0 ? graphic.StrokeThickness : 0.1);
            graphicPen.Freeze();

            switch (graphic.ShapeType)
            {
                case GraphicShapeType.Rectangle:
                    dc.DrawRectangle(graphicBrush, graphicPen,
                        new Rect(graphic.X, graphic.Y, graphic.Width, graphic.Height));
                    break;

                case GraphicShapeType.RoundedRectangle:
                    double cornerRadius = Math.Min(graphic.Width, graphic.Height) * 0.1;
                    dc.DrawRoundedRectangle(graphicBrush, graphicPen,
                        new Rect(graphic.X, graphic.Y, graphic.Width, graphic.Height),
                        cornerRadius, cornerRadius);
                    break;

                case GraphicShapeType.Circle:
                case GraphicShapeType.Ellipse:
                    dc.DrawEllipse(graphicBrush, graphicPen,
                        new Point(graphic.X, graphic.Y),
                        graphic.Width / 2, graphic.Height / 2);
                    break;

                case GraphicShapeType.Line:
                    if (graphic.Points != null && graphic.Points.Count >= 2)
                    {
                        for (int i = 0; i < graphic.Points.Count - 1; i++)
                        {
                            dc.DrawLine(graphicPen, graphic.Points[i], graphic.Points[i + 1]);
                        }
                    }
                    break;

                case GraphicShapeType.Polygon:
                    if (graphic.Points != null && graphic.Points.Count >= 3)
                    {
                        var geometry = new StreamGeometry();
                        using (var ctx = geometry.Open())
                        {
                            ctx.BeginFigure(graphic.Points[0], graphic.IsFilled, true);
                            for (int i = 1; i < graphic.Points.Count; i++)
                            {
                                ctx.LineTo(graphic.Points[i], true, false);
                            }
                        }
                        geometry.Freeze();
                        dc.DrawGeometry(graphicBrush, graphicPen, geometry);
                    }
                    break;

                case GraphicShapeType.Arc:
                    // Draw arc using StreamGeometry
                    var arcGeometry = new StreamGeometry();
                    using (var ctx = arcGeometry.Open())
                    {
                        double startRad = graphic.StartAngle * Math.PI / 180;
                        double sweepRad = graphic.SweepAngle * Math.PI / 180;
                        double rx = graphic.Width / 2;
                        double ry = graphic.Height / 2;

                        Point startPoint = new Point(
                            graphic.X + rx * Math.Cos(startRad),
                            graphic.Y + ry * Math.Sin(startRad));
                        Point endPoint = new Point(
                            graphic.X + rx * Math.Cos(startRad + sweepRad),
                            graphic.Y + ry * Math.Sin(startRad + sweepRad));

                        ctx.BeginFigure(startPoint, false, false);
                        ctx.ArcTo(endPoint, new Size(rx, ry),
                            0, Math.Abs(graphic.SweepAngle) > 180,
                            graphic.SweepAngle > 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
                            true, false);
                    }
                    arcGeometry.Freeze();
                    dc.DrawGeometry(null, graphicPen, arcGeometry);
                    break;
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

            // Middle mouse button for panning
            if (e.MiddleButton == MouseButtonState.Pressed)
            {
                if (!_isPanning)
                {
                    _isPanning = true;
                    _panStart = mousePos;
                    _lastMousePosition = mousePos;
                    CaptureMouse();
                    Cursor = Cursors.Hand;
                }
                else
                {
                    double deltaX = mousePos.X - _lastMousePosition.X;
                    double deltaY = mousePos.Y - _lastMousePosition.Y;

                    // Drag to pan: drag right = view moves right
                    PanX += deltaX;
                    PanY -= deltaY;

                    InvalidateVisual();
                }
            }
            else if (_isDraggingGraphic && e.LeftButton == MouseButtonState.Pressed && SelectedPackage != null)
            {
                // Package editor drag
                HandlePackageEditorMouseMove(mousePos);
            }
            else if (_isSelecting && e.LeftButton == MouseButtonState.Pressed)
            {
                _selectionRect = new Rect(
                    Math.Min(_selectionStart.X, mousePos.X),
                    Math.Min(_selectionStart.Y, mousePos.Y),
                    Math.Abs(mousePos.X - _selectionStart.X),
                    Math.Abs(mousePos.Y - _selectionStart.Y)
                );
                InvalidateVisual();
            }
            else if (e.LeftButton == MouseButtonState.Pressed && !_isSelecting && SelectedPackage == null)
            {
                // Start rectangle selection after small movement (only in placement mode)
                double dist = Math.Sqrt(Math.Pow(mousePos.X - _selectionStart.X, 2) +
                                       Math.Pow(mousePos.Y - _selectionStart.Y, 2));
                if (dist > 3)
                {
                    _isSelecting = true;
                    CaptureMouse();
                }
            }

            _lastMousePosition = mousePos;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Focus();

            Point mousePos = e.GetPosition(this);
            _selectionStart = mousePos;
            _lastMousePosition = mousePos;

            // Package editor mode
            if (SelectedPackage != null)
            {
                HandlePackageEditorMouseDown(mousePos);
                return;
            }

            // Try to hit test a placement
            Placement hitPlacement = HitTestPlacement(mousePos);

            if (hitPlacement != null)
            {
                bool isCtrlPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

                if (isCtrlPressed)
                {
                    // Toggle selection
                    hitPlacement.IsSelected = !hitPlacement.IsSelected;
                }
                else
                {
                    // Clear other selections and select this one
                    ClearSelection();
                    hitPlacement.IsSelected = true;
                }

                InvalidateVisual();
                RaiseSelectionChanged();
            }
            else
            {
                // No placement hit - will start rectangle select on drag
                if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    ClearSelection();
                    InvalidateVisual();
                    RaiseSelectionChanged();
                }
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);

            // Package editor mode
            if (SelectedPackage != null && _isDraggingGraphic)
            {
                HandlePackageEditorMouseUp();
                return;
            }

            if (_isSelecting)
            {
                _isSelecting = false;
                ReleaseMouseCapture();

                if (_selectionRect.Width > 5 && _selectionRect.Height > 5)
                {
                    // Select all placements in the rectangle
                    bool isCtrlPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

                    if (!isCtrlPressed)
                    {
                        ClearSelection();
                    }

                    SelectPlacementsInRect(_selectionRect);
                    RaiseSelectionChanged();
                }

                _selectionRect = Rect.Empty;
                InvalidateVisual();
            }
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);

            // Handle middle mouse button for panning
            if (e.ChangedButton == MouseButton.Middle)
            {
                Focus();
                _panStart = e.GetPosition(this);
                _lastMousePosition = _panStart;
                e.Handled = true;
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);

            // Handle middle mouse button release
            if (e.ChangedButton == MouseButton.Middle && _isPanning)
            {
                _isPanning = false;
                ReleaseMouseCapture();
                Cursor = Cursors.Arrow;
                e.Handled = true;
            }
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonDown(e);
            Focus();
            // Right-click only for context menu - no panning
        }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonUp(e);
            // Let context menu show
        }

        private Placement HitTestPlacement(Point screenPos)
        {
            if (Placements == null) return null;

            double hitRadius = 8; // Fixed screen pixels for hit testing

            foreach (var placement in Placements)
            {
                if (placement.Side != ViewSide)
                    continue;

                Point placementScreen = WorldToScreen(placement.Position);
                double dist = Math.Sqrt(Math.Pow(screenPos.X - placementScreen.X, 2) +
                                       Math.Pow(screenPos.Y - placementScreen.Y, 2));

                if (dist <= hitRadius)
                    return placement;
            }

            return null;
        }

        private void ClearSelection()
        {
            if (Placements == null) return;
            foreach (var p in Placements)
            {
                p.IsSelected = false;
            }
        }

        private void SelectPlacementsInRect(Rect screenRect)
        {
            if (Placements == null) return;

            foreach (var placement in Placements)
            {
                if (placement.Side != ViewSide)
                    continue;

                Point placementScreen = WorldToScreen(placement.Position);
                if (screenRect.Contains(placementScreen))
                {
                    placement.IsSelected = true;
                }
            }
        }

        private void RaiseSelectionChanged()
        {
            // Raise event for view/view model to handle
            if (Placements != null)
            {
                var selected = new List<Placement>();
                foreach (var p in Placements)
                {
                    if (p.IsSelected) selected.Add(p);
                }
                SelectionChanged?.Invoke(this, selected);
            }
        }

        #endregion

        #region Package Editor Mouse Handling

        private void HandlePackageEditorMouseDown(Point mousePos)
        {
            var package = SelectedPackage;
            if (package == null) return;

            Point worldPos = ScreenToWorld(mousePos);
            _dragStartWorld = worldPos;

            // Hit test pins first (they're on top)
            double hitRadius = 10 / Zoom; // Screen pixels converted to world
            for (int i = 0; i < package.Pins.Count; i++)
            {
                var pin = package.Pins[i];
                double dist = Math.Sqrt(Math.Pow(worldPos.X - pin.X, 2) + Math.Pow(worldPos.Y - pin.Y, 2));
                if (dist <= Math.Max(pin.Width, pin.Height) / 2 + hitRadius)
                {
                    SelectedPinIndex = i;
                    _isDraggingGraphic = true;
                    CaptureMouse();
                    return;
                }
            }

            // Hit test graphics
            for (int i = 0; i < package.Graphics.Count; i++)
            {
                var g = package.Graphics[i];
                if (HitTestGraphic(g, worldPos, hitRadius))
                {
                    SelectedGraphicIndex = i;
                    _isDraggingGraphic = true;
                    CaptureMouse();
                    return;
                }
            }

            // Clicked on empty space - clear selection
            SelectedGraphicIndex = -1;
            SelectedPinIndex = -1;
        }

        private bool HitTestGraphic(PackageGraphic g, Point worldPos, double hitRadius)
        {
            switch (g.ShapeType)
            {
                case GraphicShapeType.Rectangle:
                case GraphicShapeType.RoundedRectangle:
                    // Check if point is near the rectangle edges
                    var rect = new Rect(g.X, g.Y, g.Width, g.Height);
                    rect.Inflate(hitRadius, hitRadius);
                    if (rect.Contains(worldPos))
                    {
                        var innerRect = new Rect(g.X + hitRadius, g.Y + hitRadius,
                            Math.Max(0, g.Width - hitRadius * 2), Math.Max(0, g.Height - hitRadius * 2));
                        return !innerRect.Contains(worldPos) || g.IsFilled;
                    }
                    return false;

                case GraphicShapeType.Circle:
                case GraphicShapeType.Ellipse:
                    double dist = Math.Sqrt(Math.Pow(worldPos.X - g.X, 2) + Math.Pow(worldPos.Y - g.Y, 2));
                    double radius = Math.Max(g.Width, g.Height) / 2;
                    return dist <= radius + hitRadius && (g.IsFilled || dist >= radius - hitRadius);

                case GraphicShapeType.Line:
                    if (g.Points != null && g.Points.Count >= 2)
                    {
                        for (int j = 0; j < g.Points.Count - 1; j++)
                        {
                            if (DistanceToLineSegment(worldPos, g.Points[j], g.Points[j + 1]) <= hitRadius)
                                return true;
                        }
                    }
                    return false;

                case GraphicShapeType.Polygon:
                    if (g.Points != null && g.Points.Count >= 3)
                    {
                        // Check edges
                        for (int j = 0; j < g.Points.Count; j++)
                        {
                            var p1 = g.Points[j];
                            var p2 = g.Points[(j + 1) % g.Points.Count];
                            if (DistanceToLineSegment(worldPos, p1, p2) <= hitRadius)
                                return true;
                        }
                    }
                    return false;

                default:
                    return false;
            }
        }

        private double DistanceToLineSegment(Point p, Point a, Point b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double lengthSq = dx * dx + dy * dy;

            if (lengthSq == 0) return Math.Sqrt(Math.Pow(p.X - a.X, 2) + Math.Pow(p.Y - a.Y, 2));

            double t = Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSq));
            double projX = a.X + t * dx;
            double projY = a.Y + t * dy;

            return Math.Sqrt(Math.Pow(p.X - projX, 2) + Math.Pow(p.Y - projY, 2));
        }

        private void HandlePackageEditorMouseMove(Point mousePos)
        {
            if (!_isDraggingGraphic) return;

            var package = SelectedPackage;
            if (package == null) return;

            Point worldPos = ScreenToWorld(mousePos);
            double dx = worldPos.X - _dragStartWorld.X;
            double dy = worldPos.Y - _dragStartWorld.Y;

            if (_selectedPinIndex >= 0 && _selectedPinIndex < package.Pins.Count)
            {
                var pin = package.Pins[_selectedPinIndex];
                pin.X += dx;
                pin.Y += dy;
            }
            else if (_selectedGraphicIndex >= 0 && _selectedGraphicIndex < package.Graphics.Count)
            {
                var g = package.Graphics[_selectedGraphicIndex];
                g.X += dx;
                g.Y += dy;
                if (g.Points != null)
                {
                    for (int i = 0; i < g.Points.Count; i++)
                    {
                        g.Points[i] = new Point(g.Points[i].X + dx, g.Points[i].Y + dy);
                    }
                }
            }

            _dragStartWorld = worldPos;
            GraphicMoved?.Invoke(this, worldPos);
            InvalidateVisual();
        }

        private void HandlePackageEditorMouseUp()
        {
            if (_isDraggingGraphic)
            {
                _isDraggingGraphic = false;
                ReleaseMouseCapture();
            }
        }

        /// <summary>
        /// Delete the currently selected graphic or pin from the package
        /// </summary>
        public void DeleteSelectedGraphic()
        {
            var package = SelectedPackage;
            if (package == null) return;

            if (_selectedPinIndex >= 0 && _selectedPinIndex < package.Pins.Count)
            {
                package.Pins.RemoveAt(_selectedPinIndex);
                SelectedPinIndex = -1;
            }
            else if (_selectedGraphicIndex >= 0 && _selectedGraphicIndex < package.Graphics.Count)
            {
                package.Graphics.RemoveAt(_selectedGraphicIndex);
                SelectedGraphicIndex = -1;
            }
            InvalidateVisual();
        }

        #endregion

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);

            Point mousePos = e.GetPosition(this);
            Point worldBefore = ScreenToWorld(mousePos);

            // Zoom - allow up to 100000% (1000x)
            double zoomFactor = e.Delta > 0 ? 1.2 : 1.0 / 1.2;
            double newZoom = Zoom * zoomFactor;
            newZoom = Math.Max(0.001, newZoom); // No upper limit on zoom

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
                    PanX -= panAmount * Zoom;
                    InvalidateVisual();
                    e.Handled = true;
                    break;
                case Key.Right:
                    PanX += panAmount * Zoom;
                    InvalidateVisual();
                    e.Handled = true;
                    break;
                case Key.Up:
                    PanY += panAmount * Zoom;
                    InvalidateVisual();
                    e.Handled = true;
                    break;
                case Key.Down:
                    PanY -= panAmount * Zoom;
                    InvalidateVisual();
                    e.Handled = true;
                    break;
                case Key.Home:
                    // Reset view - fit content
                    if (SelectedPackage != null)
                        ZoomToFitPackage();
                    else
                        ZoomToFitPlacements();
                    e.Handled = true;
                    break;
                case Key.Delete:
                case Key.Back:
                    // Delete selected graphic/pin in package editor mode
                    if (SelectedPackage != null)
                    {
                        DeleteSelectedGraphic();
                        e.Handled = true;
                    }
                    break;
                case Key.F:
                    // Fit to view
                    if (SelectedPackage != null)
                        ZoomToFitPackage();
                    else
                        ZoomToFitPlacements();
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
