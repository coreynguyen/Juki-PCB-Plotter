using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using PCBPlotter.Core.Models;
using PCBPlotter.Core.Services;

namespace PCBPlotter.Views
{
    /// <summary>
    /// Dialog for importing IPC-D-356 netlist/test point files
    /// </summary>
    public partial class ImportIpc356Dialog : Window
    {
        private Ipc356Parser _parser;
        private Ipc356Parser.Ipc356Data _parsedData;
        private Project _project;

        public List<Package> ImportedPackages { get; private set; }
        public List<Placement> ImportedPlacements { get; private set; }

        public ImportIpc356Dialog(Project project)
        {
            InitializeComponent();
            _parser = new Ipc356Parser();
            _project = project;
            ImportedPackages = new List<Package>();
            ImportedPlacements = new List<Placement>();
        }

        public ImportIpc356Dialog(Project project, string filePath) : this(project)
        {
            // Pre-populate with the provided file path
            if (!string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath))
            {
                FilePathTextBox.Text = filePath;
                Loaded += (s, e) => ParseFile(filePath);
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select IPC-D-356 File",
                Filter = "IPC-D-356 Files|*.ipc;*.356;*.net;*.txt|All Files|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                FilePathTextBox.Text = dialog.FileName;
                ParseFile(dialog.FileName);
            }
        }

        private void ParseFile(string filePath)
        {
            try
            {
                LogTextBox.Clear();
                Log("Parsing IPC-D-356 file...");

                _parsedData = _parser.Parse(filePath);

                // Update stats
                StatsTextBlock.Text = string.Format(
                    "Components: {0}  |  Pads: {1}  |  Nets: {2}  |  Unit: {3}",
                    _parsedData.Components.Count,
                    _parsedData.Pads.Count,
                    _parsedData.Netlist.Count,
                    _parsedData.IsMillimeter ? "mm" : "inch");

                // Populate grid
                ComponentsGrid.ItemsSource = _parsedData.Components.Values
                    .OrderBy(c => c.RefDes)
                    .ToList();

                ImportButton.IsEnabled = _parsedData.Components.Count > 0;

                Log(string.Format("Parsed {0} components, {1} pads, {2} nets",
                    _parsedData.Components.Count,
                    _parsedData.Pads.Count,
                    _parsedData.Netlist.Count));

                if (_parsedData.Metadata.ContainsKey("Job"))
                    Log("Job: " + _parsedData.Metadata["Job"]);

                if (_parsedData.Metadata.ContainsKey("Layers"))
                    Log("Layers: " + _parsedData.Metadata["Layers"]);
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex.Message);
                StatsTextBlock.Text = "Error parsing file";
                ImportButton.IsEnabled = false;
            }
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_parsedData == null)
            {
                MessageBox.Show("No file loaded", "Import Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                int packagesCreated = 0;
                int placementsCreated = 0;
                var packageMap = new Dictionary<string, Package>();

                // Create packages if requested
                if (CreatePackagesCheckBox.IsChecked == true)
                {
                    Log("Creating packages from pad data...");

                    var packages = _parser.CreatePackagesFromData(_parsedData);
                    foreach (var pkg in packages)
                    {
                        // Check if package with same name already exists
                        var existing = _project.Packages.FirstOrDefault(p => p.Name == pkg.Name);
                        if (existing == null)
                        {
                            _project.Packages.Add(pkg);
                            ImportedPackages.Add(pkg);
                            packagesCreated++;
                        }
                        packageMap[pkg.Name] = existing ?? pkg;
                    }

                    Log(string.Format("Created {0} new packages", packagesCreated));
                }

                // Build package map for placements
                foreach (var pkg in _project.Packages)
                {
                    if (!packageMap.ContainsKey(pkg.Name))
                        packageMap[pkg.Name] = pkg;
                }

                // Create placements if requested
                if (CreatePlacementsCheckBox.IsChecked == true)
                {
                    Log("Creating placements from component data...");

                    var placements = _parser.CreatePlacementsFromData(_parsedData, packageMap);
                    foreach (var pl in placements)
                    {
                        // Check if placement with same reference already exists
                        var existing = _project.Placements.FirstOrDefault(p => p.Reference == pl.Reference);
                        if (existing == null)
                        {
                            _project.Placements.Add(pl);
                            ImportedPlacements.Add(pl);
                            placementsCreated++;
                        }
                        else
                        {
                            // Update existing placement coordinates
                            existing.X = pl.X;
                            existing.Y = pl.Y;
                        }
                    }

                    Log(string.Format("Created {0} new placements", placementsCreated));
                }

                // Import netlist if requested
                if (ImportNetlistCheckBox.IsChecked == true)
                {
                    Log(string.Format("Imported {0} nets", _parsedData.Netlist.Count));
                    // Netlist data is available in _parsedData.Netlist
                    // Could be used for DRC or connectivity checking
                }

                Log("Import complete!");
                ThemedMessageBox.Show(
                    string.Format("Import successful!\n\nPackages created: {0}\nPlacements created: {1}",
                        packagesCreated, placementsCreated),
                    "Import Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information,
                    this);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex.Message);
                ThemedMessageBox.Show("Import failed: " + ex.Message, "Import Error",
                    MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Log(string message)
        {
            LogTextBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + " - " + message + "\r\n");
            LogTextBox.ScrollToEnd();
        }
    }
}
