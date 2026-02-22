using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using PCBPlotter.Core.Models;
using ValorMdb;

namespace PCBPlotter.Core.Services
{
    /// <summary>
    /// Imports CircuitCAM Express / Valor CIM .cpf/.mdb files into the project model.
    /// Converts ValorMdbParser output (mils, Valor coordinate system) into project
    /// data (mm, board-relative coordinates).
    /// </summary>
    public class CpfImporter
    {
        /// <summary>
        /// Parsed Valor database (populated after Parse is called).
        /// </summary>
        public ValorDatabase ParsedData { get; private set; }

        /// <summary>
        /// Parse a .cpf or .mdb file and store the result.
        /// </summary>
        public void Parse(string filePath)
        {
            ParsedData = ValorMdbParser.Parse(filePath);
        }

        /// <summary>
        /// Convert the parsed Valor data into project model objects.
        /// Coordinates are converted from Valor mils to board-relative mm.
        /// </summary>
        public void ConvertToProject(Project project,
            out List<Package> packages,
            out List<Placement> placements,
            out List<Fiducial> fiducials,
            out BoardDefinition board)
        {
            if (ParsedData == null)
                throw new InvalidOperationException("No data parsed. Call Parse() first.");

            packages = new List<Package>();
            placements = new List<Placement>();
            fiducials = new List<Fiducial>();

            // Build a lookup of existing packages in the project by name
            var existingPackages = new Dictionary<string, Package>(StringComparer.OrdinalIgnoreCase);
            foreach (var pkg in project.Packages)
            {
                if (!string.IsNullOrEmpty(pkg.Name) && !existingPackages.ContainsKey(pkg.Name))
                    existingPackages[pkg.Name] = pkg;
            }

            // Build a lookup of existing components in the project by part number
            var existingComponents = new Dictionary<string, Component>(StringComparer.OrdinalIgnoreCase);
            foreach (var comp in project.Components)
            {
                if (!string.IsNullOrEmpty(comp.PartNumber) && !existingComponents.ContainsKey(comp.PartNumber))
                    existingComponents[comp.PartNumber] = comp;
            }

            // Track new packages we create during this import
            var newPackages = new Dictionary<string, Package>(StringComparer.OrdinalIgnoreCase);

            // ── Convert SMT placements ──────────────────────────────────────
            foreach (var rec in ParsedData.SmtPlacements)
            {
                double xMm = rec.X * ValorCoords.MilToMm;
                double yMm = rec.Y * ValorCoords.MilToMm;

                BoardSide side = rec.LayerID == 2 ? BoardSide.Bottom : BoardSide.Top;

                var placement = new Placement(rec.Ref, xMm, yMm, rec.Rot, side);

                // Resolve or create package
                string pkgName = (rec.Package ?? "").Trim();
                if (!string.IsNullOrEmpty(pkgName))
                {
                    Package pkg;
                    if (existingPackages.TryGetValue(pkgName, out pkg))
                    {
                        placement.Package = pkg;
                    }
                    else if (newPackages.TryGetValue(pkgName, out pkg))
                    {
                        placement.Package = pkg;
                    }
                    else
                    {
                        pkg = new Package(pkgName);
                        newPackages[pkgName] = pkg;
                        packages.Add(pkg);
                        placement.Package = pkg;
                    }
                }

                // Resolve or create component
                string partNum = (rec.PartNumber ?? "").Trim();
                if (!string.IsNullOrEmpty(partNum))
                {
                    Component comp;
                    if (existingComponents.TryGetValue(partNum, out comp))
                    {
                        placement.Component = comp;
                        if (!comp.ReferenceDesignators.Contains(rec.Ref))
                            comp.ReferenceDesignators.Add(rec.Ref);
                    }
                    else
                    {
                        comp = new Component(partNum);
                        comp.ReferenceDesignators.Add(rec.Ref);
                        if (placement.Package != null)
                            comp.DefaultPackage = placement.Package;
                        existingComponents[partNum] = comp;
                        placement.Component = comp;
                    }
                }

                placements.Add(placement);
            }

            // ── Convert fiducials ───────────────────────────────────────────
            foreach (var fid in ParsedData.Fiducials)
            {
                double xMm = fid.X * ValorCoords.MilToMm;
                double yMm = fid.Y * ValorCoords.MilToMm;

                BoardSide side = fid.LayerID == 2 ? BoardSide.Bottom : BoardSide.Top;

                var fiducial = new Fiducial(fid.Ref, xMm, yMm, FiducialType.Global)
                {
                    Side = side,
                    Diameter = 1.0,
                    SharedBetweenSides = true
                };

                fiducials.Add(fiducial);
            }

            // ── Build board definition from outline data ────────────────────
            board = BuildBoardDefinition();
        }

