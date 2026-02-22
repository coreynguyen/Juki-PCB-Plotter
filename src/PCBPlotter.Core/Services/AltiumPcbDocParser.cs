// =============================================================================
// AltiumPcbDocParser.cs
// Parses Altium Designer ASCII .PCBdoc / .PCB / .PRO files
// Extracts CAD data for CAM / CIS / SMT Mounter workflows
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PCBPlotter.Core.Services
{
    // =========================================================================
    // Unit conversion for Altium files
    // =========================================================================
    public static class AltiumUnits
    {
        // Altium internal unit: mil (1 mil = 0.001 inch)
        // 1 inch = 25.4 mm, so 1 mil = 0.0254 mm
        public const double MilToMm = 0.0254;

        public static double ParseMil(string value)
        {
            if (value == null) return 0.0;
            value = value.Trim();

            // Strip "mil" suffix if present
            if (value.EndsWith("mil", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(0, value.Length - 3);

            // Strip "mm" suffix (some fields may already be in mm)
            bool isMm = false;
            if (value.EndsWith("mm", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(0, value.Length - 2);
                isMm = true;
            }

            double result;
            if (!double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out result))
                return 0.0;

            return isMm ? result : result * MilToMm;
        }
    }

    // =========================================================================
    // Data structures
    // =========================================================================

    public enum AltiumOutlineVertexKind
    {
        Line = 0,
        Arc = 1
    }

    public class AltiumOutlineVertex
    {
        public AltiumOutlineVertexKind Kind;
        public double X_mm;
        public double Y_mm;
        public double CX_mm;
        public double CY_mm;
        public double StartAngle;
        public double EndAngle;
        public double Radius_mm;
    }

    public class AltiumPadRecord
    {
        public int ComponentId = -1;
        public string Net = "";
        public string Name = "";
        public double X_mm;
        public double Y_mm;
        public double XSize_mm;
        public double YSize_mm;
        public string Shape = "";
        public double HoleSize_mm;
        public double Rotation;
        public string Layer = "";
        public bool IsSmd;
    }

    public class AltiumComponentRecord
    {
        public int Id;
        public string Designator = "";
        public string Comment = "";
        public string Pattern = "";
        public string Layer = "";
        public bool IsBottomSide;
        public double X_mm;
        public double Y_mm;
        public double Rotation;
        public double Height_mm;
        public string SourceLibReference = "";
        public string SourceDescription = "";
        public string FootprintDescription = "";
        public string SourceFootprintLibrary = "";
        public string SourceComponentLibrary = "";
        public bool IsFiducial;
        public bool IsLocked;
        public double PackageWidth_mm;
        public double PackageHeight_mm;
        public int PadCount;
        public int PinCount;
        public string MountType = "SMD";
    }

    public class AltiumPackageData
    {
        public string PatternName = "";
        public string Description = "";
        public double Width_mm;
        public double Height_mm;
        public int PadCount;
        public int SmdPadCount;
        public int ThtPadCount;
        public string MountType = "SMD";
        public List<AltiumPadRecord> Pads = new List<AltiumPadRecord>();
        public int FirstComponentId = -1;
    }

    // =========================================================================
    // Record field parser
    // =========================================================================
    public class AltiumRecordFields
    {
        private Dictionary<string, string> _fields = new Dictionary<string, string>();

        public AltiumRecordFields(string line)
        {
            if (line == null) return;
            line = line.TrimStart('|').TrimEnd('\r', '\n', ' ');

            string[] parts = line.Split('|');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                int eq = part.IndexOf('=');
                if (eq > 0)
                {
                    string key = part.Substring(0, eq).ToUpperInvariant();
                    string val = part.Substring(eq + 1);
                    _fields[key] = val;
                }
            }
        }

        public string Get(string key)
        {
            string val;
            if (_fields.TryGetValue(key.ToUpperInvariant(), out val))
                return val;
            return "";
        }

        public int GetInt(string key, int defaultValue)
        {
            string val = Get(key);
            if (val.Length == 0) return defaultValue;
            int result;
            if (int.TryParse(val, out result)) return result;
            return defaultValue;
        }

        public double GetDouble(string key, double defaultValue)
        {
            string val = Get(key);
            if (val.Length == 0) return defaultValue;
            double result;
            if (double.TryParse(val, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out result))
                return result;
            return defaultValue;
        }

        public bool GetBool(string key)
        {
            return Get(key).Equals("TRUE", StringComparison.OrdinalIgnoreCase);
        }

        public string GetIndexed(string prefix, int index)
        {
            return Get(prefix + index.ToString());
        }
    }

    // =========================================================================
    // Parser result
    // =========================================================================
    public class AltiumPcbDocData
    {
        public string FileName = "";
        public string Version = "";
        public string Date = "";
        public double OriginX_mm;
        public double OriginY_mm;
        public int DisplayUnit;

        public List<AltiumOutlineVertex> BoardOutline = new List<AltiumOutlineVertex>();
        public List<AltiumComponentRecord> Components = new List<AltiumComponentRecord>();
        public List<AltiumPadRecord> Pads = new List<AltiumPadRecord>();
        public Dictionary<string, AltiumPackageData> Packages = new Dictionary<string, AltiumPackageData>();

        public List<string> Warnings = new List<string>();

        // Computed board dimensions
        public double BoardWidth_mm;
        public double BoardHeight_mm;
    }

    // =========================================================================
    // Main parser
    // =========================================================================
    public class AltiumPcbDocParser
    {
        private AltiumPcbDocData _data;
        private Dictionary<int, AltiumComponentRecord> _compById = new Dictionary<int, AltiumComponentRecord>();
        private Dictionary<int, string> _designatorByCompId = new Dictionary<int, string>();
        private Dictionary<int, string> _commentByCompId = new Dictionary<int, string>();

        public AltiumPcbDocData Parse(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("File not found: " + filePath);

            _data = new AltiumPcbDocData();
            _compById.Clear();
            _designatorByCompId.Clear();
            _commentByCompId.Clear();

            // Read with permissive encoding
            string allText;
            using (var sr = new StreamReader(filePath, Encoding.Default))
            {
                allText = sr.ReadToEnd();
            }

            // Validate it's an ASCII Altium file
            if (!allText.Contains("|RECORD="))
            {
                throw new InvalidDataException(
                    "File does not appear to be an ASCII Altium PCBdoc. " +
                    "If binary, re-export from Altium as ASCII format.");
            }

            // Normalize line endings
            allText = allText.Replace("\r\n", "\n").Replace("\r", "\n");
            string[] lines = allText.Split('\n');

            // Pass 1: Collect all records by type
            var boardLines = new List<string>();
            var componentLines = new List<string>();
            var padLines = new List<string>();
            var textLines = new List<string>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (!line.StartsWith("|RECORD=")) continue;

                string recType = ExtractRecordType(line);
                switch (recType)
                {
                    case "BOARD": boardLines.Add(line); break;
                    case "COMPONENT": componentLines.Add(line); break;
                    case "PAD": padLines.Add(line); break;
                    case "TEXT": textLines.Add(line); break;
                }
            }

            // Parse Board header + outline
            if (boardLines.Count > 0)
                ParseBoardRecord(boardLines[0]);

            // Parse Text records first (to build designator/comment maps)
            foreach (string line in textLines)
                ParseTextRecord(line);

            // Parse Component records
            foreach (string line in componentLines)
                ParseComponentRecord(line);

            // Parse Pad records
            foreach (string line in padLines)
                ParsePadRecord(line);

            // Post-processing
            ResolveDesignators();
            BuildPackageLibrary();
            ComputeComponentMetrics();
            IdentifyFiducials();
            ComputeBoardDimensions();

            return _data;
        }

        private string ExtractRecordType(string line)
        {
            int start = line.IndexOf("RECORD=");
            if (start < 0) return "";
            start += 7;
            int end = line.IndexOf('|', start);
            if (end < 0) end = line.Length;
            return line.Substring(start, end - start).ToUpperInvariant();
        }

        private void ParseBoardRecord(string line)
        {
            var f = new AltiumRecordFields(line);

            _data.FileName = f.Get("FILENAME");
            _data.Version = f.Get("VERSION");
            _data.Date = f.Get("DATE") + " " + f.Get("TIME");
            _data.OriginX_mm = AltiumUnits.ParseMil(f.Get("ORIGINX"));
            _data.OriginY_mm = AltiumUnits.ParseMil(f.Get("ORIGINY"));
            _data.DisplayUnit = f.GetInt("DISPLAYUNIT", 0);

            // Extract polygon outline vertices
            for (int i = 0; i < 1000; i++)
            {
                string kindStr = f.GetIndexed("KIND", i);
                if (kindStr.Length == 0) break;

                var v = new AltiumOutlineVertex();
                int kind;
                int.TryParse(kindStr, out kind);
                v.Kind = (kind == 1) ? AltiumOutlineVertexKind.Arc : AltiumOutlineVertexKind.Line;
                v.X_mm = AltiumUnits.ParseMil(f.GetIndexed("VX", i));
                v.Y_mm = AltiumUnits.ParseMil(f.GetIndexed("VY", i));
                v.CX_mm = AltiumUnits.ParseMil(f.GetIndexed("CX", i));
                v.CY_mm = AltiumUnits.ParseMil(f.GetIndexed("CY", i));
                v.StartAngle = f.GetDouble("SA" + i.ToString(), 0.0);
                v.EndAngle = f.GetDouble("EA" + i.ToString(), 0.0);
                v.Radius_mm = AltiumUnits.ParseMil(f.GetIndexed("R", i));

                _data.BoardOutline.Add(v);
            }
        }

        private void ParseTextRecord(string line)
        {
            var f = new AltiumRecordFields(line);

            string compStr = f.Get("COMPONENT");
            if (compStr.Length == 0) return;

            int compId;
            if (!int.TryParse(compStr, out compId)) return;

            string text = f.Get("TEXT");

            if (f.GetBool("DESIGNATOR"))
            {
                _designatorByCompId[compId] = text;
            }
            else if (f.GetBool("COMMENT"))
            {
                _commentByCompId[compId] = text;
            }
        }

        private void ParseComponentRecord(string line)
        {
            var f = new AltiumRecordFields(line);

            var comp = new AltiumComponentRecord();
            comp.Id = f.GetInt("ID", -1);
            comp.Pattern = f.Get("PATTERN");
            comp.Layer = f.Get("LAYER").ToUpperInvariant();
            comp.IsBottomSide = (comp.Layer == "BOTTOM");
            comp.X_mm = AltiumUnits.ParseMil(f.Get("X"));
            comp.Y_mm = AltiumUnits.ParseMil(f.Get("Y"));
            comp.Rotation = f.GetDouble("ROTATION", 0.0);
            comp.Height_mm = AltiumUnits.ParseMil(f.Get("HEIGHT"));
            comp.IsLocked = f.GetBool("LOCKED");

            comp.Designator = f.Get("SOURCEDESIGNATOR");
            comp.SourceLibReference = f.Get("SOURCELIBREFERENCE");
            comp.SourceDescription = f.Get("SOURCEDESCRIPTION");
            comp.FootprintDescription = f.Get("FOOTPRINTDESCRIPTION");
            comp.SourceFootprintLibrary = f.Get("SOURCEFOOTPRINTLIBRARY");
            comp.SourceComponentLibrary = f.Get("SOURCECOMPONENTLIBRARY");

            _data.Components.Add(comp);
            _compById[comp.Id] = comp;
        }

        private void ParsePadRecord(string line)
        {
            var f = new AltiumRecordFields(line);

            var pad = new AltiumPadRecord();
            pad.ComponentId = f.GetInt("COMPONENT", -1);
            pad.Net = f.Get("NET");
            pad.Name = f.Get("NAME");
            pad.X_mm = AltiumUnits.ParseMil(f.Get("X"));
            pad.Y_mm = AltiumUnits.ParseMil(f.Get("Y"));
            pad.XSize_mm = AltiumUnits.ParseMil(f.Get("XSIZE"));
            pad.YSize_mm = AltiumUnits.ParseMil(f.Get("YSIZE"));
            pad.Shape = f.Get("SHAPE").ToUpperInvariant();
            pad.HoleSize_mm = AltiumUnits.ParseMil(f.Get("HOLESIZE"));
            pad.Rotation = f.GetDouble("ROTATION", 0.0);
            pad.Layer = f.Get("LAYER").ToUpperInvariant();
            pad.IsSmd = (pad.HoleSize_mm < 0.001);

            _data.Pads.Add(pad);
        }

        private void ResolveDesignators()
        {
            foreach (var comp in _data.Components)
            {
                if (comp.Designator.Length == 0)
                {
                    string desig;
                    if (_designatorByCompId.TryGetValue(comp.Id, out desig))
                        comp.Designator = desig;
                }

                string comment;
                if (_commentByCompId.TryGetValue(comp.Id, out comment))
                {
                    if (comment != "Comment" && comment != ".Comment")
                        comp.Comment = comment;
                }
            }
        }

        private void BuildPackageLibrary()
        {
            // Group pads by component ID
            var padsByComp = new Dictionary<int, List<AltiumPadRecord>>();
            foreach (var pad in _data.Pads)
            {
                if (pad.ComponentId < 0) continue;
                List<AltiumPadRecord> list;
                if (!padsByComp.TryGetValue(pad.ComponentId, out list))
                {
                    list = new List<AltiumPadRecord>();
                    padsByComp[pad.ComponentId] = list;
                }
                list.Add(pad);
            }

            // Build package entries
            foreach (var comp in _data.Components)
            {
                string pkgName = NormalizePackageName(comp.Pattern);
                if (pkgName.Length == 0) continue;
                if (_data.Packages.ContainsKey(pkgName)) continue;

                var pkg = new AltiumPackageData();
                pkg.PatternName = comp.Pattern;
                pkg.FirstComponentId = comp.Id;
                pkg.Description = comp.FootprintDescription.Length > 0
                    ? comp.FootprintDescription : comp.SourceDescription;

                List<AltiumPadRecord> compPads;
                if (padsByComp.TryGetValue(comp.Id, out compPads) && compPads.Count > 0)
                {
                    pkg.PadCount = compPads.Count;
                    double minX = double.MaxValue, minY = double.MaxValue;
                    double maxX = double.MinValue, maxY = double.MinValue;
                    int smd = 0, tht = 0;

                    foreach (var p in compPads)
                    {
                        double px = p.X_mm;
                        double py = p.Y_mm;
                        double halfW = p.XSize_mm / 2.0;
                        double halfH = p.YSize_mm / 2.0;
                        if (px - halfW < minX) minX = px - halfW;
                        if (px + halfW > maxX) maxX = px + halfW;
                        if (py - halfH < minY) minY = py - halfH;
                        if (py + halfH > maxY) maxY = py + halfH;
                        if (p.IsSmd) smd++; else tht++;
                    }

                    pkg.Width_mm = maxX - minX;
                    pkg.Height_mm = maxY - minY;
                    pkg.SmdPadCount = smd;
                    pkg.ThtPadCount = tht;
                    pkg.MountType = (tht > 0 && smd == 0) ? "THT" :
                                    (tht > 0) ? "MIXED" : "SMD";
                    pkg.Pads = compPads;
                }

                _data.Packages[pkgName] = pkg;
            }
        }

        private void ComputeComponentMetrics()
        {
            var padsByComp = new Dictionary<int, List<AltiumPadRecord>>();
            foreach (var pad in _data.Pads)
            {
                if (pad.ComponentId < 0) continue;
                List<AltiumPadRecord> list;
                if (!padsByComp.TryGetValue(pad.ComponentId, out list))
                {
                    list = new List<AltiumPadRecord>();
                    padsByComp[pad.ComponentId] = list;
                }
                list.Add(pad);
            }

            foreach (var comp in _data.Components)
            {
                List<AltiumPadRecord> compPads;
                if (!padsByComp.TryGetValue(comp.Id, out compPads)) continue;

                comp.PadCount = compPads.Count;
                int smd = 0, tht = 0;
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;

                foreach (var p in compPads)
                {
                    if (p.X_mm < minX) minX = p.X_mm;
                    if (p.X_mm > maxX) maxX = p.X_mm;
                    if (p.Y_mm < minY) minY = p.Y_mm;
                    if (p.Y_mm > maxY) maxY = p.Y_mm;
                    if (p.IsSmd) smd++; else tht++;
                }

                comp.PinCount = smd;
                comp.PackageWidth_mm = maxX - minX;
                comp.PackageHeight_mm = maxY - minY;
                comp.MountType = (tht > 0 && smd == 0) ? "THT" :
                                 (tht > 0) ? "MIXED" : "SMD";
            }
        }

        private void IdentifyFiducials()
        {
            foreach (var comp in _data.Components)
            {
                string desig = comp.Designator.ToUpperInvariant();
                string pattern = comp.Pattern.ToUpperInvariant();
                string desc = comp.SourceDescription.ToUpperInvariant();
                string fpDesc = comp.FootprintDescription.ToUpperInvariant();

                comp.IsFiducial =
                    desig.StartsWith("FD") ||
                    desig.StartsWith("FID") ||
                    pattern.Contains("FIDUCIAL") ||
                    pattern.Contains("FID") ||
                    desc.Contains("FIDUCIAL") ||
                    fpDesc.Contains("FIDUCIAL");
            }
        }

        private void ComputeBoardDimensions()
        {
            if (_data.BoardOutline.Count > 0)
            {
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;

                foreach (var v in _data.BoardOutline)
                {
                    if (v.X_mm < minX) minX = v.X_mm;
                    if (v.X_mm > maxX) maxX = v.X_mm;
                    if (v.Y_mm < minY) minY = v.Y_mm;
                    if (v.Y_mm > maxY) maxY = v.Y_mm;
                }

                _data.BoardWidth_mm = maxX - minX;
                _data.BoardHeight_mm = maxY - minY;
            }
        }

        private string NormalizePackageName(string pattern)
        {
            if (pattern == null) return "";
            return pattern.Trim();
        }
    }
}
