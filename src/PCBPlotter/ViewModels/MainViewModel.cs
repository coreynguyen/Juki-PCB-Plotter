using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using PCBPlotter.Core.Events;
using PCBPlotter.Core.Models;
using PCBPlotter.Core.Services;

namespace PCBPlotter.ViewModels
{
    /// <summary>
    /// Recent project entry for the start screen
    /// </summary>
    public class RecentProjectInfo
    {
        public string Name { get; set; }
        public string FilePath { get; set; }
        public DateTime LastOpened { get; set; }
    }

    /// <summary>
    /// Main window view model
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        private Project _currentProject;
        private string _title = "PCB Plotter";
        private string _statusMessage = "Ready";
        private bool _isProjectLoaded;
        private bool _showStartScreen = true;
        private int _selectedTabIndex;
        private OutputPlotViewModel _outputPlotViewModel;
        private PlacementEditorViewModel _placementEditorViewModel;
        private ComponentEditorViewModel _componentEditorViewModel;
        private BomEditorViewModel _bomEditorViewModel;
        private GerberViewerViewModel _gerberViewerViewModel;
        private LogViewModel _logViewModel;
        private ObservableCollection<RecentProjectInfo> _recentProjects;
        private RecentProjectInfo _selectedRecentProject;
        private bool _useSystemTheme = true;
        private bool _useDarkTheme;
        private bool _useLightTheme;

