// =============================================================================
// ProtelImporter.cs
// Converts Protel PCB 2.8 ASCII parsed data to project models
// =============================================================================
using System;
using System.Collections.Generic;
using System.Text;
using PCBPlotter.Core.Models;

namespace PCBPlotter.Core.Services
{
    public class ProtelImporter
    {
        private ProtelProParser _parser;

        public ProtelProData ParsedData
        {
            get { return _parser?.ParsedData; }
        }

        public void Parse(string filePath)
        {
            _parser = new ProtelProParser();
            _parser.Parse(filePath);
        }

        public void ConvertToProject(Project project,
            out List<Package> packages,
            out List<Placement> placements,
            out List<Fiducial> fiducials,
            out BoardDefinition board)
        {
            packages = new List<Package>();
            placements = new List<Placement>();
            fiducials = new List<Fiducial>();
            board = null;

            if (_parser?.ParsedData == null)
                return;

            var data = _parser.ParsedData;

            // Build packages
            var pkgMap = new Dictionary<string, Package>();
            foreach (var srcPkg in data.Packages.Values)
            {
                var pkg = new Package
                {
                    Name = srcPkg.PatternName,
                    Description = srcPkg.Description,
                    Width = srcPkg.Width_mm,
                    Height = srcPkg.Height_mm,
                    PadCount = srcPkg.PadCount,
                    MountType = srcPkg.MountType
                };
                packages.Add(pkg);
                pkgMap[srcPkg.PatternName] = pkg;
            }

            // Build placements and fiducials
            foreach (var comp in data.Components)
            {
                if (comp.IsFiducial)
                {
                    var fid = new Fiducial
                    {
                        Name = comp.Designator,
                        X = comp.X_mm,
                        Y = comp.Y_mm,
                        IsBottom = comp.Side == ProtelBoardSide.Bottom,
                        Type = FiducialType.Global
                    };
                    fiducials.Add(fid);
                }
                else if (comp.MountType != "THT")
                {
                    Package pkg = null;
                    if (!string.IsNullOrEmpty(comp.Pattern))
                        pkgMap.TryGetValue(comp.Pattern, out pkg);

                    var plc = new Placement
                    {
                        RefDes = comp.Designator,
                        PartNumber = comp.Comment ?? "",
                        X = comp.X_mm,
                        Y = comp.Y_mm,
                        Rotation = comp.Rotation,
                        IsBottom = comp.Side == ProtelBoardSide.Bottom,
                        Package = pkg
                    };
                    placements.Add(plc);
                }
            }

            // Build board outline
            if (data.BoardOutlineTracks.Count > 0 || data.BoardOutlineArcs.Count > 0)
            {
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;

                foreach (var t in data.BoardOutlineTracks)
                {
                    if (t.X1_mm < minX) minX = t.X1_mm;
                    if (t.X2_mm < minX) minX = t.X2_mm;
                    if (t.X1_mm > maxX) maxX = t.X1_mm;
                    if (t.X2_mm > maxX) maxX = t.X2_mm;
                    if (t.Y1_mm < minY) minY = t.Y1_mm;
                    if (t.Y2_mm < minY) minY = t.Y2_mm;
                    if (t.Y1_mm > maxY) maxY = t.Y1_mm;
                    if (t.Y2_mm > maxY) maxY = t.Y2_mm;
                }

                foreach (var a in data.BoardOutlineArcs)
                {
                    double r = a.Radius_mm;
                    if (a.CX_mm - r < minX) minX = a.CX_mm - r;
                    if (a.CX_mm + r > maxX) maxX = a.CX_mm + r;
                    if (a.CY_mm - r < minY) minY = a.CY_mm - r;
                    if (a.CY_mm + r > maxY) maxY = a.CY_mm + r;
                }

                if (minX != double.MaxValue)
                {
                    board = new BoardDefinition
                    {
                        Width = maxX - minX,
                        Height = maxY - minY,
                        OriginX = minX,
                        OriginY = minY
                    };
                }
            }
        }

        public string GetSummary()
        {
            if (_parser?.ParsedData == null)
                return "No data loaded.";

            var data = _parser.ParsedData;
            var sb = new StringBuilder();

            sb.AppendLine("=== Protel PCB 2.8 Import Summary ===");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(data.FileVersion))
            {
                sb.AppendLine("Format: " + data.FileVersion);
                sb.AppendLine();
            }

            // Component stats
            int topCount = 0, botCount = 0;
            int smdCount = 0, thtCount = 0, fidCount = 0;

            foreach (var c in data.Components)
            {
                if (c.Side == ProtelBoardSide.Top) topCount++;
                else botCount++;
                if (c.MountType == "SMD") smdCount++;
                else if (c.MountType == "THT") thtCount++;
                if (c.IsFiducial) fidCount++;
            }

            sb.AppendLine("COMPONENTS");
            sb.AppendLine("  Total:        " + data.Components.Count);
            sb.AppendLine("  Top side:     " + topCount);
            sb.AppendLine("  Bottom side:  " + botCount);
            sb.AppendLine("  SMD:          " + smdCount);
            sb.AppendLine("  Through-hole: " + thtCount);
            sb.AppendLine("  Fiducials:    " + fidCount);
            sb.AppendLine();

            sb.AppendLine("PACKAGES");
            sb.AppendLine("  Unique: " + data.Packages.Count);
            sb.AppendLine();

            // Board outline
            if (data.BoardOutlineTracks.Count > 0 || data.BoardOutlineArcs.Count > 0)
            {
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;

                foreach (var t in data.BoardOutlineTracks)
                {
                    if (t.X1_mm < minX) minX = t.X1_mm;
                    if (t.X2_mm < minX) minX = t.X2_mm;
                    if (t.X1_mm > maxX) maxX = t.X1_mm;
                    if (t.X2_mm > maxX) maxX = t.X2_mm;
                    if (t.Y1_mm < minY) minY = t.Y1_mm;
                    if (t.Y2_mm < minY) minY = t.Y2_mm;
                    if (t.Y1_mm > maxY) maxY = t.Y1_mm;
                    if (t.Y2_mm > maxY) maxY = t.Y2_mm;
                }

                foreach (var a in data.BoardOutlineArcs)
                {
                    double r = a.Radius_mm;
                    if (a.CX_mm - r < minX) minX = a.CX_mm - r;
                    if (a.CX_mm + r > maxX) maxX = a.CX_mm + r;
                    if (a.CY_mm - r < minY) minY = a.CY_mm - r;
                    if (a.CY_mm + r > maxY) maxY = a.CY_mm + r;
                }

                if (minX != double.MaxValue)
                {
                    sb.AppendLine("BOARD OUTLINE");
                    sb.AppendLine(string.Format("  Size: {0:F2} x {1:F2} mm",
                        maxX - minX, maxY - minY));
                    sb.AppendLine(string.Format("  Track segments: {0}",
                        data.BoardOutlineTracks.Count));
                    sb.AppendLine(string.Format("  Arc segments: {0}",
                        data.BoardOutlineArcs.Count));
                }
            }

            return sb.ToString();
        }
    }
}
