using System;
using System.Collections.Generic;
using PCBPlotter.Core.Models;

namespace PCBPlotter.Core.Interfaces
{
    /// <summary>
    /// Interface for CAD file importers (Text, ODB++, IPC-2581, etc.)
    /// </summary>
    public interface ICadImporter
    {
        /// <summary>
        /// Name of this importer
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Description of supported formats
        /// </summary>
        string Description { get; }

        /// <summary>
        /// File extensions this importer handles
        /// </summary>
        string[] SupportedExtensions { get; }

        /// <summary>
        /// Whether this importer can handle the given file
        /// </summary>
        bool CanImport(string filePath);

        /// <summary>
        /// Import data from a file
        /// </summary>
        ImportResult Import(string filePath, ImportOptions options);

        /// <summary>
        /// Preview import without fully loading
        /// </summary>
        ImportPreview Preview(string filePath);
    }

    /// <summary>
    /// Options for CAD import
    /// </summary>
    public class ImportOptions
    {
        /// <summary>
        /// Source units of the file
        /// </summary>
        public Units SourceUnits { get; set; }

        /// <summary>
        /// Target units for the project
        /// </summary>
        public Units TargetUnits { get; set; }

        /// <summary>
        /// Scale factor to apply
        /// </summary>
        public double Scale { get; set; }

        /// <summary>
        /// Whether to import packages/footprints
        /// </summary>
        public bool ImportPackages { get; set; }

        /// <summary>
        /// Whether to import BOM/component data
        /// </summary>
        public bool ImportBom { get; set; }

        /// <summary>
        /// Whether to merge with existing data
        /// </summary>
        public bool MergeExisting { get; set; }

        /// <summary>
        /// Column mappings for text imports
        /// </summary>
        public Dictionary<string, int> ColumnMappings { get; set; }

        /// <summary>
        /// Delimiter for text imports
        /// </summary>
        public string Delimiter { get; set; }

        /// <summary>
        /// Number of header rows to skip
        /// </summary>
        public int HeaderRows { get; set; }

        public ImportOptions()
        {
            SourceUnits = Units.Millimeters;
            TargetUnits = Units.Millimeters;
            Scale = 1.0;
            ImportPackages = true;
            ImportBom = true;
            MergeExisting = false;
            ColumnMappings = new Dictionary<string, int>();
            Delimiter = ",";
            HeaderRows = 1;
        }
    }

    /// <summary>
    /// Result of a CAD import operation
    /// </summary>
    public class ImportResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public List<string> Warnings { get; set; }
        public List<string> Errors { get; set; }

        public List<Placement> Placements { get; set; }
        public List<Component> Components { get; set; }
        public List<Package> Packages { get; set; }
        public List<Fiducial> Fiducials { get; set; }
        public BoardDefinition Board { get; set; }

        public int PlacementCount { get { return Placements != null ? Placements.Count : 0; } }
        public int ComponentCount { get { return Components != null ? Components.Count : 0; } }
        public int PackageCount { get { return Packages != null ? Packages.Count : 0; } }

        public ImportResult()
        {
            Warnings = new List<string>();
            Errors = new List<string>();
            Placements = new List<Placement>();
            Components = new List<Component>();
            Packages = new List<Package>();
            Fiducials = new List<Fiducial>();
        }
    }

    /// <summary>
    /// Preview of import without full processing
    /// </summary>
    public class ImportPreview
    {
        public bool IsValid { get; set; }
        public string Message { get; set; }
        public Units DetectedUnits { get; set; }
        public int EstimatedPlacements { get; set; }
        public int EstimatedComponents { get; set; }
        public List<string> DetectedColumns { get; set; }
        public List<string> SampleLines { get; set; }

        public ImportPreview()
        {
            DetectedColumns = new List<string>();
            SampleLines = new List<string>();
        }
    }
}
