using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using PCBPlotter.Core.Models;

namespace PCBPlotter.Core.Services
{
    /// <summary>
    /// Exports placement data to Juki FX-3 format (.x01)
    /// XML-based format for Juki pick-and-place machines
    /// </summary>
    public class JukiFx3Exporter
    {
        #region Export Options

        public class ExportOptions
        {
            public string PwbId { get; set; } = "PCB-001";
            public string TargetMachine { get; set; } = "FX-3";
            public string RefSide { get; set; } = "FRONT";
            public string TransDir { get; set; } = "LTOR";  // Left to Right
            public string RefType { get; set; } = "SHAPE";
            public double PwbThickness { get; set; } = 1.6;
            public double BackHeight { get; set; } = 12.0;
            public double PwbHeight { get; set; } = 0.0;
            public double LayoutPosX { get; set; } = 0.0;
            public double LayoutPosY { get; set; } = 0.0;
            public BoardSide ExportSide { get; set; } = BoardSide.Top;
            public bool IncludeSkippedPlacements { get; set; } = false;

            // Circuit/panel configuration
            public bool UseCircuits { get; set; } = false;
            public int CircuitCountX { get; set; } = 1;
            public int CircuitCountY { get; set; } = 1;
            public double CircuitPitchX { get; set; } = 0.0;
            public double CircuitPitchY { get; set; } = 0.0;
        }

        #endregion

        private readonly CultureInfo _invariantCulture = CultureInfo.InvariantCulture;

        /// <summary>
        /// Export project data to Juki FX-3 format
        /// </summary>
        public void Export(Project project, string filePath, ExportOptions options = null)
        {
            if (project == null)
                throw new ArgumentNullException(nameof(project));

            options = options ?? new ExportOptions();

            // Filter placements by side
            var placements = project.Placements
                .Where(p => p.Side == options.ExportSide)
                .Where(p => options.IncludeSkippedPlacements ||
                    (options.ExportSide == BoardSide.Top ? p.IsExportEnabledTop : p.IsExportEnabledBottom))
                .ToList();

            // Get unique packages used by placements
            var usedPackages = placements
                .Where(p => p.Package != null)
                .Select(p => p.Package)
                .Distinct()
                .ToList();

            // Get fiducials for the export side
            var fiducials = project.Fiducials
                .Where(f => f.Side == options.ExportSide)
                .ToList();

            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "   ",
                Encoding = Encoding.UTF8
            };

            using (var writer = XmlWriter.Create(filePath, settings))
            {
                writer.WriteStartDocument();
                WriteProgram(writer, project, placements, usedPackages, fiducials, options);
                writer.WriteEndDocument();
            }
        }

        private void WriteProgram(XmlWriter writer, Project project, List<Placement> placements,
            List<Package> packages, List<Fiducial> fiducials, ExportOptions options)
        {
            writer.WriteStartElement("Program");

            // Info element
            WriteInfo(writer, options);

            // Core element
            WriteCore(writer, project, placements, packages, fiducials, options);

            // Model element
            WriteModel(writer, project, options);

            // Machine element (fiducials/marks)
            WriteMachine(writer, fiducials, options);

            writer.WriteEndElement(); // Program
        }

        private void WriteInfo(XmlWriter writer, ExportOptions options)
        {
            writer.WriteStartElement("Info");
            writer.WriteAttributeString("pwbid", options.PwbId);
            writer.WriteAttributeString("target", options.TargetMachine);
            writer.WriteEndElement();
        }

        private void WriteCore(XmlWriter writer, Project project, List<Placement> placements,
            List<Package> packages, List<Fiducial> fiducials, ExportOptions options)
        {
            writer.WriteStartElement("Core");

            // HeaderData
            WriteHeaderData(writer, placements, packages, fiducials);

            // Pwb (PCB configuration)
            WritePwb(writer, project, options);

            // Place (placement data)
            WritePlace(writer, placements);

            // Compo (component definitions)
            WriteCompo(writer, packages);

            writer.WriteEndElement(); // Core
        }

