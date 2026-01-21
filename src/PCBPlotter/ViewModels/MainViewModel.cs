using System;
using System.Windows;
using System.Windows.Input;
using PCBPlotter.Core.Events;
using PCBPlotter.Core.Models;
using PCBPlotter.Core.Services;

namespace PCBPlotter.ViewModels
{
    /// <summary>
    /// Main window view model
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        private Project _currentProject;
        private string _title = "PCB Plotter";
        private string _statusMessage = "Ready";
        private bool _isProjectLoaded;
        private int _selectedTabIndex;
        private OutputPlotViewModel _outputPlotViewModel;
        private PlacementEditorViewModel _placementEditorViewModel;
        private ComponentEditorViewModel _componentEditorViewModel;
        private BomEditorViewModel _bomEditorViewModel;
        private GerberViewerViewModel _gerberViewerViewModel;

        public Project CurrentProject
        {
            get { return _currentProject; }
            set
            {
                if (SetProperty(ref _currentProject, value))
                {
                    IsProjectLoaded = value != null;
                    UpdateTitle();
                    NotifyViewModels();
                }
            }
        }

        public string Title
        {
            get { return _title; }
            set { SetProperty(ref _title, value); }
        }

        public string StatusMessage
        {
            get { return _statusMessage; }
            set { SetProperty(ref _statusMessage, value); }
        }

        public bool IsProjectLoaded
        {
            get { return _isProjectLoaded; }
            set { SetProperty(ref _isProjectLoaded, value); }
        }

        public int SelectedTabIndex
        {
            get { return _selectedTabIndex; }
            set { SetProperty(ref _selectedTabIndex, value); }
        }

        public OutputPlotViewModel OutputPlotViewModel
        {
            get { return _outputPlotViewModel; }
            set { SetProperty(ref _outputPlotViewModel, value); }
        }

        public PlacementEditorViewModel PlacementEditorViewModel
        {
            get { return _placementEditorViewModel; }
            set { SetProperty(ref _placementEditorViewModel, value); }
        }

        public ComponentEditorViewModel ComponentEditorViewModel
        {
            get { return _componentEditorViewModel; }
            set { SetProperty(ref _componentEditorViewModel, value); }
        }

        public BomEditorViewModel BomEditorViewModel
        {
            get { return _bomEditorViewModel; }
            set { SetProperty(ref _bomEditorViewModel, value); }
        }

        public GerberViewerViewModel GerberViewerViewModel
        {
            get { return _gerberViewerViewModel; }
            set { SetProperty(ref _gerberViewerViewModel, value); }
        }

        public UndoRedoService UndoRedoService { get; private set; }

        // Commands
        public ICommand NewProjectCommand { get; private set; }
        public ICommand OpenProjectCommand { get; private set; }
        public ICommand SaveProjectCommand { get; private set; }
        public ICommand SaveProjectAsCommand { get; private set; }
        public ICommand CloseProjectCommand { get; private set; }
        public ICommand ExitCommand { get; private set; }
        public ICommand UndoCommand { get; private set; }
        public ICommand RedoCommand { get; private set; }
        public ICommand ImportCadCommand { get; private set; }
        public ICommand ImportBomCommand { get; private set; }
        public ICommand ImportGerberCommand { get; private set; }
        public ICommand ExportMachineFileCommand { get; private set; }
        public ICommand FloatOutputPlotCommand { get; private set; }
        public ICommand FloatPlacementEditorCommand { get; private set; }
        public ICommand FloatComponentEditorCommand { get; private set; }
        public ICommand AboutCommand { get; private set; }

        public MainViewModel()
        {
            UndoRedoService = new UndoRedoService();
            InitializeCommands();
            InitializeChildViewModels();
            SubscribeToEvents();
        }

