using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using PCBPlotter.Core.Models;

namespace PCBPlotter.Core.Services
{
    /// <summary>
    /// Imports Allegro Fabmaster (.val/.fab/.va2) files into the project model.
    /// Converts AllegroFabmasterParser output into project data (mm coordinates).
    /// </summary>
    public class FabmasterImporter
    {
        /// <summary>
        /// Parsed Fabmaster data (populated after Parse is called).
        /// </summary>
        public FabmasterData ParsedData { get; private set; }

        /// <summary>
        /// Parse a .val, .fab, or .va2 file and store the result.
        /// </summary>
        public void Parse(string filePath)
        {
            var parser = new AllegroFabmasterParser();
            ParsedData = parser.Parse(filePath);
        }

        /// <summary>
        /// Convert the parsed Fabmaster data into project model objects.
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

            // ── Convert placements ──────────────────────────────────────────────
            foreach (var rec in ParsedData.Placements)
            {
                BoardSide side = rec.IsBottomSide ? BoardSide.Bottom : BoardSide.Top;

                var placement = new Placement(rec.RefDes, rec.X, rec.Y, rec.Rotation, side);

                // Resolve or create package
                string pkgName = (rec.SymName ?? "").Trim();
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
                string partNum = (rec.DeviceType ?? "").Trim();
                if (!string.IsNullOrEmpty(partNum))
                {
                    Component comp;
                    if (existingComponents.TryGetValue(partNum, out comp))
                    {
                        placement.Component = comp;
                        if (!comp.ReferenceDesignators.Contains(rec.RefDes))
                            comp.ReferenceDesignators.Add(rec.RefDes);
                    }
                    else
                    {
                        comp = new Component(partNum);
                        comp.ReferenceDesignators.Add(rec.RefDes);
                        if (placement.Package != null)
                            comp.DefaultPackage = placement.Package;
                        existingComponents[partNum] = comp;
                        placement.Component = comp;
                    }
                }

                placements.Add(placement);
            }

            // ── Convert fiducials ───────────────────────────────────────────────
            foreach (var fid in ParsedData.Fiducials)
            {
                BoardSide side = fid.IsBottomSide ? BoardSide.Bottom : BoardSide.Top;

                var fiducial = new Fiducial(fid.RefDes, fid.X, fid.Y, FiducialType.Global)
                {
                    Side = side,
                    Diameter = 1.0,
                    SharedBetweenSides = true
                };

                fiducials.Add(fiducial);
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

            if (ParsedData.Outline != null && ParsedData.Outline.Points.Count > 0)
            {
                board.Width = ParsedData.Outline.WidthMM;
                board.Height = ParsedData.Outline.HeightMM;

                // Convert outline points to project format
                var outlinePoints = new List<Point>();
                foreach (var pt in ParsedData.Outline.Points)
                {
                    outlinePoints.Add(new Point(pt.X, pt.Y));
                }
                board.BoardOutline = outlinePoints;
            }
            else
            {
                // Fall back to board info dimensions
                board.Width = ParsedData.Board.BoardWidthMM;
                board.Height = ParsedData.Board.BoardHeightMM;
            }

            // Set origin from board info
            if (ParsedData.Board.OriginXMM != 0 || ParsedData.Board.OriginYMM != 0)
            {
                board.Origin = new Point(ParsedData.Board.OriginXMM, ParsedData.Board.OriginYMM);
            }

            return board;
        }

        /// <summary>
        /// Get a display-friendly summary of the parsed data.
        /// </summary>
        public string GetSummary()
        {
            if (ParsedData == null) return "No data loaded.";

            int topCount = ParsedData.Placements.Count(p => !p.IsBottomSide);
            int botCount = ParsedData.Placements.Count(p => p.IsBottomSide);

            string summary = string.Format(
                "Placements: {0} ({1} top, {2} bottom)\n" +
                "Fiducials: {3}\n" +
                "Components: {4} unique types\n" +
                "Packages: {5} unique footprints",
                ParsedData.Placements.Count, topCount, botCount,
                ParsedData.Fiducials.Count,
                ParsedData.Components.Count,
                ParsedData.Packages.Count);

            if (ParsedData.Board.BoardWidthMM > 0 && ParsedData.Board.BoardHeightMM > 0)
            {
                summary += string.Format("\nBoard: {0:F2} x {1:F2} mm",
                    ParsedData.Board.BoardWidthMM,
                    ParsedData.Board.BoardHeightMM);
            }

            if (!string.IsNullOrEmpty(ParsedData.Board.BoardName))
            {
                summary += string.Format("\nBoard Name: {0}", ParsedData.Board.BoardName);
            }

            summary += string.Format("\nSource Units: {0}", ParsedData.Board.Units);
            summary += string.Format("\nLayers: {0}", ParsedData.Board.LayerCount);

            if (ParsedData.Warnings.Count > 0)
            {
                summary += string.Format("\nWarnings: {0}", ParsedData.Warnings.Count);
            }

            return summary;
        }
    }
}
