using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using PCBPlotter.Core.Events;
using PCBPlotter.Core.Models;
using Component = PCBPlotter.Core.Models.Component;

namespace PCBPlotter.ViewModels
{
    /// <summary>
    /// View model for the component/package editor
    /// </summary>
    public class ComponentEditorViewModel : ViewModelBase
    {
        private Project _project;
        private ICollectionView _packagesView;
        private ICollectionView _componentsView;
        private Package _selectedPackage;
        private Component _selectedComponent;
        private string _filterText;
        private bool _isEditingPackage;
        private PackageGraphic _selectedGraphic;
        private bool _showComponentsMode = true;
        private bool _showPackagesMode;
        private Placement _selectedComponentPlacement;

        // Package editing properties
        private string _editName;
        private string _editDescription;
        private double _editWidth;
        private double _editLength;
        private double _editHeight;
        private PartClass _editPartClass;
        private bool _editHasPolarity;
        private double _editDefaultRotation;

        // Component editing properties
        private string _editComponentPartNumber;
        private string _editComponentManufacturer;
        private string _editComponentValue;
        private string _editComponentDescription;
        private Package _editComponentPackage;
        private string _editFeederSlot;
        private string _editTapeWidth;
        private string _editTapePitch;

        public Project Project
        {
            get { return _project; }
            set
            {
                if (SetProperty(ref _project, value))
                {
                    UpdatePackagesView();
                }
            }
        }

        public ICollectionView PackagesView
        {
            get { return _packagesView; }
            set { SetProperty(ref _packagesView, value); }
        }

        public ICollectionView ComponentsView
        {
            get { return _componentsView; }
            set { SetProperty(ref _componentsView, value); }
        }

        public Package SelectedPackage
        {
            get { return _selectedPackage; }
            set
            {
                if (SetProperty(ref _selectedPackage, value))
                {
                    LoadPackageForEditing();
                }
            }
        }

        public Component SelectedComponent
        {
            get { return _selectedComponent; }
            set
            {
                if (SetProperty(ref _selectedComponent, value))
                {
                    LoadComponentForEditing();
                    OnPropertyChanged("ComponentPlacements");
                }
            }
        }

        public string FilterText
        {
            get { return _filterText; }
            set
            {
                if (SetProperty(ref _filterText, value))
                {
                    PackagesView?.Refresh();
                    ComponentsView?.Refresh();
                }
            }
        }

        public bool IsEditingPackage
        {
            get { return _isEditingPackage; }
            set { SetProperty(ref _isEditingPackage, value); }
        }

        public PackageGraphic SelectedGraphic
        {
            get { return _selectedGraphic; }
            set { SetProperty(ref _selectedGraphic, value); }
        }

        public bool ShowComponentsMode
        {
            get { return _showComponentsMode; }
            set
            {
                if (SetProperty(ref _showComponentsMode, value) && value)
                {
                    ShowPackagesMode = false;
                }
            }
        }

        public bool ShowPackagesMode
        {
            get { return _showPackagesMode; }
            set
            {
                if (SetProperty(ref _showPackagesMode, value) && value)
                {
                    ShowComponentsMode = false;
                }
            }
        }

        public Placement SelectedComponentPlacement
        {
            get { return _selectedComponentPlacement; }
            set { SetProperty(ref _selectedComponentPlacement, value); }
        }

        public ObservableCollection<Placement> ComponentPlacements
        {
            get
            {
                if (SelectedComponent == null || Project == null) return new ObservableCollection<Placement>();
                return new ObservableCollection<Placement>(
                    Project.Placements.Where(p => p.Component == SelectedComponent));
            }
        }

        public ObservableCollection<Package> AvailablePackages
        {
            get { return Project?.Packages; }
        }

        #region Editing Properties

        public string EditName
        {
            get { return _editName; }
            set { SetProperty(ref _editName, value); }
        }

        public string EditDescription
        {
            get { return _editDescription; }
            set { SetProperty(ref _editDescription, value); }
        }

