using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
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
        // Keep a strong reference to prevent garbage collection
        private readonly Action<ShowDialogEvent> _showDialogHandler;

        // DWM API for dark title bar (Windows 10 1809+)
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public MainWindow()
        {
            InitializeComponent();

            // Register with window service
            WindowService.Instance.SetMainWindow(this);

            // Subscribe to dialog events - keep strong reference to handler
            _showDialogHandler = OnShowDialog;
            EventAggregator.Instance.Subscribe(_showDialogHandler);

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            SourceInitialized += MainWindow_SourceInitialized;
        }

        private void MainWindow_SourceInitialized(object sender, EventArgs e)
        {
            // Apply dark mode to title bar
            ApplyDarkTitleBar();
        }

        private void ApplyDarkTitleBar()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    int darkMode = 1; // Enable dark mode
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
                }
            }
            catch
            {
                // Ignore errors on older Windows versions
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Show start screen by default (don't auto-create project)
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // TODO: Check for unsaved changes
            EventAggregator.Instance.Unsubscribe(_showDialogHandler);
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
                    ShowPcbAreaDialog();
                    break;

                case "TranslatePlacements":
                    ShowTranslatePlacementsDialog(e.Parameter as System.Collections.Generic.List<Core.Models.Placement>);
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

            if (dialog.ShowDialog() == true && vm != null)
            {
                var mainVm = DataContext as MainViewModel;
                if (mainVm?.CurrentProject == null) return;

                if (importType == "PnpImport")
                {
                    // Import placements
                    var placements = vm.GetPlacements();
                    foreach (var placement in placements)
                    {
                        mainVm.CurrentProject.Placements.Add(placement);
                    }

                    // Refresh view and zoom to fit
                    EventAggregator.Instance.Publish(new RequestRefreshEvent { FullRefresh = true });
                    EventAggregator.Instance.Publish(new ZoomFitRequestEvent());
                    EventAggregator.Instance.Publish(new StatusMessageEvent
                    {
                        Message = string.Format("Imported {0} placements", placements.Count)
                    });

                    // Switch to Design tab to show placements
                    mainVm.SelectedTabIndex = 0;
                }
                else if (importType == "BomImport")
                {
                    // Import BOM data
                    var bomResult = vm.GetBomData();

                    // Clear existing components if requested
                    int clearedComponents = 0;
                    if (vm.ClearExistingOnImport)
                    {
                        clearedComponents = mainVm.CurrentProject.Components.Count;

                        // Clear component assignments from placements
                        foreach (var placement in mainVm.CurrentProject.Placements)
                        {
                            placement.Component = null;
                        }

                        mainVm.CurrentProject.Components.Clear();
                    }

                    // Convert BomLines to Components
                    int newComponents = 0;
                    int updatedComponents = 0;

                    foreach (var bomLine in bomResult.BomLines)
                    {
                        // Find existing component by part number or create new
                        var existing = FindComponentByPartNumber(mainVm.CurrentProject, bomLine.PartNumber);
                        if (existing != null)
                        {
                            // Update existing component
                            existing.Description = bomLine.Description;
                            existing.Manufacturer = bomLine.Manufacturer;
                            existing.ManufacturerPartNumber = bomLine.ManufacturerPartNumber;
                            existing.Value = bomLine.Value;

                            // Merge references
                            foreach (var reference in bomLine.References)
                            {
                                if (!existing.ReferenceDesignators.Contains(reference))
                                {
                                    existing.ReferenceDesignators.Add(reference);
                                }
                            }
                            updatedComponents++;
                        }
                        else
                        {
                            // Create new component
                            var component = new Core.Models.Component
                            {
                                PartNumber = bomLine.PartNumber,
                                Description = bomLine.Description,
                                Manufacturer = bomLine.Manufacturer,
                                ManufacturerPartNumber = bomLine.ManufacturerPartNumber,
                                Value = bomLine.Value
                            };
                            component.ReferenceDesignators = new System.Collections.Generic.List<string>(bomLine.References);

                            mainVm.CurrentProject.Components.Add(component);
                            newComponents++;
                        }
                    }

                    // Auto-assign components to placements by matching reference designators
                    int assignedCount = AssignComponentsToPlacements(mainVm.CurrentProject);

                    // Show warnings if any
                    if (bomResult.Warnings.Count > 0)
                    {
                        var warningMsg = string.Format("{0} warnings during import:\n\n{1}",
                            bomResult.Warnings.Count,
                            string.Join("\n", bomResult.Warnings.Take(10)));

                        if (bomResult.Warnings.Count > 10)
                            warningMsg += string.Format("\n... and {0} more", bomResult.Warnings.Count - 10);

                        MessageBox.Show(warningMsg, "Import Warnings", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }

                    EventAggregator.Instance.Publish(new RequestRefreshEvent { FullRefresh = true });

                    string statusMessage;
                    if (clearedComponents > 0)
                    {
                        statusMessage = string.Format("BOM imported: {0} cleared, {1} new components, {2} total refs, {3} placements linked",
                            clearedComponents, newComponents, bomResult.TotalReferences, assignedCount);
                    }
                    else
                    {
                        statusMessage = string.Format("BOM imported: {0} new, {1} updated, {2} total refs, {3} placements linked",
                            newComponents, updatedComponents, bomResult.TotalReferences, assignedCount);
                    }
                    EventAggregator.Instance.Publish(new StatusMessageEvent { Message = statusMessage });
                }
            }
        }

        private Core.Models.Component FindComponentByPartNumber(Core.Models.Project project, string partNumber)
        {
            if (string.IsNullOrEmpty(partNumber)) return null;

            foreach (var component in project.Components)
            {
                if (string.Equals(component.PartNumber, partNumber, StringComparison.OrdinalIgnoreCase))
                {
                    return component;
                }
            }
            return null;
        }

        /// <summary>
        /// Auto-assign components to placements by matching reference designators
        /// </summary>
        private int AssignComponentsToPlacements(Core.Models.Project project)
        {
            if (project == null) return 0;

            int assignedCount = 0;

            // Build a lookup from reference designator to component
            var refToComponent = new System.Collections.Generic.Dictionary<string, Core.Models.Component>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var component in project.Components)
            {
                if (component.ReferenceDesignators == null) continue;

                foreach (var refDes in component.ReferenceDesignators)
                {
                    if (!string.IsNullOrEmpty(refDes) && !refToComponent.ContainsKey(refDes))
                    {
                        refToComponent[refDes] = component;
                    }
                }
            }

            // Match placements to components
            foreach (var placement in project.Placements)
            {
                if (string.IsNullOrEmpty(placement.Reference)) continue;

                Core.Models.Component component;
                if (refToComponent.TryGetValue(placement.Reference, out component))
                {
                    placement.Component = component;
                    assignedCount++;
                }
            }

            return assignedCount;
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

        private void ShowPcbAreaDialog()
        {
            var mainVm = DataContext as MainViewModel;
            if (mainVm?.CurrentProject == null) return;

            // Pass existing board definition if available
            var dialog = new PcbAreaDialog(mainVm.CurrentProject.Board);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true)
            {
                // Update or create board definition
                if (mainVm.CurrentProject.Board == null)
                {
                    mainVm.CurrentProject.Board = new Core.Models.BoardDefinition();
                }

                mainVm.CurrentProject.Board.Width = dialog.BoardWidth;
                mainVm.CurrentProject.Board.Height = dialog.BoardHeight;
                mainVm.CurrentProject.Board.Origin = new System.Windows.Point(dialog.OriginX, dialog.OriginY);

                EventAggregator.Instance.Publish(new RequestRefreshEvent { FullRefresh = true });
                EventAggregator.Instance.Publish(new StatusMessageEvent
                {
                    Message = string.Format("Board area set to {0}x{1}mm", dialog.BoardWidth, dialog.BoardHeight)
                });
            }
        }

        private void ShowTranslatePlacementsDialog(System.Collections.Generic.List<Core.Models.Placement> placements)
        {
            if (placements == null || placements.Count == 0)
            {
                MessageBox.Show("No placements selected to translate.",
                    "Translate", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new TranslateDialog(placements);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true)
            {
                // Apply translation to placements
                foreach (var p in placements)
                {
                    p.X += dialog.OffsetX;
                    p.Y += dialog.OffsetY;
                }

                EventAggregator.Instance.Publish(new RequestRefreshEvent { FullRefresh = true });
                EventAggregator.Instance.Publish(new StatusMessageEvent
                {
                    Message = string.Format("Translated {0} placements by ({1}, {2})mm",
                        placements.Count, dialog.OffsetX, dialog.OffsetY)
                });
            }
        }
    }
}
