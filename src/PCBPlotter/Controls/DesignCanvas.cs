using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
        private SolidColorBrush _labelBgBrush;
        private SolidColorBrush _crosshairBrush;
        private SolidColorBrush _pin1Brush;
        private SolidColorBrush _pinBrush;
        private SolidColorBrush _fiducialBrush;
        private Pen _placementOutlinePen;
        private Pen _placementSelectedPen;
        private Pen _pin1Pen;
        private Pen _crosshairPen;
        private Pen _pinPen;
        private Pen _fiducialPen;
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

        public static readonly DependencyProperty GerberLayersProperty =
            DependencyProperty.Register("GerberLayers", typeof(ObservableCollection<GerberLayer>), typeof(DesignCanvas),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnGerberLayersChanged));

        public static readonly DependencyProperty UseScreenBlendProperty =
            DependencyProperty.Register("UseScreenBlend", typeof(bool), typeof(DesignCanvas),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

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

        public ObservableCollection<GerberLayer> GerberLayers
        {
            get { return (ObservableCollection<GerberLayer>)GetValue(GerberLayersProperty); }
            set { SetValue(GerberLayersProperty, value); }
        }

        public bool UseScreenBlend
        {
            get { return (bool)GetValue(UseScreenBlendProperty); }
            set { SetValue(UseScreenBlendProperty, value); }
        }

        public Package SelectedPackage
        {
            get { return (Package)GetValue(SelectedPackageProperty); }
            set { SetValue(SelectedPackageProperty, value); }
        }

        // Package editing state - multi-select support
        private HashSet<int> _selectedGraphicIndices = new HashSet<int>();
        private HashSet<int> _selectedPinIndices = new HashSet<int>();
        private bool _isDraggingGraphic;
        private Point _dragStartWorld;
        private bool _isPackageSelecting;
        private Point _packageSelectStart;
        private Rect _packageSelectRect;

        // Undo stack for package editing
        private Stack<PackageEditAction> _undoStack = new Stack<PackageEditAction>();
        private Stack<PackageEditAction> _redoStack = new Stack<PackageEditAction>();

        private class PackageEditAction
        {
            public List<int> GraphicIndices { get; set; }
            public List<int> PinIndices { get; set; }
            public List<Point> OldPositions { get; set; }
            public List<Point> NewPositions { get; set; }
        }

        public HashSet<int> SelectedGraphicIndices => _selectedGraphicIndices;
        public HashSet<int> SelectedPinIndices => _selectedPinIndices;

        // Legacy single-select properties for compatibility
        public int SelectedGraphicIndex
        {
            get { return _selectedGraphicIndices.Count > 0 ? _selectedGraphicIndices.First() : -1; }
            set
            {
                _selectedGraphicIndices.Clear();
                _selectedPinIndices.Clear();
                if (value >= 0) _selectedGraphicIndices.Add(value);
                InvalidateVisual();
                GraphicSelectionChanged?.Invoke(this, value);
            }
        }

        public int SelectedPinIndex
        {
            get { return _selectedPinIndices.Count > 0 ? _selectedPinIndices.First() : -1; }
            set
            {
                _selectedGraphicIndices.Clear();
                _selectedPinIndices.Clear();
                if (value >= 0) _selectedPinIndices.Add(value);
                InvalidateVisual();
                PinSelectionChanged?.Invoke(this, value);
            }
        }

        public void ClearPackageSelection()
        {
            _selectedGraphicIndices.Clear();
            _selectedPinIndices.Clear();
            InvalidateVisual();
        }

        #endregion

        #region Events

        public event EventHandler<Point> CursorPositionChanged;
        public event EventHandler<Rect> SelectionRectCompleted;
        public event EventHandler<Point> PointClicked;
        public event EventHandler<List<Placement>> SelectionChanged;
        public event EventHandler<List<GerberPrimitive>> GerberSelectionChanged;
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

            // Use HighQuality scaling for smooth zoom (anti-aliased bilinear filtering)
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);

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

            // WorldToScreen: screenX = world.X * Zoom + PanX
            // Want screen center: ActualWidth/2 = centerX * Zoom + PanX
            PanX = ActualWidth / 2 - centerX * Zoom;

            // WorldToScreen: screenY = ActualHeight - (world.Y * Zoom + PanY)
            // Want screen center: ActualHeight/2 = ActualHeight - (centerY * Zoom + PanY)
            // Solving: centerY * Zoom + PanY = ActualHeight/2
            PanY = ActualHeight / 2 - centerY * Zoom;

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

            _labelBgBrush = new SolidColorBrush(Color.FromArgb(180, 30, 30, 35));
            _labelBgBrush.Freeze();

            _crosshairBrush = new SolidColorBrush(Colors.White);
            _crosshairBrush.Freeze();

            _pin1Brush = new SolidColorBrush(Color.FromRgb(255, 100, 100));
            _pin1Brush.Freeze();

            _pinBrush = new SolidColorBrush(Color.FromRgb(200, 180, 100));
            _pinBrush.Freeze();

            _fiducialBrush = new SolidColorBrush(Color.FromRgb(255, 0, 255));
            _fiducialBrush.Freeze();

            // Create and freeze pens
            _placementOutlinePen = new Pen(new SolidColorBrush(Color.FromRgb(150, 150, 160)), 1.5);
            _placementOutlinePen.Freeze();

            _placementSelectedPen = new Pen(new SolidColorBrush(Color.FromRgb(0, 180, 255)), 2);
            _placementSelectedPen.Freeze();

            _pin1Pen = new Pen(new SolidColorBrush(Color.FromRgb(255, 100, 100)), 2);
            _pin1Pen.Freeze();

            _crosshairPen = new Pen(_crosshairBrush, 1);
            _crosshairPen.Freeze();

            _pinPen = new Pen(new SolidColorBrush(Color.FromRgb(150, 130, 80)), 0.05);
            _pinPen.Freeze();

            _fiducialPen = new Pen(_fiducialBrush, 2);
            _fiducialPen.Freeze();

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

        private static void OnGerberLayersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var canvas = d as DesignCanvas;
            if (canvas == null) return;

            // Unsubscribe from old collection
            var oldCollection = e.OldValue as ObservableCollection<GerberLayer>;
            if (oldCollection != null)
            {
                oldCollection.CollectionChanged -= canvas.OnGerberLayersCollectionChanged;
            }

            // Subscribe to new collection
            var newCollection = e.NewValue as ObservableCollection<GerberLayer>;
            if (newCollection != null)
            {
                newCollection.CollectionChanged += canvas.OnGerberLayersCollectionChanged;
            }

            // Clear all caches when layers change - bitmaps will be rendered on demand
            canvas._layerBitmaps.Clear();
            canvas._worldBounds = Rect.Empty;
            canvas._activeLayerQuadtree = null;
            canvas._activeGerberLayer = null;
            canvas._gerberCacheDirty = true;
            canvas.InvalidateVisual();
        }

        private void OnGerberLayersCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // New layers added - they will be rasterized on demand
            // Removed layers - their bitmaps will be cleaned up in EnsureLayerBitmapsBuilt
            _gerberCacheDirty = true;
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

            // Render Gerber layers
            RenderGerberLayers(dc);

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

            // Render package editor selection rectangle
            if (_isPackageSelecting && _packageSelectRect.Width > 0 && _packageSelectRect.Height > 0)
            {
                var fillBrush = new SolidColorBrush(Color.FromArgb(40, 100, 200, 255));
                fillBrush.Freeze();
                var strokePen = new Pen(new SolidColorBrush(Color.FromRgb(100, 200, 255)), 1);
                strokePen.DashStyle = DashStyles.Dash;
                strokePen.Freeze();
                dc.DrawRectangle(fillBrush, strokePen, _packageSelectRect);
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

        // ===================================================================================
        // SIMPLE LAYER BITMAP ARCHITECTURE
        // "Render Once Per Layer, Transform Forever"
        //
        // Each layer is rendered ONCE to a bitmap when loaded/changed.
        // Pan/zoom just transforms the cached bitmaps - no re-rendering.
        // ===================================================================================

        // Layer bitmap cache: layerId -> bitmap data
        private Dictionary<string, LayerBitmapCache> _layerBitmaps = new Dictionary<string, LayerBitmapCache>();

        // World bounds of all layers combined
        private Rect _worldBounds = Rect.Empty;

        // Rendering constants
        private const int MAX_BITMAP_SIZE = 8192;  // Increased for better zoom quality
        private const double BASE_PIXELS_PER_UNIT = 100.0; // 100 pixels per mm at zoom=1
        private const double MIN_SCREEN_PIXELS_PER_UNIT = 2.0; // Re-render when less than 2 screen pixels per world unit

        // Active layer for selection
        private GerberLayer _activeGerberLayer;
        private GerberQuadtree _activeLayerQuadtree;
        private bool _gerberCacheDirty = true;
        private double _lastRenderZoom = 0;

        /// <summary>
        /// Cached bitmap for a single layer
        /// </summary>
        private class LayerBitmapCache
        {
            public WriteableBitmap Bitmap { get; set; }
            public byte[] MonoPixels { get; set; }  // 1-bit storage
            public int Width { get; set; }
            public int Height { get; set; }
            public Rect WorldBounds { get; set; }
            public Color LayerColor { get; set; }
            public double Opacity { get; set; }
            public double PixelsPerUnitX { get; set; }
            public double PixelsPerUnitY { get; set; }
            public double RenderZoom { get; set; }  // Zoom level when this bitmap was rendered
        }

        /// <summary>
        /// Get bounding box of a primitive in world coordinates
        /// </summary>
        private Rect GetPrimitiveBounds(GerberPrimitive prim)
        {
            double halfW = prim.Width / 2;
            double halfH = prim.Height / 2;

            switch (prim.Type)
            {
                case GerberPrimitiveType.Circle:
                case GerberPrimitiveType.Flash:
                    return new Rect(prim.X - halfW, prim.Y - halfW, prim.Width, prim.Width);

                case GerberPrimitiveType.Rectangle:
                case GerberPrimitiveType.Obround:
                    return new Rect(prim.X - halfW, prim.Y - halfH, prim.Width, prim.Height);

                case GerberPrimitiveType.Line:
                case GerberPrimitiveType.Arc:
                case GerberPrimitiveType.Polygon:
                case GerberPrimitiveType.Contour:
                    if (prim.Points != null && prim.Points.Count > 0)
                    {
                        double pMinX = prim.Points.Min(p => p.X) - halfW;
                        double pMaxX = prim.Points.Max(p => p.X) + halfW;
                        double pMinY = prim.Points.Min(p => p.Y) - halfW;
                        double pMaxY = prim.Points.Max(p => p.Y) + halfW;
                        return new Rect(pMinX, pMinY, pMaxX - pMinX, pMaxY - pMinY);
                    }
                    return new Rect(prim.X - halfW, prim.Y - halfH, prim.Width, prim.Height);

                default:
                    return new Rect(prim.X - halfW, prim.Y - halfH,
                        Math.Max(prim.Width, 0.1), Math.Max(prim.Height, 0.1));
            }
        }

        /// <summary>
        /// Simple quadtree for spatial indexing of Gerber primitives
        /// </summary>
        private class GerberQuadtree
        {
            private const int MAX_ITEMS_PER_NODE = 50;
            private const int MAX_DEPTH = 8;

            private Rect _bounds;
            private int _depth;
            private List<GerberPrimitive> _items;
            private GerberQuadtree[] _children;

            public GerberQuadtree(Rect bounds, int depth = 0)
            {
                _bounds = bounds;
                _depth = depth;
                _items = new List<GerberPrimitive>();
            }

            public void Insert(GerberPrimitive prim)
            {
                var primBounds = prim.GetBounds();
                if (!_bounds.IntersectsWith(primBounds))
                    return;

                if (_children != null)
                {
                    foreach (var child in _children)
                        child.Insert(prim);
                    return;
                }

                _items.Add(prim);

                if (_items.Count > MAX_ITEMS_PER_NODE && _depth < MAX_DEPTH)
                {
                    Subdivide();
                }
            }

            private void Subdivide()
            {
                double halfW = _bounds.Width / 2;
                double halfH = _bounds.Height / 2;
                double x = _bounds.X;
                double y = _bounds.Y;

                _children = new GerberQuadtree[4];
                _children[0] = new GerberQuadtree(new Rect(x, y, halfW, halfH), _depth + 1);
                _children[1] = new GerberQuadtree(new Rect(x + halfW, y, halfW, halfH), _depth + 1);
                _children[2] = new GerberQuadtree(new Rect(x, y + halfH, halfW, halfH), _depth + 1);
                _children[3] = new GerberQuadtree(new Rect(x + halfW, y + halfH, halfW, halfH), _depth + 1);

                foreach (var item in _items)
                {
                    foreach (var child in _children)
                        child.Insert(item);
                }

                _items.Clear();
            }

            public List<GerberPrimitive> Query(Point worldPoint, double radius)
            {
                var results = new List<GerberPrimitive>();
                var queryRect = new Rect(worldPoint.X - radius, worldPoint.Y - radius, radius * 2, radius * 2);
                QueryRect(queryRect, results);
                return results;
            }

            public List<GerberPrimitive> QueryRect(Rect rect)
            {
                var results = new List<GerberPrimitive>();
                QueryRect(rect, results);
                return results;
            }

            private void QueryRect(Rect rect, List<GerberPrimitive> results)
            {
                if (!_bounds.IntersectsWith(rect))
                    return;

                if (_children != null)
                {
                    foreach (var child in _children)
                        child.QueryRect(rect, results);
                }
                else
                {
                    foreach (var item in _items)
                    {
                        if (rect.IntersectsWith(item.GetBounds()))
                            results.Add(item);
                    }
                }
            }
        }

        /// <summary>
        /// Render Gerber layers using direct layer-to-bitmap approach with GPU compositing.
        /// Each layer is rendered once and cached. Pan/zoom just transforms the cached bitmaps.
        /// WPF's GPU handles blending - no CPU-based pixel compositing needed.
        /// </summary>
        private void RenderGerberLayers(DrawingContext dc)
        {
            if (GerberLayers == null || GerberLayers.Count == 0)
                return;

            EnsureActiveLayerSet();
            UpdateWorldBoundsFromLayers();

            if (_worldBounds.IsEmpty)
                return;

            // Ensure each layer has a cached bitmap
            EnsureLayerBitmapsBuilt();

            // Draw each visible layer directly - let GPU handle compositing
            // This is MUCH faster than CPU-based pixel blending
            foreach (var layer in GerberLayers)
            {
                if (!layer.IsVisible)
                    continue;

                if (!_layerBitmaps.TryGetValue(layer.Id, out var cache) || cache.Bitmap == null)
                    continue;

                // Convert layer world bounds to screen coordinates
                Point screenTL = WorldToScreen(new Point(cache.WorldBounds.Left, cache.WorldBounds.Top + cache.WorldBounds.Height));
                Point screenBR = WorldToScreen(new Point(cache.WorldBounds.Right, cache.WorldBounds.Top));

                Rect screenRect = new Rect(
                    Math.Min(screenTL.X, screenBR.X),
                    Math.Min(screenTL.Y, screenBR.Y),
                    Math.Abs(screenBR.X - screenTL.X),
                    Math.Abs(screenBR.Y - screenTL.Y));

                // Use opacity for layer blending - GPU handles this efficiently
                if (layer.Opacity < 1.0)
                {
                    dc.PushOpacity(layer.Opacity);
                    dc.DrawImage(cache.Bitmap, screenRect);
                    dc.Pop();
                }
                else
                {
                    dc.DrawImage(cache.Bitmap, screenRect);
                }
            }

            // Draw active layer highlight
            if (_activeGerberLayer != null && _activeGerberLayer.IsVisible)
            {
                RenderActiveLayerHighlight(dc);
            }

            RenderGerberSelectionHighlights(dc);
        }

        /// <summary>
        /// Ensure all layers have their bitmaps built at appropriate resolution for current zoom
        /// </summary>
        private void EnsureLayerBitmapsBuilt()
        {
            foreach (var layer in GerberLayers)
            {
                if (!_layerBitmaps.TryGetValue(layer.Id, out var cache) || cache == null)
                {
                    // Build bitmap for this layer
                    var newCache = BuildLayerBitmap(layer);
                    if (newCache != null)
                    {
                        _layerBitmaps[layer.Id] = newCache;
                    }
                }
                else if (cache.LayerColor != layer.Color)
                {
                    // Color changed - rebuild just the colored bitmap
                    RebuildLayerColoredBitmap(cache, layer.Color, layer.Opacity);
                }
                else if (NeedsHigherResolution(cache, layer))
                {
                    // Zoom level requires higher resolution - rebuild entire bitmap
                    var newCache = BuildLayerBitmap(layer);
                    if (newCache != null)
                    {
                        _layerBitmaps[layer.Id] = newCache;
                    }
                }
            }

            // Remove cached bitmaps for layers that no longer exist
            var layerIds = new HashSet<string>(GerberLayers.Select(l => l.Id));
            var toRemove = _layerBitmaps.Keys.Where(k => !layerIds.Contains(k)).ToList();
            foreach (var key in toRemove)
            {
                _layerBitmaps.Remove(key);
            }
        }

        /// <summary>
        /// Check if the cached bitmap needs to be re-rendered at higher resolution
        /// </summary>
        private bool NeedsHigherResolution(LayerBitmapCache cache, GerberLayer layer)
        {
            if (!layer.IsVisible) return false;

            // Calculate current screen pixels per world unit
            double screenPixelsPerUnit = Zoom;

            // Calculate cached bitmap pixels per world unit
            double cachedPixelsPerUnit = Math.Min(cache.PixelsPerUnitX, cache.PixelsPerUnitY);

            // If we're zoomed in and the bitmap resolution is less than screen resolution,
            // we need to re-render at higher resolution
            // Use a threshold to avoid constant re-rendering
            double ratio = cachedPixelsPerUnit / screenPixelsPerUnit;

            // Re-render if bitmap has less than MIN_SCREEN_PIXELS_PER_UNIT pixels per screen pixel
            // and we haven't already hit the max bitmap size
            if (ratio < MIN_SCREEN_PIXELS_PER_UNIT && cache.Width < MAX_BITMAP_SIZE && cache.Height < MAX_BITMAP_SIZE)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Build a single layer's bitmap at resolution appropriate for current zoom
        /// </summary>
        private LayerBitmapCache BuildLayerBitmap(GerberLayer layer)
        {
            try
            {
                if (layer.Primitives == null || layer.Primitives.Count == 0)
                    return null;

                Rect layerBounds = layer.Bounds;
                if (layerBounds.IsEmpty || layerBounds.Width <= 0 || layerBounds.Height <= 0)
                    return null;

                // Add small margin to layer bounds
                layerBounds.Inflate(layerBounds.Width * 0.02, layerBounds.Height * 0.02);

                // Calculate target resolution based on current zoom
                // We want enough pixels that zooming in doesn't show pixelation
                double currentZoom = Math.Max(1.0, Zoom);

                // Target: at least MIN_SCREEN_PIXELS_PER_UNIT * 2 bitmap pixels per screen pixel
                // This provides some headroom before needing to re-render
                double targetPPU = Math.Max(BASE_PIXELS_PER_UNIT, currentZoom * MIN_SCREEN_PIXELS_PER_UNIT * 2);

                int bitmapWidth = Math.Max(1, (int)Math.Ceiling(layerBounds.Width * targetPPU));
                int bitmapHeight = Math.Max(1, (int)Math.Ceiling(layerBounds.Height * targetPPU));

                // Clamp to max size
                if (bitmapWidth > MAX_BITMAP_SIZE)
                {
                    double ratio = (double)MAX_BITMAP_SIZE / bitmapWidth;
                    bitmapWidth = MAX_BITMAP_SIZE;
                    bitmapHeight = Math.Max(1, (int)Math.Ceiling(bitmapHeight * ratio));
                }
                if (bitmapHeight > MAX_BITMAP_SIZE)
                {
                    double ratio = (double)MAX_BITMAP_SIZE / bitmapHeight;
                    bitmapHeight = MAX_BITMAP_SIZE;
                    bitmapWidth = Math.Max(1, (int)Math.Ceiling(bitmapWidth * ratio));
                }

                // Final clamp
                bitmapWidth = Math.Min(bitmapWidth, MAX_BITMAP_SIZE);
                bitmapHeight = Math.Min(bitmapHeight, MAX_BITMAP_SIZE);

                double ppuX = bitmapWidth / layerBounds.Width;
                double ppuY = bitmapHeight / layerBounds.Height;

                // Create 1-bit storage
                int bytesPerRow = (bitmapWidth + 7) / 8;
                byte[] monoPixels = new byte[bytesPerRow * bitmapHeight];

                // Rasterize all primitives
                foreach (var prim in layer.Primitives)
                {
                    RasterizePrimitive(monoPixels, bitmapWidth, bitmapHeight, bytesPerRow,
                        prim, layerBounds, ppuX, ppuY);
                }

                // Create cache entry
                var cache = new LayerBitmapCache
                {
                    MonoPixels = monoPixels,
                    Width = bitmapWidth,
                    Height = bitmapHeight,
                    WorldBounds = layerBounds,
                    LayerColor = layer.Color,
                    Opacity = layer.Opacity,
                    PixelsPerUnitX = ppuX,
                    PixelsPerUnitY = ppuY,
                    RenderZoom = currentZoom
                };

                // Build colored bitmap
                RebuildLayerColoredBitmap(cache, layer.Color, layer.Opacity);

                return cache;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error building layer bitmap for {layer.Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Rasterize a primitive to bitmap coordinates
        /// </summary>
        private void RasterizePrimitive(byte[] pixels, int width, int height, int bytesPerRow,
            GerberPrimitive prim, Rect worldBounds, double ppuX, double ppuY)
        {
            // Convert world to bitmap coordinates (Y is flipped)
            double bx = (prim.X - worldBounds.Left) * ppuX;
            double by = (worldBounds.Top + worldBounds.Height - prim.Y) * ppuY;
            double sw = prim.Width * ppuX;
            double sh = prim.Height * ppuY;

            double avgPpu = (ppuX + ppuY) / 2;

            switch (prim.Type)
            {
                case GerberPrimitiveType.Circle:
                case GerberPrimitiveType.Flash:
                    double radius = prim.Width * avgPpu / 2;
                    Fill1BitCircle(pixels, width, height, bytesPerRow, bx, by, radius);
                    break;

                case GerberPrimitiveType.Rectangle:
                    Fill1BitRectangle(pixels, width, height, bytesPerRow, bx, by, sw, sh);
                    break;

                case GerberPrimitiveType.Obround:
                    Fill1BitObround(pixels, width, height, bytesPerRow, bx, by, sw, sh);
                    break;

                case GerberPrimitiveType.Line:
                case GerberPrimitiveType.Arc:
                    if (prim.Points != null && prim.Points.Count >= 2)
                    {
                        double lineWidth = prim.Width * avgPpu;
                        for (int i = 1; i < prim.Points.Count; i++)
                        {
                            double x1 = (prim.Points[i - 1].X - worldBounds.Left) * ppuX;
                            double y1 = (worldBounds.Top + worldBounds.Height - prim.Points[i - 1].Y) * ppuY;
                            double x2 = (prim.Points[i].X - worldBounds.Left) * ppuX;
                            double y2 = (worldBounds.Top + worldBounds.Height - prim.Points[i].Y) * ppuY;
                            Fill1BitLine(pixels, width, height, bytesPerRow, x1, y1, x2, y2, lineWidth);
                        }
                    }
                    break;

                case GerberPrimitiveType.Polygon:
                case GerberPrimitiveType.Contour:
                    if (prim.Points != null && prim.Points.Count >= 3)
                    {
                        var scaledPoints = prim.Points.Select(p => new Point(
                            (p.X - worldBounds.Left) * ppuX,
                            (worldBounds.Top + worldBounds.Height - p.Y) * ppuY)).ToList();
                        Fill1BitPolygon(pixels, width, height, bytesPerRow, scaledPoints);
                    }
                    break;
            }
        }

        /// <summary>
        /// Rebuild colored bitmap from mono data when color changes.
        /// Opacity is handled by the GPU via PushOpacity during rendering.
        /// </summary>
        private void RebuildLayerColoredBitmap(LayerBitmapCache cache, Color color, double opacity)
        {
            try
            {
                cache.LayerColor = color;
                cache.Opacity = opacity;

                var bitmap = new WriteableBitmap(cache.Width, cache.Height, 96, 96, PixelFormats.Bgra32, null);
                int bytesPerRow = (cache.Width + 7) / 8;

                byte r = color.R;
                byte g = color.G;
                byte b = color.B;
                // Use full alpha - opacity is applied by GPU during rendering
                byte a = 255;

                bitmap.Lock();
                try
                {
                    unsafe
                    {
                        byte* ptr = (byte*)bitmap.BackBuffer;
                        int stride = bitmap.BackBufferStride;

                        for (int y = 0; y < cache.Height; y++)
                        {
                            for (int x = 0; x < cache.Width; x++)
                            {
                                int byteIdx = y * bytesPerRow + (x / 8);
                                int bitIdx = 7 - (x % 8);
                                bool isSet = (cache.MonoPixels[byteIdx] & (1 << bitIdx)) != 0;

                                int offset = y * stride + x * 4;
                                if (isSet)
                                {
                                    ptr[offset + 0] = b;
                                    ptr[offset + 1] = g;
                                    ptr[offset + 2] = r;
                                    ptr[offset + 3] = a;
                                }
                                else
                                {
                                    ptr[offset + 0] = 0;
                                    ptr[offset + 1] = 0;
                                    ptr[offset + 2] = 0;
                                    ptr[offset + 3] = 0;
                                }
                            }
                        }
                    }
                    bitmap.AddDirtyRect(new Int32Rect(0, 0, cache.Width, cache.Height));
                }
                finally
                {
                    bitmap.Unlock();
                }

                bitmap.Freeze();
                cache.Bitmap = bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error rebuilding layer colored bitmap: {ex.Message}");
            }
        }

        /// <summary>
        /// Update world bounds from all layers
        /// </summary>
        private void UpdateWorldBoundsFromLayers()
        {
            _worldBounds = Rect.Empty;
            foreach (var layer in GerberLayers)
            {
                if (layer.Bounds.IsEmpty)
                    continue;

                if (_worldBounds.IsEmpty)
                    _worldBounds = layer.Bounds;
                else
                    _worldBounds.Union(layer.Bounds);
            }

            if (!_worldBounds.IsEmpty)
            {
                _worldBounds.Inflate(_worldBounds.Width * 0.02, _worldBounds.Height * 0.02);
            }
        }

        /// <summary>
        /// Ensure an active layer is set (default to first visible layer)
        /// </summary>
        private void EnsureActiveLayerSet()
        {
            // Check if current active layer is still valid
            if (_activeGerberLayer != null && GerberLayers.Contains(_activeGerberLayer))
                return;

            // Find first active layer
            _activeGerberLayer = GerberLayers.FirstOrDefault(l => l.IsActive && l.IsVisible);

            // If none marked active, pick first visible layer
            if (_activeGerberLayer == null)
            {
                _activeGerberLayer = GerberLayers.FirstOrDefault(l => l.IsVisible);
                if (_activeGerberLayer != null)
                {
                    _activeGerberLayer.IsActive = true;
                }
            }

            // Rebuild quadtree for the new active layer
            if (_activeGerberLayer != null)
            {
                RebuildActiveLayerQuadtree();
            }
        }

        /// <summary>
        /// Set the active layer for selection/editing
        /// In the new architecture, all layers are pre-rasterized, so we just track which one is "active"
        /// </summary>
        public void SetActiveGerberLayer(GerberLayer layer)
        {
            if (layer == _activeGerberLayer)
                return;

            // Update active state on old layer
            if (_activeGerberLayer != null)
            {
                _activeGerberLayer.IsActive = false;
            }

            // Set new active layer
            _activeGerberLayer = layer;
            if (_activeGerberLayer != null)
            {
                _activeGerberLayer.IsActive = true;
                RebuildActiveLayerQuadtree();
            }

            // Just need to redraw the active layer indicator, no cache rebuild needed
            InvalidateVisual();
        }

        /// <summary>
        /// Get the currently active Gerber layer
        /// </summary>
        public GerberLayer GetActiveGerberLayer()
        {
            return _activeGerberLayer;
        }

        // Mono (1-bit) rasterization helpers - aliases for clarity
        private void FillMonoCircle(byte[] pixels, int width, int height, int bytesPerRow, double cx, double cy, double radius)
            => Fill1BitCircle(pixels, width, height, bytesPerRow, cx, cy, radius);

        private void FillMonoRectangle(byte[] pixels, int width, int height, int bytesPerRow, double cx, double cy, double w, double h)
            => Fill1BitRectangle(pixels, width, height, bytesPerRow, cx, cy, w, h);

        private void FillMonoLine(byte[] pixels, int width, int height, int bytesPerRow, double x1, double y1, double x2, double y2, double lineWidth)
            => Fill1BitLine(pixels, width, height, bytesPerRow, x1, y1, x2, y2, lineWidth);

        private void FillMonoPolygon(byte[] pixels, int width, int height, int bytesPerRow, List<Point> points)
            => Fill1BitPolygon(pixels, width, height, bytesPerRow, points);

        private void FillMonoRegularPolygon(byte[] pixels, int width, int height, int bytesPerRow, double cx, double cy, double radius, int vertices, double rotation)
            => Fill1BitRegularPolygon(pixels, width, height, bytesPerRow, cx, cy, radius, vertices, rotation);

        // 1-bit rasterization implementation
        private void Set1BitPixel(byte[] pixels, int width, int height, int bytesPerRow, int x, int y)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
                return;
            int byteIndex = y * bytesPerRow + (x / 8);
            int bitIndex = 7 - (x % 8);
            pixels[byteIndex] |= (byte)(1 << bitIndex);
        }

        private void Fill1BitCircle(byte[] pixels, int width, int height, int bytesPerRow, double cx, double cy, double radius)
        {
            int minX = Math.Max(0, (int)(cx - radius));
            int maxX = Math.Min(width - 1, (int)(cx + radius));
            int minY = Math.Max(0, (int)(cy - radius));
            int maxY = Math.Min(height - 1, (int)(cy + radius));
            double r2 = radius * radius;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    double dx = x - cx + 0.5;
                    double dy = y - cy + 0.5;
                    if (dx * dx + dy * dy <= r2)
                        Set1BitPixel(pixels, width, height, bytesPerRow, x, y);
                }
            }
        }

        private void Fill1BitRectangle(byte[] pixels, int width, int height, int bytesPerRow, double cx, double cy, double w, double h)
        {
            int minX = Math.Max(0, (int)(cx - w / 2));
            int maxX = Math.Min(width - 1, (int)(cx + w / 2));
            int minY = Math.Max(0, (int)(cy - h / 2));
            int maxY = Math.Min(height - 1, (int)(cy + h / 2));

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Set1BitPixel(pixels, width, height, bytesPerRow, x, y);
                }
            }
        }

        private void Fill1BitLine(byte[] pixels, int width, int height, int bytesPerRow, double x1, double y1, double x2, double y2, double lineWidth)
        {
            double dx = x2 - x1;
            double dy = y2 - y1;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 0.1) return;

            dx /= length;
            dy /= length;
            double px = -dy * lineWidth / 2;
            double py = dx * lineWidth / 2;

            var corners = new List<Point>
            {
                new Point(x1 + px, y1 + py),
                new Point(x2 + px, y2 + py),
                new Point(x2 - px, y2 - py),
                new Point(x1 - px, y1 - py)
            };
            Fill1BitPolygon(pixels, width, height, bytesPerRow, corners);

            // Round caps
            Fill1BitCircle(pixels, width, height, bytesPerRow, x1, y1, lineWidth / 2);
            Fill1BitCircle(pixels, width, height, bytesPerRow, x2, y2, lineWidth / 2);
        }

        private void Fill1BitPolygon(byte[] pixels, int width, int height, int bytesPerRow, List<Point> points)
        {
            if (points.Count < 3) return;

            double minY = double.MaxValue, maxY = double.MinValue;
            foreach (var p in points)
            {
                minY = Math.Min(minY, p.Y);
                maxY = Math.Max(maxY, p.Y);
            }

            int iMinY = Math.Max(0, (int)minY);
            int iMaxY = Math.Min(height - 1, (int)maxY);

            for (int y = iMinY; y <= iMaxY; y++)
            {
                var intersections = new List<double>();
                for (int i = 0; i < points.Count; i++)
                {
                    int j = (i + 1) % points.Count;
                    double y1 = points[i].Y, y2 = points[j].Y;
                    double x1 = points[i].X, x2 = points[j].X;

                    if ((y1 <= y && y2 > y) || (y2 <= y && y1 > y))
                    {
                        double t = (y - y1) / (y2 - y1);
                        intersections.Add(x1 + t * (x2 - x1));
                    }
                }

                intersections.Sort();
                for (int i = 0; i + 1 < intersections.Count; i += 2)
                {
                    int startX = Math.Max(0, (int)intersections[i]);
                    int endX = Math.Min(width - 1, (int)intersections[i + 1]);
                    for (int x = startX; x <= endX; x++)
                    {
                        Set1BitPixel(pixels, width, height, bytesPerRow, x, y);
                    }
                }
            }
        }

        private void Fill1BitRegularPolygon(byte[] pixels, int width, int height, int bytesPerRow, double cx, double cy, double radius, int vertices, double rotation)
        {
            var points = new List<Point>();
            double startAngle = rotation * Math.PI / 180;
            for (int i = 0; i < vertices; i++)
            {
                double angle = startAngle + (2 * Math.PI * i / vertices);
                points.Add(new Point(cx + radius * Math.Cos(angle), cy - radius * Math.Sin(angle)));
            }
            Fill1BitPolygon(pixels, width, height, bytesPerRow, points);
        }

        /// <summary>
        /// Fill an obround (stadium/discorectangle) shape - rectangle with semicircular ends
        /// </summary>
        private void Fill1BitObround(byte[] pixels, int width, int height, int bytesPerRow, double cx, double cy, double w, double h)
        {
            // Obround is a rectangle with semicircular ends
            // Draw the central rectangle first
            double r = Math.Min(w, h) / 2;

            if (w > h)
            {
                // Horizontal obround - wider than tall
                double rectW = w - h; // Width of rectangular portion
                Fill1BitRectangle(pixels, width, height, bytesPerRow, cx, cy, rectW, h);
                Fill1BitCircle(pixels, width, height, bytesPerRow, cx - rectW / 2, cy, r);
                Fill1BitCircle(pixels, width, height, bytesPerRow, cx + rectW / 2, cy, r);
            }
            else
            {
                // Vertical obround - taller than wide
                double rectH = h - w; // Height of rectangular portion
                Fill1BitRectangle(pixels, width, height, bytesPerRow, cx, cy, w, rectH);
                Fill1BitCircle(pixels, width, height, bytesPerRow, cx, cy - rectH / 2, r);
                Fill1BitCircle(pixels, width, height, bytesPerRow, cx, cy + rectH / 2, r);
            }
        }

        /// <summary>
        /// Draw a subtle highlight around active layer shapes so user knows what's selectable
        /// </summary>
        private void RenderActiveLayerHighlight(DrawingContext dc)
        {
            // Draw a subtle indicator in the corner showing which layer is active
            if (_activeGerberLayer == null) return;

            var formattedText = new FormattedText(
                "Active: " + _activeGerberLayer.Name,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                12,
                new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            // Draw background
            var textBounds = new Rect(8, 8, formattedText.Width + 12, formattedText.Height + 6);
            dc.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
                null,
                textBounds,
                3, 3);

            dc.DrawText(formattedText, new Point(14, 11));
        }

        /// <summary>
        /// Rebuild the quadtree for the active layer only
        /// </summary>
        private void RebuildActiveLayerQuadtree()
        {
            if (_activeGerberLayer == null || _activeGerberLayer.Primitives == null)
            {
                _activeLayerQuadtree = null;
                return;
            }

            // Calculate bounds for the active layer
            Rect layerBounds = _activeGerberLayer.Bounds;
            if (layerBounds.IsEmpty)
            {
                _activeLayerQuadtree = null;
                return;
            }

            // Create quadtree with some padding
            layerBounds.Inflate(layerBounds.Width * 0.1, layerBounds.Height * 0.1);
            _activeLayerQuadtree = new GerberQuadtree(layerBounds);

            foreach (var prim in _activeGerberLayer.Primitives)
            {
                _activeLayerQuadtree.Insert(prim);
            }
        }

        private void RenderGerberPrimitiveToScreen(DrawingContext dc, GerberPrimitive prim, SolidColorBrush fillBrush)
        {
            Point screenCenter = WorldToScreen(prim.Position);
            double screenWidth = prim.Width * Zoom;
            double screenHeight = prim.Height * Zoom;

            switch (prim.Type)
            {
                case GerberPrimitiveType.Circle:
                case GerberPrimitiveType.Flash:
                    dc.DrawEllipse(fillBrush, null, screenCenter, screenWidth / 2, screenHeight / 2);
                    break;

                case GerberPrimitiveType.Rectangle:
                    {
                        var rect = new Rect(
                            screenCenter.X - screenWidth / 2,
                            screenCenter.Y - screenHeight / 2,
                            screenWidth,
                            screenHeight
                        );

                        if (prim.Rotation != 0)
                        {
                            dc.PushTransform(new RotateTransform(-prim.Rotation, screenCenter.X, screenCenter.Y));
                            dc.DrawRectangle(fillBrush, null, rect);
                            dc.Pop();
                        }
                        else
                        {
                            dc.DrawRectangle(fillBrush, null, rect);
                        }
                    }
                    break;

                case GerberPrimitiveType.Obround:
                    {
                        double cornerRadius = Math.Min(screenWidth, screenHeight) / 2;
                        var rect = new Rect(
                            screenCenter.X - screenWidth / 2,
                            screenCenter.Y - screenHeight / 2,
                            screenWidth,
                            screenHeight
                        );

                        if (prim.Rotation != 0)
                        {
                            dc.PushTransform(new RotateTransform(-prim.Rotation, screenCenter.X, screenCenter.Y));
                            dc.DrawRoundedRectangle(fillBrush, null, rect, cornerRadius, cornerRadius);
                            dc.Pop();
                        }
                        else
                        {
                            dc.DrawRoundedRectangle(fillBrush, null, rect, cornerRadius, cornerRadius);
                        }
                    }
                    break;

                case GerberPrimitiveType.Line:
                case GerberPrimitiveType.Arc:
                    if (prim.Points != null && prim.Points.Count >= 2)
                    {
                        var linePen = new Pen(fillBrush, Math.Max(1, prim.Width * Zoom));
                        linePen.StartLineCap = PenLineCap.Round;
                        linePen.EndLineCap = PenLineCap.Round;
                        linePen.Freeze();

                        Point prev = WorldToScreen(prim.Points[0]);
                        for (int i = 1; i < prim.Points.Count; i++)
                        {
                            Point curr = WorldToScreen(prim.Points[i]);
                            dc.DrawLine(linePen, prev, curr);
                            prev = curr;
                        }
                    }
                    break;

                case GerberPrimitiveType.Contour:
                    if (prim.Points != null && prim.Points.Count >= 3)
                    {
                        var geometry = new StreamGeometry();
                        using (var ctx = geometry.Open())
                        {
                            Point first = WorldToScreen(prim.Points[0]);
                            ctx.BeginFigure(first, true, true);
                            for (int i = 1; i < prim.Points.Count; i++)
                            {
                                ctx.LineTo(WorldToScreen(prim.Points[i]), true, false);
                            }
                        }
                        geometry.Freeze();
                        dc.DrawGeometry(fillBrush, null, geometry);
                    }
                    break;

                case GerberPrimitiveType.Polygon:
                    {
                        int vertices = 6;
                        double radius = screenWidth / 2;
                        double startAngle = prim.Rotation * Math.PI / 180;

                        var geometry = new StreamGeometry();
                        using (var ctx = geometry.Open())
                        {
                            for (int i = 0; i <= vertices; i++)
                            {
                                double angle = startAngle + (2 * Math.PI * i / vertices);
                                double px = screenCenter.X + radius * Math.Cos(angle);
                                double py = screenCenter.Y - radius * Math.Sin(angle);

                                if (i == 0)
                                    ctx.BeginFigure(new Point(px, py), true, true);
                                else
                                    ctx.LineTo(new Point(px, py), true, false);
                            }
                        }
                        geometry.Freeze();
                        dc.DrawGeometry(fillBrush, null, geometry);
                    }
                    break;
            }
        }

        private void RenderGerberSelectionHighlights(DrawingContext dc)
        {
            if (GerberLayers == null) return;

            var selectedBrush = new SolidColorBrush(Color.FromArgb(180, 0, 200, 255));
            selectedBrush.Freeze();
            var selectedPen = new Pen(selectedBrush, 2);
            selectedPen.Freeze();

            foreach (var layer in GerberLayers)
            {
                if (!layer.IsVisible || layer.Primitives == null)
                    continue;

                foreach (var prim in layer.Primitives.Where(p => p.IsSelected))
                {
                    RenderGerberPrimitiveToScreen(dc, prim, selectedBrush);
                }
            }
        }

        /// <summary>
        /// Hit test Gerber primitives at the given screen position
        /// ONLY hits the ACTIVE layer - inactive layers are just pixels
        /// </summary>
        public GerberPrimitive HitTestGerberPrimitive(Point screenPos, double hitRadius = 5)
        {
            // Only hit test on the active layer - inactive layers are rasterized and not clickable
            if (_activeLayerQuadtree == null || _activeGerberLayer == null)
                return null;

            Point worldPos = ScreenToWorld(screenPos);
            double worldRadius = hitRadius / Zoom;

            var candidates = _activeLayerQuadtree.Query(worldPos, worldRadius);

            // Find the closest primitive
            GerberPrimitive closest = null;
            double closestDist = double.MaxValue;

            foreach (var prim in candidates)
            {
                double dist = Math.Sqrt(Math.Pow(prim.X - worldPos.X, 2) + Math.Pow(prim.Y - worldPos.Y, 2));

                // For shapes, check if point is inside
                var bounds = prim.GetBounds();
                if (bounds.Contains(worldPos))
                {
                    dist = 0;
                }

                if (dist < closestDist && dist <= worldRadius)
                {
                    closestDist = dist;
                    closest = prim;
                }
            }

            return closest;
        }

        /// <summary>
        /// Select Gerber primitives in the given screen rectangle
        /// ONLY selects from the ACTIVE layer
        /// </summary>
        public List<GerberPrimitive> SelectGerberPrimitivesInRect(Rect screenRect)
        {
            // Only select from active layer
            if (_activeLayerQuadtree == null)
                return new List<GerberPrimitive>();

            Point worldTL = ScreenToWorld(new Point(screenRect.Left, screenRect.Top));
            Point worldBR = ScreenToWorld(new Point(screenRect.Right, screenRect.Bottom));

            Rect worldRect = new Rect(
                Math.Min(worldTL.X, worldBR.X),
                Math.Min(worldTL.Y, worldBR.Y),
                Math.Abs(worldBR.X - worldTL.X),
                Math.Abs(worldBR.Y - worldTL.Y)
            );

            return _activeLayerQuadtree.QueryRect(worldRect);
        }

        /// <summary>
        /// Invalidates all Gerber layer bitmap caches, forcing re-rendering
        /// Call this when layer geometry changes (not for visibility/color changes)
        /// </summary>
        public void InvalidateGerberCache()
        {
            // Clear layer bitmap caches - they will be re-rendered on demand
            _layerBitmaps.Clear();
            _worldBounds = Rect.Empty;
            _activeLayerQuadtree = null;
            _gerberCacheDirty = true;
            InvalidateVisual();
        }

        /// <summary>
        /// Mark layers as needing redraw (for visibility/color changes, not geometry)
        /// </summary>
        public void InvalidateComposite()
        {
            InvalidateVisual();
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
                // Calculate zoom-independent line thicknesses (constant screen pixels)
                double baseThickness = 1.0 / Zoom;  // 1 screen pixel
                double selectThickness = 2.0 / Zoom; // 2 screen pixels for selection

                // Body outline brush/pen
                var bodyBrush = new SolidColorBrush(Color.FromArgb(40, 100, 150, 200));
                bodyBrush.Freeze();
                var bodyPen = new Pen(new SolidColorBrush(Color.FromRgb(100, 150, 200)), baseThickness);
                bodyPen.Freeze();

                // Selected item pen (highlighted) - slightly thicker
                var selectedPen = new Pen(new SolidColorBrush(Color.FromRgb(0, 200, 255)), selectThickness);
                selectedPen.Freeze();

                // Render package graphics
                for (int i = 0; i < package.Graphics.Count; i++)
                {
                    var graphic = package.Graphics[i];
                    bool isSelected = _selectedGraphicIndices.Contains(i);
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
                var pinPen = new Pen(new SolidColorBrush(Color.FromRgb(150, 130, 80)), baseThickness * 0.5);
                pinPen.Freeze();
                var selectedPinBrush = new SolidColorBrush(Color.FromRgb(100, 220, 255));
                selectedPinBrush.Freeze();
                var pin1Brush = new SolidColorBrush(Color.FromRgb(255, 100, 100));
                pin1Brush.Freeze();

                for (int i = 0; i < package.Pins.Count; i++)
                {
                    var pin = package.Pins[i];
                    bool isSelected = _selectedPinIndices.Contains(i);
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

                // Draw origin marker at package origin (zoom-independent)
                var originPen = new Pen(new SolidColorBrush(Colors.Cyan), baseThickness);
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
            var handlePen = new Pen(new SolidColorBrush(Color.FromRgb(0, 150, 255)), 1.0 / Zoom);
            handlePen.Freeze();

            // Zoom-independent handle size (4 screen pixels)
            double handleSize = 4.0 / Zoom;

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

            // Level of detail thresholds
            bool renderPackageDetails = ShowPackageGraphics && Zoom > 0.5;
            bool renderLabels = ShowLabels && Zoom > 0.3;  // Skip labels at very low zoom
            bool renderCrosshairs = Zoom > 0.2;  // Skip crosshairs at very low zoom

            // Get DPI once for all label rendering
            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

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
                    dc.DrawEllipse(_pin1Brush, null, pin1Pos, pin1Size, pin1Size);
                }

                // Draw center crosshair (skip at very low zoom for performance)
                if (renderCrosshairs)
                {
                    double crossSize = 3;
                    dc.DrawLine(_crosshairPen,
                        new Point(screenPos.X - crossSize, screenPos.Y),
                        new Point(screenPos.X + crossSize, screenPos.Y));
                    dc.DrawLine(_crosshairPen,
                        new Point(screenPos.X, screenPos.Y - crossSize),
                        new Point(screenPos.X, screenPos.Y + crossSize));
                }

                // Draw reference label if enabled and zoom is sufficient
                if (renderLabels && !string.IsNullOrEmpty(placement.Reference))
                {
                    double fontSize = 10; // Fixed font size
                    var formattedText = new FormattedText(
                        placement.Reference,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        _labelTypeface,
                        fontSize,
                        _labelBrush,
                        pixelsPerDip
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
                    dc.DrawRoundedRectangle(_labelBgBrush, null, labelBgRect, 2, 2);

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

                    // Viewport culling for fiducials
                    if (!viewport.Contains(screenPos))
                        continue;

                    // Draw diamond using cached pen
                    var diamondGeometry = new StreamGeometry();
                    using (var ctx = diamondGeometry.Open())
                    {
                        ctx.BeginFigure(new Point(screenPos.X, screenPos.Y - fidSize), true, true);
                        ctx.LineTo(new Point(screenPos.X + fidSize, screenPos.Y), true, false);
                        ctx.LineTo(new Point(screenPos.X, screenPos.Y + fidSize), true, false);
                        ctx.LineTo(new Point(screenPos.X - fidSize, screenPos.Y), true, false);
                    }
                    diamondGeometry.Freeze();
                    dc.DrawGeometry(null, _fiducialPen, diamondGeometry);

                    // Draw center dot
                    dc.DrawEllipse(_fiducialBrush, null, screenPos, 2, 2);

                    // Draw label if enabled and zoom sufficient
                    if (renderLabels && !string.IsNullOrEmpty(fiducial.Name))
                    {
                        double fontSize = 9;
                        var formattedText = new FormattedText(
                            fiducial.Name,
                            CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            _labelTypeface,
                            fontSize,
                            _fiducialBrush,
                            pixelsPerDip
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

                // Render pins using cached brushes
                foreach (var pin in package.Pins)
                {
                    double x = pin.X - pin.Width / 2;
                    double y = pin.Y - pin.Height / 2;
                    var pinRect = new Rect(x, y, pin.Width, pin.Height);

                    switch (pin.Shape)
                    {
                        case PinShape.Circle:
                            dc.DrawEllipse(_pinBrush, _pinPen, new Point(pin.X, pin.Y), pin.Width / 2, pin.Height / 2);
                            break;
                        case PinShape.Oval:
                            dc.DrawRoundedRectangle(_pinBrush, _pinPen, pinRect, pin.Width / 2, pin.Height / 2);
                            break;
                        case PinShape.RoundedRectangle:
                            dc.DrawRoundedRectangle(_pinBrush, _pinPen, pinRect, pin.Width * 0.2, pin.Height * 0.2);
                            break;
                        default: // Rectangle
                            dc.DrawRectangle(_pinBrush, _pinPen, pinRect);
                            break;
                    }

                    // Draw pin 1 indicator using cached brush
                    if (pin.Number == 1)
                    {
                        dc.DrawEllipse(_pin1Brush, null, new Point(pin.X, pin.Y), pin.Width * 0.2, pin.Height * 0.2);
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

            bool isCtrlPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

            // First, try to hit test a Gerber primitive on the active layer
            GerberPrimitive hitPrimitive = HitTestGerberPrimitive(mousePos);
            if (hitPrimitive != null)
            {
                if (isCtrlPressed)
                {
                    // Toggle selection
                    hitPrimitive.IsSelected = !hitPrimitive.IsSelected;
                }
                else
                {
                    // Clear other selections and select this one
                    ClearSelection();
                    hitPrimitive.IsSelected = true;
                }

                InvalidateVisual();
                RaiseGerberSelectionChanged();
                return;
            }

            // Try to hit test a placement
            Placement hitPlacement = HitTestPlacement(mousePos);

            if (hitPlacement != null)
            {
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
                // No hit - will start rectangle select on drag
                if (!isCtrlPressed)
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
            if (SelectedPackage != null && (_isDraggingGraphic || _isPackageSelecting))
            {
                HandlePackageEditorMouseUp(e.GetPosition(this));
                return;
            }

            if (_isSelecting)
            {
                _isSelecting = false;
                ReleaseMouseCapture();

                if (_selectionRect.Width > 5 && _selectionRect.Height > 5)
                {
                    bool isCtrlPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

                    if (!isCtrlPressed)
                    {
                        ClearSelection();
                    }

                    // Select Gerber primitives in rectangle (from active layer only)
                    var selectedPrimitives = SelectGerberPrimitivesInRect(_selectionRect);
                    if (selectedPrimitives.Count > 0)
                    {
                        foreach (var prim in selectedPrimitives)
                        {
                            prim.IsSelected = true;
                        }
                        RaiseGerberSelectionChanged();
                    }

                    // Also select placements in the rectangle
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
            // Clear placement selections
            if (Placements != null)
            {
                foreach (var p in Placements)
                {
                    p.IsSelected = false;
                }
            }

            // Clear Gerber primitive selections
            ClearGerberSelection();
        }

        private void ClearGerberSelection()
        {
            if (GerberLayers == null) return;
            foreach (var layer in GerberLayers)
            {
                if (layer.Primitives == null) continue;
                foreach (var prim in layer.Primitives)
                {
                    prim.IsSelected = false;
                }
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

        private void RaiseGerberSelectionChanged()
        {
            // Raise event for Gerber primitive selection changes
            var selected = new List<GerberPrimitive>();
            if (GerberLayers != null)
            {
                foreach (var layer in GerberLayers)
                {
                    if (layer.Primitives == null) continue;
                    foreach (var prim in layer.Primitives)
                    {
                        if (prim.IsSelected) selected.Add(prim);
                    }
                }
            }
            GerberSelectionChanged?.Invoke(this, selected);
        }

        #endregion

        #region Package Editor Mouse Handling

        private void HandlePackageEditorMouseDown(Point mousePos)
        {
            var package = SelectedPackage;
            if (package == null) return;

            Point worldPos = ScreenToWorld(mousePos);
            _dragStartWorld = worldPos;
            _packageSelectStart = mousePos;

            bool isCtrlPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool hitSomething = false;

            // Hit test pins first (they're on top)
            double hitRadius = 10 / Zoom; // Screen pixels converted to world
            for (int i = 0; i < package.Pins.Count; i++)
            {
                var pin = package.Pins[i];
                double dist = Math.Sqrt(Math.Pow(worldPos.X - pin.X, 2) + Math.Pow(worldPos.Y - pin.Y, 2));
                if (dist <= Math.Max(pin.Width, pin.Height) / 2 + hitRadius)
                {
                    if (isCtrlPressed)
                    {
                        // Toggle selection
                        if (_selectedPinIndices.Contains(i))
                            _selectedPinIndices.Remove(i);
                        else
                            _selectedPinIndices.Add(i);
                    }
                    else if (!_selectedPinIndices.Contains(i))
                    {
                        // Clear and select this one
                        ClearPackageSelection();
                        _selectedPinIndices.Add(i);
                    }
                    hitSomething = true;
                    _isDraggingGraphic = true;
                    SaveUndoState(); // Save state before move
                    CaptureMouse();
                    InvalidateVisual();
                    return;
                }
            }

            // Hit test graphics
            for (int i = 0; i < package.Graphics.Count; i++)
            {
                var g = package.Graphics[i];
                if (HitTestGraphic(g, worldPos, hitRadius))
                {
                    if (isCtrlPressed)
                    {
                        // Toggle selection
                        if (_selectedGraphicIndices.Contains(i))
                            _selectedGraphicIndices.Remove(i);
                        else
                            _selectedGraphicIndices.Add(i);
                    }
                    else if (!_selectedGraphicIndices.Contains(i))
                    {
                        // Clear and select this one
                        ClearPackageSelection();
                        _selectedGraphicIndices.Add(i);
                    }
                    hitSomething = true;
                    _isDraggingGraphic = true;
                    SaveUndoState(); // Save state before move
                    CaptureMouse();
                    InvalidateVisual();
                    return;
                }
            }

            // Clicked on empty space - start rectangle selection or clear
            if (!isCtrlPressed)
            {
                ClearPackageSelection();
            }
            // Will start rectangle selection on mouse move
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
            var package = SelectedPackage;
            if (package == null) return;

            Point worldPos = ScreenToWorld(mousePos);

            // Handle rectangle selection
            if (_isPackageSelecting && !_isDraggingGraphic)
            {
                _packageSelectRect = new Rect(
                    Math.Min(_packageSelectStart.X, mousePos.X),
                    Math.Min(_packageSelectStart.Y, mousePos.Y),
                    Math.Abs(mousePos.X - _packageSelectStart.X),
                    Math.Abs(mousePos.Y - _packageSelectStart.Y)
                );
                InvalidateVisual();
                return;
            }

            if (!_isDraggingGraphic)
            {
                // Start rectangle selection after small movement
                double dist = Math.Sqrt(Math.Pow(mousePos.X - _packageSelectStart.X, 2) +
                                       Math.Pow(mousePos.Y - _packageSelectStart.Y, 2));
                if (dist > 3 && _selectedGraphicIndices.Count == 0 && _selectedPinIndices.Count == 0)
                {
                    _isPackageSelecting = true;
                    CaptureMouse();
                }
                return;
            }

            double dx = worldPos.X - _dragStartWorld.X;
            double dy = worldPos.Y - _dragStartWorld.Y;

            // Move all selected pins
            foreach (int pinIdx in _selectedPinIndices)
            {
                if (pinIdx >= 0 && pinIdx < package.Pins.Count)
                {
                    var pin = package.Pins[pinIdx];
                    pin.X += dx;
                    pin.Y += dy;
                }
            }

            // Move all selected graphics
            foreach (int gIdx in _selectedGraphicIndices)
            {
                if (gIdx >= 0 && gIdx < package.Graphics.Count)
                {
                    var g = package.Graphics[gIdx];
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
            }

            _dragStartWorld = worldPos;
            GraphicMoved?.Invoke(this, worldPos);
            InvalidateVisual();
        }

        private void HandlePackageEditorMouseUp(Point mousePos)
        {
            var package = SelectedPackage;

            // Complete rectangle selection
            if (_isPackageSelecting && package != null)
            {
                _isPackageSelecting = false;
                ReleaseMouseCapture();

                if (_packageSelectRect.Width > 5 && _packageSelectRect.Height > 5)
                {
                    bool isCtrlPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
                    if (!isCtrlPressed)
                    {
                        ClearPackageSelection();
                    }

                    // Convert screen rect to world
                    Point worldTL = ScreenToWorld(new Point(_packageSelectRect.Left, _packageSelectRect.Top));
                    Point worldBR = ScreenToWorld(new Point(_packageSelectRect.Right, _packageSelectRect.Bottom));
                    Rect worldRect = new Rect(
                        Math.Min(worldTL.X, worldBR.X),
                        Math.Min(worldTL.Y, worldBR.Y),
                        Math.Abs(worldBR.X - worldTL.X),
                        Math.Abs(worldBR.Y - worldTL.Y)
                    );

                    // Select all pins in rectangle
                    for (int i = 0; i < package.Pins.Count; i++)
                    {
                        var pin = package.Pins[i];
                        if (worldRect.Contains(new Point(pin.X, pin.Y)))
                        {
                            _selectedPinIndices.Add(i);
                        }
                    }

                    // Select all graphics in rectangle
                    for (int i = 0; i < package.Graphics.Count; i++)
                    {
                        var g = package.Graphics[i];
                        Point center = GetGraphicCenter(g);
                        if (worldRect.Contains(center))
                        {
                            _selectedGraphicIndices.Add(i);
                        }
                    }
                }

                _packageSelectRect = Rect.Empty;
                InvalidateVisual();
                return;
            }

            if (_isDraggingGraphic)
            {
                _isDraggingGraphic = false;
                ReleaseMouseCapture();
                FinalizeUndoState(); // Save final positions
            }
        }

        private Point GetGraphicCenter(PackageGraphic g)
        {
            switch (g.ShapeType)
            {
                case GraphicShapeType.Circle:
                case GraphicShapeType.Ellipse:
                    return new Point(g.X, g.Y);
                case GraphicShapeType.Rectangle:
                case GraphicShapeType.RoundedRectangle:
                    return new Point(g.X + g.Width / 2, g.Y + g.Height / 2);
                case GraphicShapeType.Line:
                case GraphicShapeType.Polygon:
                    if (g.Points != null && g.Points.Count > 0)
                    {
                        double cx = 0, cy = 0;
                        foreach (var pt in g.Points)
                        {
                            cx += pt.X;
                            cy += pt.Y;
                        }
                        return new Point(cx / g.Points.Count, cy / g.Points.Count);
                    }
                    return new Point(g.X, g.Y);
                default:
                    return new Point(g.X, g.Y);
            }
        }

        private PackageEditAction _currentEditAction;

        private void SaveUndoState()
        {
            var package = SelectedPackage;
            if (package == null) return;

            _currentEditAction = new PackageEditAction
            {
                GraphicIndices = new List<int>(_selectedGraphicIndices),
                PinIndices = new List<int>(_selectedPinIndices),
                OldPositions = new List<Point>(),
                NewPositions = new List<Point>()
            };

            // Save current positions of selected graphics
            foreach (int idx in _selectedGraphicIndices)
            {
                if (idx >= 0 && idx < package.Graphics.Count)
                {
                    var g = package.Graphics[idx];
                    _currentEditAction.OldPositions.Add(new Point(g.X, g.Y));
                }
            }

            // Save current positions of selected pins
            foreach (int idx in _selectedPinIndices)
            {
                if (idx >= 0 && idx < package.Pins.Count)
                {
                    var pin = package.Pins[idx];
                    _currentEditAction.OldPositions.Add(new Point(pin.X, pin.Y));
                }
            }

            // Clear redo stack when new action is started
            _redoStack.Clear();
        }

        private void FinalizeUndoState()
        {
            var package = SelectedPackage;
            if (package == null || _currentEditAction == null) return;

            // Save final positions of selected graphics
            foreach (int idx in _currentEditAction.GraphicIndices)
            {
                if (idx >= 0 && idx < package.Graphics.Count)
                {
                    var g = package.Graphics[idx];
                    _currentEditAction.NewPositions.Add(new Point(g.X, g.Y));
                }
            }

            // Save final positions of selected pins
            foreach (int idx in _currentEditAction.PinIndices)
            {
                if (idx >= 0 && idx < package.Pins.Count)
                {
                    var pin = package.Pins[idx];
                    _currentEditAction.NewPositions.Add(new Point(pin.X, pin.Y));
                }
            }

            // Only add to undo stack if positions actually changed
            bool hasChanges = false;
            for (int i = 0; i < _currentEditAction.OldPositions.Count && i < _currentEditAction.NewPositions.Count; i++)
            {
                if (_currentEditAction.OldPositions[i] != _currentEditAction.NewPositions[i])
                {
                    hasChanges = true;
                    break;
                }
            }

            if (hasChanges)
            {
                _undoStack.Push(_currentEditAction);
            }

            _currentEditAction = null;
        }

        public void Undo()
        {
            var package = SelectedPackage;
            if (package == null || _undoStack.Count == 0) return;

            var action = _undoStack.Pop();
            int posIdx = 0;

            // Restore graphics positions
            foreach (int idx in action.GraphicIndices)
            {
                if (idx >= 0 && idx < package.Graphics.Count && posIdx < action.OldPositions.Count)
                {
                    var g = package.Graphics[idx];
                    double dx = action.OldPositions[posIdx].X - action.NewPositions[posIdx].X;
                    double dy = action.OldPositions[posIdx].Y - action.NewPositions[posIdx].Y;
                    g.X += dx;
                    g.Y += dy;
                    if (g.Points != null)
                    {
                        for (int i = 0; i < g.Points.Count; i++)
                        {
                            g.Points[i] = new Point(g.Points[i].X + dx, g.Points[i].Y + dy);
                        }
                    }
                    posIdx++;
                }
            }

            // Restore pin positions
            foreach (int idx in action.PinIndices)
            {
                if (idx >= 0 && idx < package.Pins.Count && posIdx < action.OldPositions.Count)
                {
                    var pin = package.Pins[idx];
                    pin.X = action.OldPositions[posIdx].X;
                    pin.Y = action.OldPositions[posIdx].Y;
                    posIdx++;
                }
            }

            _redoStack.Push(action);
            InvalidateVisual();
        }

        public void Redo()
        {
            var package = SelectedPackage;
            if (package == null || _redoStack.Count == 0) return;

            var action = _redoStack.Pop();
            int posIdx = 0;

            // Apply graphics positions
            foreach (int idx in action.GraphicIndices)
            {
                if (idx >= 0 && idx < package.Graphics.Count && posIdx < action.NewPositions.Count)
                {
                    var g = package.Graphics[idx];
                    double dx = action.NewPositions[posIdx].X - action.OldPositions[posIdx].X;
                    double dy = action.NewPositions[posIdx].Y - action.OldPositions[posIdx].Y;
                    g.X += dx;
                    g.Y += dy;
                    if (g.Points != null)
                    {
                        for (int i = 0; i < g.Points.Count; i++)
                        {
                            g.Points[i] = new Point(g.Points[i].X + dx, g.Points[i].Y + dy);
                        }
                    }
                    posIdx++;
                }
            }

            // Apply pin positions
            foreach (int idx in action.PinIndices)
            {
                if (idx >= 0 && idx < package.Pins.Count && posIdx < action.NewPositions.Count)
                {
                    var pin = package.Pins[idx];
                    pin.X = action.NewPositions[posIdx].X;
                    pin.Y = action.NewPositions[posIdx].Y;
                    posIdx++;
                }
            }

            _undoStack.Push(action);
            InvalidateVisual();
        }

        /// <summary>
        /// Delete all selected graphics and pins from the package
        /// </summary>
        public void DeleteSelectedGraphic()
        {
            var package = SelectedPackage;
            if (package == null) return;

            // Delete pins in reverse order to preserve indices
            var sortedPinIndices = _selectedPinIndices.OrderByDescending(x => x).ToList();
            foreach (int idx in sortedPinIndices)
            {
                if (idx >= 0 && idx < package.Pins.Count)
                {
                    package.Pins.RemoveAt(idx);
                }
            }

            // Delete graphics in reverse order to preserve indices
            var sortedGraphicIndices = _selectedGraphicIndices.OrderByDescending(x => x).ToList();
            foreach (int idx in sortedGraphicIndices)
            {
                if (idx >= 0 && idx < package.Graphics.Count)
                {
                    package.Graphics.RemoveAt(idx);
                }
            }

            ClearPackageSelection();
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

        #region Keyboard Handling

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            bool isCtrlPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            double panAmount = 50 / Zoom;

            // Handle Ctrl+key combinations first
            if (isCtrlPressed)
            {
                switch (e.Key)
                {
                    case Key.Z:
                        // Undo
                        if (SelectedPackage != null)
                        {
                            Undo();
                            e.Handled = true;
                        }
                        return;

                    case Key.Y:
                        // Redo
                        if (SelectedPackage != null)
                        {
                            Redo();
                            e.Handled = true;
                        }
                        return;

                    case Key.A:
                        // Select all
                        if (SelectedPackage != null)
                        {
                            SelectAllPackageElements();
                            e.Handled = true;
                        }
                        return;
                }
            }

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
                case Key.Escape:
                    // Clear selection
                    if (SelectedPackage != null)
                    {
                        ClearPackageSelection();
                        e.Handled = true;
                    }
                    break;
            }
        }

        public void SelectAllPackageElements()
        {
            var package = SelectedPackage;
            if (package == null) return;

            _selectedGraphicIndices.Clear();
            _selectedPinIndices.Clear();

            for (int i = 0; i < package.Graphics.Count; i++)
            {
                _selectedGraphicIndices.Add(i);
            }

            for (int i = 0; i < package.Pins.Count; i++)
            {
                _selectedPinIndices.Add(i);
            }

            InvalidateVisual();
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