        public double EditWidth
        {
            get { return _editWidth; }
            set { SetProperty(ref _editWidth, value); }
        }

        public double EditLength
        {
            get { return _editLength; }
            set { SetProperty(ref _editLength, value); }
        }

        public double EditHeight
        {
            get { return _editHeight; }
            set { SetProperty(ref _editHeight, value); }
        }

        public PartClass EditPartClass
        {
            get { return _editPartClass; }
            set { SetProperty(ref _editPartClass, value); }
        }

        public bool EditHasPolarity
        {
            get { return _editHasPolarity; }
            set { SetProperty(ref _editHasPolarity, value); }
        }

        public double EditDefaultRotation
        {
            get { return _editDefaultRotation; }
            set { SetProperty(ref _editDefaultRotation, value); }
        }

        // Component editing properties
        public string EditComponentPartNumber
        {
            get { return _editComponentPartNumber; }
            set { SetProperty(ref _editComponentPartNumber, value); }
        }

        public string EditComponentManufacturer
        {
            get { return _editComponentManufacturer; }
            set { SetProperty(ref _editComponentManufacturer, value); }
        }

        public string EditComponentValue
        {
            get { return _editComponentValue; }
            set { SetProperty(ref _editComponentValue, value); }
        }

        public string EditComponentDescription
        {
            get { return _editComponentDescription; }
            set { SetProperty(ref _editComponentDescription, value); }
        }

        public Package EditComponentPackage
        {
            get { return _editComponentPackage; }
            set { SetProperty(ref _editComponentPackage, value); }
        }

        public string EditFeederSlot
        {
            get { return _editFeederSlot; }
            set { SetProperty(ref _editFeederSlot, value); }
        }

        public string EditTapeWidth
        {
            get { return _editTapeWidth; }
            set { SetProperty(ref _editTapeWidth, value); }
        }

        public string EditTapePitch
        {
            get { return _editTapePitch; }
            set { SetProperty(ref _editTapePitch, value); }
        }

        #endregion

        public int PackageCount
        {
            get { return Project?.Packages.Count ?? 0; }
        }

        public int ComponentCount
        {
            get { return Project?.Components.Count ?? 0; }
        }

        // Commands
        public ICommand NewPackageCommand { get; private set; }
        public ICommand DuplicatePackageCommand { get; private set; }
        public ICommand DeletePackageCommand { get; private set; }
        public ICommand SavePackageCommand { get; private set; }
        public ICommand CancelEditCommand { get; private set; }
        public ICommand ImportFromLibraryCommand { get; private set; }
        public ICommand ExportToLibraryCommand { get; private set; }
        public ICommand AddRectangleCommand { get; private set; }
        public ICommand AddCircleCommand { get; private set; }
        public ICommand AddLineCommand { get; private set; }
        public ICommand AddPolygonCommand { get; private set; }
        public ICommand AddTextCommand { get; private set; }
        public ICommand AddPinCommand { get; private set; }
        public ICommand DeleteGraphicCommand { get; private set; }
        public ICommand CenterOriginCommand { get; private set; }
        public ICommand AutoSizeCommand { get; private set; }
        public ICommand SaveCommand { get; private set; }
        public ICommand ClearFilterCommand { get; private set; }

        // Component commands
        public ICommand NewComponentCommand { get; private set; }
        public ICommand DeleteComponentCommand { get; private set; }
        public ICommand AutoAssignFeedersCommand { get; private set; }
        public ICommand FocusComponentPlacementCommand { get; private set; }

        public ComponentEditorViewModel()
        {
            InitializeCommands();
            SubscribeToEvents();
        }

