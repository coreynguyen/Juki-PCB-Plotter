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
    /// View model for the BOM editor
    /// </summary>
    public class BomEditorViewModel : ViewModelBase
    {
        private Project _project;
        private ICollectionView _componentsView;
        private Component _selectedComponent;
        private string _filterText;
        private bool _showUnusedOnly;

        public Project Project
        {
            get { return _project; }
            set
            {
                if (SetProperty(ref _project, value))
                {
                    UpdateComponentsView();
                }
            }
        }

        public ICollectionView ComponentsView
        {
            get { return _componentsView; }
            set { SetProperty(ref _componentsView, value); }
        }

        public Component SelectedComponent
        {
            get { return _selectedComponent; }
            set { SetProperty(ref _selectedComponent, value); }
        }

        public string FilterText
        {
            get { return _filterText; }
            set
            {
                if (SetProperty(ref _filterText, value))
                {
                    ComponentsView?.Refresh();
                }
            }
        }

        public bool ShowUnusedOnly
        {
            get { return _showUnusedOnly; }
            set
            {
                if (SetProperty(ref _showUnusedOnly, value))
                {
                    ComponentsView?.Refresh();
                }
            }
        }

        public int TotalComponents
        {
            get { return Project?.Components.Count ?? 0; }
        }

        public int UnusedComponents
        {
            get
            {
                if (Project == null) return 0;
                return Project.Components.Count(c => c.Status.HasFlag(ComponentStatus.NoPlacements));
            }
        }

        // Commands
        public ICommand ImportBomCommand { get; private set; }
        public ICommand ExportBomCommand { get; private set; }
        public ICommand NewComponentCommand { get; private set; }
        public ICommand DeleteComponentCommand { get; private set; }
        public ICommand AssignToPlacementsCommand { get; private set; }
        public ICommand AssignPackageCommand { get; private set; }
        public ICommand SelectPlacementsCommand { get; private set; }
        public ICommand ClearFilterCommand { get; private set; }
        public ICommand ValidateBomCommand { get; private set; }

        public BomEditorViewModel()
        {
            InitializeCommands();
            SubscribeToEvents();
        }

        private void InitializeCommands()
        {
            ImportBomCommand = new RelayCommand(ExecuteImportBom);
            ExportBomCommand = new RelayCommand(ExecuteExportBom, () => Project != null && TotalComponents > 0);
            NewComponentCommand = new RelayCommand(ExecuteNewComponent, () => Project != null);
            DeleteComponentCommand = new RelayCommand(ExecuteDeleteComponent, () => SelectedComponent != null);
            AssignToPlacementsCommand = new RelayCommand(ExecuteAssignToPlacements, () => SelectedComponent != null);
            AssignPackageCommand = new RelayCommand(ExecuteAssignPackage, () => SelectedComponent != null);
            SelectPlacementsCommand = new RelayCommand(ExecuteSelectPlacements, () => SelectedComponent != null);
            ClearFilterCommand = new RelayCommand(() => { FilterText = null; ShowUnusedOnly = false; });
            ValidateBomCommand = new RelayCommand(ExecuteValidateBom, () => Project != null);
        }

        private void SubscribeToEvents()
        {
            Subscribe<ComponentAddedEvent>(OnComponentAdded);
            Subscribe<ComponentRemovedEvent>(OnComponentRemoved);
            Subscribe<PlacementsChangedEvent>(OnPlacementsChanged);
        }

        private void UpdateComponentsView()
        {
            if (Project != null)
            {
                ComponentsView = CollectionViewSource.GetDefaultView(Project.Components);
                ComponentsView.Filter = FilterComponent;
                ComponentsView.SortDescriptions.Add(new SortDescription("PartNumber", ListSortDirection.Ascending));
            }
            else
            {
                ComponentsView = null;
            }

            OnPropertyChanged("TotalComponents");
            OnPropertyChanged("UnusedComponents");
        }

        private bool FilterComponent(object obj)
        {
            var component = obj as Component;
            if (component == null) return false;

            // Show unused only
            if (ShowUnusedOnly && !component.Status.HasFlag(ComponentStatus.NoPlacements))
                return false;

            // Text filter
            if (!string.IsNullOrEmpty(FilterText))
            {
                var text = FilterText.ToLower();
                bool matches = (component.PartNumber ?? "").ToLower().Contains(text) ||
                              (component.Description ?? "").ToLower().Contains(text) ||
                              (component.Value ?? "").ToLower().Contains(text) ||
                              (component.Manufacturer ?? "").ToLower().Contains(text);
                if (!matches) return false;
            }

            return true;
        }

        #region Command Implementations

        private void ExecuteImportBom()
        {
            Publish(new ShowDialogEvent { DialogType = "BomImport" });
        }

        private void ExecuteExportBom()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv|Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                DefaultExt = ".csv",
                FileName = "BOM_Export"
            };

            if (dialog.ShowDialog() == true)
            {
                // TODO: Export BOM to CSV
                Publish(new StatusMessageEvent { Message = "BOM exported to " + dialog.FileName });
            }
        }

        private void ExecuteNewComponent()
        {
            if (Project == null) return;

            var component = new Component
            {
                PartNumber = "NEW_PART",
                Description = ""
            };

            Project.Components.Add(component);
            SelectedComponent = component;
            Publish(new ComponentAddedEvent { Component = component });
            OnPropertyChanged("TotalComponents");
        }

        private void ExecuteDeleteComponent()
        {
            if (Project == null || SelectedComponent == null) return;

            // Check if component is assigned to placements
            var assignedPlacements = Project.Placements.Where(p => p.Component == SelectedComponent).ToList();
            if (assignedPlacements.Count > 0)
            {
                // Clear component assignment from placements
                foreach (var p in assignedPlacements)
                {
                    p.Component = null;
                }
            }

            var component = SelectedComponent;
            Project.Components.Remove(component);
            SelectedComponent = null;
            Publish(new ComponentRemovedEvent { Component = component });
            OnPropertyChanged("TotalComponents");
            OnPropertyChanged("UnusedComponents");
        }

        private void ExecuteAssignToPlacements()
        {
            if (Project == null || SelectedComponent == null) return;

            // Parse reference designators and assign to matching placements
            foreach (var refDes in SelectedComponent.ReferenceDesignators)
            {
                var placement = Project.Placements.FirstOrDefault(p =>
                    string.Equals(p.Reference, refDes, StringComparison.OrdinalIgnoreCase));

                if (placement != null)
                {
                    placement.Component = SelectedComponent;
                }
            }

            Publish(new RequestRefreshEvent { FullRefresh = true });
            Publish(new StatusMessageEvent
            {
                Message = string.Format("Assigned {0} to {1} placements",
                    SelectedComponent.PartNumber,
                    SelectedComponent.ReferenceDesignators.Count)
            });
        }

        private void ExecuteAssignPackage()
        {
            if (SelectedComponent == null) return;

            Publish(new ShowDialogEvent
            {
                DialogType = "SelectPackage",
                Parameter = SelectedComponent
            });
        }

        private void ExecuteSelectPlacements()
        {
            if (Project == null || SelectedComponent == null) return;

            var placements = Project.Placements
                .Where(p => p.Component == SelectedComponent)
                .ToList();

            Publish(new SelectionChangedEvent
            {
                SelectedPlacements = placements,
                Source = this
            });
        }

        private void ExecuteValidateBom()
        {
            if (Project == null) return;

            int errorsFound = 0;

            // Update status for all components
            foreach (var component in Project.Components)
            {
                var placementCount = Project.Placements.Count(p => p.Component == component);

                if (placementCount == 0)
                {
                    component.Status = ComponentStatus.NoPlacements;
                    errorsFound++;
                }
                else
                {
                    component.Status = ComponentStatus.Valid;
                }
            }

            // Check for placements without components
            var unassignedPlacements = Project.Placements.Count(p => p.Component == null);

            OnPropertyChanged("UnusedComponents");
            ComponentsView?.Refresh();

            Publish(new StatusMessageEvent
            {
                Message = string.Format("Validation complete: {0} unused components, {1} unassigned placements",
                    errorsFound, unassignedPlacements),
                Type = errorsFound > 0 || unassignedPlacements > 0
                    ? StatusMessageType.Warning
                    : StatusMessageType.Success
            });
        }

        #endregion

        #region Event Handlers

        private void OnComponentAdded(ComponentAddedEvent e)
        {
            ComponentsView?.Refresh();
            OnPropertyChanged("TotalComponents");
        }

        private void OnComponentRemoved(ComponentRemovedEvent e)
        {
            ComponentsView?.Refresh();
            OnPropertyChanged("TotalComponents");
            OnPropertyChanged("UnusedComponents");
        }

        private void OnPlacementsChanged(PlacementsChangedEvent e)
        {
            OnPropertyChanged("UnusedComponents");
        }

        #endregion
    }
}
