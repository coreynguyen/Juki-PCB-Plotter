using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using PCBPlotter.Core.Models;

namespace PCBPlotter.Core.Services
{
    /// <summary>
    /// Parser for Gerber RS-274X and RS-274D format files
    /// </summary>
    public class GerberParser
    {
        // Format specification
        private int _integerDigits = 2;
        private int _decimalDigits = 4;
        private bool _leadingZeroOmission = true;
        private bool _absoluteCoordinates = true;
        private Units _units = Units.Millimeters;

        // Current state
        private double _currentX;
        private double _currentY;
        private int _currentAperture = 10;
        private InterpolationMode _interpolation = InterpolationMode.Linear;
        private bool _regionMode;
        private bool _darkPolarity = true;  // true = dark (add), false = clear (subtract)
        private QuadrantMode _quadrantMode = QuadrantMode.Multi;

        // Apertures and macros
        private Dictionary<int, Aperture> _apertures = new Dictionary<int, Aperture>();
        private Dictionary<string, ApertureMacro> _macros = new Dictionary<string, ApertureMacro>();

        // Parsed primitives
        private List<GerberPrimitive> _primitives = new List<GerberPrimitive>();
        private List<Point> _regionPoints = new List<Point>();

        // X2 attributes
        private Dictionary<string, string> _attributes = new Dictionary<string, string>();

        // Random colors for layers
        private static readonly Random _random = new Random();
        private static readonly System.Windows.Media.Color[] _layerColors = new[]
        {
            System.Windows.Media.Color.FromRgb(255, 0, 0),    // Red
            System.Windows.Media.Color.FromRgb(0, 255, 0),    // Green
            System.Windows.Media.Color.FromRgb(0, 0, 255),    // Blue
            System.Windows.Media.Color.FromRgb(255, 255, 0),  // Yellow
            System.Windows.Media.Color.FromRgb(255, 0, 255),  // Magenta
            System.Windows.Media.Color.FromRgb(0, 255, 255),  // Cyan
            System.Windows.Media.Color.FromRgb(255, 128, 0),  // Orange
            System.Windows.Media.Color.FromRgb(128, 0, 255),  // Purple
            System.Windows.Media.Color.FromRgb(0, 128, 255),  // Sky blue
            System.Windows.Media.Color.FromRgb(128, 255, 0),  // Lime
        };

        /// <summary>
        /// Parse a Gerber file and return a GerberLayer
        /// </summary>
        public GerberLayer Parse(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Gerber file not found", filePath);

            // Reset state
            ResetState();

            string content = File.ReadAllText(filePath);
            ParseContent(content);

            // Create layer
            var layer = new GerberLayer(Path.GetFileName(filePath), filePath);
            layer.Primitives = _primitives.ToList();
            layer.LayerType = DetectLayerType(filePath);
            layer.Color = GetLayerColor(layer.LayerType);

            return layer;
        }

        /// <summary>
        /// Parse multiple Gerber files
        /// </summary>
        public List<GerberLayer> ParseMultiple(IEnumerable<string> filePaths)
        {
            var layers = new List<GerberLayer>();
            int colorIndex = 0;

            foreach (var path in filePaths)
            {
                try
                {
                    var layer = Parse(path);
                    // Assign sequential colors
                    layer.Color = _layerColors[colorIndex % _layerColors.Length];
                    colorIndex++;
                    layers.Add(layer);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to parse {path}: {ex.Message}");
                }
            }

            return layers;
        }

        private void ResetState()
        {
            _integerDigits = 2;
            _decimalDigits = 4;
            _leadingZeroOmission = true;
            _absoluteCoordinates = true;
            _units = Units.Millimeters;
            _currentX = 0;
            _currentY = 0;
            _currentAperture = 10;
            _interpolation = InterpolationMode.Linear;
            _regionMode = false;
            _darkPolarity = true;
            _quadrantMode = QuadrantMode.Multi;
            _apertures.Clear();
            _macros.Clear();
            _primitives.Clear();
            _regionPoints.Clear();
            _attributes.Clear();
        }

        private void ParseContent(string content)
        {
            // Remove comments and split into commands
            content = RemoveComments(content);

            // Parse extended commands first (between % markers)
            ParseExtendedCommands(content);

            // Parse data blocks (lines ending with *)
            var dataBlocks = Regex.Split(content, @"\*")
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s) && !s.StartsWith("%"));

