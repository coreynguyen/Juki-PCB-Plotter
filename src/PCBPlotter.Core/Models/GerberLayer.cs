using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace PCBPlotter.Core.Models
{
    /// <summary>
    /// Represents an imported Gerber layer
    /// </summary>
    public class GerberLayer : ModelBase
    {
        private string _id;
        private string _name;
        private string _filePath;
        private GerberLayerType _layerType = GerberLayerType.Unknown;
        private bool _isVisible = true;
        private bool _isActive = false;
        private double _opacity = 1.0;
        private uint _color = 0xFF00FF00; // Green
        private List<GerberPrimitive> _primitives;
        private List<ConsumedRegion> _consumedRegions = new List<ConsumedRegion>();

        // Cached bounds - computed once on first access or when invalidated
        private Rect _cachedBounds = Rect.Empty;
        private bool _boundsDirty = true;

        /// <summary>
        /// Unique identifier
        /// </summary>
        public string Id
        {
            get { return _id; }
            set { SetProperty(ref _id, value); }
        }

        /// <summary>
        /// Layer display name
        /// </summary>
        public string Name
        {
            get { return _name; }
            set { SetProperty(ref _name, value); }
        }

        /// <summary>
        /// Original file path
        /// </summary>
        public string FilePath
        {
            get { return _filePath; }
            set { SetProperty(ref _filePath, value); }
        }

        /// <summary>
        /// Detected or assigned layer type
        /// </summary>
        public GerberLayerType LayerType
        {
            get { return _layerType; }
            set { SetProperty(ref _layerType, value); }
        }

        /// <summary>
        /// Whether this layer is visible
        /// </summary>
        public bool IsVisible
        {
            get { return _isVisible; }
            set { SetProperty(ref _isVisible, value); }
        }

        /// <summary>
        /// Whether this layer is the active/editable layer (only one can be active)
        /// Active layer has vector shapes for selection, inactive layers are rasterized
        /// </summary>
        public bool IsActive
        {
            get { return _isActive; }
            set { SetProperty(ref _isActive, value); }
        }

        /// <summary>
        /// Layer opacity (0-1)
        /// </summary>
        public double Opacity
        {
            get { return _opacity; }
            set { SetProperty(ref _opacity, Math.Max(0, Math.Min(1, value))); }
        }

        /// <summary>
        /// Layer color (ARGB)
        /// </summary>
        public uint ColorArgb
        {
            get { return _color; }
            set { SetProperty(ref _color, value); }
        }

        public Color Color
        {
            get { return Color.FromArgb((byte)(_color >> 24), (byte)(_color >> 16), (byte)(_color >> 8), (byte)_color); }
            set { ColorArgb = (uint)((value.A << 24) | (value.R << 16) | (value.G << 8) | value.B); }
        }

        /// <summary>
        /// Gerber primitives in this layer
        /// </summary>
        public List<GerberPrimitive> Primitives
        {
            get { return _primitives; }
            set
            {
                SetProperty(ref _primitives, value);
                _boundsDirty = true; // Invalidate cached bounds
            }
        }

        /// <summary>
        /// Consumed regions (simplified bounding shapes for primitives used in placements).
        /// These are drawn as solid yellow shapes to indicate used areas.
        /// </summary>
        public List<ConsumedRegion> ConsumedRegions
        {
            get { return _consumedRegions; }
        }

        /// <summary>
        /// Adds consumed regions from primitives that were used to create a placement.
        /// Clusters the primitives into islands and creates bounding shapes.
        /// </summary>
        public void AddConsumedRegions(IEnumerable<GerberPrimitive> primitives, string placementReference)
        {
            var regions = ConsumedRegion.CreateFromPrimitives(primitives, placementReference);
            _consumedRegions.AddRange(regions);
        }

        /// <summary>
        /// Removes consumed regions that were created for a specific placement reference.
        /// Used for undo operations.
        /// </summary>
        public void RemoveConsumedRegionsByReference(string placementReference)
        {
            _consumedRegions.RemoveAll(r => r.PlacementReference == placementReference);
        }

        /// <summary>
        /// Invalidate cached bounds (call after modifying primitives list)
        /// </summary>
        public void InvalidateBounds()
        {
            _boundsDirty = true;
        }

        /// <summary>
        /// Bounding box of all primitives (cached for performance)
        /// </summary>
        public Rect Bounds
        {
            get
            {
                if (_primitives == null || _primitives.Count == 0)
                    return Rect.Empty;

                // Return cached bounds if valid
                if (!_boundsDirty)
                    return _cachedBounds;

                // Recompute bounds
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;

                foreach (var prim in _primitives)
                {
                    var bounds = prim.GetBounds();
                    if (bounds.Left < minX) minX = bounds.Left;
                    if (bounds.Top < minY) minY = bounds.Top;
                    if (bounds.Right > maxX) maxX = bounds.Right;
                    if (bounds.Bottom > maxY) maxY = bounds.Bottom;
                }

                _cachedBounds = new Rect(minX, minY, maxX - minX, maxY - minY);
                _boundsDirty = false;
                return _cachedBounds;
            }
        }

        public GerberLayer()
        {
            _id = Guid.NewGuid().ToString();
            _primitives = new List<GerberPrimitive>();
        }

        public GerberLayer(string name, string filePath = null) : this()
        {
            _name = name;
            _filePath = filePath;
        }
    }

    /// <summary>
    /// Types of Gerber layers
    /// </summary>
    public enum GerberLayerType
    {
        Unknown,
        TopCopper,
        BottomCopper,
        TopSilkscreen,
        BottomSilkscreen,
        TopSoldermask,
        BottomSoldermask,
        TopPaste,
        BottomPaste,
        Outline,
        Drill
    }

    /// <summary>
    /// A primitive shape from a Gerber file
    /// </summary>
    public class GerberPrimitive : ModelBase
    {
        private string _id;
        private GerberPrimitiveType _type;
        private double _x;
        private double _y;
        private double _width;
        private double _height;
        private double _rotation;
        private List<Point> _points;
        private int _apertureIndex;
        private bool _isSelected;
        private bool _isDark = true;
        private bool _isConsumed;

        public string Id
        {
            get { return _id; }
            set { SetProperty(ref _id, value); }
        }

        public GerberPrimitiveType Type
        {
            get { return _type; }
            set { SetProperty(ref _type, value); }
        }

        public double X
        {
            get { return _x; }
            set { SetProperty(ref _x, value); }
        }

        public double Y
        {
            get { return _y; }
            set { SetProperty(ref _y, value); }
        }

        public double Width
        {
            get { return _width; }
            set { SetProperty(ref _width, value); }
        }

        public double Height
        {
            get { return _height; }
            set { SetProperty(ref _height, value); }
        }

        public double Rotation
        {
            get { return _rotation; }
            set { SetProperty(ref _rotation, value); }
        }

        public List<Point> Points
        {
            get { return _points; }
            set { SetProperty(ref _points, value); }
        }

        public int ApertureIndex
        {
            get { return _apertureIndex; }
            set { SetProperty(ref _apertureIndex, value); }
        }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value); }
        }

        /// <summary>
        /// Whether this primitive uses dark polarity (adds material).
        /// Clear/negative primitives (IsDark=false) subtract material and should not be selectable.
        /// </summary>
        public bool IsDark
        {
            get { return _isDark; }
            set { SetProperty(ref _isDark, value); }
        }

        /// <summary>
        /// Whether this primitive has been used to create a placement.
        /// Consumed primitives are rendered with a distinct overlay in the Gerber viewer.
        /// </summary>
        public bool IsConsumed
        {
            get { return _isConsumed; }
            set { SetProperty(ref _isConsumed, value); }
        }

        public Point Position
        {
            get { return new Point(X, Y); }
            set { X = value.X; Y = value.Y; }
        }

        public GerberPrimitive()
        {
            _id = Guid.NewGuid().ToString();
            _points = new List<Point>();
        }

        public Rect GetBounds()
        {
            switch (Type)
            {
                case GerberPrimitiveType.Circle:
                case GerberPrimitiveType.Flash:
                    return new Rect(X - Width / 2, Y - Height / 2, Width, Height);

                case GerberPrimitiveType.Rectangle:
                case GerberPrimitiveType.Obround:
                    return new Rect(X - Width / 2, Y - Height / 2, Width, Height);

                case GerberPrimitiveType.Line:
                case GerberPrimitiveType.Arc:  // Arcs have Points[] containing polyline segments
                case GerberPrimitiveType.Contour:
                case GerberPrimitiveType.Polygon:  // FIX: Polygons need bounds from Points, not X/Y/Width/Height
                    if (Points != null && Points.Count > 0)
                    {
                        double minX = double.MaxValue, minY = double.MaxValue;
                        double maxX = double.MinValue, maxY = double.MinValue;
                        foreach (var pt in Points)
                        {
                            if (pt.X < minX) minX = pt.X;
                            if (pt.Y < minY) minY = pt.Y;
                            if (pt.X > maxX) maxX = pt.X;
                            if (pt.Y > maxY) maxY = pt.Y;
                        }
                        // Add stroke width (for lines/arcs/contours)
                        if (Type == GerberPrimitiveType.Line || Type == GerberPrimitiveType.Arc || Type == GerberPrimitiveType.Contour)
                        {
                            minX -= Width / 2;
                            minY -= Width / 2;
                            maxX += Width / 2;
                            maxY += Width / 2;
                        }
                        return new Rect(minX, minY, maxX - minX, maxY - minY);
                    }
                    return new Rect(X, Y, 0, 0);

                default:
                    return new Rect(X - Width / 2, Y - Height / 2, Width, Height);
            }
        }
    }

    public enum GerberPrimitiveType
    {
        Circle,
        Rectangle,
        Obround,
        Polygon,
        Flash,
        Line,
        Arc,
        Contour
    }

    /// <summary>
    /// Shape type for consumed regions
    /// </summary>
    public enum ConsumedRegionShape
    {
        Rectangle,
        Ellipse
    }

    /// <summary>
    /// Represents a simplified bounding shape for consumed (used) primitives.
    /// Instead of highlighting each individual primitive, we cluster nearby primitives
    /// into islands and draw a solid shape over each cluster.
    /// </summary>
    public class ConsumedRegion
    {
        /// <summary>
        /// Bounding rectangle in world coordinates
        /// </summary>
        public Rect Bounds { get; set; }

        /// <summary>
        /// Shape type - rectangle or ellipse based on primitive analysis
        /// </summary>
        public ConsumedRegionShape Shape { get; set; }

        /// <summary>
        /// The placement reference this region is associated with
        /// </summary>
        public string PlacementReference { get; set; }

        /// <summary>
        /// Center point (computed from bounds)
        /// </summary>
        public Point Center
        {
            get { return new Point(Bounds.X + Bounds.Width / 2, Bounds.Y + Bounds.Height / 2); }
        }

        /// <summary>
        /// Creates consumed regions from a list of primitives by clustering them into islands
        /// </summary>
        public static List<ConsumedRegion> CreateFromPrimitives(IEnumerable<GerberPrimitive> primitives, string placementReference)
        {
            var regions = new List<ConsumedRegion>();
            var primList = new List<GerberPrimitive>(primitives);

            if (primList.Count == 0)
                return regions;

            // Cluster primitives into islands based on spatial proximity
            var clusters = ClusterPrimitives(primList);

            foreach (var cluster in clusters)
            {
                if (cluster.Count == 0)
                    continue;

                // Compute bounding box for this cluster
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                bool hasCircularPrimitives = false;
                int circleCount = 0;
                int rectCount = 0;

                foreach (var prim in cluster)
                {
                    var bounds = prim.GetBounds();
                    if (bounds.Left < minX) minX = bounds.Left;
                    if (bounds.Top < minY) minY = bounds.Top;
                    if (bounds.Right > maxX) maxX = bounds.Right;
                    if (bounds.Bottom > maxY) maxY = bounds.Bottom;

                    // Track primitive types for shape determination
                    if (prim.Type == GerberPrimitiveType.Circle || prim.Type == GerberPrimitiveType.Flash)
                    {
                        circleCount++;
                        // Check if roughly circular (aspect ratio near 1:1)
                        if (Math.Abs(prim.Width - prim.Height) < Math.Max(prim.Width, prim.Height) * 0.1)
                            hasCircularPrimitives = true;
                    }
                    else
                    {
                        rectCount++;
                    }
                }

                var regionBounds = new Rect(minX, minY, maxX - minX, maxY - minY);

                // Determine shape: ellipse if mostly circular primitives and roughly square bounds
                var shape = ConsumedRegionShape.Rectangle;
                double aspectRatio = regionBounds.Width / Math.Max(regionBounds.Height, 0.001);
                if (hasCircularPrimitives && circleCount > rectCount && aspectRatio > 0.8 && aspectRatio < 1.2)
                {
                    shape = ConsumedRegionShape.Ellipse;
                }

                regions.Add(new ConsumedRegion
                {
                    Bounds = regionBounds,
                    Shape = shape,
                    PlacementReference = placementReference
                });
            }

            return regions;
        }

        /// <summary>
        /// Clusters primitives into islands based on spatial proximity.
        /// Two primitives are in the same cluster if their bounding boxes overlap or are within a threshold distance.
        /// </summary>
        private static List<List<GerberPrimitive>> ClusterPrimitives(List<GerberPrimitive> primitives)
        {
            var clusters = new List<List<GerberPrimitive>>();
            var assigned = new bool[primitives.Count];

            // Distance threshold for clustering (primitives within this distance are grouped)
            const double clusterThreshold = 0.5; // mm

            for (int i = 0; i < primitives.Count; i++)
            {
                if (assigned[i])
                    continue;

                // Start a new cluster
                var cluster = new List<GerberPrimitive>();
                var queue = new Queue<int>();
                queue.Enqueue(i);
                assigned[i] = true;

                while (queue.Count > 0)
                {
                    int idx = queue.Dequeue();
                    cluster.Add(primitives[idx]);
                    var bounds1 = primitives[idx].GetBounds();

                    // Expand bounds by threshold for proximity check
                    var expandedBounds = new Rect(
                        bounds1.X - clusterThreshold,
                        bounds1.Y - clusterThreshold,
                        bounds1.Width + clusterThreshold * 2,
                        bounds1.Height + clusterThreshold * 2);

                    // Find all unassigned primitives that overlap with expanded bounds
                    for (int j = 0; j < primitives.Count; j++)
                    {
                        if (assigned[j])
                            continue;

                        var bounds2 = primitives[j].GetBounds();
                        if (expandedBounds.IntersectsWith(bounds2))
                        {
                            queue.Enqueue(j);
                            assigned[j] = true;
                        }
                    }
                }

                clusters.Add(cluster);
            }

            return clusters;
        }
    }
}