        public Project CurrentProject
        {
            get { return _currentProject; }
            set
            {
                if (SetProperty(ref _currentProject, value))
                {
                    IsProjectLoaded = value != null;
                    ShowStartScreen = value == null;
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

        public bool ShowStartScreen
        {
            get { return _showStartScreen; }
            set { SetProperty(ref _showStartScreen, value); }
        }

        public int SelectedTabIndex
        {
            get { return _selectedTabIndex; }
            set { SetProperty(ref _selectedTabIndex, value); }
        }

        public ObservableCollection<RecentProjectInfo> RecentProjects
        {
            get { return _recentProjects; }
            set { SetProperty(ref _recentProjects, value); }
        }

        public RecentProjectInfo SelectedRecentProject
        {
            get { return _selectedRecentProject; }
            set { SetProperty(ref _selectedRecentProject, value); }
        }

        public bool HasNoRecentProjects
        {
            get { return RecentProjects == null || RecentProjects.Count == 0; }
        }

        public bool UseSystemTheme
        {
            get { return _useSystemTheme; }
            set
            {
                if (SetProperty(ref _useSystemTheme, value) && value)
                {
                    UseDarkTheme = false;
                    UseLightTheme = false;
                    ApplySystemTheme();
                }
            }
        }

        public bool UseDarkTheme
        {
            get { return _useDarkTheme; }
            set
            {
                if (SetProperty(ref _useDarkTheme, value) && value)
                {
                    UseSystemTheme = false;
                    UseLightTheme = false;
                    ApplyDarkTheme();
                }
            }
        }

        public bool UseLightTheme
        {
            get { return _useLightTheme; }
            set
            {
                if (SetProperty(ref _useLightTheme, value) && value)
                {
                    UseSystemTheme = false;
                    UseDarkTheme = false;
                    ApplyLightTheme();
                }
            }
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

        public LogViewModel LogViewModel
        {
            get { return _logViewModel; }
            set { SetProperty(ref _logViewModel, value); }
        }

        public UndoRedoService UndoRedoService { get; private set; }

        // Commands
        public ICommand NewProjectCommand { get; private set; }
        public ICommand OpenProjectCommand { get; private set; }
        public ICommand OpenRecentProjectCommand { get; private set; }
        public ICommand SaveProjectCommand { get; private set; }
        public ICommand SaveProjectAsCommand { get; private set; }
        public ICommand CloseProjectCommand { get; private set; }
        public ICommand OpenSettingsCommand { get; private set; }
        public ICommand ExitCommand { get; private set; }
        public ICommand UndoCommand { get; private set; }
        public ICommand RedoCommand { get; private set; }
        public ICommand ImportPnpTextCommand { get; private set; }
        public ICommand ImportCadCommand { get; private set; }
        public ICommand ImportBomCommand { get; private set; }
        public ICommand ImportGerberCommand { get; private set; }
        public ICommand ExportMachineFileCommand { get; private set; }
        public ICommand ExportBomCommand { get; private set; }
        public ICommand ShowStartScreenCommand { get; private set; }
        public ICommand FloatDesignViewCommand { get; private set; }
        public ICommand FloatPlacementEditorCommand { get; private set; }
        public ICommand FloatComponentEditorCommand { get; private set; }
        public ICommand FloatBomEditorCommand { get; private set; }
        public ICommand FloatGerberViewerCommand { get; private set; }
        public ICommand AboutCommand { get; private set; }

        // Board commands
        public ICommand SetBoardAreaCommand { get; private set; }
        public ICommand TranslateBoardCommand { get; private set; }
        public ICommand TranslateTopPlacementsCommand { get; private set; }
        public ICommand TranslateBottomPlacementsCommand { get; private set; }

        // Start screen quick commands
        public ICommand QuickImportPnpCommand { get; private set; }
        public ICommand QuickImportCadCommand { get; private set; }
        public ICommand QuickImportGerberCommand { get; private set; }
        public ICommand CreateBlankProjectCommand { get; private set; }

        public MainViewModel()
        {
            UndoRedoService = new UndoRedoService();
            RecentProjects = new ObservableCollection<RecentProjectInfo>();
            LoadRecentProjects();
            InitializeCommands();
            InitializeChildViewModels();
            SubscribeToEvents();

            // Auto-create a new project so menus are enabled immediately
            // User can still access start screen via View menu if needed
            ExecuteNewProject();
        }

        private void InitializeCommands()
        {
            NewProjectCommand = new RelayCommand(ExecuteNewProject);
            OpenProjectCommand = new RelayCommand(ExecuteOpenProject);
            OpenRecentProjectCommand = new RelayCommand<string>(ExecuteOpenRecentProject);
            SaveProjectCommand = new RelayCommand(ExecuteSaveProject, () => IsProjectLoaded);
            SaveProjectAsCommand = new RelayCommand(ExecuteSaveProjectAs, () => IsProjectLoaded);
            CloseProjectCommand = new RelayCommand(ExecuteCloseProject, () => IsProjectLoaded);
            OpenSettingsCommand = new RelayCommand(ExecuteOpenSettings);
            ExitCommand = new RelayCommand(ExecuteExit);
            UndoCommand = new RelayCommand(ExecuteUndo, () => UndoRedoService.CanUndo);
            RedoCommand = new RelayCommand(ExecuteRedo, () => UndoRedoService.CanRedo);
            ImportPnpTextCommand = new RelayCommand(ExecuteImportPnpText, () => IsProjectLoaded);
            ImportCadCommand = new RelayCommand(ExecuteImportCad, () => IsProjectLoaded);
            ImportBomCommand = new RelayCommand(ExecuteImportBom, () => IsProjectLoaded);
            ImportGerberCommand = new RelayCommand(ExecuteImportGerber, () => IsProjectLoaded);
            ExportMachineFileCommand = new RelayCommand(ExecuteExportMachineFile, () => IsProjectLoaded);
            ExportBomCommand = new RelayCommand(ExecuteExportBom, () => IsProjectLoaded);
            ShowStartScreenCommand = new RelayCommand(() => ShowStartScreen = !ShowStartScreen);
            FloatDesignViewCommand = new RelayCommand(o => ExecuteFloatWindow("Design"));
            FloatPlacementEditorCommand = new RelayCommand(o => ExecuteFloatWindow("Placements"));
            FloatComponentEditorCommand = new RelayCommand(o => ExecuteFloatWindow("Components"));
            FloatBomEditorCommand = new RelayCommand(o => ExecuteFloatWindow("BOM"));
            FloatGerberViewerCommand = new RelayCommand(o => ExecuteFloatWindow("Gerber"));
            AboutCommand = new RelayCommand(ExecuteAbout);

            // Board commands
            SetBoardAreaCommand = new RelayCommand(ExecuteSetBoardArea, () => IsProjectLoaded);
            TranslateBoardCommand = new RelayCommand(ExecuteTranslateBoard, () => IsProjectLoaded);
            TranslateTopPlacementsCommand = new RelayCommand(ExecuteTranslateTopPlacements, () => IsProjectLoaded);
            TranslateBottomPlacementsCommand = new RelayCommand(ExecuteTranslateBottomPlacements, () => IsProjectLoaded);

            // Start screen quick commands
            QuickImportPnpCommand = new RelayCommand(ExecuteQuickImportPnp);
            QuickImportCadCommand = new RelayCommand(ExecuteQuickImportCad);
            QuickImportGerberCommand = new RelayCommand(ExecuteQuickImportGerber);
            CreateBlankProjectCommand = new RelayCommand(ExecuteCreateBlankProject);
        }

        private void InitializeChildViewModels()
        {
            OutputPlotViewModel = new OutputPlotViewModel();
            PlacementEditorViewModel = new PlacementEditorViewModel();
            ComponentEditorViewModel = new ComponentEditorViewModel();
            BomEditorViewModel = new BomEditorViewModel();
            GerberViewerViewModel = new GerberViewerViewModel();
            LogViewModel = new LogViewModel();
        }

        private void SubscribeToEvents()
        {
            Subscribe<ProjectLoadedEvent>(OnProjectLoaded);
            Subscribe<ProjectSavedEvent>(OnProjectSaved);
            Subscribe<ProjectClosedEvent>(OnProjectClosed);
            Subscribe<StatusMessageEvent>(OnStatusMessage);
            Subscribe<SelectionChangedEvent>(OnSelectionChanged);
            Subscribe<NavigateToTabEvent>(OnNavigateToTab);

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

        private void LoadRecentProjects()
        {
            // TODO: Load from settings/registry
            // For now, just create an empty list
            RecentProjects.Clear();
            OnPropertyChanged("HasNoRecentProjects");
        }

        private void AddToRecentProjects(string filePath, string name)
        {
            var existing = RecentProjects.FirstOrDefault(r => r.FilePath == filePath);
            if (existing != null)
            {
                RecentProjects.Remove(existing);
            }

            RecentProjects.Insert(0, new RecentProjectInfo
            {
                Name = name,
                FilePath = filePath,
                LastOpened = DateTime.Now
            });

            // Keep only the last 10
            while (RecentProjects.Count > 10)
            {
                RecentProjects.RemoveAt(RecentProjects.Count - 1);
            }

            OnPropertyChanged("HasNoRecentProjects");
            // TODO: Save to settings/registry
        }

        private void ApplySystemTheme()
        {
            Services.ThemeService.Instance.CurrentPreference = Services.AppTheme.System;
        }

        private void ApplyDarkTheme()
        {
            Services.ThemeService.Instance.CurrentPreference = Services.AppTheme.Dark;
        }

        private void ApplyLightTheme()
        {
            Services.ThemeService.Instance.CurrentPreference = Services.AppTheme.Light;
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
                OpenProjectFile(dialog.FileName);
            }
        }

        private void ExecuteOpenRecentProject(string filePath)
        {
            if (!string.IsNullOrEmpty(filePath))
            {
                OpenProjectFile(filePath);
            }
            else if (SelectedRecentProject != null)
            {
                OpenProjectFile(SelectedRecentProject.FilePath);
            }
        }

        private void OpenProjectFile(string filePath)
        {
            var project = ProjectService.Instance.LoadProject(filePath);
            if (project != null)
            {
                CurrentProject = project;
                AddToRecentProjects(filePath, project.Name);
                StatusMessage = "Project loaded: " + filePath;
            }
            else
            {
                MessageBox.Show("Failed to load project.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    AddToRecentProjects(dialog.FileName, CurrentProject.Name);
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

        private void ExecuteOpenSettings()
        {
            var dialog = new Views.SettingsDialog();
            dialog.Owner = Application.Current.MainWindow;
            if (dialog.ShowDialog() == true)
            {
                StatusMessage = "Settings saved";
            }
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

        private void ExecuteImportPnpText()
        {
            System.Diagnostics.Debug.WriteLine("ExecuteImportPnpText: publishing ShowDialogEvent PnpImport");
            Publish(new ShowDialogEvent { DialogType = "PnpImport" });
            System.Diagnostics.Debug.WriteLine("ExecuteImportPnpText: event published");
        }

        private void ExecuteImportCad()
        {
            Publish(new ShowDialogEvent { DialogType = "CadImport" });
        }

        private void ExecuteImportBom()
        {
            Publish(new ShowDialogEvent { DialogType = "BomImport" });
        }

        /// <summary>
        /// Unified Gerber file filter for all import dialogs.
        /// All Files is first (default) since Gerber extensions vary widely.
        /// </summary>
        public const string GerberFileFilter =
            "All Files (*.*)|*.*|" +
            "Gerber Files (*.gbr;*.ger;*.art;*.gtl;*.gbl;*.gto;*.gbo;*.gts;*.gbs;*.gtp;*.gbp;*.gko;*.gm1)|" +
            "*.gbr;*.ger;*.art;*.gtl;*.gbl;*.gto;*.gbo;*.gts;*.gbs;*.gtp;*.gbp;*.gko;*.gm1";

        private void ExecuteImportGerber()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = GerberFileFilter,
                Multiselect = true,
                Title = "Import Gerber Files"
            };

            if (dialog.ShowDialog() == true)
            {
                // Forward to GerberViewerViewModel for actual import
                GerberViewerViewModel?.ImportGerberFiles(dialog.FileNames);
                SelectedTabIndex = 4; // Switch to Gerber tab
            }
        }

        private void ExecuteExportMachineFile()
        {
            Publish(new ShowDialogEvent { DialogType = "MachineExport" });
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
                StatusMessage = "BOM exported to " + dialog.FileName;
            }
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

        private void ExecuteQuickImportPnp()
        {
            // Hide start screen and proceed with import
            // Project already exists from auto-creation on startup
            ShowStartScreen = false;
            ExecuteImportPnpText();
        }

        private void ExecuteQuickImportCad()
        {
            ShowStartScreen = false;
            ExecuteImportCad();
        }

        private void ExecuteQuickImportGerber()
        {
            ShowStartScreen = false;
            SelectedTabIndex = 4; // Switch to Gerber tab
            ExecuteImportGerber();
        }

        private void ExecuteCreateBlankProject()
        {
            // Project already exists, just hide start screen
            ShowStartScreen = false;
        }

        private void ExecuteSetBoardArea()
        {
            Publish(new ShowDialogEvent { DialogType = "PcbArea" });
        }

        private void ExecuteTranslateBoard()
        {
            // Translates board origin and all placements together
            if (CurrentProject == null) return;

            var allPlacements = CurrentProject.Placements.ToList();
            Publish(new ShowDialogEvent
            {
                DialogType = "TranslatePlacements",
                Parameter = allPlacements
            });
        }

        private void ExecuteTranslateTopPlacements()
        {
            if (CurrentProject == null) return;

            var topPlacements = CurrentProject.Placements
                .Where(p => p.Side == BoardSide.Top)
                .ToList();

            if (topPlacements.Count == 0)
            {
                StatusMessage = "No top side placements found";
                return;
            }

            Publish(new ShowDialogEvent
            {
                DialogType = "TranslatePlacements",
                Parameter = topPlacements
            });
        }

        private void ExecuteTranslateBottomPlacements()
        {
            if (CurrentProject == null) return;

            var bottomPlacements = CurrentProject.Placements
                .Where(p => p.Side == BoardSide.Bottom)
                .ToList();

            if (bottomPlacements.Count == 0)
            {
                StatusMessage = "No bottom side placements found";
                return;
            }

            Publish(new ShowDialogEvent
            {
                DialogType = "TranslatePlacements",
                Parameter = bottomPlacements
            });
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

        private void OnNavigateToTab(NavigateToTabEvent e)
        {
            // Switch to the requested tab
            SelectedTabIndex = e.TabIndex;

            // If there's a placement to scroll to, publish an event for the view to handle
            if (e.ScrollToPlacement != null)
            {
                Publish(new FocusPlacementEvent
                {
                    Placement = e.ScrollToPlacement,
                    CenterView = false
                });
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
                LogViewModel?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
