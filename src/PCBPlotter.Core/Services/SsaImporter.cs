// =============================================================================
// SsaImporter.cs
// Converts Samsung Standard ASCII (SSA) parsed data to project models
// =============================================================================
using System;
using System.Collections.Generic;
using System.Text;
using PCBPlotter.Core.Models;

namespace PCBPlotter.Core.Services
{
    public class SsaImporter
    {
        private SsaParser _parser;

        public SsaData ParsedData
        {
            get { return _parser?.ParsedData; }
        }

        public void Parse(string filePath)
        {
            _parser = new SsaParser();
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

            // Build packages from component data
            var pkgMap = new Dictionary<string, Package>();
            foreach (var compData in data.Components.Values)
            {
                string pkgName = compData.PackageType ?? compData.PartNumber ?? "Unknown";
                if (!pkgMap.ContainsKey(pkgName))
                {
                    var pkg = new Package
                    {
                        Name = pkgName,
                        Description = compData.Description ?? "",
                        Width = 0,  // SSA doesn't provide package dimensions
                        Length = 0
                    };
                    packages.Add(pkg);
                    pkgMap[pkgName] = pkg;
                }
            }

            // Build placements and fiducials
            foreach (var plc in data.Placements)
            {
                if (plc.IsFiducial)
                {
                    // Add as fiducial
                    fiducials.Add(new Fiducial
                    {
                        Name = plc.RefDes,
                        X = plc.X,
                        Y = plc.Y,
                        Side = plc.Side == "Bottom" ? BoardSide.Bottom : BoardSide.Top,
                        Type = FiducialType.Global
                    });
                }
                else if (!plc.Skip)
                {
                    // Find or create package
                    Package pkg = null;
                    string pkgName = plc.PackageType ?? plc.PartNumber ?? "Unknown";
                    pkgMap.TryGetValue(pkgName, out pkg);

                    var placement = new Placement
                    {
                        Reference = plc.RefDes,
                        X = plc.X,
                        Y = plc.Y,
                        Rotation = plc.Rotation,
                        Side = plc.Side == "Bottom" ? BoardSide.Bottom : BoardSide.Top,
                        Package = pkg
                    };
                    placements.Add(placement);
                }
            }

            // Add board-level fiducials from PCB section
            if (data.Pcb.Fiducial.Shape != SsaMarkShape.None)
            {
                // Add first fiducial point
                fiducials.Add(new Fiducial
                {
                    Name = "FID1",
                    X = data.Pcb.Fiducial.X1,
                    Y = data.Pcb.Fiducial.Y1,
                    Side = BoardSide.Top,
                    Type = FiducialType.Global
                });

                // Add second fiducial point if different
                if (data.Pcb.Fiducial.X2 != 0 || data.Pcb.Fiducial.Y2 != 0)
                {
                    fiducials.Add(new Fiducial
                    {
                        Name = "FID2",
                        X = data.Pcb.Fiducial.X2,
                        Y = data.Pcb.Fiducial.Y2,
                        Side = BoardSide.Top,
                        Type = FiducialType.Global
                    });
                }
            }

            // Build board outline from PCB size
            if (data.Board.Outline.Width > 0 && data.Board.Outline.Height > 0)
            {
                board = new BoardDefinition
                {
                    Width = data.Board.Outline.Width,
                    Height = data.Board.Outline.Height,
                    Thickness = data.Board.Outline.Thickness,
                    Origin = new System.Windows.Point(0, 0)
                };

                // Set up array configuration if present
                if (data.Board.Array != null && data.Board.Array.TotalBoards > 1)
                {
                    board.CircuitCountX = data.Board.Array.Columns;
                    board.CircuitCountY = data.Board.Array.Rows;
                    board.PitchX = Math.Abs(data.Board.Array.OffsetX);
                    board.PitchY = Math.Abs(data.Board.Array.OffsetY);
                }
            }
        }

        public string GetSummary()
        {
            if (_parser?.ParsedData == null)
                return "No data loaded.";

            return _parser.ParsedData.GetReport();
        }
    }
}
