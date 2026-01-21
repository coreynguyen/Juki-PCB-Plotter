using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using PCBPlotter.Core.Events;
using PCBPlotter.Core.Models;
using PCBPlotter.Core.Rendering;

namespace PCBPlotter.ViewModels
{
    /// <summary>
    /// View model for the output plot (main design canvas)
    /// </summary>
    public class OutputPlotViewModel : ViewModelBase
    {
        private Project _project;
        private double _zoom = 1.0;
        private double _panX;
        private double _panY;
        private Point _cursorPosition;
        private BoardSide _viewSide = BoardSide.Top;
        private ViewOrientation _viewOrientation = ViewOrientation.TopDown;
        private SelectionMode _selectionMode = SelectionMode.Single;
        private bool _showGrid = true;
        private bool _showLabels = true;
        private bool _showPolarity = true;
        private bool _showCircuitInstances = true;
        private double _gridSpacing = 1.0;
        private ObservableCollection<Placement> _selectedPlacements;
        private string _coordinateDisplay;

        public Project Project
        {
            get { return _project; }
            set
            {
                if (SetProperty(ref _project, value))
                {
                    OnPropertyChanged("Placements");
                    OnPropertyChanged("Fiducials");
                    Publish(new RequestRefreshEvent { FullRefresh = true });
                }
            }
        }

        public double Zoom
        {
            get { return _zoom; }
            set
            {
                if (SetProperty(ref _zoom, Math.Max(0.1, Math.Min(50, value))))
                {
                    Publish(new ZoomChangedEvent { ZoomLevel = _zoom });
                }
            }
        }

        public double PanX
        {
            get { return _panX; }
            set { SetProperty(ref _panX, value); }
        }

        public double PanY
        {
            get { return _panY; }
            set { SetProperty(ref _panY, value); }
        }

        public Point CursorPosition
        {
            get { return _cursorPosition; }
            set
            {
                if (SetProperty(ref _cursorPosition, value))
                {
                    UpdateCoordinateDisplay();
                }
            }
        }

        public string CoordinateDisplay
        {
            get { return _coordinateDisplay; }
            set { SetProperty(ref _coordinateDisplay, value); }
        }

        public BoardSide ViewSide
        {
            get { return _viewSide; }
            set
            {
                if (SetProperty(ref _viewSide, value))
                {
                    Publish(new ViewSideChangedEvent { Side = value });
                    Publish(new RequestRefreshEvent { FullRefresh = true });
                }
            }
        }

        public ViewOrientation ViewOrientation
        {
            get { return _viewOrientation; }
            set
            {
                if (SetProperty(ref _viewOrientation, value))
                {
                    Publish(new ViewOrientationChangedEvent { Orientation = value });
                }
            }
        }

        public SelectionMode SelectionMode
        {
            get { return _selectionMode; }
            set { SetProperty(ref _selectionMode, value); }
        }

        public bool ShowGrid
        {
            get { return _showGrid; }
            set
            {
                if (SetProperty(ref _showGrid, value))
                {
                    Publish(new RequestRefreshEvent { FullRefresh = false });
                }
            }
        }

        public bool ShowLabels
        {
            get { return _showLabels; }
            set
            {
                if (SetProperty(ref _showLabels, value))
                {
                    Publish(new RequestRefreshEvent { FullRefresh = false });
                }
            }
        }

        public bool ShowPolarity
        {
            get { return _showPolarity; }
            set
            {
                if (SetProperty(ref _showPolarity, value))
                {
                    Publish(new RequestRefreshEvent { FullRefresh = false });
                }
            }
        }

        public bool ShowCircuitInstances
        {
            get { return _showCircuitInstances; }
            set
            {
                if (SetProperty(ref _showCircuitInstances, value))
                {
                    Publish(new RequestRefreshEvent { FullRefresh = true });
                }
            }
        }

        public double GridSpacing
        {
            get { return _gridSpacing; }
            set { SetProperty(ref _gridSpacing, Math.Max(0.1, value)); }
        }