        private void WriteHeaderData(XmlWriter writer, List<Placement> placements,
            List<Package> packages, List<Fiducial> fiducials)
        {
            writer.WriteStartElement("HeaderData");

            writer.WriteStartElement("HdPwb");
            writer.WriteAttributeString("total", "1");
            writer.WriteAttributeString("comp", "1");
            writer.WriteEndElement();

            writer.WriteStartElement("HdMark");
            writer.WriteAttributeString("total", fiducials.Count.ToString());
            writer.WriteAttributeString("comp", fiducials.Count.ToString());
            writer.WriteEndElement();

            writer.WriteStartElement("HdPlace");
            writer.WriteAttributeString("total", placements.Count.ToString());
            writer.WriteAttributeString("comp", placements.Count.ToString());
            writer.WriteEndElement();

            writer.WriteStartElement("HdCompo");
            writer.WriteAttributeString("total", packages.Count.ToString());
            writer.WriteAttributeString("comp", packages.Count.ToString());
            writer.WriteEndElement();

            writer.WriteEndElement(); // HeaderData
        }

        private void WritePwb(XmlWriter writer, Project project, ExportOptions options)
        {
            writer.WriteStartElement("Pwb");

            // Basic settings
            writer.WriteStartElement("Basic");
            WriteElement(writer, "RefSide", options.RefSide);
            WriteElement(writer, "TransDir", options.TransDir);
            WriteElement(writer, "RefType", options.RefType);
            WriteElement(writer, "BaseCircuitType", "NOUSE");
            WriteElement(writer, "BocMarkSetting", "ALL");
            WriteElement(writer, "MarkBin", "NOUSE");
            WriteElement(writer, "BadMarkRecog", "POSITIVE");
            WriteElement(writer, "UseExBadMark", "STANDARD");
            writer.WriteEndElement(); // Basic

            // ConfigPwb - board dimensions
            writer.WriteStartElement("ConfigPwb");

            double boardWidth = project.Board?.Width ?? 100;
            double boardHeight = project.Board?.Height ?? 100;

            writer.WriteStartElement("OutLine");
            writer.WriteAttributeString("x", FormatCoord(boardWidth));
            writer.WriteAttributeString("y", FormatCoord(boardHeight));
            writer.WriteEndElement();

            writer.WriteStartElement("PwbThick");
            writer.WriteAttributeString("t", FormatCoord(options.PwbThickness));
            writer.WriteEndElement();

            writer.WriteStartElement("BackHeight");
            writer.WriteAttributeString("h", FormatCoord(options.BackHeight));
            writer.WriteEndElement();

            writer.WriteStartElement("PwbHeight");
            writer.WriteAttributeString("h", FormatCoord(options.PwbHeight));
            writer.WriteEndElement();

            writer.WriteEndElement(); // ConfigPwb

            // PwbId
            WriteElement(writer, "PwbId", options.PwbId);

            // ConfigCircuit - circuit/panel configuration
            WriteConfigCircuit(writer, project, options);

            writer.WriteEndElement(); // Pwb
        }

