using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PCBPlotter.Core.Models;
using PCBPlotter.Core.Services;
using ValorMdb;

namespace PCBPlotter.Views
{
    /// <summary>
    /// Dialog for importing CircuitCAM Express (.cpf) / Valor CIM (.mdb) files
    /// </summary>
    public partial class ImportCpfDialog : Window
    {
        private CpfImporter _importer;
        private Project _project;

        public List<Package> ImportedPackages { get; private set; }
        public List<Placement> ImportedPlacements { get; private set; }
        public List<Fiducial> ImportedFiducials { get; private set; }
        public BoardDefinition ImportedBoard { get; private set; }

        public ImportCpfDialog(Project project)
        {
            InitializeComponent();
            _importer = new CpfImporter();
            _project = project;
            ImportedPackages = new List<Package>();
            ImportedPlacements = new List<Placement>();
            ImportedFiducials = new List<Fiducial>();
            UpdateSummary();
        }

        public ImportCpfDialog(Project project, string sourcePath) : this(project)
        {
            SourcePathTextBox.Text = sourcePath;

            // Auto-parse if path provided
            if (!string.IsNullOrEmpty(sourcePath))
            {
                Loaded += (s, e) => ParseButton_Click(null, null);
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "CircuitCAM Express Files (*.cpf;*.mdb)|*.cpf;*.mdb|" +
                         "All Files (*.*)|*.*",
                Title = "Select CircuitCAM Express / Valor CIM File"
            };

            if (dialog.ShowDialog() == true)
            {
                SourcePathTextBox.Text = dialog.FileName;
                StatusText.Text = "File selected. Click Parse to load.";
            }
        }

        private void ParseButton_Click(object sender, RoutedEventArgs e)
        {
            string sourcePath = SourcePathTextBox.Text.Trim();
            if (string.IsNullOrEmpty(sourcePath))
            {
                ThemedMessageBox.Show("Please select a .cpf or .mdb file.", "Parse Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning, this);
                return;
            }

            try
            {
                StatusText.Text = "Parsing...";
                Mouse.OverrideCursor = Cursors.Wait;

                _importer.Parse(sourcePath);

                int total = _importer.ParsedData.SmtPlacements.Count +
                            _importer.ParsedData.ThtComponents.Count;
                StatusText.Text = string.Format("Parsed: {0} SMT + {1} THT components, {2} fiducials",
                    _importer.ParsedData.SmtPlacements.Count,
                    _importer.ParsedData.ThtComponents.Count,
                    _importer.ParsedData.Fiducials.Count);
                ImportButton.IsEnabled = _importer.ParsedData.SmtPlacements.Count > 0;

                UpdatePreview();
                UpdateSummary();
            }
            catch (Exception ex)
            {
                StatusText.Text = "Parse failed";
                ThemedMessageBox.Show("Failed to parse CPF/MDB file:\n" + ex.Message, "Parse Error",
                    MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void CategoryRadio_Changed(object sender, RoutedEventArgs e)
        {
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (_importer == null || _importer.ParsedData == null)
            {
                if (PlacementsGrid != null)
                    PlacementsGrid.ItemsSource = null;
                return;
            }

            IEnumerable<PlacementRecord> records;

            if (ShowThtRadio.IsChecked == true)
                records = _importer.ParsedData.ThtComponents;
            else if (ShowFiducialsRadio.IsChecked == true)
                records = _importer.ParsedData.Fiducials;
            else if (ShowOtherRadio.IsChecked == true)
                records = _importer.ParsedData.OtherLocations;
            else
                records = _importer.ParsedData.SmtPlacements;

            // Apply side filter
            if (ShowTopRadio.IsChecked == true)
                records = records.Where(r => r.LayerID == 1);
            else if (ShowBottomRadio.IsChecked == true)
                records = records.Where(r => r.LayerID == 2);

            // Wrap in display models that include mm values
            PlacementsGrid.ItemsSource = records.Select(r => new CpfDisplayRecord(r)).ToList();
        }

        private void UpdateSummary()
        {
            if (SummaryText == null) return;
            SummaryText.Text = _importer?.GetSummary() ?? "No data loaded.";
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_importer.ParsedData == null || _importer.ParsedData.SmtPlacements.Count == 0)
            {
                ThemedMessageBox.Show("No data to import.", "Import Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning, this);
                return;
            }

            try
            {
                List<Package> packages;
                List<Placement> placements;
                List<Fiducial> fiducials;
                BoardDefinition board;

                _importer.ConvertToProject(_project, out packages, out placements, out fiducials, out board);

                ImportedPackages = packages;
                ImportedPlacements = placements;
                ImportedFiducials = fiducials;
                ImportedBoard = board;

                ThemedMessageBox.Show(
                    string.Format("Import successful!\n\n" +
                        "Placements: {0}\n" +
                        "Packages: {1}\n" +
                        "Fiducials: {2}",
                        placements.Count, packages.Count, fiducials.Count),
                    "Import Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information,
                    this);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                ThemedMessageBox.Show("Import failed: " + ex.Message, "Import Error",
                    MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }
    }

    /// <summary>
    /// Display wrapper for PlacementRecord that adds computed mm coordinate properties.
    /// </summary>
    internal class CpfDisplayRecord
    {
        private readonly PlacementRecord _record;

        public CpfDisplayRecord(PlacementRecord record)
        {
            _record = record;
        }

        public string Ref => _record.Ref;
        public double X => _record.X;
        public double Y => _record.Y;
        public double XMm => _record.X * ValorCoords.MilToMm;
        public double YMm => _record.Y * ValorCoords.MilToMm;
        public double Rot => _record.Rot;
        public string Side => _record.Side;
        public string Package => _record.Package;
        public string PartNumber => _record.PartNumber;
    }
}
