// =============================================================================
// ProtelProParser.cs
// Parses Protel PCB Assembly 2.8 ASCII (.PRO / .PCB) files
// Extracts CAD data for CAM / CIS / SMT Mounter workflows
//
// File format: "PCB FILE 6 VERSION 2.80" or "PCB assemblyFILE 6 VERSION 2.80"
// Internal coordinate unit: 1 unit = 0.001 mil = 1 micro-inch
// All outputs converted to millimeters.
// =============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PCBPlotter.Core.Services
{
    // =========================================================================
    // Data structures
    // =========================================================================
    public class ProtelProData
    {
        public string FileVersion = "";
        public List<ProtelComponentRecord> Components = new List<ProtelComponentRecord>();
        public List<ProtelTrackSegment> BoardOutlineTracks = new List<ProtelTrackSegment>();
        public List<ProtelArcRecord> BoardOutlineArcs = new List<ProtelArcRecord>();
        public Dictionary<string, ProtelPackageData> Packages = new Dictionary<string, ProtelPackageData>();
    }

    public enum ProtelBoardSide { Top, Bottom, Unknown }

    public enum ProtelPadShape
    {
        Round = 1,
        Rectangle = 2,
        Octagonal = 3,
        Unknown = 0
    }

    public class ProtelPadRecord
    {
        public double X_mm;
        public double Y_mm;
        public double XSize_mm;
        public double YSize_mm;
        public ProtelPadShape Shape;
        public double HoleSize_mm;
        public double Rotation;
        public int Layer;
        public int Net;
        public bool IsSmd;
        public string PinName = "";
    }

    public class ProtelTrackSegment
    {
        public double X1_mm, Y1_mm;
        public double X2_mm, Y2_mm;
        public double Width_mm;
        public int Layer;
    }

    public class ProtelArcRecord
    {
        public double CX_mm, CY_mm;
        public double Radius_mm;
        public double StartAngle;
        public double EndAngle;
        public double Width_mm;
        public int Layer;
    }

    public class ProtelComponentRecord
    {
        public int Index;
        public string Pattern = "";
        public string Designator = "";
        public string Comment = "";
        public double X_mm, Y_mm;
        public int PlacementLayer;
        public ProtelBoardSide Side;
        public double Rotation;
        public bool IsFiducial;
        public List<ProtelPadRecord> Pads = new List<ProtelPadRecord>();
        public List<ProtelTrackSegment> Tracks = new List<ProtelTrackSegment>();
        public List<ProtelArcRecord> Arcs = new List<ProtelArcRecord>();
        public double PackageWidth_mm;
        public double PackageHeight_mm;
        public int PadCount;
        public int SmdPadCount;
        public int ThtPadCount;
        public string MountType = "SMD";
    }

    public class ProtelPackageData
    {
        public string PatternName = "";
        public string Description = "";
        public double Width_mm;
        public double Height_mm;
        public int PadCount;
        public int SmdPadCount;
        public int ThtPadCount;
        public string MountType = "SMD";
        public List<ProtelPadRecord> Pads = new List<ProtelPadRecord>();
        public List<ProtelTrackSegment> Tracks = new List<ProtelTrackSegment>();
    }

    // =========================================================================
    // Protel layer constants
    // =========================================================================
    public static class ProtelLayers
    {
        public const int TopCopper = 1;
        public const int BottomCopper = 16;
        public const int TopOverlay = 17;
        public const int BottomOverlay = 18;
        public const int KeepOut = 28;
        public const int Mechanical1 = 29;
        public const int MultiLayer = 34;

        public static bool IsBoardOutlineLayer(int layer)
        {
            return layer == KeepOut || layer == Mechanical1;
        }
    }

    // =========================================================================
    // Token parser helper
    // =========================================================================
    internal class ProtelTokenLine
    {
        private string[] _tokens;

        public ProtelTokenLine(string line)
        {
            if (line == null) line = "";
            _tokens = line.Trim().Split(new char[] { ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries);
        }

        public int Count { get { return _tokens.Length; } }

        public string Str(int index)
        {
            if (index < 0 || index >= _tokens.Length) return "";
            return _tokens[index];
        }

        public long Long(int index)
        {
            if (index < 0 || index >= _tokens.Length) return 0;
            long val;
            if (long.TryParse(_tokens[index], out val)) return val;
            return 0;
        }

        public int Int(int index)
        {
            if (index < 0 || index >= _tokens.Length) return 0;
            int val;
            if (int.TryParse(_tokens[index], out val)) return val;
            return 0;
        }

        public double Dbl(int index)
        {
            if (index < 0 || index >= _tokens.Length) return 0.0;
            double val;
            if (double.TryParse(_tokens[index], NumberStyles.Float,
                CultureInfo.InvariantCulture, out val))
                return val;
            return 0.0;
        }

        // Protel internal unit: 1 unit = 0.001 mil = 0.0000254 mm
        private const double InternalToMm = 0.0000254;

        public double Mm(int index)
        {
            return Long(index) * InternalToMm;
        }
    }

    // =========================================================================
    // Main parser
    // =========================================================================
    public class ProtelProParser
    {
        private string[] _lines;
        private int _pos;

        public ProtelProData ParsedData { get; private set; }

        /// <summary>
        /// Checks if a file appears to be a Protel PCB 2.8 ASCII file
        /// </summary>
        public static bool IsProtelFormat(string filePath)
        {
            try
            {
                using (var sr = new StreamReader(filePath, Encoding.Default))
                {
                    string firstLine = sr.ReadLine();
                    if (firstLine == null) return false;
                    firstLine = firstLine.TrimStart();
                    return firstLine.StartsWith("PCB FILE") ||
                           firstLine.StartsWith("PCB assemblyFILE");
                }
            }
            catch
            {
                return false;
            }
        }

        public void Parse(string filePath)
        {
            ParsedData = new ProtelProData();

            string allText;
            using (var sr = new StreamReader(filePath, Encoding.Default))
            {
                allText = sr.ReadToEnd();
            }

            allText = allText.Replace("\r\n", "\n").Replace("\r", "\n");
            _lines = allText.Split('\n');
            _pos = 0;

            ParseHeader();

            while (_pos < _lines.Length)
            {
                string line = CurrentLine();

                if (line == "COMP")
                {
                    _pos++;
                    ParseComponent();
                }
                else if (line == "FT")
                {
                    _pos++;
                    ParseFreeTrack();
                }
                else if (line == "FA")
                {
                    _pos++;
                    ParseFreeArc();
                }
                else if (line == "FS")
                {
                    _pos++;
                    SkipFreeString();
                }
                else if (line == "FV")
                {
                    _pos++;
                    SkipDataLine();
                }
                else if (line == "NETDEF")
                {
                    _pos++;
                    SkipNetDef();
                }
                else
                {
                    _pos++;
                }
            }

            BuildPackageLibrary();
            ComputeComponentMetrics();
            IdentifyFiducials();
        }

        private string CurrentLine()
        {
            if (_pos >= _lines.Length) return "";
            return _lines[_pos].Trim();
        }

        private string ReadLine()
        {
            if (_pos >= _lines.Length) return "";
            string line = _lines[_pos].Trim();
            _pos++;
            return line;
        }

        private void ParseHeader()
        {
            string headerLine = ReadLine();
            if (headerLine.StartsWith("PCB"))
            {
                ParsedData.FileVersion = headerLine;
            }

            if (_pos < _lines.Length)
            {
                string countsLine = CurrentLine();
                if (countsLine.Length > 0 && char.IsDigit(countsLine[0]))
                    _pos++;
            }
        }

        private void ParseComponent()
        {
            var comp = new ProtelComponentRecord();
            comp.Index = ParsedData.Components.Count;

            // Line 1: Pattern name
            comp.Pattern = ReadLine();

            // Line 2: Placement data
            string placementStr = ReadLine();
            var pl = new ProtelTokenLine(placementStr);
            comp.X_mm = pl.Mm(2);
            comp.Y_mm = pl.Mm(3);
            comp.PlacementLayer = pl.Int(4);
            comp.Rotation = pl.Dbl(11);

            comp.Side = (comp.PlacementLayer == ProtelLayers.BottomCopper) ?
                ProtelBoardSide.Bottom : ProtelBoardSide.Top;

            while (_pos < _lines.Length)
            {
                string line = CurrentLine();

                if (line == "ENDCOMP")
                {
                    _pos++;
                    break;
                }
                else if (line == "CS")
                {
                    _pos++;
                    ParseComponentString(comp);
                }
                else if (line == "CP")
                {
                    _pos++;
                    ParseComponentPad(comp);
                }
                else if (line == "CT")
                {
                    _pos++;
                    ParseComponentTrack(comp);
                }
                else if (line == "CA")
                {
                    _pos++;
                    ParseComponentArc(comp);
                }
                else
                {
                    _pos++;
                }
            }

            ParsedData.Components.Add(comp);
        }

        private void ParseComponentString(ProtelComponentRecord comp)
        {
            string dataLine = ReadLine();
            string textLine = ReadLine();

            if (comp.Designator.Length == 0)
            {
                comp.Designator = textLine;
            }
            else if (comp.Comment.Length == 0)
            {
                comp.Comment = textLine;
            }
        }

        private void ParseComponentPad(ProtelComponentRecord comp)
        {
            string dataLine = ReadLine();
            string pinLine = ReadLine();

            var t = new ProtelTokenLine(dataLine);
            var pad = new ProtelPadRecord();
            pad.X_mm = t.Mm(2);
            pad.Y_mm = t.Mm(3);
            pad.XSize_mm = t.Mm(4);
            pad.YSize_mm = t.Mm(5);
            int shapeInt = t.Int(6);
            pad.Shape = (ProtelPadShape)shapeInt;
            pad.HoleSize_mm = t.Mm(13);
            pad.Layer = t.Int(15);
            pad.Net = t.Int(16);
            pad.Rotation = t.Dbl(18);
            pad.IsSmd = (pad.HoleSize_mm < 0.001);
            pad.PinName = pinLine.Trim();

            comp.Pads.Add(pad);
        }

        private void ParseComponentTrack(ProtelComponentRecord comp)
        {
            string dataLine = ReadLine();
            if (_pos < _lines.Length && CurrentLine().StartsWith(" "))
                _pos++;

            var t = new ProtelTokenLine(dataLine);
            var trk = new ProtelTrackSegment();
            trk.X1_mm = t.Mm(2);
            trk.Y1_mm = t.Mm(3);
            trk.X2_mm = t.Mm(4);
            trk.Y2_mm = t.Mm(5);
            trk.Width_mm = t.Mm(6);
            trk.Layer = t.Int(7);

            comp.Tracks.Add(trk);
        }

        private void ParseComponentArc(ProtelComponentRecord comp)
        {
            string dataLine = ReadLine();
            if (_pos < _lines.Length && CurrentLine().StartsWith(" "))
                _pos++;

            var t = new ProtelTokenLine(dataLine);
            var arc = new ProtelArcRecord();
            arc.CX_mm = t.Mm(2);
            arc.CY_mm = t.Mm(3);
            arc.Radius_mm = t.Mm(4);
            arc.StartAngle = t.Dbl(5);
            arc.EndAngle = t.Dbl(6);
            arc.Width_mm = t.Mm(7);
            arc.Layer = t.Int(8);

            comp.Arcs.Add(arc);
        }

        private void ParseFreeTrack()
        {
            string dataLine = ReadLine();
            if (_pos < _lines.Length && CurrentLine().StartsWith(" "))
                _pos++;

            var t = new ProtelTokenLine(dataLine);
            var trk = new ProtelTrackSegment();
            trk.X1_mm = t.Mm(2);
            trk.Y1_mm = t.Mm(3);
            trk.X2_mm = t.Mm(4);
            trk.Y2_mm = t.Mm(5);
            trk.Width_mm = t.Mm(6);
            trk.Layer = t.Int(7);

            if (ProtelLayers.IsBoardOutlineLayer(trk.Layer))
                ParsedData.BoardOutlineTracks.Add(trk);
        }

        private void ParseFreeArc()
        {
            string dataLine = ReadLine();
            if (_pos < _lines.Length && CurrentLine().StartsWith(" "))
                _pos++;

            var t = new ProtelTokenLine(dataLine);
            var arc = new ProtelArcRecord();
            arc.CX_mm = t.Mm(2);
            arc.CY_mm = t.Mm(3);
            arc.Radius_mm = t.Mm(4);
            arc.StartAngle = t.Dbl(5);
            arc.EndAngle = t.Dbl(6);
            arc.Width_mm = t.Mm(7);
            arc.Layer = t.Int(8);

            if (ProtelLayers.IsBoardOutlineLayer(arc.Layer))
                ParsedData.BoardOutlineArcs.Add(arc);
        }

        private void SkipFreeString()
        {
            ReadLine();
            ReadLine();
        }

        private void SkipDataLine()
        {
            ReadLine();
        }

        private void SkipNetDef()
        {
            ReadLine();
            ReadLine();
        }

        private void BuildPackageLibrary()
        {
            foreach (var comp in ParsedData.Components)
            {
                string pkgKey = comp.Pattern.Trim();
                if (pkgKey.Length == 0) continue;
                if (ParsedData.Packages.ContainsKey(pkgKey)) continue;

                var pkg = new ProtelPackageData();
                pkg.PatternName = pkgKey;
                pkg.PadCount = comp.Pads.Count;

                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                int smd = 0, tht = 0;

                foreach (var p in comp.Pads)
                {
                    double halfW = p.XSize_mm / 2.0;
                    double halfH = p.YSize_mm / 2.0;
                    double px = p.X_mm, py = p.Y_mm;
                    if (px - halfW < minX) minX = px - halfW;
                    if (px + halfW > maxX) maxX = px + halfW;
                    if (py - halfH < minY) minY = py - halfH;
                    if (py + halfH > maxY) maxY = py + halfH;
                    if (p.IsSmd) smd++; else tht++;
                }

                if (comp.Pads.Count > 0)
                {
                    pkg.Width_mm = maxX - minX;
                    pkg.Height_mm = maxY - minY;
                }

                pkg.SmdPadCount = smd;
                pkg.ThtPadCount = tht;
                pkg.MountType = (tht > 0 && smd == 0) ? "THT" :
                                (tht > 0) ? "MIXED" : "SMD";

                foreach (var p in comp.Pads)
                {
                    var relPad = new ProtelPadRecord();
                    relPad.X_mm = p.X_mm - comp.X_mm;
                    relPad.Y_mm = p.Y_mm - comp.Y_mm;
                    relPad.XSize_mm = p.XSize_mm;
                    relPad.YSize_mm = p.YSize_mm;
                    relPad.Shape = p.Shape;
                    relPad.HoleSize_mm = p.HoleSize_mm;
                    relPad.Rotation = p.Rotation;
                    relPad.Layer = p.Layer;
                    relPad.IsSmd = p.IsSmd;
                    relPad.PinName = p.PinName;
                    pkg.Pads.Add(relPad);
                }

                foreach (var t in comp.Tracks)
                {
                    var relT = new ProtelTrackSegment();
                    relT.X1_mm = t.X1_mm - comp.X_mm;
                    relT.Y1_mm = t.Y1_mm - comp.Y_mm;
                    relT.X2_mm = t.X2_mm - comp.X_mm;
                    relT.Y2_mm = t.Y2_mm - comp.Y_mm;
                    relT.Width_mm = t.Width_mm;
                    relT.Layer = t.Layer;
                    pkg.Tracks.Add(relT);
                }

                ParsedData.Packages[pkgKey] = pkg;
            }
        }

        private void ComputeComponentMetrics()
        {
            foreach (var comp in ParsedData.Components)
            {
                if (comp.Pads.Count == 0) continue;

                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                int smd = 0, tht = 0;

                foreach (var p in comp.Pads)
                {
                    if (p.X_mm < minX) minX = p.X_mm;
                    if (p.X_mm > maxX) maxX = p.X_mm;
                    if (p.Y_mm < minY) minY = p.Y_mm;
                    if (p.Y_mm > maxY) maxY = p.Y_mm;
                    if (p.IsSmd) smd++; else tht++;
                }

                comp.PadCount = comp.Pads.Count;
                comp.SmdPadCount = smd;
                comp.ThtPadCount = tht;
                comp.PackageWidth_mm = maxX - minX;
                comp.PackageHeight_mm = maxY - minY;
                comp.MountType = (tht > 0 && smd == 0) ? "THT" :
                                 (tht > 0) ? "MIXED" : "SMD";
            }
        }

        private void IdentifyFiducials()
        {
            foreach (var comp in ParsedData.Components)
            {
                string desig = comp.Designator.ToUpperInvariant();
                string pattern = comp.Pattern.ToUpperInvariant();
                string comment = comp.Comment.ToUpperInvariant();

                comp.IsFiducial =
                    desig.StartsWith("FD") ||
                    desig.StartsWith("FID") ||
                    pattern.Contains("FIDUCIAL") ||
                    pattern.Contains("FID") ||
                    comment.Contains("FIDUCIAL");
            }
        }
    }
}
