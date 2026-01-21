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
    /// View model for the Gerber viewer
    /// </summary>
    public class GerberViewerViewModel : ViewModelBase
    {
        private Project _project;
        private double _zoom = 1.0;
        private double _panX;
        private double _panY;
        private Point _cursorPosition;
        private GerberLayer _selectedLayer;
        private ObservableCollection<GerberPrimitive> _selectedPrimitives;
        private string _coordinateDisplay;

        public Project Project
        {
            get { return _project; }
            set
            {
                if (SetProperty(ref _project, value))
                {
                    OnPropertyChanged("Layers");
                }
            }
        }

        public double Zoom
        {
            get { return _zoom; }
            set { SetProperty(ref _zoom, Math.Max(0.1, Math.Min(50, value))); }
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

        public GerberLayer SelectedLayer
        {
            get { return _selectedLayer; }
            set { SetProperty(ref _selectedLayer, value); }
        }

        public ObservableCollection<GerberPrimitive> SelectedPrimitives
        {
            get { return _selectedPrimitives; }
            set { SetProperty(ref _selectedPrimitives, value); }
        }

        public IEnumerable<GerberLayer> Layers
        {
            get { return Project?.GerberLayers ?? Enumerable.Empty<GerberLayer>(); }
        }

        // Commands
        public ICommand ImportGerberCommand { get; private set; }
        public ICommand RemoveLayerCommand { get; private set; }
        public ICommand ToggleLayerVisibilityCommand { get; private set; }
        public ICommand SetLayerColorCommand { get; private set; }
        public ICommand ZoomInCommand { get; private set; }
        public ICommand ZoomOutCommand { get; private set; }
        public ICommand ZoomFitCommand { get; private set; }
        public ICommand SelectAllCommand { get; private set; }
        public ICommand SelectNoneCommand { get; private set; }
        public ICommand CreatePackageFromSelectionCommand { get; private set; }
        public ICommand AddSelectionToOutputCommand { get; private set; }
        public ICommand SetBoardOutlineCommand { get; private set; }
        public ICommand SetCircuitOutlineCommand { get; private set; }
        public ICommand MeasureDistanceCommand { get; private set; }

        public GerberViewerViewModel()
        {
            _selectedPrimitives = new ObservableCollection<GerberPrimitive>();
            InitializeCommands();
            SubscribeToEvents();
        }

        private void InitializeCommands()
        {
            ImportGerberCommand = new RelayCommand(ExecuteImportGerber);
            RemoveLayerCommand = new RelayCommand(ExecuteRemoveLayer, () => SelectedLayer != null);
            ToggleLayerVisibilityCommand = new RelayCommand<GerberLayer>(ExecuteToggleLayerVisibility);
            SetLayerColorCommand = new RelayCommand<GerberLayer>(ExecuteSetLayerColor);
            ZoomInCommand = new RelayCommand(() => Zoom *= 1.2);
            ZoomOutCommand = new RelayCommand(() => Zoom /= 1.2);
            ZoomFitCommand = new RelayCommand(ExecuteZoomFit);
            SelectAllCommand = new RelayCommand(ExecuteSelectAll, () => SelectedLayer != null);
            SelectNoneCommand = new RelayCommand(ExecuteSelectNone);
            CreatePackageFromSelectionCommand = new RelayCommand(ExecuteCreatePackageFromSelection,
                () => SelectedPrimitives.Count > 0);
            AddSelectionToOutputCommand = new RelayCommand(ExecuteAddSelectionToOutput,
                () => SelectedPrimitives.Count > 0);
            SetBoardOutlineCommand = new RelayCommand(ExecuteSetBoardOutline, () => SelectedPrimitives.Count > 0);
            SetCircuitOutlineCommand = new RelayCommand(ExecuteSetCircuitOutline, () => SelectedPrimitives.Count > 0);
            MeasureDistanceCommand = new RelayCommand(ExecuteMeasureDistance);
        }

        private void SubscribeToEvents()
        {
            Subscribe<GerberLayerAddedEvent>(OnLayerAdded);
            Subscribe<GerberLayerRemovedEvent>(OnLayerRemoved);
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
                ShowGrid = true,
                GridSpacing = 1.0,
                Units = Project != null ? Project.Units : Units.Millimeters
            };
        }

        #region Command Implementations

        private void ExecuteImportGerber()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Gerber Files (*.gbr;*.ger;*.gtl;*.gbl;*.gto;*.gbo;*.gts;*.gbs)|" +
                        "*.gbr;*.ger;*.gtl;*.gbl;*.gto;*.gbo;*.gts;*.gbs|" +
                        "All Files (*.*)|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var filePath in dialog.FileNames)
                {
                    // TODO: Parse gerber file and add layer
                    var layer = new GerberLayer(
                        System.IO.Path.GetFileName(filePath),
                        filePath
                    );

                    // Detect layer type from filename
                    string ext = System.IO.Path.GetExtension(filePath).ToLower();
                    switch (ext)
                    {
                        case ".gtl":
                            layer.LayerType = GerberLayerType.TopCopper;
                            layer.Color = System.Windows.Media.Color.FromRgb(255, 0, 0);
                            break;
                        case ".gbl":
                            layer.LayerType = GerberLayerType.BottomCopper;
                            layer.Color = System.Windows.Media.Color.FromRgb(0, 0, 255);
                            break;
                        case ".gto":
                            layer.LayerType = GerberLayerType.TopSilkscreen;
                            layer.Color = System.Windows.Media.Color.FromRgb(255, 255, 0);
                            break;
                        case ".gbo":
                            layer.LayerType = GerberLayerType.BottomSilkscreen;
                            layer.Color = System.Windows.Media.Color.FromRgb(255, 255, 0);
                            break;
                        case ".gts":
                            layer.LayerType = GerberLayerType.TopSoldermask;
                            layer.Color = System.Windows.Media.Color.FromRgb(0, 255, 0);
                            break;
                        case ".gbs":
                            layer.LayerType = GerberLayerType.BottomSoldermask;
                            layer.Color = System.Windows.Media.Color.FromRgb(0, 255, 0);
                            break;
                        default:
                            layer.Color = System.Windows.Media.Color.FromRgb(0, 255, 0);
                            break;
                    }

                    Project?.GerberLayers.Add(layer);
                    Publish(new GerberLayerAddedEvent { Layer = layer });
                }
            }
        }

        private void ExecuteRemoveLayer()
        {
            if (Project == null || SelectedLayer == null) return;

            var layer = SelectedLayer;
            Project.GerberLayers.Remove(layer);
            SelectedLayer = null;
            Publish(new GerberLayerRemovedEvent { Layer = layer });
        }

        private void ExecuteToggleLayerVisibility(GerberLayer layer)
        {
            if (layer != null)
            {
                layer.IsVisible = !layer.IsVisible;
                Publish(new RequestRefreshEvent { FullRefresh = false });
            }
        }

        private void ExecuteSetLayerColor(GerberLayer layer)
        {
            // TODO: Show color picker dialog
        }

        private void ExecuteZoomFit()
        {
            if (Project == null || !Project.GerberLayers.Any()) return;

            // Calculate bounds of all visible layers
            Rect bounds = Rect.Empty;
            foreach (var layer in Project.GerberLayers.Where(l => l.IsVisible))
            {
                if (bounds.IsEmpty)
                    bounds = layer.Bounds;
                else
                    bounds.Union(layer.Bounds);
            }

            if (!bounds.IsEmpty)
            {
                // TODO: Calculate zoom to fit bounds
                Zoom = 1.0;
                PanX = -bounds.X * Zoom;
                PanY = -bounds.Y * Zoom;
            }
        }

        private void ExecuteSelectAll()
        {
            if (SelectedLayer == null) return;

            SelectedPrimitives.Clear();
            foreach (var primitive in SelectedLayer.Primitives)
            {
                primitive.IsSelected = true;
                SelectedPrimitives.Add(primitive);
            }

            Publish(new GerberSelectionChangedEvent
            {
                SelectedPrimitives = SelectedPrimitives.ToList()
            });
        }

        private void ExecuteSelectNone()
        {
            foreach (var primitive in SelectedPrimitives)
            {
                primitive.IsSelected = false;
            }
            SelectedPrimitives.Clear();

            Publish(new GerberSelectionChangedEvent
            {
                SelectedPrimitives = new List<GerberPrimitive>()
            });
        }

        private void ExecuteCreatePackageFromSelection()
        {
            if (Project == null || SelectedPrimitives.Count == 0) return;

            // Calculate bounds of selected primitives
            Rect bounds = Rect.Empty;
            foreach (var prim in SelectedPrimitives)
            {
                if (bounds.IsEmpty)
                    bounds = prim.GetBounds();
                else
                    bounds.Union(prim.GetBounds());
            }

            // Create package from primitives
            var package = new Package(Package.GeneratePackageName())
            {
                Width = bounds.Width,
                Length = bounds.Height,
                Height = 0.5
            };

            // Convert primitives to package graphics
            var centerX = bounds.X + bounds.Width / 2;
            var centerY = bounds.Y + bounds.Height / 2;

            int pinNumber = 1;
            foreach (var prim in SelectedPrimitives)
            {
                var graphic = new PackageGraphic
                {
                    X = prim.X - centerX,
                    Y = prim.Y - centerY,
                    Width = prim.Width,
                    Height = prim.Height,
                    Rotation = prim.Rotation,
                    IsPad = true
                };

                switch (prim.Type)
                {
                    case GerberPrimitiveType.Circle:
                    case GerberPrimitiveType.Flash:
                        graphic.ShapeType = GraphicShapeType.Circle;
                        break;
                    case GerberPrimitiveType.Rectangle:
                        graphic.ShapeType = GraphicShapeType.Rectangle;
                        break;
                    case GerberPrimitiveType.Obround:
                        graphic.ShapeType = GraphicShapeType.RoundedRectangle;
                        graphic.CornerRadius = Math.Min(prim.Width, prim.Height) / 2;
                        break;
                    default:
                        graphic.ShapeType = GraphicShapeType.Rectangle;
                        break;
                }

                package.Graphics.Add(graphic);

                // Add pin at pad center
                package.Pins.Add(new Pin
                {
                    Number = pinNumber++,
                    X = graphic.X,
                    Y = graphic.Y,
                    Width = prim.Width * 0.5,
                    Height = prim.Height * 0.5
                });
            }

            Project.Packages.Add(package);
            Publish(new PackageAddedEvent { Package = package });
            Publish(new StatusMessageEvent
            {
                Message = string.Format("Created package {0} with {1} pads", package.Name, package.Pins.Count)
            });
        }

        private void ExecuteAddSelectionToOutput()
        {
            if (Project == null || SelectedPrimitives.Count == 0) return;

            // Calculate center of selection
            Rect bounds = Rect.Empty;
            foreach (var prim in SelectedPrimitives)
            {
                if (bounds.IsEmpty)
                    bounds = prim.GetBounds();
                else
                    bounds.Union(prim.GetBounds());
            }

            var centerX = bounds.X + bounds.Width / 2;
            var centerY = bounds.Y + bounds.Height / 2;

            // Create placement at selection center (no reference - user can assign later)
            var placement = new Placement
            {
                X = centerX,
                Y = centerY,
                Rotation = 0,
                Side = BoardSide.Top
            };

            // Create package from selection if needed
            ExecuteCreatePackageFromSelection();
            var lastPackage = Project.Packages.LastOrDefault();
            if (lastPackage != null)
            {
                placement.Package = lastPackage;
            }

            Project.Placements.Add(placement);
            Publish(new PlacementAddedEvent { Placement = placement });
            Publish(new StatusMessageEvent { Message = "Added placement to output" });
        }

        private void ExecuteSetBoardOutline()
        {
            if (Project == null || SelectedPrimitives.Count == 0) return;

            var outline = ExtractOutlinePoints();
            if (outline.Count > 0)
            {
                Project.Board.BoardOutline = outline;

                // Calculate board dimensions
                var bounds = GetBounds(outline);
                Project.Board.Width = bounds.Width;
                Project.Board.Height = bounds.Height;

                Publish(new StatusMessageEvent { Message = "Board outline set" });
                Publish(new RequestRefreshEvent { FullRefresh = true });
            }
        }

        private void ExecuteSetCircuitOutline()
        {
            if (Project == null || SelectedPrimitives.Count == 0) return;

            var outline = ExtractOutlinePoints();
            if (outline.Count > 0)
            {
                Project.Board.CircuitOutline = outline;
                Publish(new StatusMessageEvent { Message = "Circuit outline set" });
                Publish(new RequestRefreshEvent { FullRefresh = true });
            }
        }

        private void ExecuteMeasureDistance()
        {
            Publish(new StatusMessageEvent { Message = "Click two points to measure distance" });
            // TODO: Enter measurement mode
        }

        #endregion

        #region Helper Methods

        private List<Point> ExtractOutlinePoints()
        {
            var points = new List<Point>();

            foreach (var prim in SelectedPrimitives)
            {
                if (prim.Points != null && prim.Points.Count > 0)
                {
                    points.AddRange(prim.Points);
                }
                else
                {
                    // For simple shapes, add corner points
                    var bounds = prim.GetBounds();
                    points.Add(new Point(bounds.Left, bounds.Top));
                    points.Add(new Point(bounds.Right, bounds.Top));
                    points.Add(new Point(bounds.Right, bounds.Bottom));
                    points.Add(new Point(bounds.Left, bounds.Bottom));
                }
            }

            return points;
        }

        private Rect GetBounds(List<Point> points)
        {
            if (points.Count == 0) return Rect.Empty;

            double minX = points.Min(p => p.X);
            double minY = points.Min(p => p.Y);
            double maxX = points.Max(p => p.X);
            double maxY = points.Max(p => p.Y);

            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        public void SelectPrimitivesInRect(Rect worldRect, bool addToSelection = false)
        {
            if (SelectedLayer == null) return;

            if (!addToSelection)
            {
                ExecuteSelectNone();
            }

            foreach (var primitive in SelectedLayer.Primitives)
            {
                if (worldRect.IntersectsWith(primitive.GetBounds()))
                {
                    primitive.IsSelected = true;
                    if (!SelectedPrimitives.Contains(primitive))
                    {
                        SelectedPrimitives.Add(primitive);
                    }
                }
            }

            Publish(new GerberSelectionChangedEvent
            {
                SelectedPrimitives = SelectedPrimitives.ToList()
            });
        }

        #endregion

        #region Event Handlers

        private void OnLayerAdded(GerberLayerAddedEvent e)
        {
            OnPropertyChanged("Layers");
            if (SelectedLayer == null)
            {
                SelectedLayer = e.Layer;
            }
        }

        private void OnLayerRemoved(GerberLayerRemovedEvent e)
        {
            OnPropertyChanged("Layers");
        }

        #endregion
    }
}
