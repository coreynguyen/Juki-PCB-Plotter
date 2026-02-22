// =============================================================================
// AllegroFabmasterParser.cs
// Cadence Allegro Fabmaster Extraction Script (.VAL / .FAB / .VA2) Parser
// Extracts SMT manufacturing data: placements, components, packages, outlines
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PCBPlotter.Core.Services
{
    // =========================================================================
    // Data Model
    // =========================================================================

    /// <summary>Units used in the source CAD file.</summary>
    public enum FabmasterSourceUnits
    {
        Mils,
        Inches,
        Millimeters,
        Centimeters,
        Microns,
        Unknown
    }

    /// <summary>Graphic primitive type.</summary>
    public enum FabmasterGraphicType
    {
        Line,
        Arc,
        Text
    }

    /// <summary>Arc sweep direction.</summary>
    public enum FabmasterArcDirection
    {
        Clockwise,
        CounterClockwise
    }

    /// <summary>Graphic record usage context.</summary>
    public enum FabmasterGraphicUsage
    {
        Connect,
        NotConnect,
        Shape,
        Void,
        Unknown
    }

    // -------------------------------------------------------------------------

    /// <summary>Job/board header info extracted from J-lines.</summary>
    public class FabmasterBoardInfo
    {
        public string SourceFile = "";
        public string ExtractionDate = "";
        public double MinX;
        public double MinY;
        public double MaxX;
        public double MaxY;
        public double Resolution;
        public FabmasterSourceUnits Units = FabmasterSourceUnits.Unknown;
        public string BoardName = "";
        public string TraceWidth = "";
        public int LayerCount;
        public string Status = "";

        // Computed board dimensions in millimeters
        public double BoardWidthMM;
        public double BoardHeightMM;
        public double OriginXMM;
        public double OriginYMM;
    }

    /// <summary>A 2D point in millimeters.</summary>
    public struct FabmasterPointMM
    {
        public double X;
        public double Y;

        public FabmasterPointMM(double x, double y)
        {
            X = x;
            Y = y;
        }

        public override string ToString()
        {
            return string.Format("({0:F4}, {1:F4})", X, Y);
        }
    }

    /// <summary>Pin from the netlist section with coordinates.</summary>
    public class FabmasterPinRecord
    {
        public string NetName = "";
        public string RefDes = "";
        public string PinNumber = "";
        public double X;  // in mm
        public double Y;  // in mm
        public string StartLayer = "";
        public string EndLayer = "";
        public double ViaX;
        public double ViaY;
        public bool HasVia;
    }

    /// <summary>Component definition aggregated from netlist records.</summary>
    public class FabmasterComponentDef
    {
        public string DeviceType = "";      // e.g. "CAPACITOR-CG16"
        public string DeviceLabel = "";
        public string Value = "";           // e.g. "100NF"
        public string Tolerance = "";
        public string CompClass = "";       // IC, DISCRETE, IO
        public string SymName = "";         // package footprint, e.g. "NSMC126R_RES"
        public List<string> RefDeses = new List<string>();

        /// <summary>Description synthesized from available fields.</summary>
        public string Description
        {
            get
            {
                var sb = new StringBuilder();
                if (CompClass.Length > 0) sb.Append(CompClass);
                if (DeviceType.Length > 0)
                {
                    if (sb.Length > 0) sb.Append(" / ");
                    sb.Append(DeviceType);
                }
                if (Value.Length > 0)
                {
                    if (sb.Length > 0) sb.Append(" / ");
                    sb.Append(Value);
                }
                if (Tolerance.Length > 0)
                {
                    sb.Append(" ");
                    sb.Append(Tolerance);
                }
                return sb.ToString();
            }
        }
    }

    /// <summary>Package (footprint) definition with computed dimensions.</summary>
    public class FabmasterPackageDef
    {
        public string SymName = "";         // e.g. "QFP144_5MM_REFLOW"
        public double WidthMM;              // bounding box width
        public double HeightMM;             // bounding box height
        public double PinPitchMM;           // estimated pin pitch
        public int PinCount;
        public string PackageType = "";     // derived: QFP, SOIC, BGA, SMD_2PIN, etc.
        public List<FabmasterPinRecord> Pins = new List<FabmasterPinRecord>();

        // Relative pin positions (from centroid)
        public double MinRelX, MinRelY, MaxRelX, MaxRelY;
    }

    /// <summary>A component placement on the board.</summary>
    public class FabmasterPlacement
    {
        public string RefDes = "";
        public double X;               // centroid X in mm
        public double Y;               // centroid Y in mm
        public double Rotation;        // degrees (0-360)
        public bool IsBottomSide;
        public string DeviceType = "";  // part number / device type
        public string Value = "";
        public string CompClass = "";
        public string SymName = "";     // package / graphic footprint name

        // Links
        public FabmasterComponentDef Component;
        public FabmasterPackageDef Package;
    }

    /// <summary>A fiducial mark on the board.</summary>
    public class FabmasterFiducial
    {
        public string RefDes = "";
        public double X;
        public double Y;
        public bool IsBottomSide;
        public string Type = "";        // "GLOBAL" or "LOCAL"
    }

    /// <summary>A graphic primitive (line, arc, text).</summary>
    public class FabmasterGraphicRecord
    {
        public string Class = "";        // ETCH, BOARD GEOMETRY, etc.
        public string SubClass = "";     // TOP, BOTTOM, V02, etc.
        public string NetName = "";
        public string RecordTag = "";
        public FabmasterGraphicType Type;
        public int GraphicNumber;
        public FabmasterGraphicUsage Usage = FabmasterGraphicUsage.Unknown;

        // LINE: start/end points + width
        public double X1, Y1, X2, Y2;
        public double Width;

        // ARC: start/end + center + radius + direction
        public double CenterX, CenterY;
        public double Radius;
        public FabmasterArcDirection Direction;

        // TEXT
        public string TextData = "";
        public bool Mirror;
        public string Justify = "";
    }

    /// <summary>Board outline as a closed polygon (in mm).</summary>
    public class FabmasterBoardOutline
    {
        public List<FabmasterPointMM> Points = new List<FabmasterPointMM>();
        public bool IsBoundingBoxFallback;
        public double MinX, MinY, MaxX, MaxY;

        public double WidthMM { get { return MaxX - MinX; } }
        public double HeightMM { get { return MaxY - MinY; } }
    }

    // =========================================================================
    // Parser Result
    // =========================================================================

    /// <summary>Complete parsed result of an Allegro Fabmaster file.</summary>
    public class FabmasterData
    {
        public FabmasterBoardInfo Board = new FabmasterBoardInfo();
        public FabmasterBoardOutline Outline = new FabmasterBoardOutline();
        public List<FabmasterPlacement> Placements = new List<FabmasterPlacement>();
        public List<FabmasterFiducial> Fiducials = new List<FabmasterFiducial>();
        public Dictionary<string, FabmasterComponentDef> Components = new Dictionary<string, FabmasterComponentDef>();
        public Dictionary<string, FabmasterPackageDef> Packages = new Dictionary<string, FabmasterPackageDef>();
        public List<FabmasterGraphicRecord> Graphics = new List<FabmasterGraphicRecord>();

        // Raw pin data
        public List<FabmasterPinRecord> PinLocations = new List<FabmasterPinRecord>();

        // Parse diagnostics
        public List<string> Warnings = new List<string>();
        public int TotalLinesRead;
        public int Section1Records; // netlist
        public int Section2Records; // pin XY
        public int Section3Records; // graphics
    }

    // =========================================================================
    // Parser
    // =========================================================================

    public class AllegroFabmasterParser
    {
        // --- Unit conversion factor to millimeters ---
        private double _toMM = 1.0;
        private FabmasterSourceUnits _sourceUnits = FabmasterSourceUnits.Unknown;

        // --- Section tracking ---
        private enum SectionType { None, Netlist, PinXY, Graphics }
        private SectionType _currentSection = SectionType.None;
        private string[] _currentHeader;

        // --- Intermediate storage ---
        private FabmasterData _data;

        // Netlist section keyed by RefDes -> aggregated component info
        private Dictionary<string, NetlistComponent> _netlistComps =
            new Dictionary<string, NetlistComponent>();

        // Pin locations keyed by RefDes -> list of pin (x,y,layer)
        private Dictionary<string, List<FabmasterPinRecord>> _pinsByRefDes =
            new Dictionary<string, List<FabmasterPinRecord>>();

        private class NetlistComponent
        {
            public string RefDes = "";
            public string DeviceType = "";
            public string DeviceLabel = "";
            public string Value = "";
            public string Tolerance = "";
            public string CompClass = "";
            public string SymName = "";
            public List<string> PinNames = new List<string>();
            public List<string> NetNames = new List<string>();
        }

        // =================================================================
        // Public API
        // =================================================================

        /// <summary>
        /// Parse an Allegro Fabmaster extraction file (.VAL, .FAB, .VA2).
        /// Returns a FabmasterData object with all extracted information
        /// converted to millimeters.
        /// </summary>
        public FabmasterData Parse(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("File not found: " + filePath);

            _data = new FabmasterData();
            _currentSection = SectionType.None;
            _netlistComps.Clear();
            _pinsByRefDes.Clear();

            // Read all lines
            var lines = File.ReadAllLines(filePath, Encoding.Default);
            _data.TotalLinesRead = lines.Length;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r', '\n');
                if (line.Length == 0) continue;

                char recordType = line[0];
                switch (recordType)
                {
                    case 'A':
                        ParseHeaderLine(line);
                        break;
                    case 'J':
                        ParseJobLine(line);
                        break;
                    case 'S':
                        ParseDataLine(line);
                        break;
                }
            }

            // --- Post-processing ---
            BuildComponents();
            BuildPackages();
            BuildPlacements();
            DetectFiducials();
            BuildBoardOutline();
            ComputeBoardDimensions();

            return _data;
        }

        // =================================================================
        // Section Header (A-line)
        // =================================================================

        private void ParseHeaderLine(string line)
        {
            string[] fields = SplitFields(line);

            // Identify section by header signature
            if (HasField(fields, "PIN_X") && HasField(fields, "PIN_Y"))
            {
                _currentSection = SectionType.PinXY;
            }
            else if (HasField(fields, "GRAPHIC_DATA_NAME"))
            {
                _currentSection = SectionType.Graphics;
            }
            else if (HasField(fields, "REFDES") && HasField(fields, "COMP_DEVICE_TYPE"))
            {
                _currentSection = SectionType.Netlist;
            }
            else if (HasField(fields, "REFDES") && HasField(fields, "SYM_X"))
            {
                // Some extractions have a direct placement section
                _currentSection = SectionType.Netlist;
            }
            else
            {
                _currentSection = SectionType.None;
                _data.Warnings.Add("Unknown section header: " + line);
            }

            _currentHeader = fields;
        }

        // =================================================================
        // Job Line (J-line) - Board metadata
        // =================================================================

        private void ParseJobLine(string line)
        {
            string[] f = SplitFields(line);

            // J!path!date!minX!minY!maxX!maxY!resolution!units!boardName!traceWidth!layers!status!
            if (f.Length < 10) return;

            var b = _data.Board;
            b.SourceFile = SafeGet(f, 1);
            b.ExtractionDate = SafeGet(f, 2);
            b.MinX = ParseDouble(SafeGet(f, 3));
            b.MinY = ParseDouble(SafeGet(f, 4));
            b.MaxX = ParseDouble(SafeGet(f, 5));
            b.MaxY = ParseDouble(SafeGet(f, 6));
            b.Resolution = ParseDouble(SafeGet(f, 7));
            b.BoardName = SafeGet(f, 9);
            b.TraceWidth = SafeGet(f, 10);
            b.LayerCount = ParseInt(SafeGet(f, 11));
            b.Status = SafeGet(f, 12);

            // Determine units
            string unitStr = SafeGet(f, 8).Trim().ToLower();
            SetUnits(unitStr);
            b.Units = _sourceUnits;
        }

        private void SetUnits(string unitStr)
        {
            if (unitStr == "mils" || unitStr == "mil" || unitStr == "thou")
            {
                _sourceUnits = FabmasterSourceUnits.Mils;
                _toMM = 0.0254;
            }
            else if (unitStr == "inches" || unitStr == "inch" || unitStr == "in")
            {
                _sourceUnits = FabmasterSourceUnits.Inches;
                _toMM = 25.4;
            }
            else if (unitStr == "mm" || unitStr == "millimeters" || unitStr == "millimeter")
            {
                _sourceUnits = FabmasterSourceUnits.Millimeters;
                _toMM = 1.0;
            }
            else if (unitStr == "cm" || unitStr == "centimeters" || unitStr == "centimeter")
            {
                _sourceUnits = FabmasterSourceUnits.Centimeters;
                _toMM = 10.0;
            }
            else if (unitStr == "microns" || unitStr == "um" || unitStr == "micron")
            {
                _sourceUnits = FabmasterSourceUnits.Microns;
                _toMM = 0.001;
            }
            else
            {
                // Default assumption: mils (most common for Allegro)
                _sourceUnits = FabmasterSourceUnits.Mils;
                _toMM = 0.0254;
                _data.Warnings.Add("Unknown unit string '" + unitStr + "', defaulting to mils");
            }
        }

        // =================================================================
        // Data Lines (S-lines)
        // =================================================================

        private void ParseDataLine(string line)
        {
            switch (_currentSection)
            {
                case SectionType.Netlist:
                    ParseNetlistRecord(line);
                    break;
                case SectionType.PinXY:
                    ParsePinXYRecord(line);
                    break;
                case SectionType.Graphics:
                    ParseGraphicRecord(line);
                    break;
            }
        }

        // --- Section 1: Netlist / Component data ---
        private void ParseNetlistRecord(string line)
        {
            string[] f = SplitFields(line);
            _data.Section1Records++;

            int iRefDes = FindHeaderIndex("REFDES");
            int iDevType = FindHeaderIndex("COMP_DEVICE_TYPE");
            int iDevLabel = FindHeaderIndex("COMP_DEVICE_LABEL");
            int iValue = FindHeaderIndex("COMP_VALUE");
            int iTol = FindHeaderIndex("COMP_TOL");
            int iPinName = FindHeaderIndex("PIN_NAME");
            int iCompClass = FindHeaderIndex("COMP_CLASS");
            int iSymName = FindHeaderIndex("SYM_NAME");
            int iNetName = FindHeaderIndex("NET_NAME");

            string refdes = SafeGet(f, iRefDes);
            if (refdes.Length == 0) return;

            NetlistComponent comp;
            if (!_netlistComps.TryGetValue(refdes, out comp))
            {
                comp = new NetlistComponent();
                comp.RefDes = refdes;
                _netlistComps[refdes] = comp;
            }

            // Populate component info (first non-empty wins)
            string devType = SafeGet(f, iDevType);
            string devLabel = SafeGet(f, iDevLabel);
            string value = SafeGet(f, iValue);
            string tol = SafeGet(f, iTol);
            string compClass = SafeGet(f, iCompClass);
            string symName = SafeGet(f, iSymName);
            string netName = SafeGet(f, iNetName);
            string pinName = SafeGet(f, iPinName);

            if (devType.Length > 0 && comp.DeviceType.Length == 0) comp.DeviceType = devType;
            if (devLabel.Length > 0 && comp.DeviceLabel.Length == 0) comp.DeviceLabel = devLabel;
            if (value.Length > 0 && comp.Value.Length == 0) comp.Value = value;
            if (tol.Length > 0 && comp.Tolerance.Length == 0) comp.Tolerance = tol;
            if (compClass.Length > 0 && comp.CompClass.Length == 0) comp.CompClass = compClass;
            if (symName.Length > 0 && comp.SymName.Length == 0) comp.SymName = symName;
            if (pinName.Length > 0) comp.PinNames.Add(pinName);
            if (netName.Length > 0) comp.NetNames.Add(netName);
        }

        // --- Section 2: Pin XY locations ---
        private void ParsePinXYRecord(string line)
        {
            string[] f = SplitFields(line);
            _data.Section2Records++;

            int iNetName = FindHeaderIndex("NET_NAME");
            int iRefDes = FindHeaderIndex("REFDES");
            int iPinNum = FindHeaderIndex("PIN_NUMBER");
            int iPinX = FindHeaderIndex("PIN_X");
            int iPinY = FindHeaderIndex("PIN_Y");
            int iStartLayer = FindHeaderIndex("START_LAYER_NAME");
            int iEndLayer = FindHeaderIndex("END_LAYER_NAME");
            int iViaX = FindHeaderIndex("VIA_X");
            int iViaY = FindHeaderIndex("VIA_Y");

            string refdes = SafeGet(f, iRefDes);

            var pin = new FabmasterPinRecord();
            pin.NetName = SafeGet(f, iNetName);
            pin.RefDes = refdes;
            pin.PinNumber = SafeGet(f, iPinNum);
            pin.X = ParseDouble(SafeGet(f, iPinX)) * _toMM;
            pin.Y = ParseDouble(SafeGet(f, iPinY)) * _toMM;
            pin.StartLayer = SafeGet(f, iStartLayer);
            pin.EndLayer = SafeGet(f, iEndLayer);

            string vxStr = SafeGet(f, iViaX);
            string vyStr = SafeGet(f, iViaY);
            if (vxStr.Length > 0 && vyStr.Length > 0)
            {
                pin.ViaX = ParseDouble(vxStr) * _toMM;
                pin.ViaY = ParseDouble(vyStr) * _toMM;
                pin.HasVia = true;
            }

            _data.PinLocations.Add(pin);

            // Index by RefDes
            if (refdes.Length > 0)
            {
                List<FabmasterPinRecord> list;
                if (!_pinsByRefDes.TryGetValue(refdes, out list))
                {
                    list = new List<FabmasterPinRecord>();
                    _pinsByRefDes[refdes] = list;
                }
                list.Add(pin);
            }
        }

        // --- Section 3: Graphic data ---
        private void ParseGraphicRecord(string line)
        {
            string[] f = SplitFields(line);
            _data.Section3Records++;

            var gr = new FabmasterGraphicRecord();
            gr.Class = SafeGet(f, FindHeaderIndex("CLASS"));
            gr.SubClass = SafeGet(f, FindHeaderIndex("SUBCLASS"));
            gr.NetName = SafeGet(f, FindHeaderIndex("NET_NAME"));
            gr.RecordTag = SafeGet(f, FindHeaderIndex("RECORD_TAG"));

            string gName = SafeGet(f, FindHeaderIndex("GRAPHIC_DATA_NAME"));
            string gNum = SafeGet(f, FindHeaderIndex("GRAPHIC_DATA_NUMBER"));
            gr.GraphicNumber = ParseInt(gNum);

            int iGD1 = FindHeaderIndex("GRAPHIC_DATA_1");

            // Determine usage from last non-empty field
            string usageStr = "";
            for (int i = f.Length - 1; i > 0; i--)
            {
                string v = f[i].Trim();
                if (v.Length > 0)
                {
                    usageStr = v;
                    break;
                }
            }
            gr.Usage = ParseUsage(usageStr);

            if (gName == "LINE")
            {
                gr.Type = FabmasterGraphicType.Line;
                gr.X1 = ParseDouble(SafeGet(f, iGD1)) * _toMM;
                gr.Y1 = ParseDouble(SafeGet(f, iGD1 + 1)) * _toMM;
                gr.X2 = ParseDouble(SafeGet(f, iGD1 + 2)) * _toMM;
                gr.Y2 = ParseDouble(SafeGet(f, iGD1 + 3)) * _toMM;
                gr.Width = ParseDouble(SafeGet(f, iGD1 + 4)) * _toMM;
            }
            else if (gName == "ARC")
            {
                gr.Type = FabmasterGraphicType.Arc;
                gr.X1 = ParseDouble(SafeGet(f, iGD1)) * _toMM;
                gr.Y1 = ParseDouble(SafeGet(f, iGD1 + 1)) * _toMM;
                gr.X2 = ParseDouble(SafeGet(f, iGD1 + 2)) * _toMM;
                gr.Y2 = ParseDouble(SafeGet(f, iGD1 + 3)) * _toMM;
                gr.CenterX = ParseDouble(SafeGet(f, iGD1 + 4)) * _toMM;
                gr.CenterY = ParseDouble(SafeGet(f, iGD1 + 5)) * _toMM;
                gr.Radius = ParseDouble(SafeGet(f, iGD1 + 6)) * _toMM;

                string dirStr = SafeGet(f, iGD1 + 8).ToUpper();
                gr.Direction = (dirStr.StartsWith("COUNTER"))
                    ? FabmasterArcDirection.CounterClockwise
                    : FabmasterArcDirection.Clockwise;
            }
            else if (gName == "TEXT")
            {
                gr.Type = FabmasterGraphicType.Text;
                gr.X1 = ParseDouble(SafeGet(f, iGD1)) * _toMM;
                gr.Y1 = ParseDouble(SafeGet(f, iGD1 + 1)) * _toMM;
                gr.Width = ParseDouble(SafeGet(f, iGD1 + 2)); // rotation degrees
                string mirrorStr = SafeGet(f, iGD1 + 3).ToUpper();
                gr.Mirror = (mirrorStr == "YES");
                gr.Justify = SafeGet(f, iGD1 + 4);
                gr.TextData = SafeGet(f, iGD1 + 5);

                string textContent = SafeGet(f, iGD1 + 6);
                if (textContent.Length > 0)
                    gr.TextData += "|" + textContent;
            }
            else
            {
                gr.Type = FabmasterGraphicType.Line;
                gr.X1 = ParseDouble(SafeGet(f, iGD1)) * _toMM;
                gr.Y1 = ParseDouble(SafeGet(f, iGD1 + 1)) * _toMM;
                gr.X2 = ParseDouble(SafeGet(f, iGD1 + 2)) * _toMM;
                gr.Y2 = ParseDouble(SafeGet(f, iGD1 + 3)) * _toMM;
            }

            _data.Graphics.Add(gr);
        }

        // =================================================================
        // Post-Processing
        // =================================================================

        private void BuildComponents()
        {
            var byDevice = new Dictionary<string, FabmasterComponentDef>();

            foreach (var nc in _netlistComps.Values)
            {
                string key = nc.DeviceType;
                if (key.Length == 0) key = nc.SymName;

                FabmasterComponentDef cd;
                if (!byDevice.TryGetValue(key, out cd))
                {
                    cd = new FabmasterComponentDef();
                    cd.DeviceType = nc.DeviceType;
                    cd.DeviceLabel = nc.DeviceLabel;
                    cd.Value = nc.Value;
                    cd.Tolerance = nc.Tolerance;
                    cd.CompClass = nc.CompClass;
                    cd.SymName = nc.SymName;
                    byDevice[key] = cd;
                }

                if (!cd.RefDeses.Contains(nc.RefDes))
                    cd.RefDeses.Add(nc.RefDes);
            }

            _data.Components = byDevice;
        }

        private void BuildPackages()
        {
            var pinsBySym = new Dictionary<string, List<FabmasterPinRecord>>();

            foreach (var nc in _netlistComps.Values)
            {
                if (nc.SymName.Length == 0) continue;

                List<FabmasterPinRecord> pins;
                if (_pinsByRefDes.TryGetValue(nc.RefDes, out pins))
                {
                    List<FabmasterPinRecord> symPins;
                    if (!pinsBySym.TryGetValue(nc.SymName, out symPins))
                    {
                        symPins = new List<FabmasterPinRecord>();
                        pinsBySym[nc.SymName] = symPins;
                    }

                    if (symPins.Count == 0)
                    {
                        foreach (var p in pins)
                            symPins.Add(p);
                    }
                }
            }

            foreach (var symName in pinsBySym.Keys)
            {
                var pins = pinsBySym[symName];
                if (pins.Count == 0) continue;

                var pkg = new FabmasterPackageDef();
                pkg.SymName = symName;
                pkg.PinCount = pins.Count;
                pkg.Pins = pins;

                // Compute centroid from pins
                double cx = 0, cy = 0;
                foreach (var p in pins) { cx += p.X; cy += p.Y; }
                cx /= pins.Count;
                cy /= pins.Count;

                // Compute bounding box relative to centroid
                double minRx = double.MaxValue, minRy = double.MaxValue;
                double maxRx = double.MinValue, maxRy = double.MinValue;

                foreach (var p in pins)
                {
                    double rx = p.X - cx;
                    double ry = p.Y - cy;
                    if (rx < minRx) minRx = rx;
                    if (ry < minRy) minRy = ry;
                    if (rx > maxRx) maxRx = rx;
                    if (ry > maxRy) maxRy = ry;
                }

                pkg.MinRelX = minRx;
                pkg.MinRelY = minRy;
                pkg.MaxRelX = maxRx;
                pkg.MaxRelY = maxRy;
                pkg.WidthMM = maxRx - minRx;
                pkg.HeightMM = maxRy - minRy;

                pkg.PinPitchMM = EstimatePinPitch(pins);
                pkg.PackageType = DerivePackageType(symName, pins.Count);

                _data.Packages[symName] = pkg;
            }
        }

        private void BuildPlacements()
        {
            foreach (var nc in _netlistComps.Values)
            {
                List<FabmasterPinRecord> pins;
                if (!_pinsByRefDes.TryGetValue(nc.RefDes, out pins) || pins.Count == 0)
                    continue;

                var pl = new FabmasterPlacement();
                pl.RefDes = nc.RefDes;
                pl.DeviceType = nc.DeviceType;
                pl.Value = nc.Value;
                pl.CompClass = nc.CompClass;
                pl.SymName = nc.SymName;

                // Compute centroid
                double cx = 0, cy = 0;
                foreach (var p in pins) { cx += p.X; cy += p.Y; }
                pl.X = cx / pins.Count;
                pl.Y = cy / pins.Count;

                // Determine side from pin layers
                int topCount = 0, bottomCount = 0;
                foreach (var p in pins)
                {
                    string layer = p.StartLayer.ToUpper();
                    if (layer == "TOP") topCount++;
                    else if (layer == "BOTTOM") bottomCount++;
                }
                pl.IsBottomSide = bottomCount > topCount;

                pl.Rotation = EstimateRotation(pins, pl.X, pl.Y);

                // Link to component and package defs
                FabmasterComponentDef compDef;
                string compKey = nc.DeviceType.Length > 0 ? nc.DeviceType : nc.SymName;
                if (_data.Components.TryGetValue(compKey, out compDef))
                    pl.Component = compDef;

                FabmasterPackageDef pkgDef;
                if (_data.Packages.TryGetValue(nc.SymName, out pkgDef))
                    pl.Package = pkgDef;

                _data.Placements.Add(pl);
            }
        }

        private void DetectFiducials()
        {
            var toRemove = new List<FabmasterPlacement>();

            foreach (var pl in _data.Placements)
            {
                bool isFiducial = false;
                string fidType = "LOCAL";

                string rdUpper = pl.RefDes.ToUpper();
                if (rdUpper.StartsWith("FID") || rdUpper.StartsWith("FM"))
                {
                    isFiducial = true;
                    fidType = "GLOBAL";
                }

                string dtUpper = pl.DeviceType.ToUpper();
                string snUpper = pl.SymName.ToUpper();
                if (dtUpper.Contains("FIDUCIAL") || dtUpper.Contains("FID") ||
                    snUpper.Contains("FIDUCIAL") || snUpper.Contains("FID"))
                {
                    isFiducial = true;
                    fidType = "GLOBAL";
                }

                if (snUpper.Contains("TESTPOINT") && dtUpper.Contains("TESTPOINT"))
                    continue;

                if (isFiducial)
                {
                    var fid = new FabmasterFiducial();
                    fid.RefDes = pl.RefDes;
                    fid.X = pl.X;
                    fid.Y = pl.Y;
                    fid.IsBottomSide = pl.IsBottomSide;
                    fid.Type = fidType;
                    _data.Fiducials.Add(fid);
                    toRemove.Add(pl);
                }
            }

            foreach (var pl in toRemove)
                _data.Placements.Remove(pl);
        }

        private void BuildBoardOutline()
        {
            var outline = new FabmasterBoardOutline();

            var outlineGraphics = new List<FabmasterGraphicRecord>();
            foreach (var gr in _data.Graphics)
            {
                string cls = gr.Class.ToUpper();
                string sub = gr.SubClass.ToUpper();
                if ((cls == "BOARD GEOMETRY" && sub == "OUTLINE") ||
                    (cls == "BOARD GEOMETRY" && sub == "DESIGN_OUTLINE") ||
                    (cls == "PACKAGE GEOMETRY" && sub == "PLACE_BOUND_TOP"))
                {
                    outlineGraphics.Add(gr);
                }
            }

            if (outlineGraphics.Count > 0)
            {
                foreach (var gr in outlineGraphics)
                {
                    if (gr.Type == FabmasterGraphicType.Line)
                    {
                        if (outline.Points.Count == 0)
                            outline.Points.Add(new FabmasterPointMM(gr.X1, gr.Y1));
                        outline.Points.Add(new FabmasterPointMM(gr.X2, gr.Y2));
                    }
                }
                outline.IsBoundingBoxFallback = false;
            }

            if (outline.Points.Count < 3)
            {
                double minX = _data.Board.MinX * _toMM;
                double minY = _data.Board.MinY * _toMM;
                double maxX = _data.Board.MaxX * _toMM;
                double maxY = _data.Board.MaxY * _toMM;

                outline.Points.Clear();
                outline.Points.Add(new FabmasterPointMM(minX, minY));
                outline.Points.Add(new FabmasterPointMM(maxX, minY));
                outline.Points.Add(new FabmasterPointMM(maxX, maxY));
                outline.Points.Add(new FabmasterPointMM(minX, maxY));
                outline.IsBoundingBoxFallback = true;

                _data.Warnings.Add(
                    "No BOARD GEOMETRY/OUTLINE records found. " +
                    "Board outline derived from J-line bounding box.");
            }

            outline.MinX = double.MaxValue;
            outline.MinY = double.MaxValue;
            outline.MaxX = double.MinValue;
            outline.MaxY = double.MinValue;

            foreach (var pt in outline.Points)
            {
                if (pt.X < outline.MinX) outline.MinX = pt.X;
                if (pt.Y < outline.MinY) outline.MinY = pt.Y;
                if (pt.X > outline.MaxX) outline.MaxX = pt.X;
                if (pt.Y > outline.MaxY) outline.MaxY = pt.Y;
            }

            _data.Outline = outline;
        }

        private void ComputeBoardDimensions()
        {
            var b = _data.Board;
            b.BoardWidthMM = (b.MaxX - b.MinX) * _toMM;
            b.BoardHeightMM = (b.MaxY - b.MinY) * _toMM;
            b.OriginXMM = b.MinX * _toMM;
            b.OriginYMM = b.MinY * _toMM;
        }

        // =================================================================
        // Helper: Rotation Estimation
        // =================================================================

        private double EstimateRotation(List<FabmasterPinRecord> pins, double cx, double cy)
        {
            if (pins.Count < 2) return 0.0;

            FabmasterPinRecord pin1 = null;
            int lowestNum = int.MaxValue;

            foreach (var p in pins)
            {
                if (p.PinNumber == "1" || p.PinNumber == "A1" || p.PinNumber == "A")
                {
                    pin1 = p;
                    break;
                }
                int num;
                if (int.TryParse(p.PinNumber, out num) && num < lowestNum)
                {
                    lowestNum = num;
                    pin1 = p;
                }
            }

            if (pin1 == null) return 0.0;

            double dx = pin1.X - cx;
            double dy = pin1.Y - cy;
            double angle = Math.Atan2(dy, dx) * (180.0 / Math.PI);

            if (angle < 0) angle += 360.0;

            double snapped = Math.Round(angle / 90.0) * 90.0;
            if (snapped >= 360.0) snapped = 0.0;

            return snapped;
        }

        // =================================================================
        // Helper: Pin Pitch Estimation
        // =================================================================

        private double EstimatePinPitch(List<FabmasterPinRecord> pins)
        {
            if (pins.Count < 2) return 0;

            double minDist = double.MaxValue;
            for (int i = 0; i < pins.Count && i < 50; i++)
            {
                for (int j = i + 1; j < pins.Count && j < 50; j++)
                {
                    double dx = pins[i].X - pins[j].X;
                    double dy = pins[i].Y - pins[j].Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist > 0.001 && dist < minDist)
                        minDist = dist;
                }
            }

            return (minDist < double.MaxValue) ? minDist : 0;
        }

        // =================================================================
        // Helper: Package Type Derivation
        // =================================================================

        private static string DerivePackageType(string symName, int pinCount)
        {
            string s = symName.ToUpper();
            if (s.Contains("QFP")) return "QFP";
            if (s.Contains("BGA")) return "BGA";
            if (s.Contains("PLCC")) return "PLCC";
            if (s.Contains("SOP") || s.Contains("SOIC")) return "SOIC";
            if (s.Contains("SOT23")) return "SOT23";
            if (s.Contains("SOT")) return "SOT";
            if (s.Contains("SOD")) return "SOD";
            if (s.Contains("TQFP")) return "TQFP";
            if (s.Contains("TSOP")) return "TSOP";
            if (s.Contains("LQFP")) return "LQFP";
            if (s.Contains("QFN")) return "QFN";
            if (s.Contains("DFN")) return "DFN";
            if (s.Contains("CSP")) return "CSP";
            if (s.Contains("LGA")) return "LGA";
            if (s.Contains("CONN")) return "CONNECTOR";
            if (s.Contains("HDR") || s.Contains("HEADER")) return "HEADER";
            if (s.Contains("BERG")) return "HEADER";
            if (s.Contains("TANT")) return "TANTALUM_CAP";
            if (s.Contains("OSC")) return "OSCILLATOR";
            if (s.Contains("RES") && pinCount <= 2) return "SMD_RESISTOR";
            if (s.Contains("CAP") && pinCount <= 2) return "SMD_CAPACITOR";
            if (s.Contains("TESTPOINT") || s.Contains("TP")) return "TESTPOINT";
            if (s.Contains("SMC") || s.Contains("SMD")) return "SMD_2PIN";
            if (pinCount <= 2) return "SMD_2PIN";
            if (pinCount <= 8) return "SMALL_IC";
            return "IC";
        }

        // =================================================================
        // Field Parsing Helpers
        // =================================================================

        private static string[] SplitFields(string line)
        {
            var fields = new List<string>();
            int start = 0;
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '!')
                {
                    fields.Add(line.Substring(start, i - start));
                    start = i + 1;
                }
            }
            if (start < line.Length)
                fields.Add(line.Substring(start));
            return fields.ToArray();
        }

        private int FindHeaderIndex(string fieldName)
        {
            if (_currentHeader == null) return -1;
            for (int i = 0; i < _currentHeader.Length; i++)
            {
                if (string.Compare(_currentHeader[i], fieldName, StringComparison.OrdinalIgnoreCase) == 0)
                    return i;
            }
            return -1;
        }

        private static bool HasField(string[] fields, string name)
        {
            for (int i = 0; i < fields.Length; i++)
            {
                if (string.Compare(fields[i], name, StringComparison.OrdinalIgnoreCase) == 0)
                    return true;
            }
            return false;
        }

        private static string SafeGet(string[] fields, int index)
        {
            if (index < 0 || index >= fields.Length) return "";
            return fields[index].Trim();
        }

        private static double ParseDouble(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0.0;
            double result;
            if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out result))
                return result;
            return 0.0;
        }

        private static int ParseInt(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int result;
            if (int.TryParse(s, out result))
                return result;
            return 0;
        }

        private static FabmasterGraphicUsage ParseUsage(string s)
        {
            switch (s.ToUpper())
            {
                case "CONNECT": return FabmasterGraphicUsage.Connect;
                case "NOTCONNECT": return FabmasterGraphicUsage.NotConnect;
                case "SHAPE": return FabmasterGraphicUsage.Shape;
                case "VOID": return FabmasterGraphicUsage.Void;
                default: return FabmasterGraphicUsage.Unknown;
            }
        }
    }
}
