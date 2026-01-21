using System;
using System.Windows;

namespace PCBPlotter.Core.Models
{
    /// <summary>
    /// Represents a single component placement on the PCB
    /// </summary>
    public class Placement : ModelBase
    {
        private string _id;
        private string _reference;
        private double _x;
        private double _y;
        private double _rotation;
        private BoardSide _side = BoardSide.Top;
        private Component _component;
        private Package _package;
        private PlacementStatus _status = PlacementStatus.Valid;
        private bool _isSelected;
        private bool _isExportEnabledTop;
        private bool _isExportEnabledBottom;
        private bool _isVisible = true;

        /// <summary>
        /// Unique identifier for this placement
        /// </summary>
        public string Id
        {
            get { return _id; }
            set { SetProperty(ref _id, value); }
        }

        /// <summary>
        /// Reference designator (e.g., C1, R5, U3)
        /// </summary>
        public string Reference
        {
            get { return _reference; }
            set
            {
                SetProperty(ref _reference, value);
                UpdateStatus();
            }
        }

        /// <summary>
        /// X coordinate in current units
        /// </summary>
        public double X
        {
            get { return _x; }
            set { SetProperty(ref _x, value); }
        }

        /// <summary>
        /// Y coordinate in current units
        /// </summary>
        public double Y
        {
            get { return _y; }
            set { SetProperty(ref _y, value); }
        }

        /// <summary>
        /// Rotation in degrees (0-360)
        /// </summary>
        public double Rotation
        {
            get { return _rotation; }
            set { SetProperty(ref _rotation, NormalizeAngle(value)); }
        }

        /// <summary>
        /// Which side of the board this placement is on
        /// </summary>
        public BoardSide Side
        {
            get { return _side; }
            set { SetProperty(ref _side, value); }
        }

        /// <summary>
        /// Associated component from BOM
        /// </summary>
        public Component Component
        {
            get { return _component; }
            set
            {
                SetProperty(ref _component, value);
                UpdateStatus();
            }
        }

        /// <summary>
        /// Package/footprint graphics
        /// </summary>
        public Package Package
        {
            get { return _package; }
            set
            {
                SetProperty(ref _package, value);
                UpdateStatus();
            }
        }

        /// <summary>
        /// Validation status flags
        /// </summary>
        public PlacementStatus Status
        {
            get { return _status; }
            set { SetProperty(ref _status, value); }
        }

        /// <summary>
        /// Whether this placement is currently selected
        /// </summary>
        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value); }
        }

        /// <summary>
        /// Whether this placement is enabled for top side export
        /// </summary>
        public bool IsExportEnabledTop
        {
            get { return _isExportEnabledTop; }
            set { SetProperty(ref _isExportEnabledTop, value); }
        }

        /// <summary>
        /// Whether this placement is enabled for bottom side export
        /// </summary>
        public bool IsExportEnabledBottom
        {
            get { return _isExportEnabledBottom; }
            set { SetProperty(ref _isExportEnabledBottom, value); }
        }

        /// <summary>
        /// Whether this placement is visible in the view
        /// </summary>
        public bool IsVisible
        {
            get { return _isVisible; }
            set { SetProperty(ref _isVisible, value); }
        }

        /// <summary>
        /// Position as a Point
        /// </summary>
        public Point Position
        {
            get { return new Point(X, Y); }
            set { X = value.X; Y = value.Y; }
        }

        public Placement()
        {
            _id = Guid.NewGuid().ToString();
        }

        public Placement(string reference, double x, double y, double rotation = 0, BoardSide side = BoardSide.Top)
            : this()
        {
            _reference = reference;
            _x = x;
            _y = y;
            _rotation = NormalizeAngle(rotation);
            _side = side;

            // Auto-enable export based on side
            _isExportEnabledTop = (side == BoardSide.Top);
            _isExportEnabledBottom = (side == BoardSide.Bottom);
        }

        private double NormalizeAngle(double angle)
        {
            angle = angle % 360;
            if (angle < 0) angle += 360;
            return angle;
        }

        private void UpdateStatus()
        {
            PlacementStatus newStatus = PlacementStatus.Valid;

            if (string.IsNullOrEmpty(_reference))
                newStatus |= PlacementStatus.NoReference;

            if (_component == null)
                newStatus |= PlacementStatus.NoComponent;

            if (_package == null)
                newStatus |= PlacementStatus.NoPackage;

            Status = newStatus;
        }

        /// <summary>
        /// Creates a copy of this placement with a new ID
        /// </summary>
        public Placement Clone()
        {
            return new Placement
            {
                Reference = this.Reference,
                X = this.X,
                Y = this.Y,
                Rotation = this.Rotation,
                Side = this.Side,
                Component = this.Component,
                Package = this.Package,
                IsExportEnabledTop = this.IsExportEnabledTop,
                IsExportEnabledBottom = this.IsExportEnabledBottom,
                IsVisible = this.IsVisible
            };
        }

        public override string ToString()
        {
            return string.Format("{0} @ ({1:F3}, {2:F3}) {3}°",
                Reference ?? "(unnamed)", X, Y, Rotation);
        }
    }
}
