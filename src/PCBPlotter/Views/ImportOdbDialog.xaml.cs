using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PCBPlotter.Core.Models;
using PCBPlotter.Core.Services;

namespace PCBPlotter.Views
{
    /// <summary>
    /// Dialog for importing ODB++ CAD data files
    /// </summary>
    public partial class ImportOdbDialog : Window
    {
        private OdbPlusPlusParser _parser;
        private OdbPlusPlusParser.OdbData _parsedData;
        private Project _project;

        public List<Package> ImportedPackages { get; private set; }
        public List<Placement> ImportedPlacements { get; private set; }

        public ImportOdbDialog(Project project)
        {
            InitializeComponent();
            _parser = new OdbPlusPlusParser();
            _project = project;
            ImportedPackages = new List<Package>();
            ImportedPlacements = new List<Placement>();
            UpdateSummary();
        }

        public ImportOdbDialog(Project project, string sourcePath) : this(project)
        {
            SourcePathTextBox.Text = sourcePath;

            // Auto-parse if path provided
            if (!string.IsNullOrEmpty(sourcePath))
            {
                Loaded += (s, e) => ParseButton_Click(null, null);
            }
        }

        private void BrowseFileButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "ODB++ Archives (*.tgz;*.tar.gz;*.tar;*.zip;*.odb)|*.tgz;*.tar.gz;*.tar;*.zip;*.odb|All Files (*.*)|*.*",
                Title = "Select ODB++ Archive"
            };

            if (dialog.ShowDialog() == true)
            {
                SourcePathTextBox.Text = dialog.FileName;
                StatusText.Text = "File selected. Click Parse to load.";
            }
        }

        private void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
        {
            // Use folder browser dialog
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select ODB++ Directory (containing matrix folder)";
                dialog.ShowNewFolderButton = false;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    SourcePathTextBox.Text = dialog.SelectedPath;
                    StatusText.Text = "Folder selected. Click Parse to load.";
                }
            }
        }

        private void ParseButton_Click(object sender, RoutedEventArgs e)
        {
            string sourcePath = SourcePathTextBox.Text.Trim();
            if (string.IsNullOrEmpty(sourcePath))
            {
                ThemedMessageBox.Show("Please select an ODB++ file or folder.", "Parse Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning, this);
                return;
            }

            try
            {
                StatusText.Text = "Parsing...";
                Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

                _parsedData = _parser.Parse(sourcePath);

                StatusText.Text = $"Parsed: {_parsedData.Components.Count} components";
                ImportButton.IsEnabled = _parsedData.Components.Count > 0;

                UpdatePreview();
                UpdateSummary();
            }
            catch (Exception ex)
            {
                StatusText.Text = "Parse failed";
                ThemedMessageBox.Show($"Failed to parse ODB++ data:\n{ex.Message}", "Parse Error",
                    MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void SideRadio_Changed(object sender, RoutedEventArgs e)
        {
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (_parsedData == null)
            {
                ComponentsGrid.ItemsSource = null;
                return;
            }

            IEnumerable<OdbPlusPlusParser.OdbComponent> filtered = _parsedData.Components;

            if (ShowTopRadio.IsChecked == true)
                filtered = _parsedData.TopComponents;
            else if (ShowBottomRadio.IsChecked == true)
                filtered = _parsedData.BottomComponents;

            ComponentsGrid.ItemsSource = filtered.ToList();
        }

        private void UpdateSummary()
        {
            if (_parsedData == null)
            {
                SummaryText.Text = "No data loaded.";
                return;
            }

            int topCount = _parsedData.TopComponents.Count;
            int botCount = _parsedData.BottomComponents.Count;
            int totalPins = _parsedData.Components.Sum(c => c.Pins.Count);
            int netCount = _parsedData.NetNames.Count;

            string boardInfo = "";
            if (_parsedData.BoardWidth > 0 && _parsedData.BoardHeight > 0)
            {
                boardInfo = $"\nBoard: {_parsedData.BoardWidthMm:F2} x {_parsedData.BoardHeightMm:F2} mm";
            }

            string stepsInfo = "";
            if (_parsedData.Steps.Count > 0)
            {
                stepsInfo = $"\nSteps: {string.Join(", ", _parsedData.Steps.Select(s => s.Name))}";
            }

            string layerInfo = "";
            if (_parsedData.Layers.Count > 0)
            {
                int signalLayers = _parsedData.Layers.Count(l => l.LayerType == "SIGNAL");
                layerInfo = $"\nLayers: {_parsedData.Layers.Count} total ({signalLayers} signal)";
            }

            SummaryText.Text = $"Components: {_parsedData.Components.Count} total ({topCount} top, {botCount} bottom)" +
                               $"\nPins: {totalPins} total" +
                               $"\nNets: {netCount}" +
                               boardInfo +
                               stepsInfo +
                               layerInfo;
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_parsedData == null || _parsedData.Components.Count == 0)
            {
                ThemedMessageBox.Show("No data to import.", "Import Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning, this);
                return;
            }

            try
            {
                _parser.ConvertToProject(_parsedData, _project,
                    out List<Package> packages, out List<Placement> placements);

                ImportedPackages = packages;
                ImportedPlacements = placements;

                ThemedMessageBox.Show(
                    $"Import successful!\n\nPackages created: {packages.Count}\nPlacements created: {placements.Count}",
                    "Import Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information,
                    this);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                ThemedMessageBox.Show($"Import failed: {ex.Message}", "Import Error",
                    MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }
    }
}
