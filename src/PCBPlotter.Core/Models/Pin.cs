using System;

namespace PCBPlotter.Core.Models
{
    /// <summary>
    /// Represents a pin on a package
    /// </summary>
    public class Pin : ModelBase
    {
        private int _number;
        private string _name;
        private double _x;
        private double _y;
        private double _width = 0.3;
        private double _height = 0.3;
        private PinShape _shape = PinShape.Rectangle;

        /// <summary>
        /// Pin number (1 = pin 1 for polarity)
        /// </summary>
        public int Number
        {
            get { return _number; }
            set { SetProperty(ref _number, value); }
        }

        /// <summary>
        /// Optional pin name (e.g., "VCC", "GND", "A1")
        /// </summary>
        public string Name
        {
            get { return _name; }
            set { SetProperty(ref _name, value); }
        }

        /// <summary>
        /// X position relative to package origin
        /// </summary>
        public double X
        {
            get { return _x; }
            set { SetProperty(ref _x, value); }
        }

        /// <summary>
        /// Y position relative to package origin
        /// </summary>
        public double Y
        {
            get { return _y; }
            set { SetProperty(ref _y, value); }
        }

        /// <summary>
        /// Pin pad width
        /// </summary>
        public double Width
        {
            get { return _width; }
            set { SetProperty(ref _width, value); }
        }

        /// <summary>
        /// Pin pad height
        /// </summary>
        public double Height
        {
            get { return _height; }
            set { SetProperty(ref _height, value); }
        }

        /// <summary>
        /// Pin pad shape
        /// </summary>
        public PinShape Shape
        {
            get { return _shape; }
            set { SetProperty(ref _shape, value); }
        }

        /// <summary>
        /// Whether this is pin 1 (for polarity indicator)
        /// </summary>
        public bool IsPin1
        {
            get { return _number == 1; }
        }

        public override string ToString()
        {
            if (!string.IsNullOrEmpty(Name))
                return string.Format("Pin {0} ({1})", Number, Name);
            return string.Format("Pin {0}", Number);
        }
    }

    public enum PinShape
    {
        Rectangle,
        Circle,
        Oval,
        RoundedRectangle
    }
}
