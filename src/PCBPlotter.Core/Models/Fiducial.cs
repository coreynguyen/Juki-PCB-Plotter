using System;
using System.Windows;

namespace PCBPlotter.Core.Models
{
    /// <summary>
    /// Represents a fiducial marker for machine vision alignment
    /// </summary>
    public class Fiducial : ModelBase
    {
        private string _id;
        private string _name;
        private double _x;
        private double _y;
        private double _diameter = 1.0;
        private FiducialType _type = FiducialType.Global;
        private BoardSide _side = BoardSide.Top;
        private bool _sharedBetweenSides = true;
        private int _circuitIndex = -1; // -1 = global, 0+ = local to circuit

        /// <summary>
        /// Unique identifier
        /// </summary>
        public string Id
        {
            get { return _id; }
            set { SetProperty(ref _id, value); }
        }

        /// <summary>
        /// Fiducial name/label (e.g., "FID1", "FID2")
        /// </summary>
        public string Name
        {
            get { return _name; }
            set { SetProperty(ref _name, value); }
        }

        /// <summary>
        /// X position
        /// </summary>
        public double X
        {
            get { return _x; }
            set { SetProperty(ref _x, value); }
        }

        /// <summary>
        /// Y position
        /// </summary>
        public double Y
        {
            get { return _y; }
            set { SetProperty(ref _y, value); }
        }

        /// <summary>
        /// Fiducial marker diameter
        /// </summary>
        public double Diameter
        {
            get { return _diameter; }
            set { SetProperty(ref _diameter, value); }
        }

        /// <summary>
        /// Global (board-level) or Local (circuit-level) fiducial
        /// </summary>
        public FiducialType Type
        {
            get { return _type; }
            set { SetProperty(ref _type, value); }
        }

        /// <summary>
        /// Which side this fiducial is on
        /// </summary>
        public BoardSide Side
        {
            get { return _side; }
            set { SetProperty(ref _side, value); }
        }

        /// <summary>
        /// Whether this fiducial is shared between top and bottom sides
        /// (common for global fiducials on carrier strips)
        /// </summary>
        public bool SharedBetweenSides
        {
            get { return _sharedBetweenSides; }
            set { SetProperty(ref _sharedBetweenSides, value); }
        }

        /// <summary>
        /// For local fiducials, which circuit instance this belongs to (-1 = global)
        /// </summary>
        public int CircuitIndex
        {
            get { return _circuitIndex; }
            set { SetProperty(ref _circuitIndex, value); }
        }

        /// <summary>
        /// Position as a Point
        /// </summary>
        public Point Position
        {
            get { return new Point(X, Y); }
            set { X = value.X; Y = value.Y; }
        }

        public Fiducial()
        {
            _id = Guid.NewGuid().ToString();
        }

        public Fiducial(string name, double x, double y, FiducialType type = FiducialType.Global)
            : this()
        {
            _name = name;
            _x = x;
            _y = y;
            _type = type;
        }

        /// <summary>
        /// Gets the position for bottom side export (X-flipped)
        /// </summary>
        public Point GetBottomPosition(double boardWidth)
        {
            return new Point(boardWidth - X, Y);
        }

        public override string ToString()
        {
            return string.Format("{0} ({1}) @ ({2:F3}, {3:F3})",
                Name ?? "FID", Type, X, Y);
        }
    }
}
