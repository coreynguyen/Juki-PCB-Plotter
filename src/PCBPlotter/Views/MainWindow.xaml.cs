using System;
using System.Linq;
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
        // Keep a strong reference to prevent garbage collection
        private readonly Action<ShowDialogEvent> _showDialogHandler;

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

                    EventAggregator.Instance.Publish(new RequestRefreshEvent { FullRefresh = true });
                    EventAggregator.Instance.Publish(new StatusMessageEvent
                    {
                        Message = string.Format("Imported {0} placements", placements.Count)
                    });
                }
                else if (importType == "BomImport")
                {
                    // Import BOM data
                    var bomResult = vm.GetBomData();

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
                    EventAggregator.Instance.Publish(new StatusMessageEvent
                    {
                        Message = string.Format("BOM imported: {0} new, {1} updated, {2} total refs, {3} placements linked",
                            newComponents, updatedComponents, bomResult.TotalReferences, assignedCount)
                    });
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
    }
}
