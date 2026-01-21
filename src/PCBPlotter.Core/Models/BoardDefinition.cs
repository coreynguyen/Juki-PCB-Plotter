using System;
using System.Collections.Generic;
using System.Windows;

namespace PCBPlotter.Core.Models
{
    /// <summary>
    /// Defines the physical board and circuit layout parameters
    /// </summary>
    public class BoardDefinition : ModelBase
    {
        private double _width;
        private double _height;
        private double _thickness = 1.6;
        private Point _origin;
        private Point _lowerLeftOffset;
        private List<Point> _boardOutline;
        private List<Point> _circuitOutline;

        // Panelization
        private int _circuitCountX = 1;
        private int _circuitCountY = 1;
        private double _pitchX;
        private double _pitchY;
        private Point _circuitStartPosition;

        /// <summary>
        /// Board width in current units
        /// </summary>
        public double Width
        {
            get { return _width; }
            set { SetProperty(ref _width, value); }
        }

        /// <summary>
        /// Board height in current units
        /// </summary>
        public double Height
        {
            get { return _height; }
            set { SetProperty(ref _height, value); }
        }

        /// <summary>
        /// Board thickness in mm (for reference)
        /// </summary>
        public double Thickness
        {
            get { return _thickness; }
            set { SetProperty(ref _thickness, value); }
        }

        /// <summary>
        /// Origin point (0,0 reference)
        /// </summary>
        public Point Origin
        {
            get { return _origin; }
            set { SetProperty(ref _origin, value); }
        }

        /// <summary>
        /// Offset from origin to lower-left corner of PCB (for machine export)
        /// </summary>
        public Point LowerLeftOffset
        {
            get { return _lowerLeftOffset; }
            set { SetProperty(ref _lowerLeftOffset, value); }
        }

        /// <summary>
        /// Custom board outline points (if not rectangular)
        /// </summary>
        public List<Point> BoardOutline
        {
            get { return _boardOutline; }
            set { SetProperty(ref _boardOutline, value); }
        }

        /// <summary>
        /// Circuit/panel outline points
        /// </summary>
        public List<Point> CircuitOutline
        {
            get { return _circuitOutline; }
            set { SetProperty(ref _circuitOutline, value); }
        }

        /// <summary>
        /// Number of circuit instances in X direction
        /// </summary>
        public int CircuitCountX
        {
            get { return _circuitCountX; }
            set { SetProperty(ref _circuitCountX, Math.Max(1, value)); }
        }

        /// <summary>
        /// Number of circuit instances in Y direction
        /// </summary>
        public int CircuitCountY
        {
            get { return _circuitCountY; }
            set { SetProperty(ref _circuitCountY, Math.Max(1, value)); }
        }

        /// <summary>
        /// Distance between circuit origins in X direction
        /// </summary>
        public double PitchX
        {
            get { return _pitchX; }
            set { SetProperty(ref _pitchX, value); }
        }

        /// <summary>
        /// Distance between circuit origins in Y direction
        /// </summary>
        public double PitchY
        {
            get { return _pitchY; }
            set { SetProperty(ref _pitchY, value); }
        }

        /// <summary>
        /// Starting position of first circuit (reference circuit position)
        /// </summary>
        public Point CircuitStartPosition
        {
            get { return _circuitStartPosition; }
            set { SetProperty(ref _circuitStartPosition, value); }
        }

        public BoardDefinition()
        {
            _boardOutline = new List<Point>();
            _circuitOutline = new List<Point>();
        }

        /// <summary>
        /// Gets the total number of circuit instances
        /// </summary>
        public int TotalCircuits
        {
            get { return CircuitCountX * CircuitCountY; }
        }

        /// <summary>
        /// Calculates the position of a circuit instance by index
        /// </summary>
        public Point GetCircuitPosition(int indexX, int indexY)
        {
            return new Point(
                CircuitStartPosition.X + (indexX * PitchX),
                CircuitStartPosition.Y + (indexY * PitchY)
            );
        }
    }
}
