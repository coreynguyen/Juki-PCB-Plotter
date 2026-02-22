using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using PCBPlotter.Core.Models;

namespace PCBPlotter.Core.Services
{
    /// <summary>
    /// Imports Altium Designer ASCII .pcbdoc/.pcb/.pro files into the project model.
    /// Converts AltiumPcbDocParser output into project data (mm coordinates).
    /// </summary>
    public class AltiumImporter
    {
        /// <summary>
        /// Parsed Altium data (populated after Parse is called).
        /// </summary>
        public AltiumPcbDocData ParsedData { get; private set; }

        /// <summary>
        /// Parse an Altium ASCII .pcbdoc, .pcb, or .pro file and store the result.
        /// </summary>
        public void Parse(string filePath)
        {
            var parser = new AltiumPcbDocParser();
            ParsedData = parser.Parse(filePath);
        }

        /// <summary>
        /// Convert the parsed Altium data into project model objects.
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

            // ── Convert placements and fiducials ────────────────────────────────
            foreach (var rec in ParsedData.Components)
            {
                BoardSide side = rec.IsBottomSide ? BoardSide.Bottom : BoardSide.Top;

                // Handle fiducials separately
                if (rec.IsFiducial)
                {
                    var fiducial = new Fiducial(rec.Designator, rec.X_mm, rec.Y_mm, FiducialType.Global)
                    {
                        Side = side,
                        Diameter = 1.0,
                        SharedBetweenSides = true
                    };
                    fiducials.Add(fiducial);
                    continue;
                }

                // Skip THT-only components for SMT mounter
                if (rec.MountType == "THT")
                    continue;

                var placement = new Placement(rec.Designator, rec.X_mm, rec.Y_mm, rec.Rotation, side);

                // Resolve or create package
                string pkgName = (rec.Pattern ?? "").Trim();
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
                string partNum = (rec.SourceLibReference ?? "").Trim();
                if (string.IsNullOrEmpty(partNum))
                    partNum = (rec.Comment ?? "").Trim();

                if (!string.IsNullOrEmpty(partNum))
                {
                    Component comp;
                    if (existingComponents.TryGetValue(partNum, out comp))
                    {
                        placement.Component = comp;
                        if (!comp.ReferenceDesignators.Contains(rec.Designator))
                            comp.ReferenceDesignators.Add(rec.Designator);
                    }
                    else
                    {
                        comp = new Component(partNum);
                        comp.ReferenceDesignators.Add(rec.Designator);
                        if (placement.Package != null)
                            comp.DefaultPackage = placement.Package;

                        // Add description if available
                        if (!string.IsNullOrEmpty(rec.SourceDescription))
                            comp.Description = rec.SourceDescription;

                        existingComponents[partNum] = comp;
                        placement.Component = comp;
                    }
                }

                placements.Add(placement);
            }

            // ── Build board definition from outline data ────────────────────────
            board = BuildBoardDefinition();
        }

        /// <summary>
        /// Builds a BoardDefinition from the parsed outline data.
        /// </summary>
        private BoardDefinition BuildBoardDefinition()
        {
            var board = new BoardDefinition();

            if (ParsedData.BoardOutline != null && ParsedData.BoardOutline.Count > 0)
            {
                board.Width = ParsedData.BoardWidth_mm;
                board.Height = ParsedData.BoardHeight_mm;

                // Convert outline vertices to point list
                var outlinePoints = new List<Point>();
                foreach (var v in ParsedData.BoardOutline)
                {
                    outlinePoints.Add(new Point(v.X_mm, v.Y_mm));
                }
                board.BoardOutline = outlinePoints;
            }

            // Set origin from parsed data
            if (ParsedData.OriginX_mm != 0 || ParsedData.OriginY_mm != 0)
            {
                board.Origin = new Point(ParsedData.OriginX_mm, ParsedData.OriginY_mm);
            }

            return board;
        }

        /// <summary>
        /// Get a display-friendly summary of the parsed data.
        /// </summary>
        public string GetSummary()
        {
            if (ParsedData == null) return "No data loaded.";

            int topCount = ParsedData.Components.Count(c => !c.IsBottomSide && !c.IsFiducial);
            int botCount = ParsedData.Components.Count(c => c.IsBottomSide && !c.IsFiducial);
            int fidCount = ParsedData.Components.Count(c => c.IsFiducial);
            int smdCount = ParsedData.Components.Count(c => c.MountType == "SMD" && !c.IsFiducial);
            int thtCount = ParsedData.Components.Count(c => c.MountType == "THT" && !c.IsFiducial);

            string summary = string.Format(
                "Components: {0} ({1} top, {2} bottom)\n" +
                "SMD: {3}, THT: {4}\n" +
                "Fiducials: {5}\n" +
                "Packages: {6} unique footprints",
                ParsedData.Components.Count - fidCount, topCount, botCount,
                smdCount, thtCount,
                fidCount,
                ParsedData.Packages.Count);

            if (ParsedData.BoardWidth_mm > 0 && ParsedData.BoardHeight_mm > 0)
            {
                summary += string.Format("\nBoard: {0:F2} x {1:F2} mm",
                    ParsedData.BoardWidth_mm,
                    ParsedData.BoardHeight_mm);
            }

            if (ParsedData.BoardOutline.Count > 0)
            {
                summary += string.Format("\nOutline: {0} vertices", ParsedData.BoardOutline.Count);
            }

            if (!string.IsNullOrEmpty(ParsedData.FileName))
            {
                summary += string.Format("\nFile: {0}", ParsedData.FileName);
            }

            if (!string.IsNullOrEmpty(ParsedData.Version))
            {
                summary += string.Format("\nVersion: {0}", ParsedData.Version);
            }

            if (ParsedData.Warnings.Count > 0)
            {
                summary += string.Format("\nWarnings: {0}", ParsedData.Warnings.Count);
            }

            return summary;
        }
    }
}
