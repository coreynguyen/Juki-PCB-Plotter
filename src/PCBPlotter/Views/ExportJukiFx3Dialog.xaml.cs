using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PCBPlotter.Core.Models;
using PCBPlotter.Core.Services;

namespace PCBPlotter.Views
{
    /// <summary>
    /// Dialog for exporting to Juki FX-3 format
    /// </summary>
    public partial class ExportJukiFx3Dialog : Window
    {
        private readonly Project _project;
        private readonly JukiFx3Exporter _exporter;

        public ExportJukiFx3Dialog(Project project)
        {
            InitializeComponent();

            _project = project;
            _exporter = new JukiFx3Exporter();

            InitializeFromProject();
            UpdateSummary();
        }

        private void InitializeFromProject()
        {
            if (_project == null) return;

            // Set PWB ID from project name if available
            if (!string.IsNullOrEmpty(_project.Name))
            {
                PwbIdTextBox.Text = _project.Name;
            }

            // Set default output file
            if (!string.IsNullOrEmpty(_project.FilePath))
            {
                string basePath = System.IO.Path.GetDirectoryName(_project.FilePath);
                string baseName = System.IO.Path.GetFileNameWithoutExtension(_project.FilePath);
                OutputFileTextBox.Text = System.IO.Path.Combine(basePath, baseName + ".x01");
            }
            else
            {
                OutputFileTextBox.Text = "export.x01";
            }
        }

        private void UpdateSummary()
        {
            if (_project == null)
            {
                SummaryText.Text = "No project loaded.";
                return;
            }

            var side = ExportSideCombo.SelectedIndex == 0 ? BoardSide.Top : BoardSide.Bottom;
            bool includeSkipped = IncludeSkippedCheckBox.IsChecked == true;

            int placementCount = _project.Placements
                .Count(p => p.Side == side && (includeSkipped || p.EnableForExport));

            int packageCount = _project.Placements
                .Where(p => p.Side == side && (includeSkipped || p.EnableForExport))
                .Where(p => p.Package != null)
                .Select(p => p.Package)
                .Distinct()
                .Count();

            int fiducialCount = _project.Fiducials.Count(f => f.Side == side);

            double boardWidth = _project.Board?.Width ?? 0;
            double boardHeight = _project.Board?.Height ?? 0;

            SummaryText.Text = string.Format(
                "{0} placements, {1} unique components, {2} fiducials\nBoard: {3:F2} x {4:F2} mm",
                placementCount, packageCount, fiducialCount, boardWidth, boardHeight);
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Juki FX-3 Program (*.x01)|*.x01|All Files (*.*)|*.*",
                DefaultExt = ".x01",
                Title = "Export Juki FX-3 Program",
                FileName = System.IO.Path.GetFileName(OutputFileTextBox.Text)
            };

            if (!string.IsNullOrEmpty(OutputFileTextBox.Text))
            {
                string dir = System.IO.Path.GetDirectoryName(OutputFileTextBox.Text);
                if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                {
                    dialog.InitialDirectory = dir;
                }
            }

            if (dialog.ShowDialog() == true)
            {
                OutputFileTextBox.Text = dialog.FileName;
            }
        }

        private void UsePanelCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            PanelSettingsGrid.IsEnabled = UsePanelCheckBox.IsChecked == true;
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate output file
                if (string.IsNullOrWhiteSpace(OutputFileTextBox.Text))
                {
                    MessageBox.Show("Please specify an output file.", "Export Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Build export options
                var options = new JukiFx3Exporter.ExportOptions
                {
                    PwbId = PwbIdTextBox.Text,
                    TargetMachine = (TargetMachineCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "FX-3",
                    RefSide = (RefSideCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "FRONT",
                    TransDir = (TransDirCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "LTOR",
                    PwbThickness = ParseDouble(PwbThicknessTextBox.Text, 1.6),
                    BackHeight = ParseDouble(BackHeightTextBox.Text, 12.0),
                    LayoutPosX = ParseDouble(LayoutOffsetXTextBox.Text, 0.0),
                    LayoutPosY = ParseDouble(LayoutOffsetYTextBox.Text, 0.0),
                    ExportSide = ExportSideCombo.SelectedIndex == 0 ? BoardSide.Top : BoardSide.Bottom,
                    IncludeSkippedPlacements = IncludeSkippedCheckBox.IsChecked == true,
                    UseCircuits = UsePanelCheckBox.IsChecked == true,
                    CircuitCountX = ParseInt(CircuitCountXTextBox.Text, 1),
                    CircuitCountY = ParseInt(CircuitCountYTextBox.Text, 1),
                    CircuitPitchX = ParseDouble(CircuitPitchXTextBox.Text, 0.0),
                    CircuitPitchY = ParseDouble(CircuitPitchYTextBox.Text, 0.0)
                };

                // Export
                _exporter.Export(_project, OutputFileTextBox.Text, options);

                MessageBox.Show(
                    string.Format("Successfully exported to:\n{0}", OutputFileTextBox.Text),
                    "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format("Export failed: {0}", ex.Message),
                    "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private double ParseDouble(string text, double defaultValue)
        {
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double result))
                return result;
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out result))
                return result;
            return defaultValue;
        }

        private int ParseInt(string text, int defaultValue)
        {
            if (int.TryParse(text, out int result))
                return result;
            return defaultValue;
        }
    }
}