        private void InitializeCommands()
        {
            NewProjectCommand = new RelayCommand(ExecuteNewProject);
            OpenProjectCommand = new RelayCommand(ExecuteOpenProject);
            SaveProjectCommand = new RelayCommand(ExecuteSaveProject, () => IsProjectLoaded);
            SaveProjectAsCommand = new RelayCommand(ExecuteSaveProjectAs, () => IsProjectLoaded);
            CloseProjectCommand = new RelayCommand(ExecuteCloseProject, () => IsProjectLoaded);
            ExitCommand = new RelayCommand(ExecuteExit);
            UndoCommand = new RelayCommand(ExecuteUndo, () => UndoRedoService.CanUndo);
            RedoCommand = new RelayCommand(ExecuteRedo, () => UndoRedoService.CanRedo);
            ImportCadCommand = new RelayCommand(ExecuteImportCad, () => IsProjectLoaded);
            ImportBomCommand = new RelayCommand(ExecuteImportBom, () => IsProjectLoaded);
            ImportGerberCommand = new RelayCommand(ExecuteImportGerber, () => IsProjectLoaded);
            ExportMachineFileCommand = new RelayCommand(ExecuteExportMachineFile, () => IsProjectLoaded);
            FloatOutputPlotCommand = new RelayCommand(o => ExecuteFloatWindow("OutputPlot"));
            FloatPlacementEditorCommand = new RelayCommand(o => ExecuteFloatWindow("PlacementEditor"));
            FloatComponentEditorCommand = new RelayCommand(o => ExecuteFloatWindow("ComponentEditor"));
            AboutCommand = new RelayCommand(ExecuteAbout);
        }

        private void InitializeChildViewModels()
        {
            OutputPlotViewModel = new OutputPlotViewModel();
            PlacementEditorViewModel = new PlacementEditorViewModel();
            ComponentEditorViewModel = new ComponentEditorViewModel();
            BomEditorViewModel = new BomEditorViewModel();
            GerberViewerViewModel = new GerberViewerViewModel();
        }

        private void SubscribeToEvents()
        {
            Subscribe<ProjectLoadedEvent>(OnProjectLoaded);
            Subscribe<ProjectSavedEvent>(OnProjectSaved);
            Subscribe<ProjectClosedEvent>(OnProjectClosed);
            Subscribe<StatusMessageEvent>(OnStatusMessage);
            Subscribe<SelectionChangedEvent>(OnSelectionChanged);

            UndoRedoService.StackChanged += (s, e) =>
            {
                ((RelayCommand)UndoCommand).RaiseCanExecuteChanged();
                ((RelayCommand)RedoCommand).RaiseCanExecuteChanged();
            };
        }

        private void NotifyViewModels()
        {
            OutputPlotViewModel.Project = CurrentProject;
            PlacementEditorViewModel.Project = CurrentProject;
            ComponentEditorViewModel.Project = CurrentProject;
            BomEditorViewModel.Project = CurrentProject;
            GerberViewerViewModel.Project = CurrentProject;
        }

        private void UpdateTitle()
        {
            if (CurrentProject != null && !string.IsNullOrEmpty(CurrentProject.Name))
            {
                Title = string.Format("{0} - PCB Plotter", CurrentProject.Name);
            }
            else
            {
                Title = "PCB Plotter";
            }
        }

        #region Command Implementations

        private void ExecuteNewProject()
        {
            CurrentProject = ProjectService.Instance.NewProject();
            StatusMessage = "New project created";
        }