        private void WriteConfigCircuit(XmlWriter writer, Project project, ExportOptions options)
        {
            writer.WriteStartElement("ConfigCircuit");
            writer.WriteStartElement("ConfigCircuit");
            writer.WriteAttributeString("num", "0");

            WriteElement(writer, "CircuitUse", "COMPLETE");
            WriteElement(writer, "CircuitId", "");
            WriteElement(writer, "CircuitType", options.UseCircuits ? "NONMATRIX" : "NONMATRIX");
            WriteElement(writer, "BocMarkType", "PWBBOC");

            writer.WriteStartElement("MarkName");
            writer.WriteAttributeString("shape", "0");
            writer.WriteString("BOCMARK01");
            writer.WriteEndElement();

            double circuitWidth = project.Board?.Width ?? 100;
            double circuitHeight = project.Board?.Height ?? 100;

            writer.WriteStartElement("CircuitOutLine");
            writer.WriteAttributeString("x", FormatCoord(circuitWidth));
            writer.WriteAttributeString("y", FormatCoord(circuitHeight));
            writer.WriteEndElement();

            // NonMatrix configuration for circuits/panels
            if (options.UseCircuits && (options.CircuitCountX > 1 || options.CircuitCountY > 1))
            {
                writer.WriteStartElement("NonMatrix");

                int totalCircuits = options.CircuitCountX * options.CircuitCountY;
                writer.WriteStartElement("TotalCircuit");
                writer.WriteAttributeString("cnt", totalCircuits.ToString());
                writer.WriteEndElement();

                writer.WriteStartElement("Allocation");
                int circuitNum = 0;
                for (int y = 0; y < options.CircuitCountY; y++)
                {
                    for (int x = 0; x < options.CircuitCountX; x++)
                    {
                        writer.WriteStartElement("Allocation");
                        writer.WriteAttributeString("num", circuitNum.ToString());

                        writer.WriteStartElement("RefPos");
                        writer.WriteAttributeString("x", FormatCoord(x * options.CircuitPitchX));
                        writer.WriteAttributeString("y", FormatCoord(y * options.CircuitPitchY));
                        writer.WriteEndElement();

                        writer.WriteStartElement("CircuitAngle");
                        writer.WriteAttributeString("a", "0.0000");
                        writer.WriteEndElement();

                        writer.WriteEndElement(); // Allocation
                        circuitNum++;
                    }
                }
                writer.WriteEndElement(); // Allocation (parent)

                writer.WriteEndElement(); // NonMatrix
            }

            writer.WriteEndElement(); // ConfigCircuit (inner)
            writer.WriteEndElement(); // ConfigCircuit (outer)
        }

        private void WritePlace(XmlWriter writer, List<Placement> placements)
        {
            writer.WriteStartElement("Place");

            for (int i = 0; i < placements.Count; i++)
            {
                var placement = placements[i];
                writer.WriteStartElement("Place");
                writer.WriteAttributeString("num", i.ToString());

                WriteElement(writer, "PlaceId", placement.Reference ?? $"U{i + 1}");
                WriteElement(writer, "BaseCircuitId", "A");

                writer.WriteStartElement("PlacePos");
                writer.WriteAttributeString("x", FormatCoord(placement.X));
                writer.WriteAttributeString("y", FormatCoord(placement.Y));
                writer.WriteAttributeString("z", "0.0000");
                writer.WriteEndElement();

                writer.WriteStartElement("PlaceAngle");
                writer.WriteAttributeString("angle", FormatCoord(placement.Rotation));
                writer.WriteEndElement();

                // Component name - use package name or generate from part number
                string compoName = GetComponentName(placement);
                WriteElement(writer, "CompoName", compoName);

                writer.WriteEndElement(); // Place
            }

            writer.WriteEndElement(); // Place
        }

        private void WriteCompo(XmlWriter writer, List<Package> packages)
        {
            writer.WriteStartElement("Compo");

            for (int i = 0; i < packages.Count; i++)
            {
                var package = packages[i];
                writer.WriteStartElement("Compo");
                writer.WriteAttributeString("num", i.ToString());

                writer.WriteStartElement("CompoBasic");

                WriteElement(writer, "CompoName", package.Name ?? $"PKG{i + 1}");
                WriteElement(writer, "Comment", package.Description ?? "");

                writer.WriteStartElement("CompoSize");
                writer.WriteAttributeString("w", FormatCoord(package.Width));
                writer.WriteAttributeString("l", FormatCoord(package.Length));
                writer.WriteEndElement();

                writer.WriteStartElement("CompoHeight");
                writer.WriteAttributeString("h", FormatCoord(package.Height));
                writer.WriteEndElement();

                writer.WriteStartElement("ConnecterHeight");
                writer.WriteAttributeString("h", "0.0000");
                writer.WriteEndElement();

                writer.WriteEndElement(); // CompoBasic
                writer.WriteEndElement(); // Compo
            }

            writer.WriteEndElement(); // Compo
        }