            foreach (var block in dataBlocks)
            {
                ParseDataBlock(block);
            }
        }

        private string RemoveComments(string content)
        {
            // Remove G04 comments (G04 ... *)
            content = Regex.Replace(content, @"G04[^*]*\*", "");
            return content;
        }

        private void ParseExtendedCommands(string content)
        {
            // Find all extended commands between % markers
            var extMatch = Regex.Matches(content, @"%([^%]+)%");

            foreach (Match m in extMatch)
            {
                string cmd = m.Groups[1].Value.Trim();

                if (cmd.StartsWith("FS"))
                    ParseFormatSpecification(cmd);
                else if (cmd.StartsWith("MO"))
                    ParseMode(cmd);
                else if (cmd.StartsWith("AD"))
                    ParseApertureDefinition(cmd);
                else if (cmd.StartsWith("AM"))
                    ParseApertureMacro(cmd);
                else if (cmd.StartsWith("LP"))
                    ParseLayerPolarity(cmd);
                else if (cmd.StartsWith("TF"))
                    ParseFileAttribute(cmd);
                else if (cmd.StartsWith("TA"))
                    ParseApertureAttribute(cmd);
                else if (cmd.StartsWith("TD"))
                    DeleteAttribute(cmd);
                else if (cmd.StartsWith("SR"))
                    ParseStepRepeat(cmd);
            }
        }

        private void ParseFormatSpecification(string cmd)
        {
            // %FSLAX24Y24*% - Format Specification
            // L = leading zeros omitted, T = trailing zeros omitted
            // A = absolute coordinates, I = incremental
            // X24 = 2 integer, 4 decimal digits

            if (cmd.Contains("L"))
                _leadingZeroOmission = true;
            else if (cmd.Contains("T"))
                _leadingZeroOmission = false;

            if (cmd.Contains("A"))
                _absoluteCoordinates = true;
            else if (cmd.Contains("I"))
                _absoluteCoordinates = false;

            var match = Regex.Match(cmd, @"X(\d)(\d)");
            if (match.Success)
            {
                _integerDigits = int.Parse(match.Groups[1].Value);
                _decimalDigits = int.Parse(match.Groups[2].Value);
            }
        }

        private void ParseMode(string cmd)
        {
            // %MOMM*% or %MOIN*%
            if (cmd.Contains("MM"))
                _units = Units.Millimeters;
            else if (cmd.Contains("IN"))
                _units = Units.Inches;
        }

        private void ParseApertureDefinition(string cmd)
        {
            // %ADD10C,0.010*% - Circle aperture D10
            // %ADD11R,0.060X0.060*% - Rectangle
            // %ADD12O,0.060X0.090*% - Obround
            // %ADD13P,0.100X6*% - Polygon (outer dia x vertices)

            var match = Regex.Match(cmd, @"ADD(\d+)(\w+),?(.*)");
            if (!match.Success) return;

            int dCode = int.Parse(match.Groups[1].Value);
            string type = match.Groups[2].Value;
            string parameters = match.Groups[3].Value.TrimEnd('*');

            var aperture = new Aperture { DCode = dCode };
            var parts = parameters.Split('X');

            switch (type)
            {
                case "C": // Circle
                    aperture.Type = ApertureType.Circle;
                    if (parts.Length > 0 && !string.IsNullOrEmpty(parts[0]))
                        aperture.Diameter = ParseNumber(parts[0]);
                    if (parts.Length > 1)
                        aperture.HoleDiameter = ParseNumber(parts[1]);
                    break;

                case "R": // Rectangle
                    aperture.Type = ApertureType.Rectangle;
                    if (parts.Length > 0)
                        aperture.Width = ParseNumber(parts[0]);
                    if (parts.Length > 1)
                        aperture.Height = ParseNumber(parts[1]);
                    else
                        aperture.Height = aperture.Width;
                    if (parts.Length > 2)
                        aperture.HoleDiameter = ParseNumber(parts[2]);
                    break;

                case "O": // Obround
                    aperture.Type = ApertureType.Obround;
                    if (parts.Length > 0)
                        aperture.Width = ParseNumber(parts[0]);
                    if (parts.Length > 1)
                        aperture.Height = ParseNumber(parts[1]);
                    else
                        aperture.Height = aperture.Width;
                    if (parts.Length > 2)
                        aperture.HoleDiameter = ParseNumber(parts[2]);
                    break;

                case "P": // Polygon
                    aperture.Type = ApertureType.Polygon;
                    if (parts.Length > 0)
                        aperture.Diameter = ParseNumber(parts[0]);
                    if (parts.Length > 1)
                        aperture.Vertices = (int)ParseNumber(parts[1]);
                    if (parts.Length > 2)
                        aperture.Rotation = ParseNumber(parts[2]);
                    if (parts.Length > 3)
                        aperture.HoleDiameter = ParseNumber(parts[3]);
                    break;

                default:
                    // Check if it's a macro reference
                    if (_macros.ContainsKey(type))
                    {
                        aperture.Type = ApertureType.Macro;
                        aperture.MacroName = type;
                        aperture.MacroParameters = parts.Select(p => ParseNumber(p)).ToArray();
                    }
                    break;
            }

            _apertures[dCode] = aperture;
        }

        private void ParseApertureMacro(string cmd)
        {
            // %AMTHERMAL*
            // 7,0,0,0.060,0.030,0.010,4,45*%
            // We parse basic macro definitions but don't fully execute them

            var lines = cmd.Split('*').Where(s => !string.IsNullOrEmpty(s.Trim())).ToArray();
            if (lines.Length == 0) return;

            var nameMatch = Regex.Match(lines[0], @"AM(\w+)");
            if (!nameMatch.Success) return;

            string name = nameMatch.Groups[1].Value;
            var macro = new ApertureMacro { Name = name };

            for (int i = 1; i < lines.Length; i++)
            {
                macro.Primitives.Add(lines[i].Trim());
            }

            _macros[name] = macro;
        }

        private void ParseLayerPolarity(string cmd)
        {
            // %LPD*% - Dark polarity (add)
            // %LPC*% - Clear polarity (subtract)
            _darkPolarity = cmd.Contains("D");
        }

        private void ParseFileAttribute(string cmd)
        {
            // %TF.FileFunction,Copper,L1,Top*%
            var match = Regex.Match(cmd, @"TF\.(\w+),(.+)");
            if (match.Success)
            {
                string key = match.Groups[1].Value;
                string value = match.Groups[2].Value.TrimEnd('*');
                _attributes[key] = value;
            }
        }

        private void ParseApertureAttribute(string cmd)
        {
            // %TA.AperFunction,ViaDrill*%
            // We store but don't currently use aperture attributes
        }

        private void DeleteAttribute(string cmd)
        {
            // %TD*% or %TD.AttributeName*%
            var match = Regex.Match(cmd, @"TD\.?(\w*)");
            if (match.Success && !string.IsNullOrEmpty(match.Groups[1].Value))
            {
                _attributes.Remove(match.Groups[1].Value);
            }
        }

        private void ParseStepRepeat(string cmd)
        {
            // %SRX3Y2I5.0J4.0*% - Step and repeat
            // We don't fully support this yet
        }

        private void ParseDataBlock(string block)
        {
            if (string.IsNullOrEmpty(block)) return;

            // Handle G-codes
            if (block.StartsWith("G"))
            {
                ParseGCode(block);
            }

            // Handle D-codes
            if (block.Contains("D"))
            {
                ParseDCode(block);
            }
            else if (block.StartsWith("X") || block.StartsWith("Y"))
            {
                // Coordinate without explicit D code - use last D code
                ParseCoordinateMove(block, _regionMode ? 1 : 2); // Region mode continues drawing
            }

            // Handle M-codes
            if (block.StartsWith("M"))
            {
                ParseMCode(block);
            }
        }

        private void ParseGCode(string block)
        {
            var gMatch = Regex.Match(block, @"G(\d+)");
            if (!gMatch.Success) return;

            int gCode = int.Parse(gMatch.Groups[1].Value);

            switch (gCode)
            {
                case 1: // Linear interpolation
                    _interpolation = InterpolationMode.Linear;
                    break;
                case 2: // Clockwise circular interpolation
                    _interpolation = InterpolationMode.ClockwiseArc;
                    break;
                case 3: // Counter-clockwise circular interpolation
                    _interpolation = InterpolationMode.CounterClockwiseArc;
                    break;
                case 36: // Region mode on
                    _regionMode = true;
                    _regionPoints.Clear();
                    _regionPoints.Add(new Point(_currentX, _currentY));
                    break;
                case 37: // Region mode off
                    if (_regionMode && _regionPoints.Count > 2)
                    {
                        CreateContourPrimitive();
                    }
                    _regionMode = false;
                    break;
                case 54: // Select aperture (deprecated)
                    break;
                case 74: // Single quadrant mode
                    _quadrantMode = QuadrantMode.Single;
                    break;
                case 75: // Multi quadrant mode
                    _quadrantMode = QuadrantMode.Multi;
                    break;
            }

            // Check if there are coordinates after the G code
            string remaining = Regex.Replace(block, @"G\d+", "");
            if (!string.IsNullOrEmpty(remaining) && (remaining.Contains("X") || remaining.Contains("Y") || remaining.Contains("D")))
            {
                ParseDCode(remaining);
            }
        }

        private void ParseDCode(string block)
        {
            var dMatch = Regex.Match(block, @"D(\d+)");
            if (!dMatch.Success) return;

            int dCode = int.Parse(dMatch.Groups[1].Value);

            if (dCode >= 10)
            {
                // Select aperture
                _currentAperture = dCode;
            }
            else if (dCode == 1)
            {
                // D01 - Interpolate (draw)
                ParseCoordinateMove(block, 1);
            }
            else if (dCode == 2)
            {
                // D02 - Move
                ParseCoordinateMove(block, 2);
            }
            else if (dCode == 3)
            {
                // D03 - Flash
                ParseCoordinateMove(block, 3);
            }
        }

        private void ParseCoordinateMove(string block, int operation)
        {
            double newX = _currentX;
            double newY = _currentY;
            double i = 0, j = 0;

            // Parse X coordinate
            var xMatch = Regex.Match(block, @"X(-?\d+)");
            if (xMatch.Success)
            {
                newX = ParseCoordinate(xMatch.Groups[1].Value);
                if (!_absoluteCoordinates)
                    newX += _currentX;
            }

            // Parse Y coordinate
            var yMatch = Regex.Match(block, @"Y(-?\d+)");
            if (yMatch.Success)
            {
                newY = ParseCoordinate(yMatch.Groups[1].Value);
                if (!_absoluteCoordinates)
                    newY += _currentY;
            }

            // Parse I,J for arcs (arc center offsets)
            var iMatch = Regex.Match(block, @"I(-?\d+)");
            var jMatch = Regex.Match(block, @"J(-?\d+)");
            if (iMatch.Success)
                i = ParseCoordinate(iMatch.Groups[1].Value);
            if (jMatch.Success)
                j = ParseCoordinate(jMatch.Groups[1].Value);

            switch (operation)
            {
                case 1: // Draw/Interpolate
                    if (_regionMode)
                    {
                        AddRegionPoint(newX, newY, i, j);
                    }
                    else
                    {
                        CreateLinePrimitive(_currentX, _currentY, newX, newY, i, j);
                    }
                    break;

                case 2: // Move (no draw)
                    if (_regionMode)
                    {
                        // Start new contour
                        if (_regionPoints.Count > 2)
                        {
                            CreateContourPrimitive();
                        }
                        _regionPoints.Clear();
                        _regionPoints.Add(new Point(newX, newY));
                    }
                    break;

                case 3: // Flash
                    CreateFlashPrimitive(newX, newY);
                    break;
            }

            _currentX = newX;
            _currentY = newY;
        }

        private void AddRegionPoint(double x, double y, double i, double j)
        {
            if (_interpolation == InterpolationMode.Linear)
            {
                _regionPoints.Add(new Point(x, y));
            }
            else
            {
                // For arcs, add intermediate points
                var arcPoints = CalculateArcPoints(_currentX, _currentY, x, y, i, j);
                _regionPoints.AddRange(arcPoints);
            }
        }

        private void ParseMCode(string block)
        {
            var mMatch = Regex.Match(block, @"M(\d+)");
            if (!mMatch.Success) return;

            int mCode = int.Parse(mMatch.Groups[1].Value);

            switch (mCode)
            {
                case 0: // Program stop
                case 1: // Optional stop
                case 2: // End of program
                    break;
            }
        }

        private double ParseCoordinate(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;

            bool negative = value.StartsWith("-");
            if (negative) value = value.Substring(1);

            // Apply leading/trailing zero handling
            int totalDigits = _integerDigits + _decimalDigits;

            if (_leadingZeroOmission)
            {
                // Leading zeros omitted - pad on the left
                value = value.PadLeft(totalDigits, '0');
            }
            else
            {
                // Trailing zeros omitted - pad on the right
                value = value.PadRight(totalDigits, '0');
            }

            // Split into integer and decimal parts
            string intPart = value.Substring(0, Math.Min(_integerDigits, value.Length));
            string decPart = value.Substring(Math.Min(_integerDigits, value.Length));

            double result = 0;
            if (!string.IsNullOrEmpty(intPart))
                result = double.Parse(intPart, CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(decPart))
                result += double.Parse("0." + decPart, CultureInfo.InvariantCulture);

            if (negative) result = -result;

            // Convert to mm if needed
            if (_units == Units.Inches)
                result *= 25.4;

            return result;
        }

        private double ParseNumber(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double result))
            {
                // Convert to mm if needed
                if (_units == Units.Inches)
                    result *= 25.4;
                return result;
            }
            return 0;
        }

        private void CreateFlashPrimitive(double x, double y)
        {
            if (!_apertures.TryGetValue(_currentAperture, out Aperture aperture))
                return;

            var prim = new GerberPrimitive
            {
                X = x,
                Y = y,
                ApertureIndex = _currentAperture,
                IsDark = _darkPolarity
            };

            switch (aperture.Type)
            {
                case ApertureType.Circle:
                    prim.Type = GerberPrimitiveType.Circle;
                    prim.Width = aperture.Diameter;
                    prim.Height = aperture.Diameter;
                    break;

                case ApertureType.Rectangle:
                    prim.Type = GerberPrimitiveType.Rectangle;
                    prim.Width = aperture.Width;
                    prim.Height = aperture.Height;
                    break;

                case ApertureType.Obround:
                    prim.Type = GerberPrimitiveType.Obround;
                    prim.Width = aperture.Width;
                    prim.Height = aperture.Height;
                    break;

                case ApertureType.Polygon:
                    prim.Type = GerberPrimitiveType.Polygon;
                    prim.Width = aperture.Diameter;
                    prim.Height = aperture.Diameter;
                    prim.Rotation = aperture.Rotation;
                    // Generate polygon vertices
                    {
                        int numVertices = aperture.Vertices > 2 ? aperture.Vertices : 4;
                        double radius = aperture.Diameter / 2.0;
                        double startAngle = aperture.Rotation * Math.PI / 180.0;
                        prim.Points = new List<Point>(numVertices);
                        for (int i = 0; i < numVertices; i++)
                        {
                            double angle = startAngle + (2.0 * Math.PI * i / numVertices);
                            double px = x + radius * Math.Cos(angle);
                            double py = y + radius * Math.Sin(angle);
                            prim.Points.Add(new Point(px, py));
                        }
                    }
                    break;

                case ApertureType.Macro:
                    // For macros, create a placeholder - would need full macro execution
                    prim.Type = GerberPrimitiveType.Flash;
                    prim.Width = aperture.Diameter > 0 ? aperture.Diameter : 0.5;
                    prim.Height = prim.Width;
                    break;

                default:
                    prim.Type = GerberPrimitiveType.Flash;
                    prim.Width = 0.1;
                    prim.Height = 0.1;
                    break;
            }

            _primitives.Add(prim);
        }

        private void CreateLinePrimitive(double x1, double y1, double x2, double y2, double i, double j)
        {
            if (!_apertures.TryGetValue(_currentAperture, out Aperture aperture))
                return;

            // Get line width from aperture
            double width = aperture.Type == ApertureType.Circle ? aperture.Diameter :
                          Math.Min(aperture.Width, aperture.Height);
            if (width <= 0) width = 0.1;

            if (_interpolation == InterpolationMode.Linear)
            {
                var prim = new GerberPrimitive
                {
                    Type = GerberPrimitiveType.Line,
                    Width = width,
                    ApertureIndex = _currentAperture,
                    IsDark = _darkPolarity,
                    Points = new List<Point> { new Point(x1, y1), new Point(x2, y2) }
                };
                prim.X = (x1 + x2) / 2;
                prim.Y = (y1 + y2) / 2;
                _primitives.Add(prim);
            }
            else
            {
                // Arc
                var arcPoints = CalculateArcPoints(x1, y1, x2, y2, i, j);
                if (arcPoints.Count > 0)
                {
                    var prim = new GerberPrimitive
                    {
                        Type = GerberPrimitiveType.Arc,
                        Width = width,
                        ApertureIndex = _currentAperture,
                        IsDark = _darkPolarity,
                        Points = arcPoints
                    };

                    // Calculate center for bounds
                    double sumX = 0, sumY = 0;
                    foreach (var pt in arcPoints)
                    {
                        sumX += pt.X;
                        sumY += pt.Y;
                    }
                    prim.X = sumX / arcPoints.Count;
                    prim.Y = sumY / arcPoints.Count;

                    _primitives.Add(prim);
                }
            }
        }

        private List<Point> CalculateArcPoints(double x1, double y1, double x2, double y2, double i, double j)
        {
            var points = new List<Point>();

            // Arc center
            double cx = x1 + i;
            double cy = y1 + j;

            // Radius
            double radius = Math.Sqrt(i * i + j * j);
            if (radius < 0.0001) return points;

            // Start and end angles
            double startAngle = Math.Atan2(y1 - cy, x1 - cx);
            double endAngle = Math.Atan2(y2 - cy, x2 - cx);

            // Determine sweep
            bool clockwise = _interpolation == InterpolationMode.ClockwiseArc;
            double sweep;

            if (clockwise)
            {
                sweep = startAngle - endAngle;
                if (sweep <= 0) sweep += 2 * Math.PI;
            }
            else
            {
                sweep = endAngle - startAngle;
                if (sweep <= 0) sweep += 2 * Math.PI;
            }

            // Calculate number of segments based on arc length
            int segments = Math.Max(8, (int)(sweep * radius / 0.1));

            for (int s = 0; s <= segments; s++)
            {
                double t = (double)s / segments;
                double angle;

                if (clockwise)
                    angle = startAngle - t * sweep;
                else
                    angle = startAngle + t * sweep;

                double px = cx + radius * Math.Cos(angle);
                double py = cy + radius * Math.Sin(angle);
                points.Add(new Point(px, py));
            }

            return points;
        }

        private void CreateContourPrimitive()
        {
            if (_regionPoints.Count < 3) return;

            var prim = new GerberPrimitive
            {
                Type = GerberPrimitiveType.Contour,
                Points = _regionPoints.ToList(),
                Width = 0,
                IsDark = _darkPolarity
            };

            // Calculate center
            double sumX = 0, sumY = 0;
            foreach (var pt in _regionPoints)
            {
                sumX += pt.X;
                sumY += pt.Y;
            }
            prim.X = sumX / _regionPoints.Count;
            prim.Y = sumY / _regionPoints.Count;

            _primitives.Add(prim);
        }

        private GerberLayerType DetectLayerType(string filePath)
        {
            string fileName = Path.GetFileName(filePath).ToLowerInvariant();
            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            // Check X2 attributes first
            if (_attributes.TryGetValue("FileFunction", out string fileFunction))
            {
                string func = fileFunction.ToLowerInvariant();
                if (func.Contains("copper"))
                {
                    if (func.Contains("top") || func.Contains("l1"))
                        return GerberLayerType.TopCopper;
                    if (func.Contains("bot") || func.Contains("bottom"))
                        return GerberLayerType.BottomCopper;
                }
                if (func.Contains("soldermask"))
                {
                    if (func.Contains("top"))
                        return GerberLayerType.TopSoldermask;
                    if (func.Contains("bot"))
                        return GerberLayerType.BottomSoldermask;
                }
                if (func.Contains("silkscreen") || func.Contains("legend"))
                {
                    if (func.Contains("top"))
                        return GerberLayerType.TopSilkscreen;
                    if (func.Contains("bot"))
                        return GerberLayerType.BottomSilkscreen;
                }
                if (func.Contains("paste"))
                {
                    if (func.Contains("top"))
                        return GerberLayerType.TopPaste;
                    if (func.Contains("bot"))
                        return GerberLayerType.BottomPaste;
                }
                if (func.Contains("profile") || func.Contains("outline"))
                    return GerberLayerType.Outline;
            }

            // Detect from file extension
            switch (ext)
            {
                case ".gtl":
                    return GerberLayerType.TopCopper;
                case ".gbl":
                    return GerberLayerType.BottomCopper;
                case ".gto":
                    return GerberLayerType.TopSilkscreen;
                case ".gbo":
                    return GerberLayerType.BottomSilkscreen;
                case ".gts":
                    return GerberLayerType.TopSoldermask;
                case ".gbs":
                    return GerberLayerType.BottomSoldermask;
                case ".gtp":
                    return GerberLayerType.TopPaste;
                case ".gbp":
                    return GerberLayerType.BottomPaste;
                case ".gko":
                case ".gm1":
                    return GerberLayerType.Outline;
            }

            // Detect from filename patterns
            if (fileName.Contains("top") && (fileName.Contains("copper") || fileName.Contains("layer")))
                return GerberLayerType.TopCopper;
            if (fileName.Contains("bottom") && (fileName.Contains("copper") || fileName.Contains("layer")))
                return GerberLayerType.BottomCopper;
            if (fileName.Contains("silk"))
            {
                if (fileName.Contains("top"))
                    return GerberLayerType.TopSilkscreen;
                if (fileName.Contains("bot"))
                    return GerberLayerType.BottomSilkscreen;
            }
            if (fileName.Contains("mask"))
            {
                if (fileName.Contains("top"))
                    return GerberLayerType.TopSoldermask;
                if (fileName.Contains("bot"))
                    return GerberLayerType.BottomSoldermask;
            }
            if (fileName.Contains("paste"))
            {
                if (fileName.Contains("top"))
                    return GerberLayerType.TopPaste;
                if (fileName.Contains("bot"))
                    return GerberLayerType.BottomPaste;
            }
            if (fileName.Contains("outline") || fileName.Contains("profile") || fileName.Contains("edge"))
                return GerberLayerType.Outline;

            return GerberLayerType.Unknown;
        }

        private System.Windows.Media.Color GetLayerColor(GerberLayerType type)
        {
            switch (type)
            {
                case GerberLayerType.TopCopper:
                    return System.Windows.Media.Color.FromRgb(255, 0, 0);      // Red
                case GerberLayerType.BottomCopper:
                    return System.Windows.Media.Color.FromRgb(0, 0, 255);      // Blue
                case GerberLayerType.TopSilkscreen:
                    return System.Windows.Media.Color.FromRgb(255, 255, 0);    // Yellow
                case GerberLayerType.BottomSilkscreen:
                    return System.Windows.Media.Color.FromRgb(255, 255, 128);  // Light yellow
                case GerberLayerType.TopSoldermask:
                    return System.Windows.Media.Color.FromRgb(0, 128, 0);      // Dark green
                case GerberLayerType.BottomSoldermask:
                    return System.Windows.Media.Color.FromRgb(0, 192, 0);      // Green
                case GerberLayerType.TopPaste:
                    return System.Windows.Media.Color.FromRgb(128, 128, 128);  // Gray
                case GerberLayerType.BottomPaste:
                    return System.Windows.Media.Color.FromRgb(192, 192, 192);  // Light gray
                case GerberLayerType.Outline:
                    return System.Windows.Media.Color.FromRgb(255, 255, 255);  // White
                case GerberLayerType.Drill:
                    return System.Windows.Media.Color.FromRgb(255, 128, 0);    // Orange
                default:
                    return _layerColors[_random.Next(_layerColors.Length)];
            }
        }

        #region Helper Classes

        private enum InterpolationMode
        {
            Linear,
            ClockwiseArc,
            CounterClockwiseArc
        }

        private enum QuadrantMode
        {
            Single,
            Multi
        }

        private enum ApertureType
        {
            Circle,
            Rectangle,
            Obround,
            Polygon,
            Macro
        }

        private class Aperture
        {
            public int DCode { get; set; }
            public ApertureType Type { get; set; }
            public double Diameter { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public double HoleDiameter { get; set; }
            public int Vertices { get; set; } = 4;
            public double Rotation { get; set; }
            public string MacroName { get; set; }
            public double[] MacroParameters { get; set; }
        }

        private class ApertureMacro
        {
            public string Name { get; set; }
            public List<string> Primitives { get; set; } = new List<string>();
        }

        #endregion
    }
}
