// =============================================================================
// GenCadParser.cs - GenCAD 1.4 File Parser for SMT/CAM/CIS Data Extraction
// =============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PCBPlotter.Core.Services
{
    // =========================================================================
    // Data Model Classes
    // =========================================================================
    public class GenCadData
    {
        public GenCadHeader Header = new GenCadHeader();
        public GenCadBoardOutline Board = new GenCadBoardOutline();
        public Dictionary<string, GenCadPadDef> Pads = new Dictionary<string, GenCadPadDef>();
        public Dictionary<string, GenCadPadstack> Padstacks = new Dictionary<string, GenCadPadstack>();
        public Dictionary<string, GenCadShapeDef> Shapes = new Dictionary<string, GenCadShapeDef>();
        public Dictionary<string, GenCadDeviceDef> Devices = new Dictionary<string, GenCadDeviceDef>();
        public List<GenCadComponent> Components = new List<GenCadComponent>();
        public List<GenCadFiducial> Fiducials = new List<GenCadFiducial>();

        public double ToMmFactor
        {
            get
            {
                switch (Header.Units)
                {
                    case GenCadUnits.THOU: return 0.0254;
                    case GenCadUnits.INCH: return 25.4;
                    case GenCadUnits.MM: return 1.0;
                    default: return 0.0254;
                }
            }
        }

        public double ToMm(double value) { return value * ToMmFactor; }
    }

    public enum GenCadUnits { THOU, INCH, MM, UNKNOWN }
    public enum GenCadInsertType { SMD, TH, UNKNOWN }
    public enum GenCadPadShape { RECTANGULAR, ROUND, UNKNOWN }

    public class GenCadHeader
    {
        public string Version = "";
        public string Drawing = "";
        public string Revision = "";
        public string User = "";
        public GenCadUnits Units = GenCadUnits.UNKNOWN;
        public double OriginX;
        public double OriginY;
    }

    public class GenCadLineSegment
    {
        public double X1, Y1, X2, Y2;
        public GenCadLineSegment() { }
        public GenCadLineSegment(double x1, double y1, double x2, double y2)
        {
            X1 = x1; Y1 = y1; X2 = x2; Y2 = y2;
        }
    }

    public class GenCadCircleDef
    {
        public double CenterX, CenterY, Radius;
    }

    public class GenCadRectangleDef
    {
        public double X, Y, Width, Height;
    }

    public class GenCadBoardOutline
    {
        public List<GenCadLineSegment> Lines = new List<GenCadLineSegment>();
        public List<GenCadBoardArtwork> Artworks = new List<GenCadBoardArtwork>();
    }

    public class GenCadBoardArtwork
    {
        public string Name = "";
        public string Layer = "";
        public bool Filled;
        public List<GenCadLineSegment> Lines = new List<GenCadLineSegment>();
    }

    public class GenCadPadDef
    {
        public string Name = "";
        public GenCadPadShape Shape = GenCadPadShape.UNKNOWN;
        public GenCadRectangleDef Rectangle;
        public GenCadCircleDef Circle;

        public double Width
        {
            get
            {
                if (Shape == GenCadPadShape.RECTANGULAR && Rectangle != null)
                    return Rectangle.Width;
                if (Shape == GenCadPadShape.ROUND && Circle != null)
                    return Circle.Radius * 2.0;
                return 0;
            }
        }

        public double Height
        {
            get
            {
                if (Shape == GenCadPadShape.RECTANGULAR && Rectangle != null)
                    return Rectangle.Height;
                if (Shape == GenCadPadShape.ROUND && Circle != null)
                    return Circle.Radius * 2.0;
                return 0;
            }
        }
    }

    public class GenCadPadstackPadRef
    {
        public string PadName = "";
        public string Layer = "";
        public double OffsetX, OffsetY;
        public int Rotation;
    }

    public class GenCadPadstack
    {
        public string Name = "";
        public double DrillSize;
        public List<GenCadPadstackPadRef> Pads = new List<GenCadPadstackPadRef>();
    }

    public class GenCadShapePin
    {
        public string PinNumber = "";
        public string PadstackName = "";
        public double X, Y;
        public string Layer = "";
        public double Rotation;
    }

    public class GenCadShapeDef
    {
        public string Name = "";
        public GenCadInsertType Insert = GenCadInsertType.UNKNOWN;
        public List<GenCadLineSegment> Lines = new List<GenCadLineSegment>();
        public List<GenCadCircleDef> Circles = new List<GenCadCircleDef>();
        public List<GenCadRectangleDef> Rectangles = new List<GenCadRectangleDef>();
        public List<GenCadShapePin> Pins = new List<GenCadShapePin>();

        public void ComputeBounds(out double minX, out double minY,
                                   out double maxX, out double maxY)
        {
            minX = double.MaxValue; minY = double.MaxValue;
            maxX = double.MinValue; maxY = double.MinValue;

            foreach (var l in Lines)
            {
                if (l.X1 < minX) minX = l.X1;
                if (l.Y1 < minY) minY = l.Y1;
                if (l.X2 < minX) minX = l.X2;
                if (l.Y2 < minY) minY = l.Y2;
                if (l.X1 > maxX) maxX = l.X1;
                if (l.Y1 > maxY) maxY = l.Y1;
                if (l.X2 > maxX) maxX = l.X2;
                if (l.Y2 > maxY) maxY = l.Y2;
            }

            foreach (var c in Circles)
            {
                if (c.CenterX - c.Radius < minX) minX = c.CenterX - c.Radius;
                if (c.CenterY - c.Radius < minY) minY = c.CenterY - c.Radius;
                if (c.CenterX + c.Radius > maxX) maxX = c.CenterX + c.Radius;
                if (c.CenterY + c.Radius > maxY) maxY = c.CenterY + c.Radius;
            }

            if (minX == double.MaxValue)
            {
                minX = 0; minY = 0; maxX = 0; maxY = 0;
            }
        }
    }

    public class GenCadDeviceDef
    {
        public string Name = "";
        public string PartNumber = "";
        public string Type = "";
        public int PinCount;
        public string Description = "";
    }

    public class GenCadComponent
    {
        public string RefDes = "";
        public string DeviceName = "";
        public double PlaceX, PlaceY;
        public double Rotation;
        public string Layer = "";
        public string ShapeName = "";
        public bool MirrorY;
        public bool Flip;
        public List<string> Attributes = new List<string>();
        public GenCadDeviceDef Device;
        public GenCadShapeDef Shape;
    }

    public class GenCadFiducial
    {
        public string Name = "";
        public double X, Y;
        public double Rotation;
        public string Layer = "";
        public string ShapeName = "";
    }

    // =========================================================================
    // Parser
    // =========================================================================
    public class GenCadParser
    {
        private string[] _lines;
        private int _pos;
        private GenCadData _data;

        public GenCadData ParsedData { get; private set; }

        /// <summary>
        /// Checks if a file appears to be a GenCAD file
        /// </summary>
        public static bool IsGenCadFormat(string filePath)
        {
            try
            {
                using (var sr = new StreamReader(filePath, Encoding.ASCII))
                {
                    for (int i = 0; i < 20; i++)
                    {
                        string line = sr.ReadLine();
                        if (line == null) break;
                        line = line.Trim();
                        if (line == "$HEADER" || line.StartsWith("GENCAD"))
                            return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public void Parse(string filePath)
        {
            string rawText = File.ReadAllText(filePath, Encoding.ASCII);
            rawText = rawText.Replace("\r\n", "\n").Replace("\r", "\n");
            _lines = rawText.Split(new char[] { '\n' });
            _pos = 0;
            _data = new GenCadData();

            while (_pos < _lines.Length)
            {
                string line = _lines[_pos].Trim();
                if (line == "$HEADER") ParseHeader();
                else if (line == "$BOARD") ParseBoard();
                else if (line == "$PADS") ParsePads();
                else if (line == "$PADSTACKS") ParsePadstacks();
                else if (line == "$SHAPES") ParseShapes();
                else if (line == "$COMPONENTS") ParseComponents();
                else if (line == "$DEVICES") ParseDevices();
                else _pos++;
            }

            ResolveReferences();
            ExtractFiducials();
            ParsedData = _data;
        }

        private string CurrentLine
        {
            get { return (_pos < _lines.Length) ? _lines[_pos].Trim() : ""; }
        }

        private void Advance() { _pos++; }

        private static string[] SplitTokens(string line)
        {
            var tokens = new List<string>();
            bool inQuote = false;
            var sb = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    inQuote = !inQuote;
                }
                else if ((c == ' ' || c == '\t') && !inQuote)
                {
                    if (sb.Length > 0)
                    {
                        tokens.Add(sb.ToString());
                        sb.Length = 0;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            if (sb.Length > 0) tokens.Add(sb.ToString());
            return tokens.ToArray();
        }

        private static double ParseDouble(string s)
        {
            double val;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out val))
                return val;
            return 0;
        }

        private static int ParseInt(string s)
        {
            int val;
            if (int.TryParse(s, out val)) return val;
            return 0;
        }

        private void ParseHeader()
        {
            Advance();
            while (_pos < _lines.Length)
            {
                string line = CurrentLine;
                if (line == "$ENDHEADER") { Advance(); return; }
                string[] tok = SplitTokens(line);
                if (tok.Length == 0) { Advance(); continue; }

                switch (tok[0])
                {
                    case "GENCAD":
                        if (tok.Length > 1) _data.Header.Version = tok[1];
                        break;
                    case "DRAWING":
                        if (tok.Length > 1) _data.Header.Drawing = tok[1];
                        break;
                    case "REVISION":
                        if (tok.Length > 1) _data.Header.Revision = tok[1];
                        break;
                    case "USER":
                        if (tok.Length > 1) _data.Header.User = tok[1];
                        break;
                    case "UNITS":
                        if (tok.Length > 1)
                        {
                            string u = tok[1].ToUpper();
                            if (u == "THOU" || u == "MIL" || u == "MILS")
                                _data.Header.Units = GenCadUnits.THOU;
                            else if (u == "INCH" || u == "IN")
                                _data.Header.Units = GenCadUnits.INCH;
                            else if (u == "MM" || u == "METRIC")
                                _data.Header.Units = GenCadUnits.MM;
                        }
                        break;
                    case "ORIGIN":
                        if (tok.Length > 2)
                        {
                            _data.Header.OriginX = ParseDouble(tok[1]);
                            _data.Header.OriginY = ParseDouble(tok[2]);
                        }
                        break;
                }
                Advance();
            }
        }

        private void ParseBoard()
        {
            Advance();
            GenCadBoardArtwork currentArtwork = null;

            while (_pos < _lines.Length)
            {
                string line = CurrentLine;
                if (line == "$ENDBOARD") { Advance(); return; }
                string[] tok = SplitTokens(line);
                if (tok.Length == 0) { Advance(); continue; }

                if (tok[0] == "ARTWORK")
                {
                    currentArtwork = new GenCadBoardArtwork();
                    if (tok.Length > 1) currentArtwork.Name = tok[1];
                    if (tok.Length > 2) currentArtwork.Layer = tok[2];
                    _data.Board.Artworks.Add(currentArtwork);
                }
                else if (tok[0] == "FILLED")
                {
                    if (currentArtwork != null && tok.Length > 1)
                        currentArtwork.Filled = (tok[1] != "0");
                }
                else if (tok[0] == "LINE" && tok.Length >= 5)
                {
                    var seg = new GenCadLineSegment(
                        ParseDouble(tok[1]), ParseDouble(tok[2]),
                        ParseDouble(tok[3]), ParseDouble(tok[4]));
                    if (currentArtwork != null)
                        currentArtwork.Lines.Add(seg);
                    else
                        _data.Board.Lines.Add(seg);
                }
                Advance();
            }
        }

        private void ParsePads()
        {
            Advance();
            GenCadPadDef currentPad = null;

            while (_pos < _lines.Length)
            {
                string line = CurrentLine;
                if (line == "$ENDPADS") { Advance(); return; }
                string[] tok = SplitTokens(line);
                if (tok.Length == 0) { Advance(); continue; }

                if (tok[0] == "PAD" && tok.Length >= 3)
                {
                    currentPad = new GenCadPadDef();
                    currentPad.Name = tok[1];
                    string shapeStr = tok[2].ToUpper();
                    if (shapeStr == "RECTANGULAR") currentPad.Shape = GenCadPadShape.RECTANGULAR;
                    else if (shapeStr == "ROUND") currentPad.Shape = GenCadPadShape.ROUND;
                    _data.Pads[currentPad.Name] = currentPad;
                }
                else if (tok[0] == "RECTANGLE" && currentPad != null && tok.Length >= 5)
                {
                    currentPad.Rectangle = new GenCadRectangleDef
                    {
                        X = ParseDouble(tok[1]),
                        Y = ParseDouble(tok[2]),
                        Width = ParseDouble(tok[3]),
                        Height = ParseDouble(tok[4])
                    };
                }
                else if (tok[0] == "CIRCLE" && currentPad != null && tok.Length >= 4)
                {
                    currentPad.Circle = new GenCadCircleDef
                    {
                        CenterX = ParseDouble(tok[1]),
                        CenterY = ParseDouble(tok[2]),
                        Radius = ParseDouble(tok[3])
                    };
                }
                Advance();
            }
        }

        private void ParsePadstacks()
        {
            Advance();
            GenCadPadstack currentStack = null;

            while (_pos < _lines.Length)
            {
                string line = CurrentLine;
                if (line == "$ENDPADSTACKS") { Advance(); return; }
                string[] tok = SplitTokens(line);
                if (tok.Length == 0) { Advance(); continue; }

                if (tok[0] == "PADSTACK" && tok.Length >= 2)
                {
                    currentStack = new GenCadPadstack();
                    currentStack.Name = tok[1];
                    if (tok.Length > 2) currentStack.DrillSize = ParseDouble(tok[2]);
                    _data.Padstacks[currentStack.Name] = currentStack;
                }
                else if (tok[0] == "PAD" && currentStack != null && tok.Length >= 5)
                {
                    var pr = new GenCadPadstackPadRef
                    {
                        PadName = tok[1],
                        Layer = tok[2],
                        OffsetX = ParseDouble(tok[3]),
                        Rotation = ParseInt(tok[4])
                    };
                    currentStack.Pads.Add(pr);
                }
                Advance();
            }
        }

        private void ParseShapes()
        {
            Advance();
            GenCadShapeDef currentShape = null;

            while (_pos < _lines.Length)
            {
                string line = CurrentLine;
                if (line == "$ENDSHAPES") { Advance(); return; }
                string[] tok = SplitTokens(line);
                if (tok.Length == 0) { Advance(); continue; }

                if (tok[0] == "SHAPE")
                {
                    currentShape = new GenCadShapeDef();
                    if (tok.Length > 1) currentShape.Name = tok[1];
                    _data.Shapes[currentShape.Name] = currentShape;
                }
                else if (tok[0] == "INSERT" && currentShape != null)
                {
                    if (tok.Length > 1)
                    {
                        string ins = tok[1].ToUpper();
                        if (ins == "SMD") currentShape.Insert = GenCadInsertType.SMD;
                        else if (ins == "TH") currentShape.Insert = GenCadInsertType.TH;
                    }
                }
                else if (tok[0] == "LINE" && currentShape != null && tok.Length >= 5)
                {
                    currentShape.Lines.Add(new GenCadLineSegment(
                        ParseDouble(tok[1]), ParseDouble(tok[2]),
                        ParseDouble(tok[3]), ParseDouble(tok[4])));
                }
                else if (tok[0] == "CIRCLE" && currentShape != null && tok.Length >= 4)
                {
                    currentShape.Circles.Add(new GenCadCircleDef
                    {
                        CenterX = ParseDouble(tok[1]),
                        CenterY = ParseDouble(tok[2]),
                        Radius = ParseDouble(tok[3])
                    });
                }
                else if (tok[0] == "RECTANGLE" && currentShape != null && tok.Length >= 5)
                {
                    currentShape.Rectangles.Add(new GenCadRectangleDef
                    {
                        X = ParseDouble(tok[1]),
                        Y = ParseDouble(tok[2]),
                        Width = ParseDouble(tok[3]),
                        Height = ParseDouble(tok[4])
                    });
                }
                else if (tok[0] == "PIN" && currentShape != null && tok.Length >= 6)
                {
                    var pin = new GenCadShapePin
                    {
                        PinNumber = tok[1],
                        PadstackName = tok[2],
                        X = ParseDouble(tok[3]),
                        Y = ParseDouble(tok[4]),
                        Layer = tok[5]
                    };
                    if (tok.Length > 6) pin.Rotation = ParseDouble(tok[6]);
                    currentShape.Pins.Add(pin);
                }
                Advance();
            }
        }

        private void ParseComponents()
        {
            Advance();
            GenCadComponent current = null;

            while (_pos < _lines.Length)
            {
                string line = CurrentLine;
                if (line == "$ENDCOMPONENTS") { Advance(); return; }
                string[] tok = SplitTokens(line);
                if (tok.Length == 0) { Advance(); continue; }

                if (tok[0] == "COMPONENT" && tok.Length >= 2)
                {
                    current = new GenCadComponent();
                    current.RefDes = tok[1];
                    _data.Components.Add(current);
                }
                else if (tok[0] == "DEVICE" && current != null && tok.Length >= 2)
                {
                    current.DeviceName = tok[1];
                }
                else if (tok[0] == "PLACE" && current != null && tok.Length >= 3)
                {
                    current.PlaceX = ParseDouble(tok[1]);
                    current.PlaceY = ParseDouble(tok[2]);
                }
                else if (tok[0] == "ROTATION" && current != null && tok.Length >= 2)
                {
                    current.Rotation = ParseDouble(tok[1]);
                    if (current.Rotation >= 360.0) current.Rotation -= 360.0;
                }
                else if (tok[0] == "LAYER" && current != null && tok.Length >= 2)
                {
                    current.Layer = tok[1].ToUpper();
                }
                else if (tok[0] == "SHAPE" && current != null && tok.Length >= 2)
                {
                    current.ShapeName = tok[1];
                    for (int i = 2; i < tok.Length; i++)
                    {
                        if (tok[i].ToUpper() == "MIRRORY") current.MirrorY = true;
                        if (tok[i].ToUpper() == "FLIP") current.Flip = true;
                    }
                }
                else if (tok[0] == "ATTRIBUTE" && current != null)
                {
                    current.Attributes.Add(line);
                }
                Advance();
            }
        }

        private void ParseDevices()
        {
            Advance();
            GenCadDeviceDef current = null;

            while (_pos < _lines.Length)
            {
                string line = CurrentLine;
                if (line == "$ENDDEVICES") { Advance(); return; }
                string[] tok = SplitTokens(line);
                if (tok.Length == 0) { Advance(); continue; }

                if (tok[0] == "DEVICE" && tok.Length >= 2)
                {
                    current = new GenCadDeviceDef();
                    current.Name = tok[1];
                    _data.Devices[current.Name] = current;
                }
                else if (tok[0] == "PART" && current != null && tok.Length >= 2)
                {
                    current.PartNumber = tok[1];
                }
                else if (tok[0] == "TYPE" && current != null && tok.Length >= 2)
                {
                    current.Type = tok[1];
                }
                else if (tok[0] == "PINCOUNT" && current != null && tok.Length >= 2)
                {
                    current.PinCount = ParseInt(tok[1]);
                }
                else if (tok[0] == "DESC" && current != null && tok.Length >= 2)
                {
                    current.Description = tok[1];
                }
                Advance();
            }
        }

        private void ResolveReferences()
        {
            foreach (var comp in _data.Components)
            {
                if (_data.Devices.ContainsKey(comp.DeviceName))
                    comp.Device = _data.Devices[comp.DeviceName];
                if (_data.Shapes.ContainsKey(comp.ShapeName))
                    comp.Shape = _data.Shapes[comp.ShapeName];
            }
        }

        private void ExtractFiducials()
        {
            var nonFiducials = new List<GenCadComponent>();

            foreach (var comp in _data.Components)
            {
                bool isFid = false;

                if (comp.DeviceName.IndexOf("SNAP", StringComparison.OrdinalIgnoreCase) >= 0)
                    isFid = true;
                if (comp.RefDes.StartsWith("$GEN$"))
                    isFid = true;
                if (comp.RefDes.StartsWith("FID", StringComparison.OrdinalIgnoreCase))
                    isFid = true;
                if (comp.ShapeName.IndexOf("SNAP", StringComparison.OrdinalIgnoreCase) >= 0)
                    isFid = true;
                if (comp.ShapeName.IndexOf("FIDUCIAL", StringComparison.OrdinalIgnoreCase) >= 0)
                    isFid = true;

                foreach (string attr in comp.Attributes)
                {
                    if (attr.IndexOf("TESTPOINT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        attr.IndexOf("FIDUCIAL", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        isFid = true;
                        break;
                    }
                }

                if (isFid)
                {
                    _data.Fiducials.Add(new GenCadFiducial
                    {
                        Name = comp.RefDes,
                        X = comp.PlaceX,
                        Y = comp.PlaceY,
                        Rotation = comp.Rotation,
                        Layer = comp.Layer,
                        ShapeName = comp.ShapeName
                    });
                }
                else
                {
                    nonFiducials.Add(comp);
                }
            }

            _data.Components = nonFiducials;
        }
    }
}
