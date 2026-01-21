using System;
using System.Windows;

namespace PCBPlotter.Core.Models
{
    /// <summary>
    /// Represents an instance of the reference circuit in a panelized board
    /// </summary>
    public class CircuitInstance : ModelBase
    {
        private string _id;
        private string _name;
        private int _indexX;
        private int _indexY;
        private double _offsetX;
        private double _offsetY;
        private double _rotation;
        private bool _isVisible = true;
        private bool _isEnabled = true;

        /// <summary>
        /// Unique identifier
        /// </summary>
        public string Id
        {
            get { return _id; }
            set { SetProperty(ref _id, value); }
        }

        /// <summary>
        /// Display name (e.g., "Circuit 1", "Circuit 2")
        /// </summary>
        public string Name
        {
            get { return _name; }
            set { SetProperty(ref _name, value); }
        }

        /// <summary>
        /// Index in X direction (0-based)
        /// </summary>
        public int IndexX
        {
            get { return _indexX; }
            set { SetProperty(ref _indexX, value); }
        }

        /// <summary>
        /// Index in Y direction (0-based)
        /// </summary>
        public int IndexY
        {
            get { return _indexY; }
            set { SetProperty(ref _indexY, value); }
        }

        /// <summary>
        /// X offset from reference circuit origin
        /// </summary>
        public double OffsetX
        {
            get { return _offsetX; }
            set { SetProperty(ref _offsetX, value); }
        }

        /// <summary>
        /// Y offset from reference circuit origin
        /// </summary>
        public double OffsetY
        {
            get { return _offsetY; }
            set { SetProperty(ref _offsetY, value); }
        }

        /// <summary>
        /// Rotation relative to reference circuit (for rotated panels)
        /// </summary>
        public double Rotation
        {
            get { return _rotation; }
            set { SetProperty(ref _rotation, NormalizeAngle(value)); }
        }

        /// <summary>
        /// Whether this instance is visible in the output view
        /// </summary>
        public bool IsVisible
        {
            get { return _isVisible; }
            set { SetProperty(ref _isVisible, value); }
        }

        /// <summary>
        /// Whether this instance is enabled for export
        /// </summary>
        public bool IsEnabled
        {
            get { return _isEnabled; }
            set { SetProperty(ref _isEnabled, value); }
        }

        /// <summary>
        /// Combined index (for ordering)
        /// </summary>
        public int LinearIndex
        {
            get { return _indexY * 100 + _indexX; } // Assumes less than 100 columns
        }

        /// <summary>
        /// Offset as a Point
        /// </summary>
        public Point Offset
        {
            get { return new Point(OffsetX, OffsetY); }
            set { OffsetX = value.X; OffsetY = value.Y; }
        }

        public CircuitInstance()
        {
            _id = Guid.NewGuid().ToString();
        }

        public CircuitInstance(int indexX, int indexY, double offsetX, double offsetY, double rotation = 0)
            : this()
        {
            _indexX = indexX;
            _indexY = indexY;
            _offsetX = offsetX;
            _offsetY = offsetY;
            _rotation = NormalizeAngle(rotation);
            _name = string.Format("Circuit ({0},{1})", indexX + 1, indexY + 1);
        }

        private double NormalizeAngle(double angle)
        {
            angle = angle % 360;
            if (angle < 0) angle += 360;
            return angle;
        }

        /// <summary>
        /// Transforms a point from reference circuit coordinates to this instance's coordinates
        /// </summary>
        public Point TransformPoint(Point referencePoint)
        {
            // Apply rotation around origin first
            double rad = Rotation * Math.PI / 180.0;
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);

            double rx = referencePoint.X * cos - referencePoint.Y * sin;
            double ry = referencePoint.X * sin + referencePoint.Y * cos;

            // Then apply offset
            return new Point(rx + OffsetX, ry + OffsetY);
        }

        /// <summary>
        /// Transforms a rotation from reference circuit to this instance
        /// </summary>
        public double TransformRotation(double referenceRotation)
        {
            return NormalizeAngle(referenceRotation + Rotation);
        }

        public override string ToString()
        {
            return Name ?? string.Format("Instance ({0},{1})", IndexX, IndexY);
        }
    }
}