        public ObservableCollection<Placement> SelectedPlacements
        {
            get { return _selectedPlacements; }
            set { SetProperty(ref _selectedPlacements, value); }
        }

        public IEnumerable<Placement> Placements
        {
            get { return _project != null ? _project.Placements : Enumerable.Empty<Placement>(); }
        }

        public IEnumerable<Fiducial> Fiducials
        {
            get { return _project != null ? _project.Fiducials : Enumerable.Empty<Fiducial>(); }
        }

        // Commands
        public ICommand ZoomInCommand { get; private set; }
        public ICommand ZoomOutCommand { get; private set; }
        public ICommand ZoomFitCommand { get; private set; }
        public ICommand ZoomResetCommand { get; private set; }
        public ICommand ToggleSideCommand { get; private set; }
        public ICommand SelectAllCommand { get; private set; }
        public ICommand SelectNoneCommand { get; private set; }
        public ICommand SelectSamePartCommand { get; private set; }
        public ICommand SelectSamePackageCommand { get; private set; }
        public ICommand RotateSelectionCommand { get; private set; }
        public ICommand DeleteSelectionCommand { get; private set; }
        public ICommand SetOriginCommand { get; private set; }
        public ICommand MeasureDistanceCommand { get; private set; }
        public ICommand FocusSelectionCommand { get; private set; }
        public ICommand EnableForExportCommand { get; private set; }
        public ICommand DisableForExportCommand { get; private set; }
        public ICommand AddPcbAreaCommand { get; private set; }
        public ICommand AddPlacementCommand { get; private set; }
        public ICommand AddFiducialCommand { get; private set; }

        public OutputPlotViewModel()
        {
            _selectedPlacements = new ObservableCollection<Placement>();
            InitializeCommands();
            SubscribeToEvents();
        }

        private void InitializeCommands()
        {
            ZoomInCommand = new RelayCommand(() => Zoom *= 1.2);
            ZoomOutCommand = new RelayCommand(() => Zoom /= 1.2);
            ZoomFitCommand = new RelayCommand(ExecuteZoomFit);
            ZoomResetCommand = new RelayCommand(() => { Zoom = 1.0; PanX = 0; PanY = 0; });
            ToggleSideCommand = new RelayCommand(() => ViewSide = ViewSide == BoardSide.Top ? BoardSide.Bottom : BoardSide.Top);
            SelectAllCommand = new RelayCommand(ExecuteSelectAll);
            SelectNoneCommand = new RelayCommand(ExecuteSelectNone);
            SelectSamePartCommand = new RelayCommand(ExecuteSelectSamePart, () => SelectedPlacements.Count > 0);
            SelectSamePackageCommand = new RelayCommand(ExecuteSelectSamePackage, () => SelectedPlacements.Count > 0);
            RotateSelectionCommand = new RelayCommand<double>(ExecuteRotateSelection, d => SelectedPlacements.Count > 0);
            DeleteSelectionCommand = new RelayCommand(ExecuteDeleteSelection, () => SelectedPlacements.Count > 0);
            SetOriginCommand = new RelayCommand(ExecuteSetOrigin, () => SelectedPlacements.Count == 1);
            MeasureDistanceCommand = new RelayCommand(ExecuteMeasureDistance);
            FocusSelectionCommand = new RelayCommand(ExecuteFocusSelection, () => SelectedPlacements.Count > 0);
            EnableForExportCommand = new RelayCommand(ExecuteEnableForExport, () => SelectedPlacements.Count > 0);
            DisableForExportCommand = new RelayCommand(ExecuteDisableForExport, () => SelectedPlacements.Count > 0);
            AddPcbAreaCommand = new RelayCommand(ExecuteAddPcbArea, () => Project != null);
            AddPlacementCommand = new RelayCommand(ExecuteAddPlacement, () => Project != null);
            AddFiducialCommand = new RelayCommand(ExecuteAddFiducial, () => Project != null);
        }

