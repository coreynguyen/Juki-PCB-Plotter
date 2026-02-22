// =============================================================================
// GenCadImporter.cs
// Converts GenCAD parsed data to project models
// =============================================================================
using System;
using System.Collections.Generic;
using System.Text;
using PCBPlotter.Core.Models;

namespace PCBPlotter.Core.Services
{
    public class GenCadImporter
    {
        private GenCadParser _parser;

        public GenCadData ParsedData
        {
            get { return _parser?.ParsedData; }
        }

        public void Parse(string filePath)
        {
            _parser = new GenCadParser();
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

            // Build packages from shapes
            var pkgMap = new Dictionary<string, Package>();
            foreach (var shape in data.Shapes.Values)
            {
                double minX, minY, maxX, maxY;
                shape.ComputeBounds(out minX, out minY, out maxX, out maxY);

                var pkg = new Package
                {
                    Name = shape.Name,
                    Description = "",
                    Width = data.ToMm(maxX - minX),
                    Length = data.ToMm(maxY - minY)
                };
                packages.Add(pkg);
                pkgMap[shape.Name] = pkg;
            }

            // Build placements
            foreach (var comp in data.Components)
            {
                // Skip through-hole only components for SMT
                if (comp.Shape != null && comp.Shape.Insert == GenCadInsertType.TH)
                    continue;

                Package pkg = null;
                if (!string.IsNullOrEmpty(comp.ShapeName))
                    pkgMap.TryGetValue(comp.ShapeName, out pkg);

                string partNumber = "";
                if (comp.Device != null && !string.IsNullOrEmpty(comp.Device.PartNumber))
                    partNumber = comp.Device.PartNumber;

                var plc = new Placement
                {
                    Reference = comp.RefDes,
                    X = data.ToMm(comp.PlaceX),
                    Y = data.ToMm(comp.PlaceY),
                    Rotation = comp.Rotation,
                    Side = comp.Layer == "BOTTOM" ? BoardSide.Bottom : BoardSide.Top,
                    Package = pkg
                };
                placements.Add(plc);
            }

            // Build fiducials
            foreach (var fid in data.Fiducials)
            {
                fiducials.Add(new Fiducial
                {
                    Name = fid.Name,
                    X = data.ToMm(fid.X),
                    Y = data.ToMm(fid.Y),
                    Side = fid.Layer == "BOTTOM" ? BoardSide.Bottom : BoardSide.Top,
                    Type = FiducialType.Global
                });
            }

            // Build board outline
            if (data.Board.Lines.Count > 0)
            {
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;

                foreach (var l in data.Board.Lines)
                {
                    double x1 = data.ToMm(l.X1), y1 = data.ToMm(l.Y1);
                    double x2 = data.ToMm(l.X2), y2 = data.ToMm(l.Y2);
                    if (x1 < minX) minX = x1;
                    if (x2 < minX) minX = x2;
                    if (y1 < minY) minY = y1;
                    if (y2 < minY) minY = y2;
                    if (x1 > maxX) maxX = x1;
                    if (x2 > maxX) maxX = x2;
                    if (y1 > maxY) maxY = y1;
                    if (y2 > maxY) maxY = y2;
                }

                if (minX != double.MaxValue)
                {
                    board = new BoardDefinition
                    {
                        Width = maxX - minX,
                        Height = maxY - minY,
                        Origin = new System.Windows.Point(minX, minY)
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

            sb.AppendLine("=== GenCAD Import Summary ===");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(data.Header.Version))
            {
                sb.AppendLine("GenCAD Version: " + data.Header.Version);
            }
            if (!string.IsNullOrEmpty(data.Header.Drawing))
            {
                sb.AppendLine("Drawing: " + data.Header.Drawing);
            }
            sb.AppendLine("Units: " + data.Header.Units);
            sb.AppendLine();

            // Count by side and type
            int topCount = 0, botCount = 0;
            int smdCount = 0, thCount = 0;

            foreach (var c in data.Components)
            {
                bool isSmd = (c.Shape != null && c.Shape.Insert == GenCadInsertType.SMD);
                if (c.Layer == "TOP") topCount++;
                else if (c.Layer == "BOTTOM") botCount++;
                if (isSmd) smdCount++;
                else thCount++;
            }

            sb.AppendLine("COMPONENTS");
            sb.AppendLine("  Total:        " + data.Components.Count);
            sb.AppendLine("  Top side:     " + topCount);
            sb.AppendLine("  Bottom side:  " + botCount);
            sb.AppendLine("  SMD:          " + smdCount);
            sb.AppendLine("  Through-hole: " + thCount);
            sb.AppendLine("  Fiducials:    " + data.Fiducials.Count);
            sb.AppendLine();

            sb.AppendLine("LIBRARY");
            sb.AppendLine("  Devices: " + data.Devices.Count);
            sb.AppendLine("  Shapes:  " + data.Shapes.Count);
            sb.AppendLine("  Pads:    " + data.Pads.Count);
            sb.AppendLine();

            // Board outline
            if (data.Board.Lines.Count > 0)
            {
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;

                foreach (var l in data.Board.Lines)
                {
                    double x1 = data.ToMm(l.X1), y1 = data.ToMm(l.Y1);
                    double x2 = data.ToMm(l.X2), y2 = data.ToMm(l.Y2);
                    if (x1 < minX) minX = x1;
                    if (x2 < minX) minX = x2;
                    if (y1 < minY) minY = y1;
                    if (y2 < minY) minY = y2;
                    if (x1 > maxX) maxX = x1;
                    if (x2 > maxX) maxX = x2;
                    if (y1 > maxY) maxY = y1;
                    if (y2 > maxY) maxY = y2;
                }

                if (minX != double.MaxValue)
                {
                    sb.AppendLine("BOARD OUTLINE");
                    sb.AppendLine(string.Format("  Size: {0:F2} x {1:F2} mm",
                        maxX - minX, maxY - minY));
                    sb.AppendLine("  Segments: " + data.Board.Lines.Count);
                }
            }

            return sb.ToString();
        }
    }
}
