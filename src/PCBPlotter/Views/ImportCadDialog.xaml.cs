using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PCBPlotter.Core.Models;
using PCBPlotter.Core.Services;

namespace PCBPlotter.Views
{
    /// <summary>
    /// Unified dialog for importing CAD data from multiple formats:
    /// - CircuitCAM Express (.cpf, .mdb)
    /// - Allegro Fabmaster (.val, .fab, .va2)
    /// </summary>
    public partial class ImportCadDialog : Window
    {
        private enum CadFormat { Unknown, CircuitCAM, AllegroFabmaster }

        private CadFormat _detectedFormat = CadFormat.Unknown;
        private CpfImporter _cpfImporter;
        private FabmasterImporter _fabmasterImporter;
        private Project _project;

        // Unified placement data for preview
        private List<CadPlacementRecord> _allPlacements = new List<CadPlacementRecord>();
        private List<CadPlacementRecord> _allFiducials = new List<CadPlacementRecord>();

        public List<Package> ImportedPackages { get; private set; }
        public List<Placement> ImportedPlacements { get; private set; }
        public List<Fiducial> ImportedFiducials { get; private set; }
        public BoardDefinition ImportedBoard { get; private set; }

        public ImportCadDialog(Project project)
        {
            InitializeComponent();
            _project = project;
            ImportedPackages = new List<Package>();
            ImportedPlacements = new List<Placement>();
            ImportedFiducials = new List<Fiducial>();
            UpdateSummary();
        }

        public ImportCadDialog(Project project, string sourcePath) : this(project)
        {
            SourcePathTextBox.Text = sourcePath;
            DetectFormat(sourcePath);

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
                Filter = "CAD Files (*.cpf;*.mdb;*.val;*.fab;*.va2)|*.cpf;*.mdb;*.val;*.fab;*.va2|" +
                         "CircuitCAM Express (*.cpf;*.mdb)|*.cpf;*.mdb|" +
                         "Allegro Fabmaster (*.val;*.fab;*.va2)|*.val;*.fab;*.va2|" +
                         "All Files (*.*)|*.*",
                Title = "Select CAD Data File"
            };

            if (dialog.ShowDialog() == true)
            {
                SourcePathTextBox.Text = dialog.FileName;
                DetectFormat(dialog.FileName);
                StatusText.Text = "File selected. Click Parse to load.";
            }
        }

        private void DetectFormat(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            switch (ext)
            {
                case ".cpf":
                case ".mdb":
                    _detectedFormat = CadFormat.CircuitCAM;
                    FormatText.Text = "CircuitCAM Express (.cpf/.mdb)";
                    break;

                case ".val":
                case ".fab":
                case ".va2":
                    _detectedFormat = CadFormat.AllegroFabmaster;
                    FormatText.Text = "Allegro Fabmaster (.val/.fab/.va2)";
                    break;

                default:
                    _detectedFormat = CadFormat.Unknown;
                    FormatText.Text = "(unknown format)";
                    break;
            }
        }