        private void SubscribeToEvents()
        {
            Subscribe<SelectionChangedEvent>(OnSelectionChanged);
            Subscribe<FocusPlacementEvent>(OnFocusPlacement);
            Subscribe<ZoomFitRequestEvent>(OnZoomFitRequest);
            Subscribe<RequestRefreshEvent>(OnRefreshRequest);
        }

        private void OnZoomFitRequest(ZoomFitRequestEvent e)
        {
            ExecuteZoomFit();
        }

        private void OnRefreshRequest(RequestRefreshEvent e)
        {
            // Notify bindings to refresh
            OnPropertyChanged("Placements");
            OnPropertyChanged("Fiducials");
            OnPropertyChanged("Project");
        }

        private void UpdateCoordinateDisplay()
        {
            var units = Project != null ? Project.Units : Units.Millimeters;
            string unitSuffix = units == Units.Millimeters ? "mm" : units == Units.Mils ? "mil" : "in";
            CoordinateDisplay = string.Format("X: {0:F3} {2}  Y: {1:F3} {2}",
                CursorPosition.X, CursorPosition.Y, unitSuffix);
        }

        public RenderContext CreateRenderContext(double viewportWidth, double viewportHeight)
        {
            return new RenderContext
            {
                Zoom = Zoom,
                PanX = PanX,
                PanY = PanY,
                ViewportWidth = viewportWidth,
                ViewportHeight = viewportHeight,
                ViewSide = ViewSide,
                Orientation = ViewOrientation,
                ShowPolarity = ShowPolarity,
                ShowLabels = ShowLabels,
                ShowGrid = ShowGrid,
                GridSpacing = GridSpacing,
                Units = Project != null ? Project.Units : Units.Millimeters,
                BoardWidth = Project != null ? Project.Board.Width : 0
            };
        }

        #region Command Implementations

        private void ExecuteZoomFit()
        {
            if (Project == null || !Project.Placements.Any())
            {
                Zoom = 1.0;
                PanX = 0;
                PanY = 0;
                return;
            }

            // Calculate bounds of all placements
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var placement in Project.Placements)
            {
                if (placement.X < minX) minX = placement.X;
                if (placement.Y < minY) minY = placement.Y;
                if (placement.X > maxX) maxX = placement.X;
                if (placement.Y > maxY) maxY = placement.Y;
            }

            // Add fiducials to bounds
            foreach (var fiducial in Project.Fiducials)
            {
                if (fiducial.X < minX) minX = fiducial.X;
                if (fiducial.Y < minY) minY = fiducial.Y;
                if (fiducial.X > maxX) maxX = fiducial.X;
                if (fiducial.Y > maxY) maxY = fiducial.Y;
            }

            // Add margin (10%)
            double margin = Math.Max(maxX - minX, maxY - minY) * 0.1;
            if (margin < 5) margin = 5;
            minX -= margin;
            minY -= margin;
            maxX += margin;
            maxY += margin;

            double boundsWidth = maxX - minX;
            double boundsHeight = maxY - minY;

            if (boundsWidth <= 0) boundsWidth = 100;
            if (boundsHeight <= 0) boundsHeight = 100;

            // Calculate zoom to fit (assuming 800x600 viewport for now - will be updated by view)
            double viewportWidth = 800;
            double viewportHeight = 600;

            double zoomX = viewportWidth / boundsWidth;
            double zoomY = viewportHeight / boundsHeight;
            Zoom = Math.Min(zoomX, zoomY) * 0.9; // 90% to leave margin

            // Calculate pan to center
            double centerX = (minX + maxX) / 2;
            double centerY = (minY + maxY) / 2;
            PanX = viewportWidth / 2 - centerX * Zoom;
            PanY = viewportHeight / 2 - centerY * Zoom;

            Publish(new RequestRefreshEvent { FullRefresh = true });
        }