        private void InitializeCommands()
        {
            NewPackageCommand = new RelayCommand(ExecuteNewPackage);
            DuplicatePackageCommand = new RelayCommand(ExecuteDuplicatePackage, () => SelectedPackage != null);
            DeletePackageCommand = new RelayCommand(ExecuteDeletePackage, () => SelectedPackage != null);
            SavePackageCommand = new RelayCommand(ExecuteSavePackage, () => IsEditingPackage);
            CancelEditCommand = new RelayCommand(ExecuteCancelEdit);
            ImportFromLibraryCommand = new RelayCommand(ExecuteImportFromLibrary);
            ExportToLibraryCommand = new RelayCommand(ExecuteExportToLibrary, () => SelectedPackage != null);
            AddRectangleCommand = new RelayCommand(ExecuteAddRectangle, () => SelectedPackage != null);
            AddCircleCommand = new RelayCommand(ExecuteAddCircle, () => SelectedPackage != null);
            AddLineCommand = new RelayCommand(ExecuteAddLine, () => SelectedPackage != null);
            AddPolygonCommand = new RelayCommand(ExecuteAddPolygon, () => SelectedPackage != null);
            AddTextCommand = new RelayCommand(ExecuteAddText, () => SelectedPackage != null);
            AddPinCommand = new RelayCommand(ExecuteAddPin, () => SelectedPackage != null);
            DeleteGraphicCommand = new RelayCommand(ExecuteDeleteGraphic, () => SelectedGraphic != null);
            CenterOriginCommand = new RelayCommand(ExecuteCenterOrigin, () => SelectedPackage != null);
            AutoSizeCommand = new RelayCommand(ExecuteAutoSize, () => SelectedPackage != null);
            SaveCommand = new RelayCommand(ExecuteSave);
            ClearFilterCommand = new RelayCommand(() => FilterText = "");

            // Component commands
            NewComponentCommand = new RelayCommand(ExecuteNewComponent, () => Project != null);
            DeleteComponentCommand = new RelayCommand(ExecuteDeleteComponent, () => SelectedComponent != null);
            AutoAssignFeedersCommand = new RelayCommand(ExecuteAutoAssignFeeders, () => Project != null && Project.Components.Count > 0);
            FocusComponentPlacementCommand = new RelayCommand(ExecuteFocusComponentPlacement, () => SelectedComponentPlacement != null);
        }

        private void SubscribeToEvents()
        {
            Subscribe<PackageAddedEvent>(OnPackageAdded);
            Subscribe<PackageRemovedEvent>(OnPackageRemoved);
        }

        private void UpdatePackagesView()
        {
            if (Project != null)
            {
                PackagesView = CollectionViewSource.GetDefaultView(Project.Packages);
                PackagesView.Filter = FilterPackage;
                PackagesView.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));

