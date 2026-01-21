using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;

namespace PCBPlotter.Core.Models
{
    /// <summary>
    /// Represents a component package/footprint with graphics and pin information
    /// </summary>
    public class Package : ModelBase
    {
        private string _id;
        private string _name;
        private string _description;
        private Point _origin;
        private double _width;
        private double _length;
        private double _height;
        private PartClass _partClass = PartClass.Chip;
        private bool _hasPolarity;
        private double _defaultRotation;
        private ObservableCollection<PackageGraphic> _graphics;
        private ObservableCollection<Pin> _pins;

        /// <summary>
        /// Unique identifier
        /// </summary>
        public string Id
        {
            get { return _id; }
            set { SetProperty(ref _id, value); }
        }

        /// <summary>
        /// Package name (e.g., "0805", "SOT23", "LQFP48")
        /// </summary>
        public string Name
        {
            get { return _name; }
            set { SetProperty(ref _name, value); }
        }

        /// <summary>
        /// Optional description
        /// </summary>
        public string Description
        {
            get { return _description; }
            set { SetProperty(ref _description, value); }
        }

        /// <summary>
        /// Origin point (center) relative to graphics
        /// </summary>
        public Point Origin
        {
            get { return _origin; }
            set { SetProperty(ref _origin, value); }
        }

        /// <summary>
        /// Package width in mm (X dimension)
        /// </summary>
        public double Width
        {
            get { return _width; }
            set { SetProperty(ref _width, value); }
        }

        /// <summary>
        /// Package length in mm (Y dimension)
        /// </summary>
        public double Length
        {
            get { return _length; }
            set { SetProperty(ref _length, value); }
        }

        /// <summary>
        /// Package height in mm (Z dimension)
        /// </summary>
        public double Height
        {
            get { return _height; }
            set { SetProperty(ref _height, value); }
        }

        /// <summary>
        /// Part classification for machine algorithms
        /// </summary>
        public PartClass PartClass
        {
            get { return _partClass; }
            set { SetProperty(ref _partClass, value); }
        }

        /// <summary>
        /// Whether this package has polarity (show pin 1 indicator)
        /// </summary>
        public bool HasPolarity
        {
            get { return _hasPolarity; }
            set { SetProperty(ref _hasPolarity, value); }
        }

        /// <summary>
        /// Default rotation offset (machine feed orientation correction)
        /// </summary>
        public double DefaultRotation
        {
            get { return _defaultRotation; }
            set { SetProperty(ref _defaultRotation, value); }
        }

        /// <summary>
        /// Visual graphics for rendering
        /// </summary>
        public ObservableCollection<PackageGraphic> Graphics
        {
            get { return _graphics; }
            set { SetProperty(ref _graphics, value); }
        }

        /// <summary>
        /// Pin definitions
        /// </summary>
        public ObservableCollection<Pin> Pins
        {
            get { return _pins; }
            set { SetProperty(ref _pins, value); }
        }

        /// <summary>
        /// Gets the bounding box of all graphics
        /// </summary>
        public Rect Bounds
        {
            get
            {
                if (_graphics == null || _graphics.Count == 0)
                    return new Rect(-0.5, -0.5, 1, 1);

                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;

                foreach (var graphic in _graphics)
                {
                    var bounds = graphic.GetBounds();
                    if (bounds.Left < minX) minX = bounds.Left;
                    if (bounds.Top < minY) minY = bounds.Top;
                    if (bounds.Right > maxX) maxX = bounds.Right;
                    if (bounds.Bottom > maxY) maxY = bounds.Bottom;
                }

                return new Rect(minX, minY, maxX - minX, maxY - minY);
            }
        }

        public Package()
        {
            _id = Guid.NewGuid().ToString();
            _graphics = new ObservableCollection<PackageGraphic>();
            _pins = new ObservableCollection<Pin>();
        }

        public Package(string name) : this()
        {
            _name = name;
        }

        /// <summary>
        /// Creates a simple rectangular chip package
        /// </summary>
        public static Package CreateChipPackage(string name, double width, double length, double height = 0.5)
        {
            var package = new Package(name)
            {
                Width = width,
                Length = length,
                Height = height,
                PartClass = PartClass.Chip,
                HasPolarity = false
            };

            // Add body rectangle
            package.Graphics.Add(new PackageGraphic
            {
                ShapeType = GraphicShapeType.Rectangle,
                X = -width / 2,
                Y = -length / 2,
                Width = width,
                Height = length,
                IsFilled = true
            });

            // Add pads (simple 2-terminal chip)
            double padWidth = width * 0.3;
            package.Graphics.Add(new PackageGraphic
            {
                ShapeType = GraphicShapeType.Rectangle,
                X = -width / 2,
                Y = -length / 2,
                Width = padWidth,
                Height = length,
                IsPad = true
            });
            package.Graphics.Add(new PackageGraphic
            {
                ShapeType = GraphicShapeType.Rectangle,
                X = width / 2 - padWidth,
                Y = -length / 2,
                Width = padWidth,
                Height = length,
                IsPad = true
            });

            // Add pins
            package.Pins.Add(new Pin { Number = 1, X = -width / 2 + padWidth / 2, Y = 0 });
            package.Pins.Add(new Pin { Number = 2, X = width / 2 - padWidth / 2, Y = 0 });

            return package;
        }

        /// <summary>
        /// Generates a timestamped package name
        /// </summary>
        public static string GeneratePackageName()
        {
            return "PACKAGE" + DateTime.Now.ToString("yyMMddHHmmss");
        }

        public override string ToString()
        {
            return Name ?? "(unnamed package)";
        }
    }
}
