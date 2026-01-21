using System;
using System.Collections.Generic;
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
    /// View model for the placement editor (list view of all placements)
    /// </summary>
    public class PlacementEditorViewModel : ViewModelBase
    {
        private Project _project;
        private ICollectionView _placementsView;
        private string _filterText;
        private BoardSide? _filterSide;
        private PlacementStatus? _filterStatus;
        private ObservableCollection<Placement> _selectedPlacements;

        public Project Project
        {
            get { return _project; }
            set
            {
                if (SetProperty(ref _project, value))
                {
                    UpdatePlacementsView();
                }
            }
        }

        public ICollectionView PlacementsView
        {
            get { return _placementsView; }
            set { SetProperty(ref _placementsView, value); }
        }

        public string FilterText
        {
            get { return _filterText; }
            set
            {
                if (SetProperty(ref _filterText, value))
                {
                    PlacementsView?.Refresh();
                }
            }
        }

        public BoardSide? FilterSide
        {
            get { return _filterSide; }
            set
            {
                if (SetProperty(ref _filterSide, value))
                {
                    PlacementsView?.Refresh();
                }
            }
        }

        public PlacementStatus? FilterStatus
        {
            get { return _filterStatus; }
            set
            {
                if (SetProperty(ref _filterStatus, value))
                {
                    PlacementsView?.Refresh();
                }
            }
        }

        public ObservableCollection<Placement> SelectedPlacements
        {
            get { return _selectedPlacements; }
            set { SetProperty(ref _selectedPlacements, value); }
        }

        public int TotalCount
        {
            get { return Project?.Placements.Count ?? 0; }
        }

        public int FilteredCount
        {
            get { return PlacementsView?.Cast<Placement>().Count() ?? 0; }
        }

        public int SelectedCount
        {
            get { return SelectedPlacements?.Count ?? 0; }
        }

        // Commands
        public ICommand ClearFilterCommand { get; private set; }
        public ICommand SelectAllVisibleCommand { get; private set; }
        public ICommand SelectNoneCommand { get; private set; }
        public ICommand FocusInPlotCommand { get; private set; }
        public ICommand AssignPackageCommand { get; private set; }
        public ICommand AssignComponentCommand { get; private set; }
        public ICommand SetSideCommand { get; private set; }
        public ICommand EnableExportCommand { get; private set; }
        public ICommand DisableExportCommand { get; private set; }
        public ICommand DeleteSelectedCommand { get; private set; }
        public ICommand RotateSelectedCommand { get; private set; }

        public PlacementEditorViewModel()
        {
            _selectedPlacements = new ObservableCollection<Placement>();
            _selectedPlacements.CollectionChanged += (s, e) => OnPropertyChanged("SelectedCount");
            InitializeCommands();
            SubscribeToEvents();
        }

        private void InitializeCommands()
        {
            ClearFilterCommand = new RelayCommand(() =>
            {
                FilterText = null;
                FilterSide = null;
                FilterStatus = null;
            });

            SelectAllVisibleCommand = new RelayCommand(ExecuteSelectAllVisible);
            SelectNoneCommand = new RelayCommand(ExecuteSelectNone);
            FocusInPlotCommand = new RelayCommand(ExecuteFocusInPlot, () => SelectedPlacements.Count > 0);
            AssignPackageCommand = new RelayCommand(ExecuteAssignPackage, () => SelectedPlacements.Count > 0);
            AssignComponentCommand = new RelayCommand(ExecuteAssignComponent, () => SelectedPlacements.Count > 0);
            SetSideCommand = new RelayCommand<BoardSide>(ExecuteSetSide, s => SelectedPlacements.Count > 0);
            EnableExportCommand = new RelayCommand(ExecuteEnableExport, () => SelectedPlacements.Count > 0);
            DisableExportCommand = new RelayCommand(ExecuteDisableExport, () => SelectedPlacements.Count > 0);
            DeleteSelectedCommand = new RelayCommand(ExecuteDeleteSelected, () => SelectedPlacements.Count > 0);
            RotateSelectedCommand = new RelayCommand<double>(ExecuteRotateSelected, d => SelectedPlacements.Count > 0);
        }

        private void SubscribeToEvents()
        {
            Subscribe<SelectionChangedEvent>(OnSelectionChanged);
            Subscribe<PlacementsChangedEvent>(OnPlacementsChanged);
        }

        private void UpdatePlacementsView()
        {
            if (Project != null)
            {
                PlacementsView = CollectionViewSource.GetDefaultView(Project.Placements);
                PlacementsView.Filter = FilterPlacement;
                PlacementsView.SortDescriptions.Add(new SortDescription("Reference", ListSortDirection.Ascending));
            }
            else
            {
                PlacementsView = null;
            }

            OnPropertyChanged("TotalCount");
            OnPropertyChanged("FilteredCount");
        }

        private bool FilterPlacement(object obj)
        {
            var placement = obj as Placement;
            if (placement == null) return false;

            // Text filter
            if (!string.IsNullOrEmpty(FilterText))
            {
                var text = FilterText.ToLower();
                bool matches = (placement.Reference ?? "").ToLower().Contains(text) ||
                              (placement.Component?.PartNumber ?? "").ToLower().Contains(text) ||
                              (placement.Package?.Name ?? "").ToLower().Contains(text);
                if (!matches) return false;
            }

            // Side filter
            if (FilterSide.HasValue && placement.Side != FilterSide.Value)
                return false;

            // Status filter
            if (FilterStatus.HasValue && (placement.Status & FilterStatus.Value) == 0)
                return false;

            return true;
        }

        #region Command Implementations

        private void ExecuteSelectAllVisible()
        {
            SelectedPlacements.Clear();
            foreach (Placement p in PlacementsView)
            {
                p.IsSelected = true;
                SelectedPlacements.Add(p);
            }
            PublishSelectionChanged();
        }

        private void ExecuteSelectNone()
        {
            foreach (var p in SelectedPlacements)
            {
                p.IsSelected = false;
            }
            SelectedPlacements.Clear();
            PublishSelectionChanged();
        }

        private void ExecuteFocusInPlot()
        {
            if (SelectedPlacements.Count == 1)
            {
                Publish(new FocusPlacementEvent
                {
                    Placement = SelectedPlacements[0],
                    CenterView = true
                });
            }
            else if (SelectedPlacements.Count > 1)
            {
                Publish(new FocusMultiplePlacementsEvent
                {
                    Placements = SelectedPlacements.ToList(),
                    CenterView = true
                });
            }
        }

        private void ExecuteAssignPackage()
        {
            // TODO: Show package selection dialog
            Publish(new ShowDialogEvent
            {
                DialogType = "SelectPackage",
                Parameter = SelectedPlacements.ToList()
            });
        }

        private void ExecuteAssignComponent()
        {
            // TODO: Show component selection dialog
            Publish(new ShowDialogEvent
            {
                DialogType = "SelectComponent",
                Parameter = SelectedPlacements.ToList()
            });
        }

        private void ExecuteSetSide(BoardSide side)
        {
            foreach (var p in SelectedPlacements)
            {
                p.Side = side;
            }
            Publish(new RequestRefreshEvent { FullRefresh = true });
        }

        private void ExecuteEnableExport()
        {
            foreach (var p in SelectedPlacements)
            {
                if (p.Side == BoardSide.Top)
                    p.IsExportEnabledTop = true;
                else
                    p.IsExportEnabledBottom = true;
            }
        }

        private void ExecuteDisableExport()
        {
            foreach (var p in SelectedPlacements)
            {
                if (p.Side == BoardSide.Top)
                    p.IsExportEnabledTop = false;
                else
                    p.IsExportEnabledBottom = false;
            }
        }

        private void ExecuteDeleteSelected()
        {
            if (Project == null) return;

            var toDelete = SelectedPlacements.ToList();
            foreach (var p in toDelete)
            {
                Project.Placements.Remove(p);
            }
            SelectedPlacements.Clear();

            Publish(new PlacementsChangedEvent
            {
                Placements = toDelete,
                ChangeType = "Removed"
            });
            OnPropertyChanged("TotalCount");
            OnPropertyChanged("FilteredCount");
        }

        private void ExecuteRotateSelected(double angle)
        {
            foreach (var p in SelectedPlacements)
            {
                p.Rotation += angle;
            }
            Publish(new RequestRefreshEvent { FullRefresh = false });
        }

        #endregion

        private void PublishSelectionChanged()
        {
            Publish(new SelectionChangedEvent
            {
                SelectedPlacements = SelectedPlacements.ToList(),
                Source = this
            });
        }

        public void OnPlacementSelected(Placement placement, bool addToSelection)
        {
            if (!addToSelection)
            {
                foreach (var p in SelectedPlacements)
                {
                    p.IsSelected = false;
                }
                SelectedPlacements.Clear();
            }

            if (placement != null)
            {
                placement.IsSelected = true;
                if (!SelectedPlacements.Contains(placement))
                {
                    SelectedPlacements.Add(placement);
                }
            }

            PublishSelectionChanged();
        }

        #region Event Handlers

        private void OnSelectionChanged(SelectionChangedEvent e)
        {
            if (e.Source == this) return;

            SelectedPlacements.Clear();
            foreach (var p in e.SelectedPlacements)
            {
                SelectedPlacements.Add(p);
            }
        }

        private void OnPlacementsChanged(PlacementsChangedEvent e)
        {
            PlacementsView?.Refresh();
            OnPropertyChanged("TotalCount");
            OnPropertyChanged("FilteredCount");
        }

        #endregion
    }
}