        /// <summary>
        /// Builds a BoardDefinition from the parsed outline segments.
        /// </summary>
        private BoardDefinition BuildBoardDefinition()
        {
            var board = new BoardDefinition();

            if (ParsedData.BoardOutline != null && ParsedData.BoardOutline.Count > 0)
            {
                // Find bounding box of board outline in Valor mils, then convert to mm
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;

                foreach (var seg in ParsedData.BoardOutline)
                {
                    minX = Math.Min(minX, Math.Min(seg.X1, seg.X2));
                    minY = Math.Min(minY, Math.Min(seg.Y1, seg.Y2));
                    maxX = Math.Max(maxX, Math.Max(seg.X1, seg.X2));
                    maxY = Math.Max(maxY, Math.Max(seg.Y1, seg.Y2));
                }

                board.Width = (maxX - minX) * ValorCoords.MilToMm;
                board.Height = (maxY - minY) * ValorCoords.MilToMm;

                // Convert outline segments to point list (mm)
                var outlinePoints = new List<Point>();
                foreach (var seg in ParsedData.BoardOutline)
                {
                    outlinePoints.Add(new Point(seg.X1 * ValorCoords.MilToMm, seg.Y1 * ValorCoords.MilToMm));
                    outlinePoints.Add(new Point(seg.X2 * ValorCoords.MilToMm, seg.Y2 * ValorCoords.MilToMm));
                }
                board.BoardOutline = outlinePoints;
            }

            if (ParsedData.CircuitOutline != null && ParsedData.CircuitOutline.Count > 0)
            {
                var circuitPoints = new List<Point>();
                foreach (var seg in ParsedData.CircuitOutline)
                {
                    circuitPoints.Add(new Point(seg.X1 * ValorCoords.MilToMm, seg.Y1 * ValorCoords.MilToMm));
                    circuitPoints.Add(new Point(seg.X2 * ValorCoords.MilToMm, seg.Y2 * ValorCoords.MilToMm));
                }
                board.CircuitOutline = circuitPoints;
            }

            // Set origin from _ORG marker (converted to mm)
            if (ParsedData.OriginX != 0 || ParsedData.OriginY != 0)
            {
                board.Origin = new Point(
                    ParsedData.OriginX * ValorCoords.MilToMm,
                    ParsedData.OriginY * ValorCoords.MilToMm);
            }

            return board;
        }

        /// <summary>
        /// Get a display-friendly summary of the parsed data.
        /// </summary>
        public string GetSummary()
        {
            if (ParsedData == null) return "No data loaded.";

            int smtTop = ParsedData.SmtPlacements.Count(p => p.LayerID == 1);
            int smtBot = ParsedData.SmtPlacements.Count(p => p.LayerID == 2);

            string summary = string.Format(
                "SMT Placements: {0} ({1} top, {2} bottom)\n" +
                "THT Components: {3}\n" +
                "Fiducials: {4}\n" +
                "Other Locations: {5}",
                ParsedData.SmtPlacements.Count, smtTop, smtBot,
                ParsedData.ThtComponents.Count,
                ParsedData.Fiducials.Count,
                ParsedData.OtherLocations.Count);

            if (ParsedData.BoardOutline.Count > 0)
            {
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                foreach (var seg in ParsedData.BoardOutline)
                {
                    minX = Math.Min(minX, Math.Min(seg.X1, seg.X2));
                    minY = Math.Min(minY, Math.Min(seg.Y1, seg.Y2));
                    maxX = Math.Max(maxX, Math.Max(seg.X1, seg.X2));
                    maxY = Math.Max(maxY, Math.Max(seg.Y1, seg.Y2));
                }
                double wMm = (maxX - minX) * ValorCoords.MilToMm;
                double hMm = (maxY - minY) * ValorCoords.MilToMm;
                summary += string.Format("\nBoard: {0:F2} x {1:F2} mm", wMm, hMm);
            }

            if (ParsedData.OriginX != 0 || ParsedData.OriginY != 0)
            {
                summary += string.Format("\nOrigin (_ORG): {0:F3}, {1:F3} mm",
                    ParsedData.OriginX * ValorCoords.MilToMm,
                    ParsedData.OriginY * ValorCoords.MilToMm);
            }

            // Count unique packages
            var uniquePackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in ParsedData.SmtPlacements)
            {
                if (!string.IsNullOrEmpty(p.Package))
                    uniquePackages.Add(p.Package);
            }
            summary += string.Format("\nUnique Packages: {0}", uniquePackages.Count);

            // Count unique part numbers
            var uniqueParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in ParsedData.SmtPlacements)
            {
                if (!string.IsNullOrEmpty(p.PartNumber))
                    uniqueParts.Add(p.PartNumber);
            }
            summary += string.Format("\nUnique Part Numbers: {0}", uniqueParts.Count);

            return summary;
        }
    }
}
