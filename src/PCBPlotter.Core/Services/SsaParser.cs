// =============================================================================
// SsaParser.cs - Samsung Standard ASCII (Extended SSA / MARK3) Parser
// =============================================================================
// Purpose: Extract CAD data from SSA exports for CAM/CIS/Mounter workflows
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PCBPlotter.Core.Services
{
    // =========================================================================
    // Enumerations
    // =========================================================================

    /// <summary>Coordinate origin convention.</summary>
    public enum SsaCoordinateOrigin
    {
        LowerLeft,
        LowerRight,
        UpperLeft,
        UpperRight,
        Unknown
    }

    /// <summary>Unit system for all dimensional data.</summary>
    public enum SsaUnitSystem
    {
        Millimeter,
        Inch,
        Mil,
        Unknown
    }

    /// <summary>Fiducial or mark shape type.</summary>
    public enum SsaMarkShape
    {
        None,
        Circle,
        Square,
        Diamond,
        Cross,
        Unknown
    }

    /// <summary>Local fiducial shape for placement-level fiducials.</summary>
    public enum SsaLocalFiducialShape
    {
        None,
        Circle,
        Square,
        Diamond,
        Cross,
        Unknown
    }

    // =========================================================================
    // Data Model Classes
    // =========================================================================

    /// <summary>Board outline / dimensions.</summary>
    public class SsaBoardOutline
    {
        /// <summary>Board width (X dimension) in mm.</summary>
        public double Width;

        /// <summary>Board height (Y dimension) in mm.</summary>
        public double Height;

        /// <summary>Board thickness in mm.</summary>
        public double Thickness;

        public override string ToString()
        {
            return string.Format("{0:F3} x {1:F3} x {2:F3} mm", Width, Height, Thickness);
        }
    }

    /// <summary>Array / panelization configuration.</summary>
    public class SsaArrayConfig
    {
        /// <summary>Number of columns in the array.</summary>
        public int Columns;

        /// <summary>Number of rows in the array.</summary>
        public int Rows;

        /// <summary>Array coordinate origin.</summary>
        public SsaCoordinateOrigin Origin;

        /// <summary>Step offset between array instances (X) in mm.</summary>
        public double OffsetX;

        /// <summary>Step offset between array instances (Y) in mm.</summary>
        public double OffsetY;

        /// <summary>Total board count (Columns * Rows).</summary>
        public int TotalBoards { get { return Columns * Rows; } }

        public override string ToString()
        {
            return string.Format("{0}x{1} ({2} boards), Offset=({3:F3}, {4:F3}), Origin={5}",
                Columns, Rows, TotalBoards, OffsetX, OffsetY, Origin);
        }
    }

    /// <summary>Fiducial mark definition.</summary>
    public class SsaFiducial
    {
        /// <summary>Shape of the fiducial mark.</summary>
        public SsaMarkShape Shape;

        /// <summary>Fiducial position X in mm.</summary>
        public double X1;

        /// <summary>Fiducial position Y in mm.</summary>
        public double Y1;

        /// <summary>Second fiducial X (or size parameter) in mm.</summary>
        public double X2;

        /// <summary>Second fiducial Y (or size parameter) in mm.</summary>
        public double Y2;

        public override string ToString()
        {
            return string.Format("{0} at ({1:F3},{2:F3}) / ({3:F3},{4:F3})",
                Shape, X1, Y1, X2, Y2);
        }
    }

    /// <summary>Accept/Bad mark definition.</summary>
    public class SsaBoardMark
    {
        public SsaMarkShape Shape;
        public double X;
        public double Y;

        public override string ToString()
        {
            return string.Format("{0} at ({1:F3},{2:F3})", Shape, X, Y);
        }
    }

    /// <summary>Local fiducial pair for a placement (used for fine-pitch ICs).</summary>
    public class SsaLocalFiducialPair
    {
        public SsaLocalFiducialShape Shape;
        public double LF1_X;
        public double LF1_Y;
        public double LF2_X;
        public double LF2_Y;

        public bool IsActive
        {
            get { return Shape != SsaLocalFiducialShape.None; }
        }

        public override string ToString()
        {
            if (!IsActive) return "NONE";
            return string.Format("{0} LF1=({1:F3},{2:F3}) LF2=({3:F3},{4:F3})",
                Shape, LF1_X, LF1_Y, LF2_X, LF2_Y);
        }
    }

    /// <summary>Component definition derived from placements (part number aggregation).</summary>
    public class SsaComponentData
    {
        /// <summary>Part number / name from the machine library.</summary>
        public string PartNumber;

        /// <summary>Part feature / feeder specification.</summary>
        public string PartFeature;

        /// <summary>Description or comment.</summary>
        public string Description;

        /// <summary>Package type inferred from part number pattern (if available).</summary>
        public string PackageType;

        /// <summary>Number of placements referencing this component.</summary>
        public int PlacementCount;

        /// <summary>List of reference designators using this component.</summary>
        public List<string> ReferenceDesignators;

        public SsaComponentData()
        {
            ReferenceDesignators = new List<string>();
        }

        public override string ToString()
        {
            return string.Format("{0} [{1}] - {2} placements",
                PartNumber, PackageType ?? "Unknown", PlacementCount);
        }
    }

    /// <summary>Package/graphic data derived from component groupings.</summary>
    public class SsaPackageData
    {
        /// <summary>Package type identifier.</summary>
        public string PackageType;

        /// <summary>Component part numbers that use this package.</summary>
        public List<string> PartNumbers;

        /// <summary>Number of components using this package.</summary>
        public int ComponentCount;

        public SsaPackageData()
        {
            PartNumbers = new List<string>();
        }
    }

    /// <summary>Single component placement record.</summary>
    public class SsaPlacement
    {
        /// <summary>Reference designator (e.g. "R1", "U3").</summary>
        public string RefDes;

        /// <summary>X position relative to board origin, in mm.</summary>
        public double X;

        /// <summary>Y position relative to board origin, in mm.</summary>
        public double Y;

        /// <summary>Z height offset in mm (usually 0).</summary>
        public double Z;

        /// <summary>Rotation angle in degrees (0-359.9).</summary>
        public double Rotation;

        /// <summary>Local fiducial data for this placement.</summary>
        public SsaLocalFiducialPair LocalFiducials;

        /// <summary>Cluster/Camera number.</summary>
        public int ClusterNumber;

        /// <summary>Skip flag: false=Mount, true=Skip.</summary>
        public bool Skip;

        /// <summary>Part number / name (must match machine library).</summary>
        public string PartNumber;

        /// <summary>Part feature (feeder specification).</summary>
        public string PartFeature;

        /// <summary>Description / comment.</summary>
        public string Description;

        /// <summary>Assigned board side (Top/Bottom). Derived from context or defaults to Top.</summary>
        public string Side;

        /// <summary>Inferred package type from part number prefix pattern.</summary>
        public string PackageType;

        /// <summary>Reference designator prefix (component class: R, C, U, etc.).</summary>
        public string RefDesPrefix
        {
            get
            {
                if (string.IsNullOrEmpty(RefDes)) return "";
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < RefDes.Length; i++)
                {
                    if (char.IsLetter(RefDes[i]))
                        sb.Append(RefDes[i]);
                    else
                        break;
                }
                return sb.ToString();
            }
        }

        /// <summary>Check if this is a fiducial placement.</summary>
        public bool IsFiducial
        {
            get
            {
                if (string.IsNullOrEmpty(RefDes)) return false;
                string prefix = RefDesPrefix.ToUpperInvariant();
                return prefix == "FID" || prefix == "FD" || prefix == "FIDUCIAL";
            }
        }

        public SsaPlacement()
        {
            LocalFiducials = new SsaLocalFiducialPair();
            Side = "Top";
        }

        public override string ToString()
        {
            return string.Format("{0} ({1:F3},{2:F3}) {3:F1}° P/N:{4} {5}",
                RefDes, X, Y, Rotation, PartNumber, Skip ? "[SKIP]" : "");
        }
    }

    /// <summary>Complete PCB configuration from the [PCB] section.</summary>
    public class SsaPcbConfig
    {
        public SsaUnitSystem Units;
        public SsaCoordinateOrigin Coordinate;
        public double Rotation;
        public double PlacementOriginX;
        public double PlacementOriginY;
        public SsaFiducial Fiducial;
        public SsaBoardMark AcceptMark;
        public SsaBoardMark BadMark;

        public SsaPcbConfig()
        {
            Units = SsaUnitSystem.Unknown;
            Coordinate = SsaCoordinateOrigin.Unknown;
            Fiducial = new SsaFiducial();
            AcceptMark = new SsaBoardMark();
            BadMark = new SsaBoardMark();
        }
    }

    /// <summary>Complete board configuration from the [BOARD] section.</summary>
    public class SsaBoardConfig
    {
        public string BoardName;
        public SsaBoardOutline Outline;
        public SsaArrayConfig Array;

        public SsaBoardConfig()
        {
            BoardName = "";
            Outline = new SsaBoardOutline();
            Array = new SsaArrayConfig();
        }
    }

    // =========================================================================
    // Unit Converter
    // =========================================================================

    /// <summary>Converts between unit systems. All internal storage is millimeters.</summary>
    public static class SsaUnitConverter
    {
        public const double InchToMm = 25.4;
        public const double MilToMm = 0.0254;

        /// <summary>Convert a value from the source unit system to millimeters.</summary>
        public static double ToMillimeters(double value, SsaUnitSystem sourceUnit)
        {
            switch (sourceUnit)
            {
                case SsaUnitSystem.Millimeter:
                    return value;
                case SsaUnitSystem.Inch:
                    return value * InchToMm;
                case SsaUnitSystem.Mil:
                    return value * MilToMm;
                default:
                    return value; // Assume mm if unknown
            }
        }
    }

    // =========================================================================
    // SSA Parse Result
    // =========================================================================

    /// <summary>Complete parsed SSA file data.</summary>
    public class SsaData
    {
        // --- Raw Sections ---
        public SsaPcbConfig Pcb;
        public SsaBoardConfig Board;
        public List<SsaPlacement> Placements;

        // --- Derived Data ---
        public Dictionary<string, SsaComponentData> Components;
        public Dictionary<string, SsaPackageData> Packages;
        public List<SsaFiducial> AllFiducials;

        // --- Metadata ---
        public string SourceFile;
        public SsaUnitSystem OriginalUnits;
        public bool UnitsConverted;
        public List<string> ParseWarnings;
        public List<string> ValidationErrors;

        public SsaData()
        {
            Pcb = new SsaPcbConfig();
            Board = new SsaBoardConfig();
            Placements = new List<SsaPlacement>();
            Components = new Dictionary<string, SsaComponentData>();
            Packages = new Dictionary<string, SsaPackageData>();
            AllFiducials = new List<SsaFiducial>();
            ParseWarnings = new List<string>();
            ValidationErrors = new List<string>();
        }

        /// <summary>Board outline shortcut.</summary>
        public SsaBoardOutline BoardOutline { get { return Board.Outline; } }

        /// <summary>Circuit/array outline: full panel dimensions.</summary>
        public SsaBoardOutline CircuitOutline
        {
            get
            {
                SsaBoardOutline co = new SsaBoardOutline();
                if (Board.Array != null && Board.Array.Columns > 0 && Board.Array.Rows > 0)
                {
                    co.Width = Board.Outline.Width +
                        (Board.Array.Columns - 1) * Math.Abs(Board.Array.OffsetX);
                    co.Height = Board.Outline.Height +
                        (Board.Array.Rows - 1) * Math.Abs(Board.Array.OffsetY);
                    co.Thickness = Board.Outline.Thickness;
                }
                else
                {
                    co.Width = Board.Outline.Width;
                    co.Height = Board.Outline.Height;
                    co.Thickness = Board.Outline.Thickness;
                }
                return co;
            }
        }

        /// <summary>Print a summary report.</summary>
        public string GetReport()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== SSA Import Summary ===");
            sb.AppendLine();

            sb.AppendLine(string.Format("Source File: {0}", SourceFile ?? "(unknown)"));
            sb.AppendLine(string.Format("Original Units: {0}", OriginalUnits));
            if (UnitsConverted)
                sb.AppendLine("(Auto-converted to millimeters)");
            sb.AppendLine();

            sb.AppendLine(string.Format("Board Name: {0}", Board.BoardName));
            sb.AppendLine(string.Format("PCB Size: {0}", Board.Outline));
            sb.AppendLine(string.Format("Coordinate System: {0}", Pcb.Coordinate));
            sb.AppendLine();

            if (Board.Array != null && Board.Array.TotalBoards > 1)
            {
                sb.AppendLine("ARRAY CONFIGURATION");
                sb.AppendLine(string.Format("  {0}", Board.Array));
                sb.AppendLine(string.Format("  Panel Size: {0}", CircuitOutline));
                sb.AppendLine();
            }

            sb.AppendLine("PLACEMENTS");
            int skipCount = 0;
            foreach (var p in Placements)
                if (p.Skip) skipCount++;
            sb.AppendLine(string.Format("  Total: {0}", Placements.Count));
            sb.AppendLine(string.Format("  Active: {0}", Placements.Count - skipCount));
            sb.AppendLine(string.Format("  Skipped: {0}", skipCount));
            sb.AppendLine();

            sb.AppendLine("COMPONENTS");
            sb.AppendLine(string.Format("  Unique: {0}", Components.Count));
            sb.AppendLine();

            sb.AppendLine("FIDUCIALS");
            if (Pcb.Fiducial.Shape != SsaMarkShape.None)
                sb.AppendLine(string.Format("  Board Fiducial: {0}", Pcb.Fiducial));
            int lfCount = 0;
            foreach (var p in Placements)
                if (p.LocalFiducials.IsActive) lfCount++;
            sb.AppendLine(string.Format("  Local Fiducials: {0} placement(s)", lfCount));

            if (ParseWarnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("WARNINGS:");
                foreach (string w in ParseWarnings)
                    sb.AppendLine("  " + w);
            }

            return sb.ToString();
        }
    }

    // =========================================================================
    // SSA Parser
    // =========================================================================

    /// <summary>
    /// Parser for Samsung Standard ASCII (Extended SSA) format files.
    /// Supports MARK3 and T-Solution exports.
    /// All values are normalized to millimeters internally.
    /// </summary>
    public class SsaParser
    {
        private List<string> _lines;
        private SsaData _data;
        private SsaUnitSystem _fileUnits;

        public SsaData ParsedData { get { return _data; } }

        /// <summary>Parse an SSA file from the given path.</summary>
        public SsaData Parse(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("SSA file not found: " + filePath);

            string content = File.ReadAllText(filePath, Encoding.ASCII);
            return ParseContent(content, filePath);
        }

        /// <summary>Parse SSA content from a string.</summary>
        public SsaData ParseContent(string content, string sourceName)
        {
            _data = new SsaData();
            _data.SourceFile = sourceName;
            _fileUnits = SsaUnitSystem.Millimeter; // Default

            // Normalize line endings and split
            content = content.Replace("\r\n", "\n").Replace("\r", "\n");
            _lines = new List<string>();
            string[] rawLines = content.Split('\n');
            for (int i = 0; i < rawLines.Length; i++)
                _lines.Add(rawLines[i]);

            // Parse each section
            ParseVersion();
            ParsePcbSection();
            ParseBoardSection();
            ParsePlacementsSection();

            // Record unit conversion state
            _data.OriginalUnits = _fileUnits;
            _data.UnitsConverted = (_fileUnits != SsaUnitSystem.Millimeter);

            // Build derived data
            BuildComponentData();
            BuildPackageData();
            BuildFiducialList();

            return _data;
        }

        // =====================================================================
        // Section Parsers
        // =====================================================================

        private void ParseVersion()
        {
            // [VERSION] section is typically empty or has version info
            FindSection("VERSION");
        }

        private void ParsePcbSection()
        {
            int startLine = FindSection("PCB");
            if (startLine < 0)
            {
                _data.ParseWarnings.Add("[PCB] section not found");
                return;
            }

            int endLine = FindSectionEnd(startLine);
            for (int i = startLine + 1; i < endLine; i++)
            {
                string line = _lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                string key, value;
                SplitKeyValue(line, out key, out value);

                switch (key.ToUpperInvariant())
                {
                    case "UNIT SYSTEM":
                        _fileUnits = ParseUnitSystem(value);
                        _data.Pcb.Units = _fileUnits;
                        break;
                    case "COORDINATE":
                        _data.Pcb.Coordinate = ParseCoordinateOrigin(value);
                        break;
                    case "ROTATION":
                        _data.Pcb.Rotation = ParseDouble(value);
                        break;
                    case "PLACEMENT ORIGIN":
                        ParsePlacementOrigin(value);
                        break;
                    case "FIDUCIAL":
                        ParseFiducialLine(value);
                        break;
                    case "ACCEPT MARK":
                        _data.Pcb.AcceptMark = ParseBoardMark(value);
                        break;
                    case "BAD MARK":
                        _data.Pcb.BadMark = ParseBoardMark(value);
                        break;
                }
            }
        }

        private void ParseBoardSection()
        {
            int startLine = FindSection("BOARD");
            if (startLine < 0)
            {
                _data.ParseWarnings.Add("[BOARD] section not found");
                return;
            }

            int endLine = FindSectionEnd(startLine);
            for (int i = startLine + 1; i < endLine; i++)
            {
                string line = _lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                string key, value;
                SplitKeyValue(line, out key, out value);

                switch (key.ToUpperInvariant())
                {
                    case "BOARD NAME":
                        _data.Board.BoardName = value.Trim();
                        break;
                    case "PCB SIZE":
                        ParsePcbSize(value);
                        break;
                    case "ARRAY":
                        ParseArrayConfig(value);
                        break;
                    case "ARRAY OFFSET":
                        ParseArrayOffset(value);
                        break;
                }
            }
        }

        private void ParsePlacementsSection()
        {
            int startLine = FindSection("PLACEMENTS");
            if (startLine < 0)
            {
                _data.ParseWarnings.Add("[PLACEMENTS] section not found");
                return;
            }

            int endLine = FindSectionEnd(startLine);
            for (int i = startLine + 1; i < endLine; i++)
            {
                string line = _lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                // Placement lines start with a quoted reference designator
                if (line.StartsWith("\""))
                {
                    SsaPlacement p = ParsePlacementLine(line, i + 1);
                    if (p != null)
                        _data.Placements.Add(p);
                }
            }
        }

        // =====================================================================
        // Field Parsers
        // =====================================================================

        private void ParsePlacementOrigin(string value)
        {
            string[] parts = SplitComma(value);
            double x = 0, y = 0;
            int idx = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0) continue;
                if (idx == 0) { x = ConvertToMm(ParseDouble(p)); idx++; }
                else if (idx == 1) { y = ConvertToMm(ParseDouble(p)); idx++; }
            }
            _data.Pcb.PlacementOriginX = x;
            _data.Pcb.PlacementOriginY = y;
        }

        private void ParseFiducialLine(string value)
        {
            string[] parts = SplitComma(value);
            if (parts.Length >= 5)
            {
                _data.Pcb.Fiducial.Shape = ParseMarkShape(parts[0].Trim());
                _data.Pcb.Fiducial.X1 = ConvertToMm(ParseDouble(parts[1]));
                _data.Pcb.Fiducial.Y1 = ConvertToMm(ParseDouble(parts[2]));
                _data.Pcb.Fiducial.X2 = ConvertToMm(ParseDouble(parts[3]));
                _data.Pcb.Fiducial.Y2 = ConvertToMm(ParseDouble(parts[4]));
            }
            else if (parts.Length >= 3)
            {
                _data.Pcb.Fiducial.Shape = ParseMarkShape(parts[0].Trim());
                _data.Pcb.Fiducial.X1 = ConvertToMm(ParseDouble(parts[1]));
                _data.Pcb.Fiducial.Y1 = ConvertToMm(ParseDouble(parts[2]));
            }
        }

        private SsaBoardMark ParseBoardMark(string value)
        {
            SsaBoardMark mark = new SsaBoardMark();
            string[] parts = SplitComma(value);
            if (parts.Length >= 1)
                mark.Shape = ParseMarkShape(parts[0].Trim());
            if (parts.Length >= 2)
                mark.X = ConvertToMm(ParseDouble(parts[1]));
            if (parts.Length >= 3)
                mark.Y = ConvertToMm(ParseDouble(parts[2]));
            return mark;
        }

        private void ParsePcbSize(string value)
        {
            string[] parts = SplitComma(value);
            if (parts.Length >= 3)
            {
                _data.Board.Outline.Width = ConvertToMm(ParseDouble(parts[0]));
                _data.Board.Outline.Height = ConvertToMm(ParseDouble(parts[1]));
                _data.Board.Outline.Thickness = ConvertToMm(ParseDouble(parts[2]));
            }
            else if (parts.Length >= 2)
            {
                _data.Board.Outline.Width = ConvertToMm(ParseDouble(parts[0]));
                _data.Board.Outline.Height = ConvertToMm(ParseDouble(parts[1]));
            }
        }

        private void ParseArrayConfig(string value)
        {
            string[] parts = SplitComma(value);
            if (parts.Length >= 2)
            {
                _data.Board.Array.Columns = ParseInt(parts[0]);
                _data.Board.Array.Rows = ParseInt(parts[1]);
            }
            if (parts.Length >= 3)
            {
                _data.Board.Array.Origin = ParseCoordinateOrigin(parts[2].Trim());
            }
        }

        private void ParseArrayOffset(string value)
        {
            string[] parts = SplitComma(value);
            if (parts.Length >= 2)
            {
                _data.Board.Array.OffsetX = ConvertToMm(ParseDouble(parts[0]));
                _data.Board.Array.OffsetY = ConvertToMm(ParseDouble(parts[1]));
            }
        }

        private SsaPlacement ParsePlacementLine(string line, int lineNumber)
        {
            // Tokenize the line respecting quoted strings
            List<string> tokens = TokenizeLine(line);
            if (tokens.Count < 6)
            {
                _data.ParseWarnings.Add(string.Format(
                    "Line {0}: Placement has only {1} tokens (expected 13-15)",
                    lineNumber, tokens.Count));
                return null;
            }

            SsaPlacement p = new SsaPlacement();
            try
            {
                int idx = 0;

                // 1: Reference Designator (quoted)
                p.RefDes = StripQuotes(tokens[idx++]);

                // 2-4: X, Y, Z
                p.X = ConvertToMm(ParseDouble(tokens[idx++]));
                p.Y = ConvertToMm(ParseDouble(tokens[idx++]));
                p.Z = ConvertToMm(ParseDouble(tokens[idx++]));

                // 5: Rotation
                p.Rotation = ParseDouble(tokens[idx++]);

                // 6: Local Fiducial Shape
                p.LocalFiducials.Shape = ParseLocalFidShape(tokens[idx++]);

                // 7-10: LF1_X, LF1_Y, LF2_X, LF2_Y
                if (idx + 3 < tokens.Count)
                {
                    p.LocalFiducials.LF1_X = ConvertToMm(ParseDouble(tokens[idx++]));
                    p.LocalFiducials.LF1_Y = ConvertToMm(ParseDouble(tokens[idx++]));
                    p.LocalFiducials.LF2_X = ConvertToMm(ParseDouble(tokens[idx++]));
                    p.LocalFiducials.LF2_Y = ConvertToMm(ParseDouble(tokens[idx++]));
                }

                // 11: Cluster/Camera Number
                if (idx < tokens.Count)
                    p.ClusterNumber = ParseInt(tokens[idx++]);

                // 12: Skip flag (0=Mount, 1=Skip)
                if (idx < tokens.Count)
                    p.Skip = (ParseInt(tokens[idx++]) != 0);

                // 13: Part Number (quoted)
                if (idx < tokens.Count)
                    p.PartNumber = StripQuotes(tokens[idx++]);

                // 14: Part Feature (quoted)
                if (idx < tokens.Count)
                    p.PartFeature = StripQuotes(tokens[idx++]);

                // 15: Description/Comment (quoted)
                if (idx < tokens.Count)
                    p.Description = StripQuotes(tokens[idx++]);

                // Default side assignment (SSA files are typically single-side)
                p.Side = "Top";

                // Infer package type from part number prefix
                p.PackageType = InferPackageType(p.PartNumber, p.RefDesPrefix);
            }
            catch (Exception ex)
            {
                _data.ParseWarnings.Add(string.Format(
                    "Line {0}: Error parsing placement: {1}", lineNumber, ex.Message));
                return null;
            }

            return p;
        }

        // =====================================================================
        // Derived Data Builders
        // =====================================================================

        private void BuildComponentData()
        {
            _data.Components.Clear();
            foreach (SsaPlacement p in _data.Placements)
            {
                string key = p.PartNumber ?? "(unknown)";
                SsaComponentData cd;
                if (!_data.Components.TryGetValue(key, out cd))
                {
                    cd = new SsaComponentData();
                    cd.PartNumber = key;
                    cd.PartFeature = p.PartFeature;
                    cd.Description = p.Description;
                    cd.PackageType = p.PackageType;
                    _data.Components[key] = cd;
                }
                cd.PlacementCount++;
                cd.ReferenceDesignators.Add(p.RefDes);
            }
        }

        private void BuildPackageData()
        {
            _data.Packages.Clear();
            foreach (SsaComponentData cd in _data.Components.Values)
            {
                string pkgKey = cd.PackageType ?? "Unknown";
                SsaPackageData pkg;
                if (!_data.Packages.TryGetValue(pkgKey, out pkg))
                {
                    pkg = new SsaPackageData();
                    pkg.PackageType = pkgKey;
                    _data.Packages[pkgKey] = pkg;
                }
                if (!pkg.PartNumbers.Contains(cd.PartNumber))
                    pkg.PartNumbers.Add(cd.PartNumber);
                pkg.ComponentCount += cd.PlacementCount;
            }
        }

        private void BuildFiducialList()
        {
            _data.AllFiducials.Clear();
            // Board-level fiducial
            if (_data.Pcb.Fiducial.Shape != SsaMarkShape.None)
                _data.AllFiducials.Add(_data.Pcb.Fiducial);
        }

        // =====================================================================
        // Package Type Inference
        // =====================================================================

        private string InferPackageType(string partNumber, string refDesPrefix)
        {
            if (!string.IsNullOrEmpty(partNumber))
            {
                string pn = partNumber.ToUpperInvariant();

                // Common chip resistor/capacitor sizes
                string[] sizes = { "0201", "0402", "0603", "0805", "1206", "1210", "2010", "2512" };
                foreach (string sz in sizes)
                {
                    if (pn.Contains(sz))
                        return sz;
                }

                // QFP, SOIC, SOP, QFN, BGA patterns
                if (pn.Contains("QFP")) return "QFP";
                if (pn.Contains("QFN")) return "QFN";
                if (pn.Contains("BGA")) return "BGA";
                if (pn.Contains("SOIC")) return "SOIC";
                if (pn.Contains("SOP")) return "SOP";
                if (pn.Contains("SOT")) return "SOT";
                if (pn.Contains("TSSOP")) return "TSSOP";
            }

            // Fallback: infer from reference designator prefix
            if (!string.IsNullOrEmpty(refDesPrefix))
            {
                switch (refDesPrefix.ToUpperInvariant())
                {
                    case "R": return "Chip Resistor";
                    case "C": return "Chip Capacitor";
                    case "L": return "Inductor";
                    case "U": return "IC";
                    case "D": return "Diode";
                    case "Q": return "Transistor";
                    case "F": return "Fuse";
                    case "J": return "Connector";
                    case "Y": return "Crystal";
                    case "T": return "Transformer";
                    case "LED": return "LED";
                    case "FB": return "Ferrite Bead";
                    case "SW": return "Switch";
                    case "TP": return "Test Point";
                }
            }

            return null;
        }

        // =====================================================================
        // Tokenizer & Utility Methods
        // =====================================================================

        private List<string> TokenizeLine(string line)
        {
            List<string> tokens = new List<string>();
            int i = 0;
            while (i < line.Length)
            {
                // Skip whitespace
                while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
                    i++;
                if (i >= line.Length) break;

                if (line[i] == '"')
                {
                    // Quoted token - find closing quote
                    int start = i;
                    i++; // skip opening quote
                    while (i < line.Length && line[i] != '"')
                        i++;
                    if (i < line.Length) i++; // skip closing quote
                    tokens.Add(line.Substring(start, i - start));
                }
                else
                {
                    // Unquoted token - read until whitespace
                    int start = i;
                    while (i < line.Length && line[i] != ' ' && line[i] != '\t')
                        i++;
                    tokens.Add(line.Substring(start, i - start));
                }
            }
            return tokens;
        }

        private int FindSection(string sectionName)
        {
            string target = "[" + sectionName + "]";
            for (int i = 0; i < _lines.Count; i++)
            {
                if (_lines[i].Trim().Equals(target, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private int FindSectionEnd(int startLine)
        {
            for (int i = startLine + 1; i < _lines.Count; i++)
            {
                string trimmed = _lines[i].Trim();
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                    return i;
            }
            return _lines.Count;
        }

        private void SplitKeyValue(string line, out string key, out string value)
        {
            int eqIdx = line.IndexOf('=');
            if (eqIdx >= 0)
            {
                key = line.Substring(0, eqIdx).Trim();
                value = line.Substring(eqIdx + 1).Trim();
            }
            else
            {
                key = line.Trim();
                value = "";
            }
        }

        private string[] SplitComma(string value)
        {
            return value.Split(',');
        }

        private string StripQuotes(string s)
        {
            if (s == null) return "";
            s = s.Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
                return s.Substring(1, s.Length - 2);
            return s;
        }

        private double ParseDouble(string s)
        {
            if (s == null) return 0;
            s = s.Trim();
            double val;
            if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out val))
                return val;
            return 0;
        }

        private int ParseInt(string s)
        {
            if (s == null) return 0;
            s = s.Trim();
            int val;
            if (int.TryParse(s, out val))
                return val;
            return 0;
        }

        private double ConvertToMm(double value)
        {
            return SsaUnitConverter.ToMillimeters(value, _fileUnits);
        }

        private SsaUnitSystem ParseUnitSystem(string value)
        {
            if (value == null) return SsaUnitSystem.Unknown;
            string v = value.Trim().ToUpperInvariant();
            if (v.Contains("MILIMETER") || v.Contains("MILLIMETER") || v.Contains("MM"))
                return SsaUnitSystem.Millimeter;
            if (v.Contains("INCH") || v.Contains("IN"))
                return SsaUnitSystem.Inch;
            if (v.Contains("MIL") && !v.Contains("MILIMETER") && !v.Contains("MILLIMETER"))
                return SsaUnitSystem.Mil;
            return SsaUnitSystem.Unknown;
        }

        private SsaCoordinateOrigin ParseCoordinateOrigin(string value)
        {
            if (value == null) return SsaCoordinateOrigin.Unknown;
            string v = value.Trim().ToUpperInvariant();
            if (v.Contains("LOWER") && v.Contains("RIGHT")) return SsaCoordinateOrigin.LowerRight;
            if (v.Contains("LOWER") && v.Contains("LEFT")) return SsaCoordinateOrigin.LowerLeft;
            if (v.Contains("UPPER") && v.Contains("RIGHT")) return SsaCoordinateOrigin.UpperRight;
            if (v.Contains("UPPER") && v.Contains("LEFT")) return SsaCoordinateOrigin.UpperLeft;
            return SsaCoordinateOrigin.Unknown;
        }

        private SsaMarkShape ParseMarkShape(string value)
        {
            if (value == null) return SsaMarkShape.Unknown;
            string v = value.Trim().ToUpperInvariant();
            if (v == "NONE") return SsaMarkShape.None;
            if (v == "CIRCLE") return SsaMarkShape.Circle;
            if (v == "SQUARE") return SsaMarkShape.Square;
            if (v == "DIAMOND") return SsaMarkShape.Diamond;
            if (v == "CROSS") return SsaMarkShape.Cross;
            return SsaMarkShape.Unknown;
        }

        private SsaLocalFiducialShape ParseLocalFidShape(string value)
        {
            if (value == null) return SsaLocalFiducialShape.Unknown;
            string v = value.Trim().ToUpperInvariant();
            if (v == "NONE") return SsaLocalFiducialShape.None;
            if (v == "CIRCLE") return SsaLocalFiducialShape.Circle;
            if (v == "SQUARE") return SsaLocalFiducialShape.Square;
            if (v == "DIAMOND") return SsaLocalFiducialShape.Diamond;
            if (v == "CROSS") return SsaLocalFiducialShape.Cross;
            return SsaLocalFiducialShape.Unknown;
        }
    }
}