        private void ExecuteOpenProject()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PCB Plotter Project (*.jpp)|*.jpp|All Files (*.*)|*.*",
                DefaultExt = ".jpp"
            };

            if (dialog.ShowDialog() == true)
            {
                var project = ProjectService.Instance.LoadProject(dialog.FileName);
                if (project != null)
                {
                    CurrentProject = project;
                    StatusMessage = "Project loaded: " + dialog.FileName;
                }
                else
                {
                    MessageBox.Show("Failed to load project.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExecuteSaveProject()
        {
            if (string.IsNullOrEmpty(CurrentProject.FilePath))
            {
                ExecuteSaveProjectAs();
                return;
            }

            if (ProjectService.Instance.SaveProject())
            {
                StatusMessage = "Project saved";
            }
            else
            {
                MessageBox.Show("Failed to save project.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExecuteSaveProjectAs()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PCB Plotter Project (*.jpp)|*.jpp|All Files (*.*)|*.*",
                DefaultExt = ".jpp",
                FileName = CurrentProject.Name ?? "Untitled"
            };

            if (dialog.ShowDialog() == true)
            {
                if (ProjectService.Instance.SaveProject(dialog.FileName))
                {
                    CurrentProject.Name = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
                    UpdateTitle();
                    StatusMessage = "Project saved: " + dialog.FileName;
                }
                else
                {
                    MessageBox.Show("Failed to save project.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExecuteCloseProject()
        {
            // TODO: Check for unsaved changes
            ProjectService.Instance.CloseProject();
            CurrentProject = null;
            StatusMessage = "Project closed";
        }

        private void ExecuteExit()
        {
            // TODO: Check for unsaved changes
            Application.Current.Shutdown();
        }

        private void ExecuteUndo()
        {
            UndoRedoService.Undo();
            StatusMessage = "Undo: " + (UndoRedoService.RedoDescription ?? "");
        }

        private void ExecuteRedo()
        {
            UndoRedoService.Redo();
            StatusMessage = "Redo: " + (UndoRedoService.UndoDescription ?? "");
        }

        private void ExecuteImportCad()
        {
            // TODO: Open CAD import wizard
            Publish(new ShowDialogEvent { DialogType = "CadImport" });
        }

        private void ExecuteImportBom()
        {
            // TODO: Open BOM import wizard
            Publish(new ShowDialogEvent { DialogType = "BomImport" });
        }

        private void ExecuteImportGerber()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Gerber Files (*.gbr;*.ger;*.gtl;*.gbl;*.gto;*.gbo)|*.gbr;*.ger;*.gtl;*.gbl;*.gto;*.gbo|All Files (*.*)|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var file in dialog.FileNames)
                {
                    // TODO: Import gerber file
                    StatusMessage = "Importing: " + System.IO.Path.GetFileName(file);
                }
            }
        }

        private void ExecuteExportMachineFile()
        {
            // TODO: Open export dialog
            Publish(new ShowDialogEvent { DialogType = "MachineExport" });
        }

        private void ExecuteFloatWindow(string viewType)
        {
            Publish(new FloatWindowEvent { ViewType = viewType, Float = true });
        }

        private void ExecuteAbout()
        {
            MessageBox.Show(
                "PCB Plotter v1.0\n\n" +
                "A modern PCB pick-and-place data preparation tool.\n\n" +
                "Supports Juki H8H export format.",
                "About PCB Plotter",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }

        #endregion

        #region Event Handlers

        private void OnProjectLoaded(ProjectLoadedEvent e)
        {
            CurrentProject = e.Project;
        }

        private void OnProjectSaved(ProjectSavedEvent e)
        {
            UpdateTitle();
        }

        private void OnProjectClosed(ProjectClosedEvent e)
        {
            CurrentProject = null;
        }

        private void OnStatusMessage(StatusMessageEvent e)
        {
            StatusMessage = e.Message;
        }

        private void OnSelectionChanged(SelectionChangedEvent e)
        {
            if (e.SelectedPlacements.Count == 1)
            {
                StatusMessage = string.Format("Selected: {0}", e.SelectedPlacements[0].Reference);
            }
            else if (e.SelectedPlacements.Count > 1)
            {
                StatusMessage = string.Format("Selected: {0} placements", e.SelectedPlacements.Count);
            }
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                OutputPlotViewModel?.Dispose();
                PlacementEditorViewModel?.Dispose();
                ComponentEditorViewModel?.Dispose();
                BomEditorViewModel?.Dispose();
                GerberViewerViewModel?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
