using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace PCBPlotter.Core.Models
{
    /// <summary>
    /// Represents a single graphic element within a package
    /// </summary>
    public class PackageGraphic : ModelBase
    {
        private GraphicShapeType _shapeType = GraphicShapeType.Rectangle;
        private double _x;
        private double _y;
        private double _width;
        private double _height;
        private double _rotation;
        private double _cornerRadius;
        private bool _isFilled = true;
        private bool _isPad;
        private bool _isPin1Indicator;
        private double _strokeThickness = 0.1;
        private List<Point> _points;
        private string _text;
        private double _textSize = 0.5;
        private double _startAngle;
        private double _sweepAngle = 90;

        // Colors stored as ARGB for serialization
        private uint _fillColor = 0xFF4A4A4A;  // Dark gray
        private uint _strokeColor = 0xFFFFFFFF; // White

        public GraphicShapeType ShapeType
        {
            get { return _shapeType; }
            set { SetProperty(ref _shapeType, value); }
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

        public double CornerRadius
        {
            get { return _cornerRadius; }
            set { SetProperty(ref _cornerRadius, value); }
        }

        public bool IsFilled
        {
            get { return _isFilled; }
            set { SetProperty(ref _isFilled, value); }
        }

        public bool IsPad
        {
            get { return _isPad; }
            set { SetProperty(ref _isPad, value); }
        }

        public bool IsPin1Indicator
        {
            get { return _isPin1Indicator; }
            set { SetProperty(ref _isPin1Indicator, value); }
        }

        public double StrokeThickness
        {
            get { return _strokeThickness; }
            set { SetProperty(ref _strokeThickness, value); }
        }

        /// <summary>
        /// Points for polygon or line shapes
        /// </summary>
        public List<Point> Points
        {
            get { return _points; }
            set { SetProperty(ref _points, value); }
        }

        /// <summary>
        /// Text content for text shapes
        /// </summary>
        public string Text
        {
            get { return _text; }
            set { SetProperty(ref _text, value); }
        }

        public double TextSize
        {
            get { return _textSize; }
            set { SetProperty(ref _textSize, value); }
        }

        /// <summary>
        /// Start angle for arc shapes (in degrees)
        /// </summary>
        public double StartAngle
        {
            get { return _startAngle; }
            set { SetProperty(ref _startAngle, value); }
        }

        /// <summary>
        /// Sweep angle for arc shapes (in degrees)
        /// </summary>
        public double SweepAngle
        {
            get { return _sweepAngle; }
            set { SetProperty(ref _sweepAngle, value); }
        }

        public uint FillColorArgb
        {
            get { return _fillColor; }
            set { SetProperty(ref _fillColor, value); }
        }

        public uint StrokeColorArgb
        {
            get { return _strokeColor; }
            set { SetProperty(ref _strokeColor, value); }
        }

        public Color FillColor
        {
            get { return Color.FromArgb((byte)(_fillColor >> 24), (byte)(_fillColor >> 16), (byte)(_fillColor >> 8), (byte)_fillColor); }
            set { FillColorArgb = (uint)((value.A << 24) | (value.R << 16) | (value.G << 8) | value.B); }
        }

        public Color StrokeColor
        {
            get { return Color.FromArgb((byte)(_strokeColor >> 24), (byte)(_strokeColor >> 16), (byte)(_strokeColor >> 8), (byte)_strokeColor); }
            set { StrokeColorArgb = (uint)((value.A << 24) | (value.R << 16) | (value.G << 8) | value.B); }
        }

        public PackageGraphic()
        {
            _points = new List<Point>();
        }

        /// <summary>
        /// Gets the bounding box of this graphic
        /// </summary>
        public Rect GetBounds()
        {
            switch (ShapeType)
            {
                case GraphicShapeType.Rectangle:
                case GraphicShapeType.RoundedRectangle:
                    return new Rect(X, Y, Width, Height);

                case GraphicShapeType.Circle:
                    return new Rect(X - Width / 2, Y - Width / 2, Width, Width);

                case GraphicShapeType.Ellipse:
                    return new Rect(X - Width / 2, Y - Height / 2, Width, Height);

                case GraphicShapeType.Line:
                    if (Points != null && Points.Count >= 2)
                    {
                        double minX = Math.Min(Points[0].X, Points[1].X);
                        double minY = Math.Min(Points[0].Y, Points[1].Y);
                        double maxX = Math.Max(Points[0].X, Points[1].X);
                        double maxY = Math.Max(Points[0].Y, Points[1].Y);
                        return new Rect(minX, minY, maxX - minX, maxY - minY);
                    }
                    return new Rect(X, Y, 0, 0);

                case GraphicShapeType.Polygon:
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
                        return new Rect(minX, minY, maxX - minX, maxY - minY);
                    }
                    return new Rect(X, Y, 0, 0);

                case GraphicShapeType.Text:
                    // Approximate text bounds
                    return new Rect(X, Y, Text != null ? Text.Length * TextSize * 0.6 : 0, TextSize);

                default:
                    return new Rect(X, Y, Width, Height);
            }
        }

        /// <summary>
        /// Creates a deep copy of this graphic
        /// </summary>
        public PackageGraphic Clone()
        {
            var clone = new PackageGraphic
            {
                ShapeType = this.ShapeType,
                X = this.X,
                Y = this.Y,
                Width = this.Width,
                Height = this.Height,
                Rotation = this.Rotation,
                CornerRadius = this.CornerRadius,
                IsFilled = this.IsFilled,
                IsPad = this.IsPad,
                IsPin1Indicator = this.IsPin1Indicator,
                StrokeThickness = this.StrokeThickness,
                Text = this.Text,
                TextSize = this.TextSize,
                StartAngle = this.StartAngle,
                SweepAngle = this.SweepAngle,
                FillColorArgb = this.FillColorArgb,
                StrokeColorArgb = this.StrokeColorArgb
            };

            if (this.Points != null)
            {
                clone.Points = new List<Point>(this.Points);
            }

            return clone;
        }
    }
}
