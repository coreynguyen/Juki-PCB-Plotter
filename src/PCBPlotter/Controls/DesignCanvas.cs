using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        public event EventHandler<Placement> PlacementDoubleClicked;

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

            // Crisp vector rendering settings - prevent any bitmap scaling or anti-aliasing blur
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;

            InitializeBrushesAndPens();

            Loaded += (s, e) => InvalidateVisual();
            Unloaded += (s, e) => _isDisposed = true;
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

            // OPTIMIZATION: If this canvas is hidden (GPU mode active), clear data and return early.
            // This prevents the CPU canvas from doing heavy quadtree building when not visible.
            if (canvas.Visibility != Visibility.Visible)
            {
                lock (canvas._quadtreeLock)
                {
                    canvas._layerQuadtrees.Clear();
                    canvas._pendingQuadtreeBuilds.Clear();
                }
                canvas._worldBounds = Rect.Empty;
                canvas._activeLayerQuadtree = null;
                canvas._activeGerberLayer = null;
                canvas._gerberCacheDirty = true;
                return;
            }

            // Clear all caches when layers change - quadtrees will be rebuilt on demand
            lock (canvas._quadtreeLock)
            {
                canvas._layerQuadtrees.Clear();
                canvas._pendingQuadtreeBuilds.Clear();
            }
            canvas._worldBounds = Rect.Empty;
            canvas._activeLayerQuadtree = null;
            canvas._activeGerberLayer = null;
            canvas._gerberCacheDirty = true;
            canvas.InvalidateVisual();
        }

        private void OnGerberLayersCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // New layers added - quadtrees will be built on demand
            // Removed layers will be cleaned up when quadtrees are rebuilt
            _gerberCacheDirty = true;
            lock (_quadtreeLock)
            {
                _layerQuadtrees.Clear();
                _pendingQuadtreeBuilds.Clear();
            }

            // OPTIMIZATION: Skip invalidation if not visible (GPU mode active)
            if (Visibility == Visibility.Visible)
            {
                InvalidateVisual();
            }
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

            // CRITICAL FIX: Layout safety check
            // If window hasn't been sized yet, coordinate math will fail and objects will
            // collapse to top-left corner (0,0). Wait for layout to complete.
            if (ActualWidth == 0 || ActualHeight == 0)
                return;

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
        // DIRECT VECTOR RENDERING ARCHITECTURE
        //
        // All Gerber primitives are rendered as vectors directly to the DrawingContext.
        // No bitmaps are created - shapes are drawn at full resolution for any zoom level.
        // Uses quadtree spatial indexing for O(log n) viewport culling.
        // ===================================================================================

        // Quadtree per layer for viewport culling - O(log n) lookup of visible primitives
        private Dictionary<string, GerberQuadtree> _layerQuadtrees = new Dictionary<string, GerberQuadtree>();

        // ASYNC FIX: Track pending quadtree builds to avoid UI freeze
        private HashSet<string> _pendingQuadtreeBuilds = new HashSet<string>();
        private readonly object _quadtreeLock = new object();
        private volatile bool _isDisposed;

        // Frozen brush cache per layer color (ARGB -> SolidColorBrush)
        private Dictionary<uint, SolidColorBrush> _layerBrushCache = new Dictionary<uint, SolidColorBrush>();

        // Frozen pen cache (keyed by color ARGB + thickness) - eliminates per-frame Pen allocations
        private Dictionary<long, Pen> _penCache = new Dictionary<long, Pen>();

        // LOD threshold: skip primitives smaller than this many screen pixels
        private const double MIN_PRIMITIVE_SCREEN_PIXELS = 0.5;

        // Reusable list for quadtree queries to avoid GC allocations during panning/zooming
        private List<GerberPrimitive> _reusableVisibleList = new List<GerberPrimitive>(10000);

        // World bounds of all layers combined
        private Rect _worldBounds = Rect.Empty;

        // Active layer for selection (used for hit testing)
        private GerberLayer _activeGerberLayer;
        private GerberQuadtree _activeLayerQuadtree;
        private bool _gerberCacheDirty = true;

        // NOTE: GerberQuadtree moved to GerberQuadtree.cs as a shared class

        /// <summary>
        /// Render Gerber layers using direct vector rendering.
        /// Vectors are rendered directly at the current zoom level - no bitmap scaling.
        /// Uses quadtree for viewport culling and LOD for skipping tiny primitives.
        /// </summary>
        private void RenderGerberLayers(DrawingContext dc)
        {
            if (GerberLayers == null || GerberLayers.Count == 0)
                return;

            EnsureActiveLayerSet();

            // Only update world bounds when cache is dirty (layers changed)
            // This prevents O(n) iteration every frame
            if (_gerberCacheDirty || _worldBounds.IsEmpty)
            {
                UpdateWorldBoundsFromLayers();
                _gerberCacheDirty = false;
            }

            if (_worldBounds.IsEmpty)
                return;

            double currentZoom = Zoom;
            Rect visibleWorld = GetVisibleWorldBounds();

            // Build quadtrees for layers that don't have them yet
            EnsureLayerQuadtreesBuilt();

            // Draw each visible layer using direct vector rendering
            foreach (var layer in GerberLayers)
            {
                if (!layer.IsVisible)
                    continue;

                var layerBounds = layer.Bounds;
                if (layerBounds.IsEmpty)
                    continue;

                // Skip layers that don't intersect the visible area
                if (!layerBounds.IntersectsWith(visibleWorld))
                    continue;

                RenderLayerVectors(dc, layer, visibleWorld, currentZoom);
            }

            // Draw active layer highlight
            if (_activeGerberLayer != null && _activeGerberLayer.IsVisible)
            {
                RenderActiveLayerHighlight(dc);
            }

            RenderGerberSelectionHighlights(dc);
        }

        /// <summary>
        /// Ensure quadtrees are built for all layers (for viewport culling).
        /// ASYNC FIX: Builds quadtrees on background threads to prevent UI freeze.
        /// </summary>
        private void EnsureLayerQuadtreesBuilt()
        {
            if (GerberLayers == null)
                return;

            foreach (var layer in GerberLayers)
            {
                if (layer.Primitives == null || layer.Primitives.Count == 0)
                    continue;

                bool needsBuild = false;
                lock (_quadtreeLock)
                {
                    if (!_layerQuadtrees.ContainsKey(layer.Id) && !_pendingQuadtreeBuilds.Contains(layer.Id))
                    {
                        _pendingQuadtreeBuilds.Add(layer.Id);
                        needsBuild = true;
                    }
                }

                if (needsBuild)
                {
                    // Snapshot data for background thread
                    var layerId = layer.Id;
                    var bounds = layer.Bounds;
                    var primitives = layer.Primitives.ToList(); // Copy to avoid threading issues

                    System.Threading.Tasks.Task.Run(() =>
                    {
                        try
                        {
                            if (_isDisposed) return;

                            if (!bounds.IsEmpty)
                            {
                                bounds.Inflate(bounds.Width * 0.02, bounds.Height * 0.02);
                                var quadtree = new GerberQuadtree(bounds);
                                foreach (var prim in primitives)
                                {
                                    if (_isDisposed) return;
                                    // Index all primitives for rendering (selection is filtered by IsDark elsewhere)
                                    quadtree.Insert(prim);
                                }

                                // Update on UI thread
                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    if (_isDisposed) return;
                                    lock (_quadtreeLock)
                                    {
                                        _layerQuadtrees[layerId] = quadtree;
                                        _pendingQuadtreeBuilds.Remove(layerId);
                                    }
                                    InvalidateVisual(); // Trigger redraw now that quadtree is ready
                                }));
                            }
                            else
                            {
                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    if (_isDisposed) return;
                                    lock (_quadtreeLock)
                                    {
                                        _pendingQuadtreeBuilds.Remove(layerId);
                                    }
                                }));
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error building quadtree for layer {layerId}: {ex}");
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (_isDisposed) return;
                                lock (_quadtreeLock)
                                {
                                    _pendingQuadtreeBuilds.Remove(layerId);
                                }
                            }));
                        }
                    });
                }
            }
        }

        /// <summary>
        /// Get or create a frozen brush for the given ARGB color
        /// </summary>
        private SolidColorBrush GetLayerBrush(uint argb)
        {
            if (_layerBrushCache.TryGetValue(argb, out var brush))
                return brush;

            var color = Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            brush = new SolidColorBrush(color);
            brush.Freeze();
            _layerBrushCache[argb] = brush;
            return brush;
        }

        /// <summary>
        /// Get or create a frozen pen for the given brush and thickness.
        /// Pens are cached to eliminate per-frame allocations.
        /// Keys by color hash + rounded thickness to prevent infinite cache growth.
        /// </summary>
        private Pen GetCachedPen(SolidColorBrush brush, double thickness)
        {
            // Round thickness to 2 decimal places to avoid infinite cache growth with zoom
            int thicknessKey = (int)(thickness * 100);
            long key = ((long)brush.Color.GetHashCode() << 32) | (uint)thicknessKey;

            if (!_penCache.TryGetValue(key, out var pen))
            {
                pen = new Pen(brush, thickness);
                pen.StartLineCap = PenLineCap.Round;
                pen.EndLineCap = PenLineCap.Round;
                pen.Freeze();
                _penCache[key] = pen;
            }
            return pen;
        }

        // Maximum polygon batch size before flushing
        private const int POLY_BATCH_LIMIT = 2000;

        /// <summary>
        /// Render a layer using FAST DIRECT vector rendering.
        ///
        /// OPTIMIZED STRATEGY:
        /// - Circles/Pads: Use dc.DrawEllipse directly (WPF native optimization)
        /// - Rectangles/Obrounds: Use dc.DrawRectangle/DrawRoundedRectangle (fast path)
        /// - Lines/Arcs: Use dc.DrawLine with cached Pens (WPF handles thickness natively)
        /// - Polygons/Contours: Only these get batched into StreamGeometry
        /// - Rotated shapes: Batch into StreamGeometry
        ///
        /// KEY INSIGHT: Manually tessellating lines into quads is SLOWER than letting
        /// WPF handle thick lines natively via dc.DrawLine with a Pen. WPF's internal
        /// renderer is optimized for this. We only batch actual polygons.
        /// </summary>
        private void RenderLayerVectors(DrawingContext dc, GerberLayer layer, Rect visibleWorld, double currentZoom)
        {
            // --- Step 1: Data Retrieval (Async Safe) ---
            List<GerberPrimitive> visiblePrimitives;
            GerberQuadtree quadtree = null;
            lock (_quadtreeLock)
            {
                _layerQuadtrees.TryGetValue(layer.Id, out quadtree);
            }

            if (quadtree != null)
            {
                // OPTIMIZATION: Reuse list to reduce GC pressure during panning/zooming.
                // Since OnRender is single-threaded and RenderLayerVectors completes
                // before the next layer is processed, this is safe.
                quadtree.QueryRect(visibleWorld, _reusableVisibleList);
                visiblePrimitives = _reusableVisibleList;
            }
            else
            {
                // ASYNC POP-IN BEHAVIOR:
                // If quadtree isn't ready and layer is huge, SKIP IT completely.
                // This prevents the multi-minute freeze on load.
                // The layer will "pop in" automatically when background thread finishes.
                var allPrims = layer.Primitives;
                if (allPrims.Count > 2000)
                {
                    // Skip - wait for quadtree to be built
                    return;
                }

                // Small layers can be brute-forced safely
                visiblePrimitives = allPrims
                    .Where(p => p.GetBounds().IntersectsWith(visibleWorld))
                    .ToList();
            }

            if (visiblePrimitives.Count == 0)
                return;

            // --- Step 2: Setup Resources ---
            var brush = GetLayerBrush(layer.ColorArgb);
            var holeBrush = new SolidColorBrush(BackgroundColor);
            holeBrush.Freeze();
            bool hasOpacity = layer.Opacity < 1.0;
            if (hasOpacity)
            {
                dc.PushOpacity(layer.Opacity);
            }

            // Dynamic LOD: skip more detail while panning/selecting for smoother interaction
            // Still: show 0.5 pixel details. Moving: skip anything smaller than 2 pixels.
            double screenPixelThreshold = (_isPanning || _isSelecting) ? 2.0 : MIN_PRIMITIVE_SCREEN_PIXELS;
            double minWorldSize = screenPixelThreshold / currentZoom;

            // --- Step 3: Render Loop (Optimized) ---
            // CRITICAL: Render primitives IN ORDER to respect Gerber polarity semantics.
            // Clear primitives erase what came BEFORE them, not what comes AFTER.
            // Each primitive uses either the layer brush (dark) or hole brush (clear).
            // We ONLY batch Polygons/Contours and rotated shapes.
            // Lines, Arcs, Pads, and Rects are drawn DIRECTLY for maximum speed.

            StreamGeometry polyBatch = null;
            StreamGeometryContext polyCtx = null;
            int polyCount = 0;
            SolidColorBrush currentBatchBrush = brush; // Track which brush the current batch uses

            foreach (var prim in visiblePrimitives)
            {
                // LOD: Skip primitives that are too small to see
                // For Polygon/Contour types, compute actual bounds from Points since Width/Height may be 0
                double primSize = Math.Max(prim.Width, prim.Height);
                if ((prim.Type == GerberPrimitiveType.Polygon || prim.Type == GerberPrimitiveType.Contour) && primSize < minWorldSize)
                {
                    // Compute actual size from Points array
                    if (prim.Points != null && prim.Points.Count >= 3)
                    {
                        var bounds = prim.GetBounds();
                        primSize = Math.Max(bounds.Width, bounds.Height);
                    }
                }
                if (primSize < minWorldSize &&
                    prim.Type != GerberPrimitiveType.Line &&
                    prim.Type != GerberPrimitiveType.Arc)
                    continue;

                // Coordinate Safety (prevents NaN/Infinity glitches)
                if (double.IsNaN(prim.X) || double.IsInfinity(prim.X) ||
                    double.IsNaN(prim.Y) || double.IsInfinity(prim.Y))
                    continue;

                // Select brush based on polarity - dark primitives add, clear primitives erase
                SolidColorBrush primBrush = prim.IsDark ? brush : holeBrush;

                // If polarity changed, flush the polygon batch before continuing
                if (polyBatch != null && primBrush != currentBatchBrush)
                {
                    polyCtx.Close();
                    polyBatch.Freeze();
                    dc.DrawGeometry(currentBatchBrush, null, polyBatch);
                    polyBatch = null;
                    polyCtx = null;
                    polyCount = 0;
                }
                currentBatchBrush = primBrush;

                Point screenPos = WorldToScreen(new Point(prim.X, prim.Y));
                double screenWidth = prim.Width * currentZoom;
                double screenHeight = prim.Height * currentZoom;

                switch (prim.Type)
                {
                    // ===== FASTEST: Direct Drawing for Simple Shapes =====
                    case GerberPrimitiveType.Circle:
                    case GerberPrimitiveType.Flash:
                        // Flush polygon batch before direct drawing (different geometry type)
                        if (polyBatch != null)
                        {
                            polyCtx.Close();
                            polyBatch.Freeze();
                            dc.DrawGeometry(currentBatchBrush, null, polyBatch);
                            polyBatch = null;
                            polyCtx = null;
                            polyCount = 0;
                        }
                        double r = Math.Max(screenWidth / 2, 0.5);
                        dc.DrawEllipse(primBrush, null, screenPos, r, r);
                        break;

                    case GerberPrimitiveType.Rectangle:
                        if (prim.Rotation == 0)
                        {
                            // Flush polygon batch before direct drawing
                            if (polyBatch != null)
                            {
                                polyCtx.Close();
                                polyBatch.Freeze();
                                dc.DrawGeometry(currentBatchBrush, null, polyBatch);
                                polyBatch = null;
                                polyCtx = null;
                                polyCount = 0;
                            }
                            double rw = Math.Max(screenWidth, 0.5);
                            double rh = Math.Max(screenHeight, 0.5);
                            dc.DrawRectangle(primBrush, null, new Rect(screenPos.X - rw / 2, screenPos.Y - rh / 2, rw, rh));
                        }
                        else
                        {
                            // Rotated rects go to polygon batch
                            if (polyBatch == null)
                            {
                                polyBatch = new StreamGeometry();
                                polyBatch.FillRule = FillRule.Nonzero;
                                polyCtx = polyBatch.Open();
                                currentBatchBrush = primBrush;
                            }
                            AddRotatedRectToContext(polyCtx, screenPos, screenWidth, screenHeight, prim.Rotation);
                            polyCount++;
                        }
                        break;

                    case GerberPrimitiveType.Obround:
                        if (prim.Rotation == 0)
                        {
                            // Flush polygon batch before direct drawing
                            if (polyBatch != null)
                            {
                                polyCtx.Close();
                                polyBatch.Freeze();
                                dc.DrawGeometry(currentBatchBrush, null, polyBatch);
                                polyBatch = null;
                                polyCtx = null;
                                polyCount = 0;
                            }
                            double ow = Math.Max(screenWidth, 0.5);
                            double oh = Math.Max(screenHeight, 0.5);
                            double cr = Math.Min(ow, oh) / 2;
                            dc.DrawRoundedRectangle(primBrush, null, new Rect(screenPos.X - ow / 2, screenPos.Y - oh / 2, ow, oh), cr, cr);
                        }
                        else
                        {
                            // Rotated obround -> batch as rounded rect approximation
                            if (polyBatch == null)
                            {
                                polyBatch = new StreamGeometry();
                                polyBatch.FillRule = FillRule.Nonzero;
                                polyCtx = polyBatch.Open();
                                currentBatchBrush = primBrush;
                            }
                            double w = Math.Max(screenWidth, 0.5) / 2;
                            double h = Math.Max(screenHeight, 0.5) / 2;
                            AddRoundedRectToContext(polyCtx, screenPos, w, h, Math.Min(w, h));
                            polyCount++;
                        }
                        break;

                    // ===== CRITICAL FIX: Draw Lines Directly (No Manual Tessellation) =====
                    // Using dc.DrawLine with a cached Pen is MUCH faster than manually
                    // calculating 4 corner points per segment and batching into StreamGeometry.
                    // WPF's internal renderer handles thick lines natively and efficiently.
                    case GerberPrimitiveType.Line:
                        if (prim.Points != null && prim.Points.Count >= 2)
                        {
                            // Flush polygon batch before direct drawing
                            if (polyBatch != null)
                            {
                                polyCtx.Close();
                                polyBatch.Freeze();
                                dc.DrawGeometry(currentBatchBrush, null, polyBatch);
                                polyBatch = null;
                                polyCtx = null;
                                polyCount = 0;
                            }
                            // Get a cached pen for this thickness (round caps built-in)
                            var pen = GetCachedPen(primBrush, Math.Max(screenWidth, 1.0));

                            Point p1 = WorldToScreen(prim.Points[0]);
                            Point p2 = WorldToScreen(prim.Points[1]);
                            dc.DrawLine(pen, p1, p2);
                        }
                        break;

                    case GerberPrimitiveType.Arc:
                        // Arcs are polylines - draw each segment directly
                        if (prim.Points != null && prim.Points.Count >= 2)
                        {
                            // Flush polygon batch before direct drawing
                            if (polyBatch != null)
                            {
                                polyCtx.Close();
                                polyBatch.Freeze();
                                dc.DrawGeometry(currentBatchBrush, null, polyBatch);
                                polyBatch = null;
                                polyCtx = null;
                                polyCount = 0;
                            }
                            var pen = GetCachedPen(primBrush, Math.Max(screenWidth, 1.0));
                            Point lastPt = WorldToScreen(prim.Points[0]);
                            for (int i = 1; i < prim.Points.Count; i++)
                            {
                                Point currPt = WorldToScreen(prim.Points[i]);
                                dc.DrawLine(pen, lastPt, currPt);
                                lastPt = currPt;
                            }
                        }
                        break;

                    // ===== BATCH PATH: Only Polygons/Contours need batching =====
                    case GerberPrimitiveType.Polygon:
                    case GerberPrimitiveType.Contour:
                        if (prim.Points != null && prim.Points.Count >= 3)
                        {
                            if (polyBatch == null)
                            {
                                polyBatch = new StreamGeometry();
                                polyBatch.FillRule = FillRule.Nonzero;
                                polyCtx = polyBatch.Open();
                                currentBatchBrush = primBrush;
                            }

                            polyCtx.BeginFigure(WorldToScreen(prim.Points[0]), true, true);
                            for (int i = 1; i < prim.Points.Count; i++)
                            {
                                polyCtx.LineTo(WorldToScreen(prim.Points[i]), false, false);
                            }
                            polyCount++;
                        }
                        break;
                }

                // Flush polygon batch if it gets too large (prevents geometry complexity issues)
                if (polyCount >= POLY_BATCH_LIMIT)
                {
                    polyCtx.Close();
                    polyBatch.Freeze();
                    dc.DrawGeometry(currentBatchBrush, null, polyBatch);
                    polyBatch = null;
                    polyCtx = null;
                    polyCount = 0;
                }
            }

            // Final flush of any remaining polygons
            if (polyBatch != null)
            {
                polyCtx.Close();
                polyBatch.Freeze();
                dc.DrawGeometry(currentBatchBrush, null, polyBatch);
            }

            if (hasOpacity)
            {
                dc.Pop();
            }
        }

        /// <summary>
        /// Add a rotated rectangle to a StreamGeometryContext for batching
        /// </summary>
        private void AddRotatedRectToContext(StreamGeometryContext ctx, Point center, double width, double height, double angleDegrees)
        {
            double rad = angleDegrees * Math.PI / 180.0;
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);
            double hw = width / 2;
            double hh = height / 2;

            // Corner offsets before rotation
            Point[] corners = new Point[]
            {
                new Point(-hw, -hh),
                new Point(hw, -hh),
                new Point(hw, hh),
                new Point(-hw, hh)
            };

            // First corner
            double rx = corners[0].X * cos - corners[0].Y * sin;
            double ry = corners[0].X * sin + corners[0].Y * cos;
            ctx.BeginFigure(new Point(center.X + rx, center.Y + ry), true, true);

            // Remaining corners
            for (int i = 1; i < 4; i++)
            {
                rx = corners[i].X * cos - corners[i].Y * sin;
                ry = corners[i].X * sin + corners[i].Y * cos;
                ctx.LineTo(new Point(center.X + rx, center.Y + ry), false, false);
            }
        }

        /// <summary>
        /// Add a circle approximation (octagon) to a StreamGeometryContext for batching
        /// </summary>
        private void AddCircleToContext(StreamGeometryContext ctx, Point center, double radius)
        {
            // Approximate circle with 8 points (octagon) - faster than bezier curves
            const int segments = 8;
            double angleStep = 2 * Math.PI / segments;

            Point first = new Point(center.X + radius, center.Y);
            ctx.BeginFigure(first, true, true);

            for (int i = 1; i < segments; i++)
            {
                double angle = i * angleStep;
                ctx.LineTo(new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle)), false, false);
            }
        }

        /// <summary>
        /// Add a rounded rectangle to a StreamGeometryContext for batching
        /// </summary>
        private void AddRoundedRectToContext(StreamGeometryContext ctx, Point center, double halfWidth, double halfHeight, double cornerRadius)
        {
            // Simplified rounded rect using straight edges with small corner cuts
            double r = Math.Min(cornerRadius, Math.Min(halfWidth, halfHeight));
            double x = center.X;
            double y = center.Y;

            // Start at top-left after corner
            ctx.BeginFigure(new Point(x - halfWidth + r, y - halfHeight), true, true);
            ctx.LineTo(new Point(x + halfWidth - r, y - halfHeight), false, false);
            // Top-right corner (quarter circle approximation with 2 segments)
            ctx.LineTo(new Point(x + halfWidth, y - halfHeight + r), false, false);
            ctx.LineTo(new Point(x + halfWidth, y + halfHeight - r), false, false);
            // Bottom-right corner
            ctx.LineTo(new Point(x + halfWidth - r, y + halfHeight), false, false);
            ctx.LineTo(new Point(x - halfWidth + r, y + halfHeight), false, false);
            // Bottom-left corner
            ctx.LineTo(new Point(x - halfWidth, y + halfHeight - r), false, false);
            ctx.LineTo(new Point(x - halfWidth, y - halfHeight + r), false, false);
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

                // Only render dark (additive) selected primitives - clear primitives are subtractive and should not show selection
                foreach (var prim in layer.Primitives.Where(p => p.IsSelected && p.IsDark))
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
                // Skip non-dark (clear/negative) primitives - they subtract material and shouldn't be selectable
                if (!prim.IsDark)
                    continue;

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
        /// Hit test all visible layers at a screen position to find which layer has geometry there.
        /// Returns the topmost (last in collection) visible layer with a primitive at that point.
        /// Used for Alt+Click layer picking in the viewport.
        /// </summary>
        public GerberLayer HitTestLayerAtScreenPos(Point screenPos, double hitRadius = 5)
        {
            if (GerberLayers == null || GerberLayers.Count == 0)
                return null;

            Point worldPos = ScreenToWorld(screenPos);
            double worldRadius = hitRadius / Zoom;

            // Iterate in reverse (top-most rendered layer first)
            for (int i = GerberLayers.Count - 1; i >= 0; i--)
            {
                var layer = GerberLayers[i];
                if (!layer.IsVisible) continue;

                var layerBounds = layer.Bounds;
                if (layerBounds.IsEmpty) continue;

                var expanded = layerBounds;
                expanded.Inflate(worldRadius, worldRadius);
                if (!expanded.Contains(worldPos)) continue;

                foreach (var prim in layer.Primitives)
                {
                    if (!prim.IsDark) continue;
                    var primBounds = prim.GetBounds();
                    primBounds.Inflate(worldRadius, worldRadius);
                    if (primBounds.Contains(worldPos))
                        return layer;
                }
            }

            return null;
        }

        /// <summary>
        /// Select Gerber primitives in the given screen rectangle
        /// ONLY selects from the ACTIVE layer, and only dark (visible) primitives
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

            // Filter out clear/negative primitives - only return dark (visible) ones
            return _activeLayerQuadtree.QueryRect(worldRect).Where(p => p.IsDark).ToList();
        }

        /// <summary>
        /// Invalidates all Gerber layer caches, forcing re-rendering
        /// Call this when layer geometry changes (not for visibility/color changes)
        /// </summary>
        public void InvalidateGerberCache()
        {
            // Clear layer quadtrees - they will be rebuilt on demand
            lock (_quadtreeLock)
            {
                _layerQuadtrees.Clear();
                _pendingQuadtreeBuilds.Clear();
            }
            _worldBounds = Rect.Empty;
            _activeLayerQuadtree = null;
            _gerberCacheDirty = true;
            InvalidateVisual();
        }

        /// <summary>
        /// Get the visible world bounds based on current viewport
        /// </summary>
        private Rect GetVisibleWorldBounds()
        {
            Point worldTL = ScreenToWorld(new Point(0, 0));
            Point worldBR = ScreenToWorld(new Point(ActualWidth, ActualHeight));
            return new Rect(
                Math.Min(worldTL.X, worldBR.X),
                Math.Min(worldTL.Y, worldBR.Y),
                Math.Abs(worldBR.X - worldTL.X),
                Math.Abs(worldBR.Y - worldTL.Y));
        }

        /// <summary>
        /// Mark layers as needing redraw (for visibility/color changes, not geometry)
        /// </summary>
        public void InvalidateComposite()
        {
            InvalidateVisual();
        }

        /// <summary>
        /// Calculate combined world bounds from all layers
        /// </summary>
        private void UpdateWorldBoundsFromLayers()
        {
            _worldBounds = Rect.Empty;
            if (GerberLayers == null)
                return;

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
            if (GerberLayers == null || GerberLayers.Count == 0)
                return;

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

            InvalidateVisual();
        }

        /// <summary>
        /// Get the currently active Gerber layer
        /// </summary>
        public GerberLayer GetActiveGerberLayer()
        {
            return _activeGerberLayer;
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

        /// <summary>
        /// Event raised when Alt+Click picks a layer in the viewport
        /// </summary>
        public event EventHandler<GerberLayer> LayerPicked;

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Focus();

            // Handle double-click (FrameworkElement doesn't have OnMouseDoubleClick)
            if (e.ClickCount == 2)
            {
                HandleMouseDoubleClick(e);
                if (e.Handled) return;
            }

            Point mousePos = e.GetPosition(this);
            _selectionStart = mousePos;
            _lastMousePosition = mousePos;

            // Alt+Click: pick layer under cursor and switch to it
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                var pickedLayer = HitTestLayerAtScreenPos(mousePos);
                if (pickedLayer != null)
                {
                    SetActiveGerberLayer(pickedLayer);
                    LayerPicked?.Invoke(this, pickedLayer);
                    e.Handled = true;
                    return;
                }
            }

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

        private void HandleMouseDoubleClick(MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            Point mousePos = e.GetPosition(this);
            Placement hitPlacement = HitTestPlacement(mousePos);
            if (hitPlacement != null)
            {
                PlacementDoubleClicked?.Invoke(this, hitPlacement);
                e.Handled = true;
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
