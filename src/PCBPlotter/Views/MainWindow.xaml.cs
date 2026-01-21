using System.Windows;
using PCBPlotter.Core.Events;
using PCBPlotter.Services;
using PCBPlotter.ViewModels;

namespace PCBPlotter.Views
{
    /// <summary>
    /// Main application window
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Register with window service
            WindowService.Instance.SetMainWindow(this);

            // Subscribe to dialog events
            EventAggregator.Instance.Subscribe<ShowDialogEvent>(OnShowDialog);

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Show start screen by default (don't auto-create project)
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // TODO: Check for unsaved changes
            EventAggregator.Instance.Unsubscribe<ShowDialogEvent>(OnShowDialog);
            WindowService.Instance.CloseAllFloatingWindows();
        }

        private void OnShowDialog(ShowDialogEvent e)
        {
            // Ensure we're on the UI thread
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => OnShowDialog(e));
                return;
            }

            System.Diagnostics.Debug.WriteLine("OnShowDialog called with type: " + e.DialogType);

            switch (e.DialogType)
            {
                case "PnpImport":
                case "BomImport":
                    ShowTextImportDialog(e.DialogType);
                    break;

                case "CadImport":
                    ShowCadImportDialog();
                    break;

                case "MachineExport":
                    ShowMachineExportDialog();
                    break;

                case "PcbArea":
                    // TODO: Show PCB area dialog
                    MessageBox.Show("PCB Area dialog coming soon.", "Add PCB Area",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    break;

                default:
                    System.Diagnostics.Debug.WriteLine("Unknown dialog type: " + e.DialogType);
                    break;
            }
        }

        private void ShowTextImportDialog(string importType)
        {
            System.Diagnostics.Debug.WriteLine("ShowTextImportDialog called with type: " + importType);

            var dialog = new TextImportDialog();
            dialog.Owner = this;

            // Set the import type based on the dialog type
            var vm = dialog.DataContext as TextImportViewModel;
            if (vm != null)
            {
                vm.ImportType = importType == "BomImport" ? "BOM" : "Placements (PNP)";
            }

            if (dialog.ShowDialog() == true)
            {
                // Import completed - get placements from the dialog
                if (vm != null && importType == "PnpImport")
                {
                    var placements = vm.GetPlacements();
                    var mainVm = DataContext as MainViewModel;
                    if (mainVm?.CurrentProject != null)
                    {
                        foreach (var placement in placements)
                        {
                            mainVm.CurrentProject.Placements.Add(placement);
                        }
                        EventAggregator.Instance.Publish(new RequestRefreshEvent { FullRefresh = true });
                        EventAggregator.Instance.Publish(new StatusMessageEvent
                        {
                            Message = string.Format("Imported {0} placements", placements.Count)
                        });
                    }
                }
            }
        }

        private void ShowCadImportDialog()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "CAD Files (*.xml;*.ipc)|*.xml;*.ipc|ODB++ (*.zip;*.tgz)|*.zip;*.tgz|All Files (*.*)|*.*",
                Title = "Import CAD Data"
            };

            if (dialog.ShowDialog() == true)
            {
                // TODO: Implement CAD import
                MessageBox.Show("CAD import not yet implemented.\nSelected: " + dialog.FileName,
                    "Import CAD", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ShowMachineExportDialog()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Juki H8H (*.h8h)|*.h8h|CSV (*.csv)|*.csv|All Files (*.*)|*.*",
                DefaultExt = ".h8h",
                Title = "Export Machine File"
            };

            if (dialog.ShowDialog() == true)
            {
                // TODO: Implement machine export
                MessageBox.Show("Machine export not yet implemented.\nTarget: " + dialog.FileName,
                    "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