        private void ExecuteSelectAll()
        {
            if (Project == null) return;

            var toSelect = Project.Placements
                .Where(p => p.Side == ViewSide || ViewSide == BoardSide.Top)
                .ToList();

            foreach (var p in toSelect)
            {
                p.IsSelected = true;
            }

            UpdateSelection();
        }

        private void ExecuteSelectNone()
        {
            foreach (var p in SelectedPlacements.ToList())
            {
                p.IsSelected = false;
            }
            SelectedPlacements.Clear();
            Publish(new SelectionChangedEvent { SelectedPlacements = new List<Placement>(), Source = this });
        }

        private void ExecuteSelectSamePart()
        {
            if (Project == null || SelectedPlacements.Count == 0) return;

            var components = SelectedPlacements
                .Where(p => p.Component != null)
                .Select(p => p.Component)
                .Distinct()
                .ToList();

            foreach (var placement in Project.Placements)
            {
                if (components.Contains(placement.Component))
                {
                    placement.IsSelected = true;
                }
            }

            UpdateSelection();
        }

        private void ExecuteSelectSamePackage()
        {
            if (Project == null || SelectedPlacements.Count == 0) return;

            var packages = SelectedPlacements
                .Where(p => p.Package != null)
                .Select(p => p.Package)
                .Distinct()
                .ToList();

            foreach (var placement in Project.Placements)
            {
                if (packages.Contains(placement.Package))
                {
                    placement.IsSelected = true;
                }
            }

            UpdateSelection();
        }

        private void ExecuteRotateSelection(double angle)
        {
            foreach (var placement in SelectedPlacements)
            {
                placement.Rotation += angle;
            }
            Publish(new RequestRefreshEvent { FullRefresh = false });
        }

        private void ExecuteDeleteSelection()
        {
            if (Project == null) return;

            foreach (var placement in SelectedPlacements.ToList())
            {
                Project.Placements.Remove(placement);
                Publish(new PlacementRemovedEvent { Placement = placement });
            }

            SelectedPlacements.Clear();
            Publish(new RequestRefreshEvent { FullRefresh = true });
        }

        private void ExecuteSetOrigin()
        {
            if (Project == null || SelectedPlacements.Count != 1) return;

            var placement = SelectedPlacements[0];
            var offsetX = placement.X;
            var offsetY = placement.Y;

            // Move all placements relative to new origin
            foreach (var p in Project.Placements)
            {
                p.X -= offsetX;
                p.Y -= offsetY;
            }

            foreach (var f in Project.Fiducials)
            {
                f.X -= offsetX;
                f.Y -= offsetY;
            }

            Project.Board.Origin = new Point(0, 0);
            Publish(new RequestRefreshEvent { FullRefresh = true });
            Publish(new StatusMessageEvent { Message = string.Format("Origin set to {0}", placement.Reference) });
        }

        private void ExecuteMeasureDistance()
        {
            // TODO: Enter measurement mode
            Publish(new StatusMessageEvent { Message = "Click two points to measure distance" });
        }

        private void ExecuteFocusSelection()
        {
            if (SelectedPlacements.Count == 0) return;

            Publish(new FocusMultiplePlacementsEvent
            {
                Placements = SelectedPlacements.ToList(),
                CenterView = true
            });
        }

        private void ExecuteEnableForExport()
        {
            foreach (var placement in SelectedPlacements)
            {
                if (ViewSide == BoardSide.Top)
                    placement.IsExportEnabledTop = true;
                else
                    placement.IsExportEnabledBottom = true;
            }
            Publish(new RequestRefreshEvent { FullRefresh = false });
        }

        private void ExecuteDisableForExport()
        {
            foreach (var placement in SelectedPlacements)
            {
                if (ViewSide == BoardSide.Top)
                    placement.IsExportEnabledTop = false;
                else
                    placement.IsExportEnabledBottom = false;
            }
            Publish(new RequestRefreshEvent { FullRefresh = false });
        }