        private void WriteModel(XmlWriter writer, Project project, ExportOptions options)
        {
            writer.WriteStartElement("Model");
            writer.WriteStartElement("Pwb");

            WriteElement(writer, "PwbId", options.PwbId);
            WriteElement(writer, "ModelName", options.TargetMachine);

            writer.WriteStartElement("LayoutPos");
            writer.WriteAttributeString("x", FormatCoord(options.LayoutPosX));
            writer.WriteAttributeString("y", FormatCoord(options.LayoutPosY));
            writer.WriteEndElement();

            writer.WriteStartElement("CircuitLayoutPos");
            writer.WriteStartElement("CircuitLayoutPos");
            writer.WriteAttributeString("num", "0");
            writer.WriteAttributeString("x", "0.0000");
            writer.WriteAttributeString("y", "0.0000");
            writer.WriteEndElement();
            writer.WriteEndElement();

            writer.WriteEndElement(); // Pwb
            writer.WriteEndElement(); // Model
        }

        private void WriteMachine(XmlWriter writer, List<Fiducial> fiducials, ExportOptions options)
        {
            writer.WriteStartElement("Machine");
            writer.WriteStartElement("MarkData");
            writer.WriteStartElement("MarkData");
            writer.WriteAttributeString("num", "0");

            WriteElement(writer, "MarkType", "BOCMARK");
            WriteElement(writer, "MarkId", "BOCMARK01");

            writer.WriteStartElement("EffectiveCount");
            writer.WriteAttributeString("cnt", Math.Min(fiducials.Count, 4).ToString());
            writer.WriteEndElement();

            writer.WriteStartElement("FiducialMark");

            // Write up to 4 fiducial marks
            for (int i = 0; i < 4; i++)
            {
                writer.WriteStartElement("FiducialMark");
                writer.WriteAttributeString("occ", "0");
                writer.WriteAttributeString("num", i.ToString());

                if (i < fiducials.Count)
                {
                    var fiducial = fiducials[i];

                    writer.WriteStartElement("MarkPos");
                    writer.WriteAttributeString("x", FormatCoord(fiducial.X));
                    writer.WriteAttributeString("y", FormatCoord(fiducial.Y));
                    writer.WriteEndElement();

                    writer.WriteStartElement("Diameter");
                    writer.WriteAttributeString("d", FormatCoord(fiducial.Diameter > 0 ? fiducial.Diameter : 1.0));
                    writer.WriteEndElement();
                }
                else
                {
                    writer.WriteStartElement("Diameter");
                    writer.WriteAttributeString("d", "0.0000");
                    writer.WriteEndElement();
                }

                writer.WriteEndElement(); // FiducialMark
            }

            writer.WriteEndElement(); // FiducialMark (parent)

            writer.WriteEndElement(); // MarkData
            writer.WriteEndElement(); // MarkData (parent)
            writer.WriteEndElement(); // Machine
        }

        #region Helper Methods

        private void WriteElement(XmlWriter writer, string name, string value)
        {
            writer.WriteStartElement(name);
            writer.WriteString(value ?? "");
            writer.WriteEndElement();
        }

        private string FormatCoord(double value)
        {
            return value.ToString("F4", _invariantCulture);
        }

        private string GetComponentName(Placement placement)
        {
            // Use package name if available
            if (placement.Package != null && !string.IsNullOrEmpty(placement.Package.Name))
            {
                return placement.Package.Name;
            }

            // Fall back to part number from component
            if (placement.Component != null && !string.IsNullOrEmpty(placement.Component.PartNumber))
            {
                return placement.Component.PartNumber;
            }

            // Generate from reference
            return $"COMP-{placement.Reference}";
        }

        #endregion
    }
}
