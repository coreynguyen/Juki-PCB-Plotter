using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using PCBPlotter.Core.Events;
using PCBPlotter.Core.Models;

namespace PCBPlotter.ViewModels
{
    /// <summary>
    /// View model for the package/footprint editor
    /// </summary>
    public class ComponentEditorViewModel : ViewModelBase
    {
        private Project _project;
        private ICollectionView _packagesView;
        private Package _selectedPackage;
        private string _filterText;
        private bool _isEditingPackage;
        private PackageGraphic _selectedGraphic;

        // Package editing properties
        private string _editName;
        private string _editDescription;
        private double _editWidth;
        private double _editLength;
        private double _editHeight;
        private PartClass _editPartClass;
        private bool _editHasPolarity;
        private double _editDefaultRotation;

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

        public Package SelectedPackage
        {
            get { return _selectedPackage; }
            set
            {
                if (SetProperty(ref _selectedPackage, value))
                {
                    LoadPackageForEditing();
                    OnPropertyChanged("PinCount");
                    // Force command CanExecute re-evaluation
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
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
            set
            {
                if (SetProperty(ref _selectedGraphic, value))
                {
                    // Notify all shape property bindings
                    OnPropertyChanged("SelectedShapeX");
                    OnPropertyChanged("SelectedShapeY");
                    OnPropertyChanged("SelectedShapeWidth");
                    OnPropertyChanged("SelectedShapeHeight");
                    OnPropertyChanged("SelectedShapeRotation");
                    OnPropertyChanged("SelectedShapeStroke");
                    OnPropertyChanged("SelectedShapeFilled");
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        // Selected shape property accessors
        public double SelectedShapeX
        {
            get { return _selectedGraphic?.X ?? 0; }
            set { if (_selectedGraphic != null) { _selectedGraphic.X = value; OnPropertyChanged(); RefreshCanvas(); } }
        }

        public double SelectedShapeY
        {
            get { return _selectedGraphic?.Y ?? 0; }
            set { if (_selectedGraphic != null) { _selectedGraphic.Y = value; OnPropertyChanged(); RefreshCanvas(); } }
        }

        public double SelectedShapeWidth
        {
            get { return _selectedGraphic?.Width ?? 0; }
            set { if (_selectedGraphic != null) { _selectedGraphic.Width = value; OnPropertyChanged(); RefreshCanvas(); } }
        }

        public double SelectedShapeHeight
        {
            get { return _selectedGraphic?.Height ?? 0; }
            set { if (_selectedGraphic != null) { _selectedGraphic.Height = value; OnPropertyChanged(); RefreshCanvas(); } }
        }

        public double SelectedShapeRotation
        {
            get { return _selectedGraphic?.Rotation ?? 0; }
            set { if (_selectedGraphic != null) { _selectedGraphic.Rotation = value; OnPropertyChanged(); RefreshCanvas(); } }
        }

        public double SelectedShapeStroke
        {
            get { return _selectedGraphic?.StrokeThickness ?? 0.1; }
            set { if (_selectedGraphic != null) { _selectedGraphic.StrokeThickness = value; OnPropertyChanged(); RefreshCanvas(); } }
        }

        public bool SelectedShapeFilled
        {
            get { return _selectedGraphic?.IsFilled ?? false; }
            set { if (_selectedGraphic != null) { _selectedGraphic.IsFilled = value; OnPropertyChanged(); RefreshCanvas(); } }
        }

        private void RefreshCanvas()
        {
            Publish(new PackageModifiedEvent { Package = SelectedPackage });
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

        #endregion

        public int PackageCount
        {
            get { return Project?.Packages.Count ?? 0; }
        }

        public int PinCount
        {
            get { return SelectedPackage?.Pins.Count ?? 0; }
        }

        // Commands
        public ICommand NewPackageCommand { get; private set; }
        public ICommand DuplicatePackageCommand { get; private set; }
        public ICommand DeletePackageCommand { get; private set; }
        public ICommand SavePackageCommand { get; private set; }
        public ICommand CancelEditCommand { get; private set; }
        public ICommand ImportFromLibraryCommand { get; private set; }
        public ICommand ExportToLibraryCommand { get; private set; }
        public ICommand ImportIPCCommand { get; private set; }
        public ICommand ClearFilterCommand { get; private set; }

        // Shape tools
        public ICommand AddRectangleCommand { get; private set; }
        public ICommand AddCircleCommand { get; private set; }
        public ICommand AddLineCommand { get; private set; }
        public ICommand AddPolygonCommand { get; private set; }
        public ICommand AddArcCommand { get; private set; }
        public ICommand AddTextCommand { get; private set; }
        public ICommand AddPinCommand { get; private set; }
        public ICommand DeleteGraphicCommand { get; private set; }

        // Utilities
        public ICommand CenterOriginCommand { get; private set; }
        public ICommand AutoSizeCommand { get; private set; }
        public ICommand MirrorXCommand { get; private set; }
        public ICommand MirrorYCommand { get; private set; }

        // Shape manipulation
        public ICommand RotateShape90Command { get; private set; }
        public ICommand FlipShapeHCommand { get; private set; }
        public ICommand FlipShapeVCommand { get; private set; }

        // Quick create commands
        public ICommand CreateChipCommand { get; private set; }
        public ICommand CreateSOT23Command { get; private set; }
        public ICommand CreateSOICCommand { get; private set; }
        public ICommand CreateQFPCommand { get; private set; }
        public ICommand CreateBGACommand { get; private set; }

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
            ImportIPCCommand = new RelayCommand(ExecuteImportIPC);
            ClearFilterCommand = new RelayCommand(() => FilterText = "");

            // Shape tools
            AddRectangleCommand = new RelayCommand(ExecuteAddRectangle, () => SelectedPackage != null);
            AddCircleCommand = new RelayCommand(ExecuteAddCircle, () => SelectedPackage != null);
            AddLineCommand = new RelayCommand(ExecuteAddLine, () => SelectedPackage != null);
            AddPolygonCommand = new RelayCommand(ExecuteAddPolygon, () => SelectedPackage != null);
            AddArcCommand = new RelayCommand(ExecuteAddArc, () => SelectedPackage != null);
            AddTextCommand = new RelayCommand(ExecuteAddText, () => SelectedPackage != null);
            AddPinCommand = new RelayCommand<string>(ExecuteAddPin, s => SelectedPackage != null);
            DeleteGraphicCommand = new RelayCommand(ExecuteDeleteGraphic, () => SelectedGraphic != null);

            // Utilities
            CenterOriginCommand = new RelayCommand(ExecuteCenterOrigin, () => SelectedPackage != null);
            AutoSizeCommand = new RelayCommand(ExecuteAutoSize, () => SelectedPackage != null);
            MirrorXCommand = new RelayCommand(ExecuteMirrorX, () => SelectedPackage != null);
            MirrorYCommand = new RelayCommand(ExecuteMirrorY, () => SelectedPackage != null);

            // Quick create
            CreateChipCommand = new RelayCommand(ExecuteCreateChip);
            CreateSOT23Command = new RelayCommand(ExecuteCreateSOT23);
            CreateSOICCommand = new RelayCommand(ExecuteCreateSOIC);
            CreateQFPCommand = new RelayCommand(ExecuteCreateQFP);
            CreateBGACommand = new RelayCommand(ExecuteCreateBGA);

            // Shape manipulation
            RotateShape90Command = new RelayCommand(ExecuteRotateShape90, () => SelectedGraphic != null);
            FlipShapeHCommand = new RelayCommand(ExecuteFlipShapeH, () => SelectedGraphic != null);
            FlipShapeVCommand = new RelayCommand(ExecuteFlipShapeV, () => SelectedGraphic != null);
        }

        private void ExecuteRotateShape90()
        {
            if (SelectedGraphic == null) return;
            SelectedGraphic.Rotation = (SelectedGraphic.Rotation + 90) % 360;
            // For rectangle/ellipse, swap width/height
            if (SelectedGraphic.ShapeType == GraphicShapeType.Rectangle ||
                SelectedGraphic.ShapeType == GraphicShapeType.RoundedRectangle ||
                SelectedGraphic.ShapeType == GraphicShapeType.Ellipse)
            {
                double temp = SelectedGraphic.Width;
                SelectedGraphic.Width = SelectedGraphic.Height;
                SelectedGraphic.Height = temp;
            }
            OnPropertyChanged("SelectedShapeRotation");
            OnPropertyChanged("SelectedShapeWidth");
            OnPropertyChanged("SelectedShapeHeight");
            RefreshCanvas();
        }

        private void ExecuteFlipShapeH()
        {
            if (SelectedGraphic == null) return;
            SelectedGraphic.X = -SelectedGraphic.X;
            if (SelectedGraphic.Points != null)
            {
                for (int i = 0; i < SelectedGraphic.Points.Count; i++)
                {
                    SelectedGraphic.Points[i] = new System.Windows.Point(-SelectedGraphic.Points[i].X, SelectedGraphic.Points[i].Y);
                }
            }
            OnPropertyChanged("SelectedShapeX");
            RefreshCanvas();
        }

        private void ExecuteFlipShapeV()
        {
            if (SelectedGraphic == null) return;
            SelectedGraphic.Y = -SelectedGraphic.Y;
            if (SelectedGraphic.Points != null)
            {
                for (int i = 0; i < SelectedGraphic.Points.Count; i++)
                {
                    SelectedGraphic.Points[i] = new System.Windows.Point(SelectedGraphic.Points[i].X, -SelectedGraphic.Points[i].Y);
                }
            }
            OnPropertyChanged("SelectedShapeY");
            RefreshCanvas();
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
            }
            else
            {
                PackagesView = null;
            }
            OnPropertyChanged("PackageCount");
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
            Publish(new ShowDialogEvent { DialogType = "ImportPackageLibrary" });
        }

        private void ExecuteExportToLibrary()
        {
            Publish(new ShowDialogEvent
            {
                DialogType = "ExportPackageLibrary",
                Parameter = SelectedPackage
            });
        }

        private void ExecuteImportIPC()
        {
            Publish(new ShowDialogEvent { DialogType = "ImportIPC356" });
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
                IsFilled = false,
                StrokeThickness = 0.1
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
                IsFilled = false,
                StrokeThickness = 0.1
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
                IsFilled = false,
                StrokeThickness = 0.1
            };
            // Triangle by default
            graphic.Points.Add(new System.Windows.Point(0, -0.5));
            graphic.Points.Add(new System.Windows.Point(0.5, 0.5));
            graphic.Points.Add(new System.Windows.Point(-0.5, 0.5));

            SelectedPackage.Graphics.Add(graphic);
            Publish(new PackageModifiedEvent { Package = SelectedPackage });
        }

        private void ExecuteAddArc()
        {
            if (SelectedPackage == null) return;

            SelectedPackage.Graphics.Add(new PackageGraphic
            {
                ShapeType = GraphicShapeType.Arc,
                X = 0,
                Y = 0,
                Width = 0.5,
                Height = 0.5,
                StartAngle = 0,
                SweepAngle = 90,
                StrokeThickness = 0.1
            });

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

        private void ExecuteAddPin(string shapeType)
        {
            if (SelectedPackage == null) return;

            int nextPinNumber = SelectedPackage.Pins.Count > 0
                ? SelectedPackage.Pins.Max(p => p.Number) + 1
                : 1;

            PinShape shape = PinShape.Rectangle;
            if (shapeType == "Round") shape = PinShape.Circle;
            else if (shapeType == "Oblong") shape = PinShape.Oval;

            SelectedPackage.Pins.Add(new Pin
            {
                Number = nextPinNumber,
                X = 0,
                Y = 0,
                Width = 0.3,
                Height = 0.3,
                Shape = shape
            });

            OnPropertyChanged("PinCount");
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

        private void ExecuteMirrorX()
        {
            if (SelectedPackage == null) return;

            foreach (var graphic in SelectedPackage.Graphics)
            {
                graphic.X = -graphic.X;
                if (graphic.Points != null)
                {
                    for (int i = 0; i < graphic.Points.Count; i++)
                    {
                        graphic.Points[i] = new System.Windows.Point(
                            -graphic.Points[i].X, graphic.Points[i].Y);
                    }
                }
            }

            foreach (var pin in SelectedPackage.Pins)
            {
                pin.X = -pin.X;
            }

            Publish(new PackageModifiedEvent { Package = SelectedPackage });
        }

        private void ExecuteMirrorY()
        {
            if (SelectedPackage == null) return;

            foreach (var graphic in SelectedPackage.Graphics)
            {
                graphic.Y = -graphic.Y;
                if (graphic.Points != null)
                {
                    for (int i = 0; i < graphic.Points.Count; i++)
                    {
                        graphic.Points[i] = new System.Windows.Point(
                            graphic.Points[i].X, -graphic.Points[i].Y);
                    }
                }
            }

            foreach (var pin in SelectedPackage.Pins)
            {
                pin.Y = -pin.Y;
            }

            Publish(new PackageModifiedEvent { Package = SelectedPackage });
        }

        #region Quick Create Commands

        private void ExecuteCreateChip()
        {
            if (Project == null) return;

            // Create a standard 2-pin chip (0603 size by default: 1.6mm x 0.8mm)
            var package = new Package("CHIP_0603")
            {
                Width = 1.6,
                Length = 0.8,
                Height = 0.45,
                PartClass = PartClass.Chip,
                HasPolarity = false
            };

            // Add body outline
            package.Graphics.Add(new PackageGraphic
            {
                ShapeType = GraphicShapeType.Rectangle,
                X = -0.8,
                Y = -0.4,
                Width = 1.6,
                Height = 0.8,
                IsFilled = false,
                StrokeThickness = 0.05
            });

            // Add pins
            package.Pins.Add(new Pin { Number = 1, X = -0.7, Y = 0, Width = 0.4, Height = 0.6, Shape = PinShape.Rectangle });
            package.Pins.Add(new Pin { Number = 2, X = 0.7, Y = 0, Width = 0.4, Height = 0.6, Shape = PinShape.Rectangle });

            Project.Packages.Add(package);
            SelectedPackage = package;
            Publish(new PackageAddedEvent { Package = package });
            OnPropertyChanged("PackageCount");
        }

        private void ExecuteCreateSOT23()
        {
            if (Project == null) return;

            // Create SOT23-3 package
            var package = new Package("SOT23-3")
            {
                Width = 2.9,
                Length = 1.3,
                Height = 1.0,
                PartClass = PartClass.SOT,
                HasPolarity = true
            };

            // Add body outline
            package.Graphics.Add(new PackageGraphic
            {
                ShapeType = GraphicShapeType.Rectangle,
                X = -1.45,
                Y = -0.65,
                Width = 2.9,
                Height = 1.3,
                IsFilled = false,
                StrokeThickness = 0.05
            });

            // Add polarity marker (pin 1 indicator)
            package.Graphics.Add(new PackageGraphic
            {
                ShapeType = GraphicShapeType.Circle,
                X = -1.1,
                Y = -0.3,
                Width = 0.2,
                Height = 0.2,
                IsFilled = true
            });

            // Add pins (SOT23-3 layout)
            package.Pins.Add(new Pin { Number = 1, X = -0.95, Y = -1.1, Width = 0.6, Height = 0.7, Shape = PinShape.Rectangle });
            package.Pins.Add(new Pin { Number = 2, X = 0.95, Y = -1.1, Width = 0.6, Height = 0.7, Shape = PinShape.Rectangle });
            package.Pins.Add(new Pin { Number = 3, X = 0, Y = 1.1, Width = 0.6, Height = 0.7, Shape = PinShape.Rectangle });

            Project.Packages.Add(package);
            SelectedPackage = package;
            Publish(new PackageAddedEvent { Package = package });
            OnPropertyChanged("PackageCount");
        }

        private void ExecuteCreateSOIC()
        {
            if (Project == null) return;

            // Create SOIC-8 package
            var package = new Package("SOIC-8")
            {
                Width = 5.0,
                Length = 4.0,
                Height = 1.75,
                PartClass = PartClass.SOP,
                HasPolarity = true
            };

            double pitch = 1.27;
            double pinWidth = 0.5;
            double pinHeight = 1.0;
            double bodyWidth = 3.9;
            double pinSpan = 5.8;

            // Add body outline
            package.Graphics.Add(new PackageGraphic
            {
                ShapeType = GraphicShapeType.Rectangle,
                X = -bodyWidth / 2,
                Y = -2.0,
                Width = bodyWidth,
                Height = 4.0,
                IsFilled = false,
                StrokeThickness = 0.1
            });

            // Add pin 1 marker
            package.Graphics.Add(new PackageGraphic
            {
                ShapeType = GraphicShapeType.Circle,
                X = -1.5,
                Y = -1.5,
                Width = 0.3,
                Height = 0.3,
                IsFilled = true
            });

            // Add pins (4 on each side)
            for (int i = 0; i < 4; i++)
            {
                double y = -1.905 + i * pitch;
                // Left side (pins 1-4)
                package.Pins.Add(new Pin
                {
                    Number = i + 1,
                    X = -pinSpan / 2,
                    Y = y,
                    Width = pinHeight,
                    Height = pinWidth,
                    Shape = PinShape.Rectangle
                });
                // Right side (pins 8-5)
                package.Pins.Add(new Pin
                {
                    Number = 8 - i,
                    X = pinSpan / 2,
                    Y = y,
                    Width = pinHeight,
                    Height = pinWidth,
                    Shape = PinShape.Rectangle
                });
            }

            Project.Packages.Add(package);
            SelectedPackage = package;
            Publish(new PackageAddedEvent { Package = package });
            OnPropertyChanged("PackageCount");
        }

        private void ExecuteCreateQFP()
        {
            if (Project == null) return;

            // Create TQFP-32 package (7x7mm body, 0.8mm pitch)
            var package = new Package("TQFP-32")
            {
                Width = 9.0,
                Length = 9.0,
                Height = 1.0,
                PartClass = PartClass.QFP,
                HasPolarity = true
            };

            double bodySize = 7.0;
            double pitch = 0.8;
            double pinWidth = 0.37;
            double pinHeight = 1.0;
            int pinsPerSide = 8;

            // Add body outline
            package.Graphics.Add(new PackageGraphic
            {
                ShapeType = GraphicShapeType.Rectangle,
                X = -bodySize / 2,
                Y = -bodySize / 2,
                Width = bodySize,
                Height = bodySize,
                IsFilled = false,
                StrokeThickness = 0.1
            });

            // Add pin 1 marker
            package.Graphics.Add(new PackageGraphic
            {
                ShapeType = GraphicShapeType.Circle,
                X = -bodySize / 2 + 0.5,
                Y = -bodySize / 2 + 0.5,
                Width = 0.4,
                Height = 0.4,
                IsFilled = true
            });

            double startOffset = -(pinsPerSide - 1) * pitch / 2;
            int pinNum = 1;

            // Bottom side (pins 1-8)
            for (int i = 0; i < pinsPerSide; i++)
            {
                package.Pins.Add(new Pin
                {
                    Number = pinNum++,
                    X = startOffset + i * pitch,
                    Y = -4.5,
                    Width = pinWidth,
                    Height = pinHeight,
                    Shape = PinShape.Rectangle
                });
            }

            // Right side (pins 9-16)
            for (int i = 0; i < pinsPerSide; i++)
            {
                package.Pins.Add(new Pin
                {
                    Number = pinNum++,
                    X = 4.5,
                    Y = startOffset + i * pitch,
                    Width = pinHeight,
                    Height = pinWidth,
                    Shape = PinShape.Rectangle
                });
            }

            // Top side (pins 17-24)
            for (int i = 0; i < pinsPerSide; i++)
            {
                package.Pins.Add(new Pin
                {
                    Number = pinNum++,
                    X = -startOffset - i * pitch,
                    Y = 4.5,
                    Width = pinWidth,
                    Height = pinHeight,
                    Shape = PinShape.Rectangle
                });
            }

            // Left side (pins 25-32)
            for (int i = 0; i < pinsPerSide; i++)
            {
                package.Pins.Add(new Pin
                {
                    Number = pinNum++,
                    X = -4.5,
                    Y = -startOffset - i * pitch,
                    Width = pinHeight,
                    Height = pinWidth,
                    Shape = PinShape.Rectangle
                });
            }

            Project.Packages.Add(package);
            SelectedPackage = package;
            Publish(new PackageAddedEvent { Package = package });
            OnPropertyChanged("PackageCount");
        }

        private void ExecuteCreateBGA()
        {
            if (Project == null) return;

            // Create BGA-49 (7x7 grid, 0.8mm pitch)
            var package = new Package("BGA-49")
            {
                Width = 6.0,
                Length = 6.0,
                Height = 1.2,
                PartClass = PartClass.BGA,
                HasPolarity = true
            };

            double pitch = 0.8;
            double ballDia = 0.4;
            int gridSize = 7;

            // Add body outline
            package.Graphics.Add(new PackageGraphic
            {
                ShapeType = GraphicShapeType.Rectangle,
                X = -3.0,
                Y = -3.0,
                Width = 6.0,
                Height = 6.0,
                IsFilled = false,
                StrokeThickness = 0.1
            });

            // Add pin 1 marker (A1 corner)
            package.Graphics.Add(new PackageGraphic
            {
                ShapeType = GraphicShapeType.Circle,
                X = -2.5,
                Y = -2.5,
                Width = 0.3,
                Height = 0.3,
                IsFilled = true
            });

            double startOffset = -(gridSize - 1) * pitch / 2;
            string[] rows = { "A", "B", "C", "D", "E", "F", "G" };
            int pinNum = 1;

            for (int row = 0; row < gridSize; row++)
            {
                for (int col = 0; col < gridSize; col++)
                {
                    package.Pins.Add(new Pin
                    {
                        Number = pinNum++,
                        Name = rows[row] + (col + 1).ToString(),
                        X = startOffset + col * pitch,
                        Y = startOffset + row * pitch,
                        Width = ballDia,
                        Height = ballDia,
                        Shape = PinShape.Circle
                    });
                }
            }

            Project.Packages.Add(package);
            SelectedPackage = package;
            Publish(new PackageAddedEvent { Package = package });
            OnPropertyChanged("PackageCount");
        }

        #endregion

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