        private void ParseButton_Click(object sender, RoutedEventArgs e)
        {
            string sourcePath = SourcePathTextBox.Text.Trim();
            if (string.IsNullOrEmpty(sourcePath))
            {
                ThemedMessageBox.Show("Please select a CAD file.", "Parse Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning, this);
                return;
            }

            if (_detectedFormat == CadFormat.Unknown)
            {
                DetectFormat(sourcePath);
            }

            if (_detectedFormat == CadFormat.Unknown)
            {
                ThemedMessageBox.Show(
                    "Unable to determine file format. Please select a supported CAD file:\n" +
                    "- CircuitCAM Express (.cpf, .mdb)\n" +
                    "- Allegro Fabmaster (.val, .fab, .va2)",
                    "Format Error", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                return;
            }

            try
            {
                StatusText.Text = "Parsing...";
                Mouse.OverrideCursor = Cursors.Wait;

                _allPlacements.Clear();
                _allFiducials.Clear();

                if (_detectedFormat == CadFormat.CircuitCAM)
                {
                    ParseCircuitCAM(sourcePath);
                }
                else if (_detectedFormat == CadFormat.AllegroFabmaster)
                {
                    ParseFabmaster(sourcePath);
                }

                UpdatePreview();
                UpdateSummary();
            }
            catch (Exception ex)
            {
                StatusText.Text = "Parse failed";
                ThemedMessageBox.Show("Failed to parse file:\n" + ex.Message, "Parse Error",
                    MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void ParseCircuitCAM(string sourcePath)
        {
            _cpfImporter = new CpfImporter();
            _cpfImporter.Parse(sourcePath);

            var data = _cpfImporter.ParsedData;

            // Convert to unified display records
            foreach (var rec in data.SmtPlacements)
            {
                _allPlacements.Add(new CadPlacementRecord
                {
                    RefDes = rec.Ref,
                    X = rec.X * ValorMdb.ValorCoords.MilToMm,
                    Y = rec.Y * ValorMdb.ValorCoords.MilToMm,
                    Rotation = rec.Rot,
                    IsBottom = rec.LayerID == 2,
                    Package = rec.Package,
                    PartNumber = rec.PartNumber
                });
            }

            foreach (var fid in data.Fiducials)
            {
                _allFiducials.Add(new CadPlacementRecord
                {
                    RefDes = fid.Ref,
                    X = fid.X * ValorMdb.ValorCoords.MilToMm,
                    Y = fid.Y * ValorMdb.ValorCoords.MilToMm,
                    Rotation = fid.Rot,
                    IsBottom = fid.LayerID == 2,
                    Package = "",
                    PartNumber = "FIDUCIAL"
                });
            }

            int smtCount = data.SmtPlacements.Count;
            int thtCount = data.ThtComponents.Count;
            int fidCount = data.Fiducials.Count;

            StatusText.Text = string.Format("Parsed: {0} SMT + {1} THT placements, {2} fiducials",
                smtCount, thtCount, fidCount);
            ImportButton.IsEnabled = smtCount > 0;
        }

        private void ParseFabmaster(string sourcePath)
        {
            _fabmasterImporter = new FabmasterImporter();
            _fabmasterImporter.Parse(sourcePath);

            var data = _fabmasterImporter.ParsedData;

            // Convert to unified display records
            foreach (var rec in data.Placements)
            {
                _allPlacements.Add(new CadPlacementRecord
                {
                    RefDes = rec.RefDes,
                    X = rec.X,
                    Y = rec.Y,
                    Rotation = rec.Rotation,
                    IsBottom = rec.IsBottomSide,
                    Package = rec.SymName,
                    PartNumber = rec.DeviceType
                });
            }

            foreach (var fid in data.Fiducials)
            {
                _allFiducials.Add(new CadPlacementRecord
                {
                    RefDes = fid.RefDes,
                    X = fid.X,
                    Y = fid.Y,
                    Rotation = 0,
                    IsBottom = fid.IsBottomSide,
                    Package = "",
                    PartNumber = "FIDUCIAL"
                });
            }

            int plcCount = data.Placements.Count;
            int fidCount = data.Fiducials.Count;

            StatusText.Text = string.Format("Parsed: {0} placements, {1} fiducials",
                plcCount, fidCount);
            ImportButton.IsEnabled = plcCount > 0;
        }

        private void CategoryRadio_Changed(object sender, RoutedEventArgs e)
        {
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (PlacementsGrid == null) return;

            IEnumerable<CadPlacementRecord> records;

            if (ShowFiducialsRadio.IsChecked == true)
                records = _allFiducials;
            else
                records = _allPlacements;

            // Apply side filter
            if (ShowTopRadio.IsChecked == true)
                records = records.Where(r => !r.IsBottom);
            else if (ShowBottomRadio.IsChecked == true)
                records = records.Where(r => r.IsBottom);

            PlacementsGrid.ItemsSource = records.ToList();
        }

        private void UpdateSummary()
        {
            if (SummaryText == null) return;

            if (_detectedFormat == CadFormat.CircuitCAM && _cpfImporter != null)
            {
                SummaryText.Text = _cpfImporter.GetSummary();
            }
            else if (_detectedFormat == CadFormat.AllegroFabmaster && _fabmasterImporter != null)
            {
                SummaryText.Text = _fabmasterImporter.GetSummary();
            }
            else
            {
                SummaryText.Text = "No data loaded.";
            }
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                List<Package> packages;
                List<Placement> placements;
                List<Fiducial> fiducials;
                BoardDefinition board;

                if (_detectedFormat == CadFormat.CircuitCAM)
                {
                    if (_cpfImporter?.ParsedData == null || _cpfImporter.ParsedData.SmtPlacements.Count == 0)
                    {
                        ThemedMessageBox.Show("No data to import.", "Import Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning, this);
                        return;
                    }

                    _cpfImporter.ConvertToProject(_project, out packages, out placements, out fiducials, out board);
                }
                else if (_detectedFormat == CadFormat.AllegroFabmaster)
                {
                    if (_fabmasterImporter?.ParsedData == null || _fabmasterImporter.ParsedData.Placements.Count == 0)
                    {
                        ThemedMessageBox.Show("No data to import.", "Import Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning, this);
                        return;
                    }

                    _fabmasterImporter.ConvertToProject(_project, out packages, out placements, out fiducials, out board);
                }
                else
                {
                    ThemedMessageBox.Show("Unknown format - cannot import.", "Import Error",
                        MessageBoxButton.OK, MessageBoxImage.Error, this);
                    return;
                }

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
    /// Unified display record for CAD placements from any format.
    /// </summary>
    internal class CadPlacementRecord
    {
        public string RefDes { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Rotation { get; set; }
        public bool IsBottom { get; set; }
        public string Package { get; set; }
        public string PartNumber { get; set; }

        public string SideText => IsBottom ? "Bottom" : "Top";
    }
}