        private void ExecuteAddPcbArea()
        {
            // TODO: Show dialog to define PCB area
            Publish(new ShowDialogEvent { DialogType = "PcbArea" });
            Publish(new StatusMessageEvent { Message = "Draw PCB area on canvas" });
        }

        private void ExecuteAddPlacement()
        {
            if (Project == null) return;

            // Create a new placement at cursor position
            var placement = new Placement
            {
                Reference = string.Format("U{0}", Project.Placements.Count + 1),
                X = CursorPosition.X,
                Y = CursorPosition.Y,
                Rotation = 0,
                Side = ViewSide
            };

            Project.Placements.Add(placement);
            SelectPlacement(placement);
            Publish(new RequestRefreshEvent { FullRefresh = true });
            Publish(new StatusMessageEvent { Message = string.Format("Added placement {0}", placement.Reference) });
        }

        private void ExecuteAddFiducial()
        {
            if (Project == null) return;

            var fiducial = new Fiducial
            {
                Name = string.Format("FID{0}", Project.Fiducials.Count + 1),
                X = CursorPosition.X,
                Y = CursorPosition.Y,
                Side = ViewSide,
                Type = FiducialType.Global
            };

            Project.Fiducials.Add(fiducial);
            Publish(new RequestRefreshEvent { FullRefresh = true });
            Publish(new StatusMessageEvent { Message = string.Format("Added fiducial {0}", fiducial.Name) });
        }

        #endregion

        #region Selection

        public void SelectPlacement(Placement placement, bool addToSelection = false)
        {
            if (!addToSelection)
            {
                foreach (var p in SelectedPlacements)
                {
                    p.IsSelected = false;
                }
                SelectedPlacements.Clear();
            }

            if (placement != null && !SelectedPlacements.Contains(placement))
            {
                placement.IsSelected = true;
                SelectedPlacements.Add(placement);
            }

            Publish(new SelectionChangedEvent
            {
                SelectedPlacements = SelectedPlacements.ToList(),
                Source = this
            });
        }

        public void SelectPlacementsInRect(Rect worldRect, bool addToSelection = false)
        {
            if (Project == null) return;

            if (!addToSelection)
            {
                foreach (var p in SelectedPlacements)
                {
                    p.IsSelected = false;
                }
                SelectedPlacements.Clear();
            }

            foreach (var placement in Project.Placements)
            {
                if (worldRect.Contains(placement.Position))
                {
                    placement.IsSelected = true;
                    if (!SelectedPlacements.Contains(placement))
                    {
                        SelectedPlacements.Add(placement);
                    }
                }
            }

            Publish(new SelectionChangedEvent
            {
                SelectedPlacements = SelectedPlacements.ToList(),
                Source = this
            });
        }

        private void UpdateSelection()
        {
            if (Project == null) return;

            SelectedPlacements.Clear();
            foreach (var p in Project.Placements.Where(p => p.IsSelected))
            {
                SelectedPlacements.Add(p);
            }

            Publish(new SelectionChangedEvent
            {
                SelectedPlacements = SelectedPlacements.ToList(),
                Source = this
            });
        }

        #endregion

        #region Event Handlers

        private void OnSelectionChanged(SelectionChangedEvent e)
        {
            if (e.Source == this) return;

            // Sync selection from other sources
            foreach (var p in Project?.Placements ?? Enumerable.Empty<Placement>())
            {
                p.IsSelected = e.SelectedPlacements.Contains(p);
            }

            SelectedPlacements.Clear();
            foreach (var p in e.SelectedPlacements)
            {
                SelectedPlacements.Add(p);
            }

            Publish(new RequestRefreshEvent { FullRefresh = false });
        }

        private void OnFocusPlacement(FocusPlacementEvent e)
        {
            if (e.CenterView && e.Placement != null)
            {
                // TODO: Center view on placement
                PanX = -e.Placement.X * Zoom;
                PanY = -e.Placement.Y * Zoom;
            }
        }

        #endregion
    }
}
