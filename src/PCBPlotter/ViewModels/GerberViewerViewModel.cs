using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using PCBPlotter.Core.Events;
using PCBPlotter.Core.Models;
using PCBPlotter.Core.Rendering;
using PCBPlotter.Core.Services;
using PCBPlotter.Views;

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
        private double _viewportWidth = 800;
        private double _viewportHeight = 600;
        private Point _cursorPosition;
        private GerberLayer _selectedLayer;
        private ObservableCollection<GerberPrimitive> _selectedPrimitives;
        private string _coordinateDisplay;
        private BoardSide _activeAssignmentSide = BoardSide.Top;

        public Project Project
        {
            get { return _project; }
            set
            {
                if (SetProperty(ref _project, value))
                {
                    OnPropertyChanged("Layers");
                    OnPropertyChanged("GerberLayersCollection");
                }
            }
        }

        public double Zoom
        {
            get { return _zoom; }
            set { SetProperty(ref _zoom, Math.Max(0.1, Math.Min(500, value))); }
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

        /// <summary>
        /// Active assignment layer for new placements (Top or Bottom)
        /// </summary>
        public BoardSide ActiveAssignmentSide
        {
            get { return _activeAssignmentSide; }
            set { SetProperty(ref _activeAssignmentSide, value); }
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

        public ObservableCollection<GerberLayer> GerberLayersCollection
        {
            get { return Project?.GerberLayers; }
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

        // Layer stepping commands
        public ICommand StepLayerUpCommand { get; private set; }
        public ICommand StepLayerDownCommand { get; private set; }
        public ICommand InvertLayersCommand { get; private set; }
        public ICommand ShowAllLayersCommand { get; private set; }
        public ICommand HideAllLayersCommand { get; private set; }

        // Layer reorder & management commands
        public ICommand MoveLayerUpCommand { get; private set; }
        public ICommand MoveLayerDownCommand { get; private set; }
        public ICommand RenameLayerCommand { get; private set; }
        public ICommand RandomizeColorsCommand { get; private set; }

        // Event to notify view of layer changes requiring refresh
        public event Action LayerVisibilityChanged;
        public event Action CanvasRefreshRequested;

        // Event to notify view when active layer changes (for canvas update)
        public event Action<GerberLayer> ActiveLayerChanged;

        // Event to request the view to select a layer in the ListBox
        public event Action<GerberLayer> RequestLayerListSelection;

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

            // Layer stepping commands
            StepLayerUpCommand = new RelayCommand(ExecuteStepLayerUp, () => Project?.GerberLayers?.Count > 0);
            StepLayerDownCommand = new RelayCommand(ExecuteStepLayerDown, () => Project?.GerberLayers?.Count > 0);
            InvertLayersCommand = new RelayCommand(ExecuteInvertLayers, () => Project?.GerberLayers?.Count > 0);
            ShowAllLayersCommand = new RelayCommand(ExecuteShowAllLayers, () => Project?.GerberLayers?.Count > 0);
            HideAllLayersCommand = new RelayCommand(ExecuteHideAllLayers, () => Project?.GerberLayers?.Count > 0);

            // Layer reorder & management commands
            MoveLayerUpCommand = new RelayCommand(ExecuteMoveLayerUp, () => SelectedLayer != null && Project?.GerberLayers?.Count > 1);
            MoveLayerDownCommand = new RelayCommand(ExecuteMoveLayerDown, () => SelectedLayer != null && Project?.GerberLayers?.Count > 1);
            RenameLayerCommand = new RelayCommand(ExecuteRenameLayer, () => SelectedLayer != null);
            RandomizeColorsCommand = new RelayCommand(ExecuteRandomizeColors, () => Project?.GerberLayers?.Count > 0);
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

        #region Public Methods for View

        /// <summary>
        /// Set a layer as the active layer (called from view on double-click)
        /// </summary>
        public void SetActiveLayer(GerberLayer layer)
        {
            if (layer == null) return;

            // Deactivate all layers
            if (Project?.GerberLayers != null)
            {
                foreach (var l in Project.GerberLayers)
                    l.IsActive = false;
            }

            layer.IsActive = true;
            SelectedLayer = layer;
            ActiveLayerChanged?.Invoke(layer);
        }

        /// <summary>
        /// Toggle visibility of the given layers (called from view on Space key)
        /// </summary>
        public void ToggleSelectedLayersVisibility(List<GerberLayer> layers)
        {
            foreach (var layer in layers)
            {
                layer.IsVisible = !layer.IsVisible;
            }
            NotifyLayerVisibilityChanged();
        }

        /// <summary>
        /// Remove multiple selected layers (called from view on Delete key or Remove button)
        /// </summary>
        public void RemoveSelectedLayers(List<GerberLayer> layers)
        {
            if (Project == null) return;

            foreach (var layer in layers)
            {
                Project.GerberLayers.Remove(layer);
                Publish(new GerberLayerRemovedEvent { Layer = layer });
            }

            SelectedLayer = null;
            OnPropertyChanged("Layers");
            OnPropertyChanged("GerberLayersCollection");
            NotifyLayerVisibilityChanged();
        }

        /// <summary>
        /// Notify view that layer visibility changed (public for view to call)
        /// </summary>
        public void NotifyLayerVisibilityChanged()
        {
            LayerVisibilityChanged?.Invoke();
            Publish(new RequestRefreshEvent { FullRefresh = true });
        }

        /// <summary>
        /// Try to pick a layer at the given world position by hit-testing all visible layers.
        /// Returns the topmost layer that has a primitive at that position.
        /// </summary>
        public GerberLayer PickLayerAtWorldPosition(Point worldPos, double hitRadius)
        {
            if (Project?.GerberLayers == null) return null;

            // Iterate in reverse order (top-most layer first, since later layers render on top)
            for (int i = Project.GerberLayers.Count - 1; i >= 0; i--)
            {
                var layer = Project.GerberLayers[i];
                if (!layer.IsVisible) continue;

                // Quick bounds check
                var bounds = layer.Bounds;
                if (bounds.IsEmpty) continue;
                var expandedBounds = bounds;
                expandedBounds.Inflate(hitRadius, hitRadius);
                if (!expandedBounds.Contains(worldPos)) continue;

                // Check primitives
                foreach (var prim in layer.Primitives)
                {
                    if (!prim.IsDark) continue;
                    var primBounds = prim.GetBounds();
                    primBounds.Inflate(hitRadius, hitRadius);
                    if (primBounds.Contains(worldPos))
                    {
                        return layer;
                    }
                }
            }

            return null;
        }

        #endregion

        #region Command Implementations

        private void ExecuteImportGerber()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "All Files (*.*)|*.*|" +
                        "Gerber Files (*.gbr;*.ger;*.art;*.gtl;*.gbl;*.gto;*.gbo;*.gts;*.gbs;*.gtp;*.gbp;*.gko;*.gm1)|" +
                        "*.gbr;*.ger;*.art;*.gtl;*.gbl;*.gto;*.gbo;*.gts;*.gbs;*.gtp;*.gbp;*.gko;*.gm1",
                Multiselect = true,
                Title = "Import Gerber Files"
            };

            if (dialog.ShowDialog() == true)
            {
                ImportGerberFiles(dialog.FileNames);
            }
        }

        public void ImportGerberFiles(IEnumerable<string> filePaths)
        {
            var parser = new GerberParser();
            int importedCount = 0;

            foreach (var filePath in filePaths)
            {
                try
                {
                    var layer = parser.Parse(filePath);

                    // Override color with hash-based color for consistency and uniqueness
                    if (layer.LayerType == GerberLayerType.Unknown)
                    {
                        layer.Color = LayerColorHelper.ColorFromName(layer.Name);
                    }

                    if (Project != null)
                    {
                        Project.GerberLayers.Add(layer);
                        Publish(new GerberLayerAddedEvent { Layer = layer });
                        importedCount++;
                    }
                }
                catch (Exception ex)
                {
                    Publish(new StatusMessageEvent
                    {
                        Message = $"Failed to import {System.IO.Path.GetFileName(filePath)}: {ex.Message}",
                        Type = StatusMessageType.Error
                    });
                }
            }

            if (importedCount > 0)
            {
                // Ensure no duplicate colors among all layers
                EnsureDistinctColors();

                Publish(new StatusMessageEvent
                {
                    Message = $"Imported {importedCount} Gerber layer(s) with {Layers.Sum(l => l.Primitives?.Count ?? 0)} primitives"
                });

                // Auto zoom-to-fit after import
                ExecuteZoomFit();
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
                LayerVisibilityChanged?.Invoke();
                Publish(new RequestRefreshEvent { FullRefresh = true });
            }
        }

        /// <summary>
        /// Move the selected layer up in the collection (renders earlier = behind)
        /// </summary>
        private void ExecuteMoveLayerUp()
        {
            if (Project?.GerberLayers == null || SelectedLayer == null) return;

            int index = Project.GerberLayers.IndexOf(SelectedLayer);
            if (index <= 0) return;

            Project.GerberLayers.Move(index, index - 1);
            OnPropertyChanged("Layers");
            NotifyLayerVisibilityChanged();
            RequestLayerListSelection?.Invoke(SelectedLayer);
        }

        /// <summary>
        /// Move the selected layer down in the collection (renders later = in front)
        /// </summary>
        private void ExecuteMoveLayerDown()
        {
            if (Project?.GerberLayers == null || SelectedLayer == null) return;

            int index = Project.GerberLayers.IndexOf(SelectedLayer);
            if (index < 0 || index >= Project.GerberLayers.Count - 1) return;

            Project.GerberLayers.Move(index, index + 1);
            OnPropertyChanged("Layers");
            NotifyLayerVisibilityChanged();
            RequestLayerListSelection?.Invoke(SelectedLayer);
        }

        /// <summary>
        /// Rename the selected layer via input dialog
        /// </summary>
        private void ExecuteRenameLayer()
        {
            if (SelectedLayer == null) return;

            var inputDialog = new InputDialog(
                "Rename Layer",
                "Enter new name for layer:",
                SelectedLayer.Name);
            inputDialog.Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);

            if (inputDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(inputDialog.Value))
            {
                SelectedLayer.Name = inputDialog.Value.Trim();
                OnPropertyChanged("Layers");
            }
        }

        /// <summary>
        /// Re-randomize colors of all layers ensuring distinct colors
        /// </summary>
        private void ExecuteRandomizeColors()
        {
            if (Project?.GerberLayers == null || Project.GerberLayers.Count == 0) return;

            var rng = new Random();
            foreach (var layer in Project.GerberLayers)
            {
                // Generate a new random seed to perturb the hash
                layer.Color = LayerColorHelper.ColorFromHash(
                    LayerColorHelper.HashString(layer.Name) ^ rng.Next());
            }

            EnsureDistinctColors();
            NotifyLayerVisibilityChanged();
        }

        /// <summary>
        /// Step to the next layer up in the stack (shows only that layer)
        /// </summary>
        private void ExecuteStepLayerUp()
        {
            if (Project?.GerberLayers == null || Project.GerberLayers.Count == 0)
                return;

            var layers = Project.GerberLayers.ToList();
            int currentIndex = SelectedLayer != null ? layers.IndexOf(SelectedLayer) : -1;

            // Find next index (wrap around)
            int nextIndex = (currentIndex - 1 + layers.Count) % layers.Count;

            // Hide all layers, show and select the next one
            foreach (var layer in layers)
            {
                layer.IsVisible = false;
                layer.IsActive = false;
            }

            layers[nextIndex].IsVisible = true;
            layers[nextIndex].IsActive = true;
            SelectedLayer = layers[nextIndex];

            ActiveLayerChanged?.Invoke(layers[nextIndex]);
            LayerVisibilityChanged?.Invoke();
            Publish(new RequestRefreshEvent { FullRefresh = true });
        }

        /// <summary>
        /// Step to the next layer down in the stack (shows only that layer)
        /// </summary>
        private void ExecuteStepLayerDown()
        {
            if (Project?.GerberLayers == null || Project.GerberLayers.Count == 0)
                return;

            var layers = Project.GerberLayers.ToList();
            int currentIndex = SelectedLayer != null ? layers.IndexOf(SelectedLayer) : -1;

            // Find next index (wrap around)
            int nextIndex = (currentIndex + 1) % layers.Count;

            // Hide all layers, show and select the next one
            foreach (var layer in layers)
            {
                layer.IsVisible = false;
                layer.IsActive = false;
            }

            layers[nextIndex].IsVisible = true;
            layers[nextIndex].IsActive = true;
            SelectedLayer = layers[nextIndex];

            ActiveLayerChanged?.Invoke(layers[nextIndex]);
            LayerVisibilityChanged?.Invoke();
            Publish(new RequestRefreshEvent { FullRefresh = true });
        }

        /// <summary>
        /// Invert visibility of all layers
        /// </summary>
        private void ExecuteInvertLayers()
        {
            if (Project?.GerberLayers == null)
                return;

            foreach (var layer in Project.GerberLayers)
            {
                layer.IsVisible = !layer.IsVisible;
            }

            LayerVisibilityChanged?.Invoke();
            Publish(new RequestRefreshEvent { FullRefresh = true });
        }

        /// <summary>
        /// Show all layers
        /// </summary>
        private void ExecuteShowAllLayers()
        {
            if (Project?.GerberLayers == null)
                return;

            foreach (var layer in Project.GerberLayers)
            {
                layer.IsVisible = true;
            }

            LayerVisibilityChanged?.Invoke();
            Publish(new RequestRefreshEvent { FullRefresh = true });
        }

        /// <summary>
        /// Hide all layers
        /// </summary>
        private void ExecuteHideAllLayers()
        {
            if (Project?.GerberLayers == null)
                return;

            foreach (var layer in Project.GerberLayers)
            {
                layer.IsVisible = false;
            }

            LayerVisibilityChanged?.Invoke();
            Publish(new RequestRefreshEvent { FullRefresh = true });
        }

        private void ExecuteSetLayerColor(GerberLayer layer)
        {
            // TODO: Show color picker dialog
        }

        private void ExecuteZoomFit()
        {
            // Use stored viewport dimensions (updated by View when canvas size changes)
            ZoomToFitWithViewport(_viewportWidth, _viewportHeight);
        }

        /// <summary>
        /// Updates the stored viewport size. Called by the View when canvas size changes.
        /// </summary>
        public void UpdateViewportSize(double width, double height)
        {
            if (width > 0 && height > 0)
            {
                _viewportWidth = width;
                _viewportHeight = height;
            }
        }

        public void ZoomToFitWithViewport(double viewportWidth, double viewportHeight)
        {
            // Store the viewport size for future ZoomFit calls
            UpdateViewportSize(viewportWidth, viewportHeight);

            if (Project == null || !Project.GerberLayers.Any()) return;

            // Calculate bounds of all visible layers
            Rect bounds = Rect.Empty;
            foreach (var layer in Project.GerberLayers.Where(l => l.IsVisible))
            {
                var layerBounds = layer.Bounds;
                if (!layerBounds.IsEmpty)
                {
                    if (bounds.IsEmpty)
                        bounds = layerBounds;
                    else
                        bounds.Union(layerBounds);
                }
            }

            if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0)
            {
                double marginFactor = 0.9;
                double zoomX = (viewportWidth * marginFactor) / bounds.Width;
                double zoomY = (viewportHeight * marginFactor) / bounds.Height;
                Zoom = Math.Min(zoomX, zoomY);

                double centerX = bounds.X + bounds.Width / 2;
                double centerY = bounds.Y + bounds.Height / 2;
                PanX = viewportWidth / 2 - centerX * Zoom;
                PanY = viewportHeight / 2 - centerY * Zoom;
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

            // Use the classifier pipeline to create an intelligent package
            var package = PackageBodyGenerator.BuildPackage(
                SelectedPrimitives.ToList());

            Project.Packages.Add(package);
            Publish(new PackageAddedEvent { Package = package });
            Publish(new StatusMessageEvent
            {
                Message = string.Format("Created package '{0}' ({1}) with {2} pads",
                    package.Name, package.PartClass, package.Pins.Count)
            });
        }

        /// <summary>
        /// Creates a placement from selected gerber primitives.
        /// Runs feature extraction, classification, package body generation,
        /// then creates a placement at the centroid and links everything.
        /// Navigates to the Design tab to show the result.
        /// </summary>
        private void ExecuteAddSelectionToOutput()
        {
            if (Project == null || SelectedPrimitives.Count == 0) return;

            var primitives = SelectedPrimitives.ToList();

            // Run feature extraction and classification
            var features = ComponentClassifier.ExtractFeatures(primitives);
            var classification = ComponentClassifier.Classify(features);

            // Prompt user for optional reference name, pre-fill with auto-generated
            var generatedName = GenerateUniquePlacementReference();
            var inputDialog = new InputDialog(
                "Create Placement from Selection",
                string.Format("Detected: {0} ({1})\n\nEnter reference designator (leave blank for '{2}'):",
                    classification.SuggestedName, classification.Description, generatedName),
                "");
            inputDialog.Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);

            // Provide existing references for duplicate validation
            inputDialog.ExistingValues = new HashSet<string>(
                Project.Placements
                    .Where(p => !string.IsNullOrEmpty(p.Reference))
                    .Select(p => p.Reference.ToUpperInvariant()),
                StringComparer.OrdinalIgnoreCase);

            if (inputDialog.ShowDialog() != true)
                return;

            var reference = string.IsNullOrWhiteSpace(inputDialog.Value)
                ? generatedName
                : inputDialog.Value.Trim();

            // Build classified package with body graphics, pins, and pin 1 indicator
            var package = PackageBodyGenerator.BuildPackage(primitives, classification.SuggestedName);

            // Compute the orientation of this specific instance
            var instanceAngle = ComponentClassifier.ComputePrincipalAngle(features);

            // Check if a package with this name already exists
            double placementRotation = 0;
            var existingPackage = Project.Packages.FirstOrDefault(
                p => p.Name == package.Name && p.Pins.Count == package.Pins.Count);
            if (existingPackage != null)
            {
                // Compute rotation relative to the canonical package orientation
                // The canonical angle was stored when the package was first created
                double canonicalAngle = existingPackage.DefaultRotation;
                placementRotation = instanceAngle - canonicalAngle;
                // Normalize to [0, 360) - CCW convention
                placementRotation = placementRotation % 360;
                if (placementRotation < 0) placementRotation += 360;
                package = existingPackage;
            }
            else
            {
                // First instance defines the canonical orientation
                package.DefaultRotation = instanceAngle;
                Project.Packages.Add(package);
                Publish(new PackageAddedEvent { Package = package });
            }

            // Create placement at the centroid
            var placement = new Placement
            {
                Reference = reference,
                X = features.Centroid.X,
                Y = features.Centroid.Y,
                Rotation = placementRotation,
                Side = _activeAssignmentSide,
                Package = package
            };

            Project.Placements.Add(placement);
            Publish(new PlacementAddedEvent { Placement = placement });

            Publish(new StatusMessageEvent
            {
                Message = string.Format("Created placement '{0}' -> {1} ({2}, {3} pads) at ({4:F2}, {5:F2}) rot {6}°",
                    reference, package.Name, classification.PartClass,
                    package.Pins.Count, features.Centroid.X, features.Centroid.Y, placementRotation)
            });

            // Mark primitives as consumed and create simplified bounding shapes for visualization
            foreach (var prim in primitives)
            {
                prim.IsConsumed = true;
                prim.IsSelected = false;
            }

            // Add consumed regions (clustered bounding shapes) to the active layer
            // These will be rendered as solid yellow shapes to indicate used areas
            if (SelectedLayer != null)
            {
                SelectedLayer.AddConsumedRegions(primitives, reference);
            }

            // Clear selection so user can immediately start the next selection
            SelectedPrimitives.Clear();
            Publish(new GerberSelectionChangedEvent
            {
                SelectedPrimitives = new List<GerberPrimitive>()
            });

            // Request canvas refresh to show the consumed regions
            CanvasRefreshRequested?.Invoke();

            // Navigate to the Design tab (tab index 0) to show the result
            Publish(new NavigateToTabEvent
            {
                TabIndex = 0,
                ScrollToPlacement = placement
            });
        }

        /// <summary>
        /// Generates a unique placement reference designator (P1, P2, etc.)
        /// </summary>
        private string GenerateUniquePlacementReference()
        {
            const string prefix = "P";
            int number = 1;

            // Find existing references with the same prefix and get the highest number
            var existingNumbers = Project.Placements
                .Where(p => !string.IsNullOrEmpty(p.Reference) && p.Reference.StartsWith(prefix))
                .Select(p =>
                {
                    int n;
                    if (int.TryParse(p.Reference.Substring(prefix.Length), out n))
                        return n;
                    return 0;
                })
                .Where(n => n > 0)
                .ToList();

            if (existingNumbers.Count > 0)
            {
                number = existingNumbers.Max() + 1;
            }

            return prefix + number;
        }

        private void ExecuteSetBoardOutline()
        {
            if (Project == null || SelectedPrimitives.Count == 0) return;

            var outline = ExtractOutlinePoints();
            if (outline.Count > 0)
            {
                Project.Board.BoardOutline = outline;

                // Calculate board dimensions
                var outlineBounds = GetBounds(outline);
                Project.Board.Width = outlineBounds.Width;
                Project.Board.Height = outlineBounds.Height;

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

        /// <summary>
        /// Ensure all layers have distinct colors. If two layers share the same color,
        /// shift the duplicate to a new hash-derived color.
        /// </summary>
        private void EnsureDistinctColors()
        {
            if (Project?.GerberLayers == null) return;

            var usedColors = new HashSet<uint>();
            foreach (var layer in Project.GerberLayers)
            {
                uint colorVal = layer.ColorArgb;
                if (usedColors.Contains(colorVal))
                {
                    // Shift color by hashing name + attempt counter
                    int attempt = 1;
                    uint newColor;
                    do
                    {
                        newColor = LayerColorHelper.ColorArgbFromHash(
                            LayerColorHelper.HashString(layer.Name + "_" + attempt));
                        attempt++;
                    } while (usedColors.Contains(newColor) && attempt < 100);

                    layer.ColorArgb = newColor;
                    colorVal = newColor;
                }
                usedColors.Add(colorVal);
            }
        }

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
                    var primBounds = prim.GetBounds();
                    points.Add(new Point(primBounds.Left, primBounds.Top));
                    points.Add(new Point(primBounds.Right, primBounds.Top));
                    points.Add(new Point(primBounds.Right, primBounds.Bottom));
                    points.Add(new Point(primBounds.Left, primBounds.Bottom));
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
                // Skip non-dark (clear/negative) primitives - they subtract material and shouldn't be selectable
                if (!primitive.IsDark)
                    continue;

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

        /// <summary>
        /// Selects a primitive at the specified world position (for GPU canvas click handling)
        /// </summary>
        public void SelectPrimitiveAtPoint(Point worldPos, bool addToSelection = false, double hitRadiusWorld = 0.5)
        {
            if (SelectedLayer == null) return;

            double hitRadius = hitRadiusWorld;

            GerberPrimitive closestPrimitive = null;
            double closestDistance = double.MaxValue;

            foreach (var primitive in SelectedLayer.Primitives)
            {
                // Skip non-dark (clear/negative) primitives
                if (!primitive.IsDark)
                    continue;

                // Calculate distance to primitive center
                double dx = worldPos.X - primitive.X;
                double dy = worldPos.Y - primitive.Y;
                double distance = Math.Sqrt(dx * dx + dy * dy);

                // Check if point is within primitive bounds with some tolerance
                var primBounds = primitive.GetBounds();
                primBounds.Inflate(hitRadius, hitRadius);

                if (primBounds.Contains(worldPos) && distance < closestDistance)
                {
                    closestDistance = distance;
                    closestPrimitive = primitive;
                }
            }

            if (closestPrimitive != null)
            {
                if (addToSelection)
                {
                    // Toggle selection
                    closestPrimitive.IsSelected = !closestPrimitive.IsSelected;
                    if (closestPrimitive.IsSelected)
                    {
                        if (!SelectedPrimitives.Contains(closestPrimitive))
                            SelectedPrimitives.Add(closestPrimitive);
                    }
                    else
                    {
                        SelectedPrimitives.Remove(closestPrimitive);
                    }
                }
                else
                {
                    ExecuteSelectNone();
                    closestPrimitive.IsSelected = true;
                    SelectedPrimitives.Add(closestPrimitive);
                }

                Publish(new GerberSelectionChangedEvent
                {
                    SelectedPrimitives = SelectedPrimitives.ToList()
                });
            }
            else if (!addToSelection)
            {
                // Clicked on empty space - clear selection
                ExecuteSelectNone();
            }
        }

        #endregion

        #region Event Handlers

        private void OnLayerAdded(GerberLayerAddedEvent e)
        {
            OnPropertyChanged("Layers");
            OnPropertyChanged("GerberLayersCollection");
            if (SelectedLayer == null)
            {
                SelectedLayer = e.Layer;
            }
        }

        private void OnLayerRemoved(GerberLayerRemovedEvent e)
        {
            OnPropertyChanged("Layers");
            OnPropertyChanged("GerberLayersCollection");
        }

        #endregion
    }

    /// <summary>
    /// Helper to generate consistent, distinct colors from layer names using hashing.
    /// </summary>
    public static class LayerColorHelper
    {
        /// <summary>
        /// Generate a deterministic color from a layer name.
        /// Same name always produces the same color.
        /// </summary>
        public static System.Windows.Media.Color ColorFromName(string name)
        {
            int hash = HashString(name ?? "");
            return ColorFromHash(hash);
        }

        /// <summary>
        /// Stable string hash (FNV-1a) that doesn't change across runs.
        /// </summary>
        public static int HashString(string s)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in s)
                {
                    hash ^= c;
                    hash *= 16777619;
                }
                return (int)hash;
            }
        }

        /// <summary>
        /// Convert a hash integer into a vivid, saturated color using HSL.
        /// Hue is spread across the spectrum; saturation and lightness are fixed
        /// to ensure bright, distinguishable colors.
        /// </summary>
        public static System.Windows.Media.Color ColorFromHash(int hash)
        {
            // Use golden-ratio-based hue distribution for maximum spread
            double hue = ((hash & 0x7FFFFFFF) % 360);
            double saturation = 0.75 + ((hash >> 8) & 0xFF) / 1024.0; // 0.75-1.0
            double lightness = 0.45 + ((hash >> 16) & 0xFF) / 1024.0; // 0.45-0.7

            return HslToColor(hue, saturation, lightness);
        }

        /// <summary>
        /// Return ARGB uint from hash (for EnsureDistinctColors)
        /// </summary>
        public static uint ColorArgbFromHash(int hash)
        {
            var c = ColorFromHash(hash);
            return (uint)((c.A << 24) | (c.R << 16) | (c.G << 8) | c.B);
        }

        private static System.Windows.Media.Color HslToColor(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
            double m = l - c / 2;

            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }

            return System.Windows.Media.Color.FromRgb(
                (byte)Math.Round((r + m) * 255),
                (byte)Math.Round((g + m) * 255),
                (byte)Math.Round((b + m) * 255));
        }
    }
}
