using System;
using System.Collections.Generic;
using PCBPlotter.Core.Models;

namespace PCBPlotter.Core.Events
{
    #region Project Events

    public class ProjectLoadedEvent
    {
        public Project Project { get; set; }
    }

    public class ProjectSavedEvent
    {
        public Project Project { get; set; }
        public string FilePath { get; set; }
    }

    public class ProjectModifiedEvent
    {
        public Project Project { get; set; }
    }

    public class ProjectClosedEvent { }

    #endregion

    #region Selection Events

    public class SelectionChangedEvent
    {
        public List<Placement> SelectedPlacements { get; set; }
        public object Source { get; set; }

        public SelectionChangedEvent()
        {
            SelectedPlacements = new List<Placement>();
        }
    }

    public class FocusPlacementEvent
    {
        public Placement Placement { get; set; }
        public bool CenterView { get; set; }
    }

    public class FocusMultiplePlacementsEvent
    {
        public List<Placement> Placements { get; set; }
        public bool CenterView { get; set; }

        public FocusMultiplePlacementsEvent()
        {
            Placements = new List<Placement>();
        }
    }

    #endregion

    #region Placement Events

    public class PlacementAddedEvent
    {
        public Placement Placement { get; set; }
    }

    public class PlacementRemovedEvent
    {
        public Placement Placement { get; set; }
    }

    public class PlacementsChangedEvent
    {
        public List<Placement> Placements { get; set; }
        public string ChangeType { get; set; } // "Added", "Removed", "Modified"

        public PlacementsChangedEvent()
        {
            Placements = new List<Placement>();
        }
    }

    public class PlacementMovedEvent
    {
        public Placement Placement { get; set; }
        public double OldX { get; set; }
        public double OldY { get; set; }
    }

    public class PlacementRotatedEvent
    {
        public Placement Placement { get; set; }
        public double OldRotation { get; set; }
    }

    #endregion

    #region Component/Package Events

    public class ComponentAddedEvent
    {
        public Component Component { get; set; }
    }

    public class ComponentRemovedEvent
    {
        public Component Component { get; set; }
    }

    public class PackageAddedEvent
    {
        public Package Package { get; set; }
    }

    public class PackageRemovedEvent
    {
        public Package Package { get; set; }
    }

    public class PackageModifiedEvent
    {
        public Package Package { get; set; }
    }

    #endregion

    #region View Events

    public class ViewSideChangedEvent
    {
        public BoardSide Side { get; set; }
    }

    public class ViewOrientationChangedEvent
    {
        public ViewOrientation Orientation { get; set; }
    }

    public class ZoomChangedEvent
    {
        public double ZoomLevel { get; set; }
    }

    public class PanChangedEvent
    {
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
    }

    public class RequestRefreshEvent
    {
        public bool FullRefresh { get; set; }
    }

    #endregion

    #region Import/Export Events

    public class ImportStartedEvent
    {
        public string ImportType { get; set; }
        public string FilePath { get; set; }
    }

    public class ImportCompletedEvent
    {
        public string ImportType { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public int ItemCount { get; set; }
    }

    public class ExportStartedEvent
    {
        public string ExportType { get; set; }
        public string FilePath { get; set; }
    }

    public class ExportCompletedEvent
    {
        public string ExportType { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public List<string> OutputFiles { get; set; }

        public ExportCompletedEvent()
        {
            OutputFiles = new List<string>();
        }
    }

    #endregion

    #region UI Events

    public class StatusMessageEvent
    {
        public string Message { get; set; }
        public StatusMessageType Type { get; set; }
        public int DurationMs { get; set; }

        public StatusMessageEvent()
        {
            Type = StatusMessageType.Info;
            DurationMs = 3000;
        }
    }

    public enum StatusMessageType
    {
        Info,
        Success,
        Warning,
        Error
    }

    public class ShowDialogEvent
    {
        public string DialogType { get; set; }
        public object Parameter { get; set; }
    }

    public class FloatWindowEvent
    {
        public string ViewType { get; set; }
        public bool Float { get; set; } // true = float, false = dock
    }

    #endregion

    #region Gerber Events

    public class GerberLayerAddedEvent
    {
        public GerberLayer Layer { get; set; }
    }

    public class GerberLayerRemovedEvent
    {
        public GerberLayer Layer { get; set; }
    }

    public class GerberSelectionChangedEvent
    {
        public List<GerberPrimitive> SelectedPrimitives { get; set; }

        public GerberSelectionChangedEvent()
        {
            SelectedPrimitives = new List<GerberPrimitive>();
        }
    }

    public class CreatePackageFromGerberEvent
    {
        public List<GerberPrimitive> Primitives { get; set; }
        public string SuggestedName { get; set; }

        public CreatePackageFromGerberEvent()
        {
            Primitives = new List<GerberPrimitive>();
        }
    }

    #endregion
}
