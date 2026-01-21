using System;
using System.Collections.Generic;
using PCBPlotter.Core.Models;

namespace PCBPlotter.Core.Interfaces
{
    /// <summary>
    /// Interface for machine file exporters (Juki H8H, etc.)
    /// </summary>
    public interface IMachineExporter
    {
        /// <summary>
        /// Name of this exporter
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Description
        /// </summary>
        string Description { get; }

        /// <summary>
        /// File extension for output
        /// </summary>
        string FileExtension { get; }

        /// <summary>
        /// Export project data to machine file(s)
        /// </summary>
        ExportResult Export(Project project, ExportOptions options);

        /// <summary>
        /// Validate project data before export
        /// </summary>
        ValidationResult Validate(Project project, ExportOptions options);
    }

    /// <summary>
    /// Options for machine export
    /// </summary>
    public class ExportOptions
    {
        /// <summary>
        /// Output file path
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// Which side(s) to export
        /// </summary>
        public ExportSides Sides { get; set; }

        /// <summary>
        /// Fiducial type to use
        /// </summary>
        public FiducialType FiducialType { get; set; }

        /// <summary>
        /// Whether to auto-generate dual files for top/bottom
        /// </summary>
        public bool AutoDualFile { get; set; }

        /// <summary>
        /// Custom name for side 1
        /// </summary>
        public string Side1Name { get; set; }

        /// <summary>
        /// Custom name for side 2
        /// </summary>
        public string Side2Name { get; set; }

        /// <summary>
        /// Only export placements in these selection sets
        /// </summary>
        public List<string> SelectionSetIds { get; set; }

        /// <summary>
        /// Only export enabled placements
        /// </summary>
        public bool OnlyExportEnabled { get; set; }

        /// <summary>
        /// Include disabled circuit instances
        /// </summary>
        public bool IncludeDisabledCircuits { get; set; }

        public ExportOptions()
        {
            Sides = ExportSides.Both;
            FiducialType = FiducialType.Global;
            AutoDualFile = true;
            Side1Name = "SIDE1";
            Side2Name = "SIDE2";
            SelectionSetIds = new List<string>();
            OnlyExportEnabled = true;
            IncludeDisabledCircuits = false;
        }
    }

    public enum ExportSides
    {
        TopOnly,
        BottomOnly,
        Both
    }

    /// <summary>
    /// Result of an export operation
    /// </summary>
    public class ExportResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public List<string> OutputFiles { get; set; }
        public List<string> Warnings { get; set; }

        public int TopPlacementCount { get; set; }
        public int BottomPlacementCount { get; set; }

        public ExportResult()
        {
            OutputFiles = new List<string>();
            Warnings = new List<string>();
        }
    }

    /// <summary>
    /// Validation result before export
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<ValidationIssue> Issues { get; set; }

        public ValidationResult()
        {
            IsValid = true;
            Issues = new List<ValidationIssue>();
        }

        public void AddError(string message, string detail = null)
        {
            Issues.Add(new ValidationIssue { Severity = IssueSeverity.Error, Message = message, Detail = detail });
            IsValid = false;
        }

        public void AddWarning(string message, string detail = null)
        {
            Issues.Add(new ValidationIssue { Severity = IssueSeverity.Warning, Message = message, Detail = detail });
        }

        public void AddInfo(string message, string detail = null)
        {
            Issues.Add(new ValidationIssue { Severity = IssueSeverity.Info, Message = message, Detail = detail });
        }
    }

    public class ValidationIssue
    {
        public IssueSeverity Severity { get; set; }
        public string Message { get; set; }
        public string Detail { get; set; }
        public object RelatedObject { get; set; }
    }

    public enum IssueSeverity
    {
        Info,
        Warning,
        Error
    }
}