                ComponentsView = CollectionViewSource.GetDefaultView(Project.Components);
                ComponentsView.Filter = FilterComponent;
                ComponentsView.SortDescriptions.Add(new SortDescription("PartNumber", ListSortDirection.Ascending));
            }
            else
            {
                PackagesView = null;
                ComponentsView = null;
            }
            OnPropertyChanged("PackageCount");
            OnPropertyChanged("ComponentCount");
            OnPropertyChanged("AvailablePackages");
        }

        private bool FilterComponent(object obj)
        {
            if (string.IsNullOrEmpty(FilterText)) return true;

            var component = obj as Component;
            if (component == null) return false;

            var text = FilterText.ToLower();
            return (component.PartNumber ?? "").ToLower().Contains(text) ||
                   (component.Manufacturer ?? "").ToLower().Contains(text) ||
                   (component.Value ?? "").ToLower().Contains(text) ||
                   (component.Description ?? "").ToLower().Contains(text);
        }

        private bool FilterPackage(object obj)
        {
            if (string.IsNullOrEmpty(FilterText)) return true;

            var package = obj as Package;
            if (package == null) return false;

            var text = FilterText.ToLower();
            return (package.Name ?? "").ToLower().Contains(text) ||
                   (package.Description ?? "").ToLower().Contains(text);
        }

        private void LoadPackageForEditing()
        {
            if (SelectedPackage != null)
            {
                EditName = SelectedPackage.Name;
                EditDescription = SelectedPackage.Description;
                EditWidth = SelectedPackage.Width;
                EditLength = SelectedPackage.Length;
                EditHeight = SelectedPackage.Height;
                EditPartClass = SelectedPackage.PartClass;
                EditHasPolarity = SelectedPackage.HasPolarity;
                EditDefaultRotation = SelectedPackage.DefaultRotation;
                IsEditingPackage = true;
            }
            else
            {
                IsEditingPackage = false;
            }
        }

        private void LoadComponentForEditing()
        {
            if (SelectedComponent != null)
            {
                EditComponentPartNumber = SelectedComponent.PartNumber;
                EditComponentManufacturer = SelectedComponent.Manufacturer;
                EditComponentValue = SelectedComponent.Value;
                EditComponentDescription = SelectedComponent.Description;
                EditComponentPackage = SelectedComponent.Package;
                EditFeederSlot = SelectedComponent.FeederSlot;
                EditTapeWidth = SelectedComponent.TapeWidth;
                EditTapePitch = SelectedComponent.TapePitch;
            }
            else
            {
                EditComponentPartNumber = null;
                EditComponentManufacturer = null;
                EditComponentValue = null;
                EditComponentDescription = null;
                EditComponentPackage = null;
                EditFeederSlot = null;
                EditTapeWidth = null;
                EditTapePitch = null;
            }
        }

        #region Command Implementations

        private void ExecuteNewPackage()
        {
            if (Project == null) return;

            var package = new Package(Package.GeneratePackageName())
            {
                Width = 1.0,
                Length = 1.0,
                Height = 0.5
            };

            Project.Packages.Add(package);
            SelectedPackage = package;
            Publish(new PackageAddedEvent { Package = package });
            OnPropertyChanged("PackageCount");
        }

        private void ExecuteDuplicatePackage()
        {
            if (Project == null || SelectedPackage == null) return;

            var newPackage = new Package(SelectedPackage.Name + "_copy")
            {
                Description = SelectedPackage.Description,
                Width = SelectedPackage.Width,
                Length = SelectedPackage.Length,
                Height = SelectedPackage.Height,
                PartClass = SelectedPackage.PartClass,
                HasPolarity = SelectedPackage.HasPolarity,
                DefaultRotation = SelectedPackage.DefaultRotation,
                Origin = SelectedPackage.Origin
            };

            foreach (var graphic in SelectedPackage.Graphics)
            {
                newPackage.Graphics.Add(graphic.Clone());
            }

            foreach (var pin in SelectedPackage.Pins)
            {
                newPackage.Pins.Add(new Pin
                {
                    Number = pin.Number,
                    Name = pin.Name,
                    X = pin.X,
                    Y = pin.Y,
                    Width = pin.Width,
                    Height = pin.Height,
                    Shape = pin.Shape
                });
            }

            Project.Packages.Add(newPackage);
            SelectedPackage = newPackage;
            Publish(new PackageAddedEvent { Package = newPackage });
            OnPropertyChanged("PackageCount");
        }

        private void ExecuteDeletePackage()
        {
            if (Project == null || SelectedPackage == null) return;

            // Check if package is in use
            var inUse = Project.Placements.Any(p => p.Package == SelectedPackage);
            if (inUse)
            {
                Publish(new StatusMessageEvent
                {
                    Message = "Cannot delete package: it is in use by placements",
                    Type = StatusMessageType.Warning
                });
                return;
            }

            var package = SelectedPackage;
            Project.Packages.Remove(package);
            SelectedPackage = null;
            Publish(new PackageRemovedEvent { Package = package });
            OnPropertyChanged("PackageCount");
        }

        private void ExecuteSavePackage()
        {
            if (SelectedPackage == null) return;

            SelectedPackage.Name = EditName;
            SelectedPackage.Description = EditDescription;
            SelectedPackage.Width = EditWidth;
            SelectedPackage.Length = EditLength;
            SelectedPackage.Height = EditHeight;
            SelectedPackage.PartClass = EditPartClass;
            SelectedPackage.HasPolarity = EditHasPolarity;
            SelectedPackage.DefaultRotation = EditDefaultRotation;

            Publish(new PackageModifiedEvent { Package = SelectedPackage });
            Publish(new StatusMessageEvent { Message = "Package saved" });
        }

        private void ExecuteCancelEdit()
        {
            LoadPackageForEditing(); // Reload original values
        }

        private void ExecuteImportFromLibrary()
        {
            // TODO: Open library browser dialog
            Publish(new ShowDialogEvent { DialogType = "ImportPackageLibrary" });
        }

        private void ExecuteExportToLibrary()
        {
            // TODO: Export to master library
            Publish(new ShowDialogEvent
            {
                DialogType = "ExportPackageLibrary",
                Parameter = SelectedPackage
            });
        }

        private void ExecuteAddRectangle()
        {
            if (SelectedPackage == null) return;

            SelectedPackage.Graphics.Add(new PackageGraphic
            {
                ShapeType = GraphicShapeType.Rectangle,
                X = -0.5,
                Y = -0.5,
                Width = 1.0,
                Height = 1.0,
                IsFilled = true
            });

            Publish(new PackageModifiedEvent { Package = SelectedPackage });
        }

        private void ExecuteAddCircle()
        {
            if (SelectedPackage == null) return;

            SelectedPackage.Graphics.Add(new PackageGraphic
            {
                ShapeType = GraphicShapeType.Circle,
                X = 0,
                Y = 0,
                Width = 0.5,
                Height = 0.5,
                IsFilled = true
            });

            Publish(new PackageModifiedEvent { Package = SelectedPackage });
        }

        private void ExecuteAddLine()
        {
            if (SelectedPackage == null) return;

            var graphic = new PackageGraphic
            {
                ShapeType = GraphicShapeType.Line,
                StrokeThickness = 0.1
            };
            graphic.Points.Add(new System.Windows.Point(-0.5, 0));
            graphic.Points.Add(new System.Windows.Point(0.5, 0));

            SelectedPackage.Graphics.Add(graphic);
            Publish(new PackageModifiedEvent { Package = SelectedPackage });
        }

        private void ExecuteAddPolygon()
        {
            if (SelectedPackage == null) return;

            var graphic = new PackageGraphic
            {
                ShapeType = GraphicShapeType.Polygon,
                IsFilled = true
            };
            // Triangle by default
            graphic.Points.Add(new System.Windows.Point(0, -0.5));
            graphic.Points.Add(new System.Windows.Point(0.5, 0.5));
            graphic.Points.Add(new System.Windows.Point(-0.5, 0.5));

            SelectedPackage.Graphics.Add(graphic);
            Publish(new PackageModifiedEvent { Package = SelectedPackage });
        }

        private void ExecuteAddText()
        {
            if (SelectedPackage == null) return;

            SelectedPackage.Graphics.Add(new PackageGraphic
            {
                ShapeType = GraphicShapeType.Text,
                X = 0,
                Y = 0,
                Text = "Text",
                TextSize = 0.5
            });

            Publish(new PackageModifiedEvent { Package = SelectedPackage });
        }

        private void ExecuteAddPin()
        {
            if (SelectedPackage == null) return;

            int nextPinNumber = SelectedPackage.Pins.Count > 0
                ? SelectedPackage.Pins.Max(p => p.Number) + 1
                : 1;

            SelectedPackage.Pins.Add(new Pin
            {
                Number = nextPinNumber,
                X = 0,
                Y = 0,
                Width = 0.3,
                Height = 0.3
            });

            Publish(new PackageModifiedEvent { Package = SelectedPackage });
        }

        private void ExecuteDeleteGraphic()
        {
            if (SelectedPackage == null || SelectedGraphic == null) return;

            SelectedPackage.Graphics.Remove(SelectedGraphic);
            SelectedGraphic = null;
            Publish(new PackageModifiedEvent { Package = SelectedPackage });
        }

        private void ExecuteCenterOrigin()
        {
            if (SelectedPackage == null) return;

            var bounds = SelectedPackage.Bounds;
            var centerX = bounds.X + bounds.Width / 2;
            var centerY = bounds.Y + bounds.Height / 2;

            // Move all graphics to center origin
            foreach (var graphic in SelectedPackage.Graphics)
            {
                graphic.X -= centerX;
                graphic.Y -= centerY;

                if (graphic.Points != null)
                {
                    for (int i = 0; i < graphic.Points.Count; i++)
                    {
                        graphic.Points[i] = new System.Windows.Point(
                            graphic.Points[i].X - centerX,
                            graphic.Points[i].Y - centerY);
                    }
                }
            }

            foreach (var pin in SelectedPackage.Pins)
            {
                pin.X -= centerX;
                pin.Y -= centerY;
            }

            SelectedPackage.Origin = new System.Windows.Point(0, 0);
            Publish(new PackageModifiedEvent { Package = SelectedPackage });
        }

        private void ExecuteAutoSize()
        {
            if (SelectedPackage == null) return;

            var bounds = SelectedPackage.Bounds;
            EditWidth = bounds.Width;
            EditLength = bounds.Height;
        }

        private void ExecuteSave()
        {
            if (ShowComponentsMode && SelectedComponent != null)
            {
                // Save component
                SelectedComponent.PartNumber = EditComponentPartNumber;
                SelectedComponent.Manufacturer = EditComponentManufacturer;
                SelectedComponent.Value = EditComponentValue;
                SelectedComponent.Description = EditComponentDescription;
                SelectedComponent.Package = EditComponentPackage;
                SelectedComponent.FeederSlot = EditFeederSlot;
                SelectedComponent.TapeWidth = EditTapeWidth;
                SelectedComponent.TapePitch = EditTapePitch;

                Publish(new StatusMessageEvent { Message = "Component saved" });
            }
            else if (ShowPackagesMode && SelectedPackage != null)
            {
                ExecuteSavePackage();
            }
        }

        private void ExecuteNewComponent()
        {
            if (Project == null) return;

            var component = new Component
            {
                PartNumber = string.Format("PART{0:D4}", Project.Components.Count + 1)
            };

            Project.Components.Add(component);
            ComponentsView?.Refresh();
            SelectedComponent = component;
            OnPropertyChanged("ComponentCount");
            Publish(new ComponentAddedEvent { Component = component });
        }

        private void ExecuteDeleteComponent()
        {
            if (Project == null || SelectedComponent == null) return;

            // Check if component is in use
            var inUse = Project.Placements.Any(p => p.Component == SelectedComponent);
            if (inUse)
            {
                Publish(new StatusMessageEvent
                {
                    Message = "Cannot delete component: it is in use by placements",
                    Type = StatusMessageType.Warning
                });
                return;
            }

            var component = SelectedComponent;
            Project.Components.Remove(component);
            SelectedComponent = null;
            ComponentsView?.Refresh();
            OnPropertyChanged("ComponentCount");
            Publish(new ComponentRemovedEvent { Component = component });
        }

        private void ExecuteAutoAssignFeeders()
        {
            if (Project == null) return;

            int slot = 1;
            var sortedComponents = Project.Components
                .OrderBy(c => c.Package?.Width ?? 0)
                .ThenBy(c => c.PartNumber);

            foreach (var component in sortedComponents)
            {
                component.FeederSlot = slot.ToString();
                slot++;
            }

            ComponentsView?.Refresh();
            Publish(new StatusMessageEvent
            {
                Message = string.Format("Assigned {0} feeder slots", Project.Components.Count)
            });
        }

        private void ExecuteFocusComponentPlacement()
        {
            if (SelectedComponentPlacement == null) return;

            Publish(new FocusPlacementEvent
            {
                Placement = SelectedComponentPlacement,
                CenterView = true
            });
        }

        #endregion

        #region Event Handlers

        private void OnPackageAdded(PackageAddedEvent e)
        {
            PackagesView?.Refresh();
            OnPropertyChanged("PackageCount");
        }

        private void OnPackageRemoved(PackageRemovedEvent e)
        {
            PackagesView?.Refresh();
            OnPropertyChanged("PackageCount");
        }

        #endregion
    }
}
