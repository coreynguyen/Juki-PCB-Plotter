using System;
using System.ComponentModel;

namespace PCBPlotter.Core.Models
{
    /// <summary>
    /// Side of the PCB
    /// </summary>
    public enum BoardSide
    {
        [Description("Top")]
        Top,
        [Description("Bottom")]
        Bottom
    }

    /// <summary>
    /// Type of fiducial marker
    /// </summary>
    public enum FiducialType
    {
        [Description("Global (Board)")]
        Global,
        [Description("Local (Circuit)")]
        Local
    }

    /// <summary>
    /// Part classification for machine algorithms
    /// </summary>
    public enum PartClass
    {
        [Description("Chip (0201, 0402, 0603, 0805, etc.)")]
        Chip,
        [Description("SOT (SOT23, SOT223, etc.)")]
        SOT,
        [Description("SOP (SOIC, SSOP, TSSOP, etc.)")]
        SOP,
        [Description("QFP (LQFP, TQFP, etc.)")]
        QFP,
        [Description("QFN/DFN")]
        QFN,
        [Description("BGA")]
        BGA,
        [Description("Connector")]
        Connector,
        [Description("Electrolytic Capacitor")]
        ElectrolyticCap,
        [Description("LED")]
        LED,
        [Description("Crystal/Oscillator")]
        Crystal,
        [Description("Other")]
        Other
    }

    /// <summary>
    /// Shape types for package graphics
    /// </summary>
    public enum GraphicShapeType
    {
        Rectangle,
        RoundedRectangle,
        Circle,
        Ellipse,
        Line,
        Polygon,
        Arc,
        Text
    }

    /// <summary>
    /// Placement validation status
    /// </summary>
    [Flags]
    public enum PlacementStatus
    {
        Valid = 0,
        NoReference = 1,
        NoComponent = 2,
        NoPackage = 4,
        Collision = 8,
        MissingBomEntry = 16
    }

    /// <summary>
    /// Component/BOM validation status
    /// </summary>
    [Flags]
    public enum ComponentStatus
    {
        Valid = 0,
        NotUsed = 1,
        NoPartNumber = 2,
        NoPlacements = 4
    }

    /// <summary>
    /// Units for coordinates and dimensions
    /// </summary>
    public enum Units
    {
        [Description("Millimeters")]
        Millimeters,
        [Description("Mils (thou)")]
        Mils,
        [Description("Inches")]
        Inches
    }

    /// <summary>
    /// Selection tool modes
    /// </summary>
    public enum SelectionMode
    {
        [Description("Single Select")]
        Single,
        [Description("Box Select")]
        Box,
        [Description("Select Same Part")]
        SamePart,
        [Description("Select Same Package")]
        SamePackage,
        [Description("Select by Side")]
        BySide
    }

    /// <summary>
    /// View orientation for the output plot
    /// </summary>
    public enum ViewOrientation
    {
        [Description("Top Down")]
        TopDown,
        [Description("Bottom Up (X-Ray)")]
        BottomUp
    }

    /// <summary>
    /// Renderer type for graphics
    /// </summary>
    public enum RendererType
    {
        [Description("WPF DrawingVisual (Default)")]
        WpfDrawingVisual,
        [Description("DirectX (GPU Accelerated)")]
        DirectX,
        [Description("GDI+ (Legacy/Fallback)")]
        GdiPlus
    }
}
