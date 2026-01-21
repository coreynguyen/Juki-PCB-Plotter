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
        private double _opacity = 1.0;
        private uint _color = 0xFF00FF00; // Green
        private List<GerberPrimitive> _primitives;

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
            set { SetProperty(ref _primitives, value); }
        }

        /// <summary>
        /// Bounding box of all primitives
        /// </summary>
        public Rect Bounds
        {
            get
            {
                if (_primitives == null || _primitives.Count == 0)
                    return Rect.Empty;

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

                return new Rect(minX, minY, maxX - minX, maxY - minY);
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
                case GerberPrimitiveType.Contour:
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
                        // Add stroke width
                        minX -= Width / 2;
                        minY -= Width / 2;
                        maxX += Width / 2;
                        maxY += Width / 2;
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
}
