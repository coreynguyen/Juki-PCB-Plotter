using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using PCBPlotter.Core.Models;
using PCBPlotter.Core.Utilities;

namespace PCBPlotter.ViewModels
{
    public class ImportField
    {
        public string FieldName { get; set; }
        public string DisplayName { get; set; }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    public class ColumnMapping : ViewModelBase
    {
        private ImportField _selectedField;
        public int ColumnIndex { get; set; }
        public string HeaderText { get; set; }

        public ImportField SelectedField
        {
            get { return _selectedField; }
            set { SetProperty(ref _selectedField, value); }
        }
    }

    /// <summary>
    /// Result of a BOM import with reference expansion
    /// </summary>
    public class BomImportResult
    {
        public List<BomLine> BomLines { get; set; } = new List<BomLine>();
        public List<string> Warnings { get; set; } = new List<string>();
        public int TotalReferences { get; set; }
    }

    public class TextImportViewModel : ViewModelBase
    {
        private string _filePath;
        private string _selectedDelimiter = "Tab";
        private string _selectedEncoding = "UTF-8";
        private int _skipRows = 0;
        private bool _useFirstRowAsHeader = true;
        private bool _mergeDelimiters = false;
        private string _importType = "Placements (PNP)";
        private string _boardSide = "Top";
        private string _units = "Millimeters";
        private string _angleFormat = "Degrees";
        private DataTable _previewData;
        private int _recordCount;
        private ObservableCollection<ColumnMapping> _columnMappings;
        private bool _showPlacementOptions = true;
        private string _importWarnings;

        public string FilePath
        {
            get { return _filePath; }
            set
            {
                if (SetProperty(ref _filePath, value))
                {
                    ParseFile();
                }
            }
        }

        public string SelectedDelimiter
        {
            get { return _selectedDelimiter; }
            set
            {
                if (SetProperty(ref _selectedDelimiter, value))
                {
                    ParseFile();
                }
            }
        }

        public string SelectedEncoding
        {
            get { return _selectedEncoding; }
            set
            {
                if (SetProperty(ref _selectedEncoding, value))
                {
                    ParseFile();
                }
            }
        }

        public int SkipRows
        {
            get { return _skipRows; }
            set
            {
                if (SetProperty(ref _skipRows, Math.Max(0, value)))
                {
                    ParseFile();
                }
            }
        }

        public bool UseFirstRowAsHeader
        {
            get { return _useFirstRowAsHeader; }
            set
            {
                if (SetProperty(ref _useFirstRowAsHeader, value))
                {
                    ParseFile();
                }
            }
        }

        public bool MergeDelimiters
        {
            get { return _mergeDelimiters; }
            set
            {
                if (SetProperty(ref _mergeDelimiters, value))
                {
                    ParseFile();
                }
            }
        }

        public string ImportType
        {
            get { return _importType; }
            set
            {
                if (SetProperty(ref _importType, value))
                {
                    UpdateAvailableFields();
                    ShowPlacementOptions = (value == "Placements (PNP)");
                }
            }
        }

        public string BoardSide
        {
            get { return _boardSide; }
            set { SetProperty(ref _boardSide, value); }
        }

        public string Units
        {
            get { return _units; }
            set { SetProperty(ref _units, value); }
        }

        public string AngleFormat
        {
            get { return _angleFormat; }
            set { SetProperty(ref _angleFormat, value); }
        }

        public DataTable PreviewData
        {
            get { return _previewData; }
            set { SetProperty(ref _previewData, value); }
        }

        public int RecordCount
        {
            get { return _recordCount; }
            set { SetProperty(ref _recordCount, value); }
        }

        public ObservableCollection<ColumnMapping> ColumnMappings
        {
            get { return _columnMappings; }
            set { SetProperty(ref _columnMappings, value); }
        }

        public bool ShowPlacementOptions
        {
            get { return _showPlacementOptions; }
            set { SetProperty(ref _showPlacementOptions, value); }
        }

        public string ImportWarnings
        {
            get { return _importWarnings; }
            set { SetProperty(ref _importWarnings, value); }
        }

        public List<string> Delimiters { get; } = new List<string>
        {
            "Tab", "Comma", "Semicolon", "Space", "Pipe"
        };

        public List<string> Encodings { get; } = new List<string>
        {
            "UTF-8", "ASCII", "Unicode", "UTF-16", "Windows-1252"
        };

        public List<string> BoardSides { get; } = new List<string>
        {
            "Top", "Bottom", "Both (from data)"
        };

        public List<string> UnitOptions { get; } = new List<string>
        {
            "Millimeters", "Mils", "Inches"
        };

        public List<string> AngleFormats { get; } = new List<string>
        {
            "Degrees", "Radians"
        };

        public List<string> ImportTypes { get; } = new List<string>
        {
            "Placements (PNP)", "BOM"
        };

        public ObservableCollection<ImportField> AvailableFields { get; private set; }

        public ICommand BrowseCommand { get; private set; }
        public ICommand ImportCommand { get; private set; }
        public ICommand SaveMappingCommand { get; private set; }

        public TextImportViewModel()
        {
            ColumnMappings = new ObservableCollection<ColumnMapping>();
            AvailableFields = new ObservableCollection<ImportField>();
            UpdateAvailableFields();
            InitializeCommands();
        }

        private void InitializeCommands()
        {
            BrowseCommand = new RelayCommand(ExecuteBrowse);
            ImportCommand = new RelayCommand(ExecuteImport, () => PreviewData != null && PreviewData.Rows.Count > 0);
            SaveMappingCommand = new RelayCommand(ExecuteSaveMapping);
        }

        private void UpdateAvailableFields()
        {
            AvailableFields.Clear();
            AvailableFields.Add(new ImportField { FieldName = "", DisplayName = "(Skip)" });

            if (ImportType == "Placements (PNP)")
            {
                AvailableFields.Add(new ImportField { FieldName = "Reference", DisplayName = "Reference" });
                AvailableFields.Add(new ImportField { FieldName = "X", DisplayName = "X Position" });
                AvailableFields.Add(new ImportField { FieldName = "Y", DisplayName = "Y Position" });
                AvailableFields.Add(new ImportField { FieldName = "Rotation", DisplayName = "Rotation" });
                AvailableFields.Add(new ImportField { FieldName = "Side", DisplayName = "Side (Top/Bottom)" });
                AvailableFields.Add(new ImportField { FieldName = "PartNumber", DisplayName = "Part Number" });
                AvailableFields.Add(new ImportField { FieldName = "PackageName", DisplayName = "Package Name" });
                AvailableFields.Add(new ImportField { FieldName = "Value", DisplayName = "Value" });
                AvailableFields.Add(new ImportField { FieldName = "Description", DisplayName = "Description" });
            }
            else if (ImportType == "BOM")
            {
                AvailableFields.Add(new ImportField { FieldName = "PartNumber", DisplayName = "Part Number" });
                AvailableFields.Add(new ImportField { FieldName = "References", DisplayName = "References" });
                AvailableFields.Add(new ImportField { FieldName = "Value", DisplayName = "Value" });
                AvailableFields.Add(new ImportField { FieldName = "PackageName", DisplayName = "Package" });
                AvailableFields.Add(new ImportField { FieldName = "Manufacturer", DisplayName = "Manufacturer" });
                AvailableFields.Add(new ImportField { FieldName = "MPN", DisplayName = "MPN" });
                AvailableFields.Add(new ImportField { FieldName = "Description", DisplayName = "Description" });
                AvailableFields.Add(new ImportField { FieldName = "Quantity", DisplayName = "Quantity" });
            }

            // Re-detect fields after changing type
            foreach (var mapping in ColumnMappings)
            {
                mapping.SelectedField = DetectField(mapping.HeaderText);
            }
        }

        private LengthUnit GetSelectedUnit()
        {
            switch (Units)
            {
                case "Mils": return LengthUnit.Mils;
                case "Inches": return LengthUnit.Inches;
                default: return LengthUnit.Millimeters;
            }
        }

        private char GetDelimiterChar()
        {
            switch (SelectedDelimiter)
            {
                case "Tab": return '\t';
                case "Comma": return ',';
                case "Semicolon": return ';';
                case "Space": return ' ';
                case "Pipe": return '|';
                default: return '\t';
            }
        }

        private Encoding GetEncoding()
        {
            switch (SelectedEncoding)
            {
                case "UTF-8": return Encoding.UTF8;
                case "ASCII": return Encoding.ASCII;
                case "Unicode": return Encoding.Unicode;
                case "UTF-16": return Encoding.GetEncoding("UTF-16");
                case "Windows-1252": return Encoding.GetEncoding(1252);
                default: return Encoding.UTF8;
            }
        }

        private void ParseFile()
        {
            if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath))
            {
                PreviewData = null;
                RecordCount = 0;
                return;
            }

            try
            {
                var lines = File.ReadAllLines(FilePath, GetEncoding());
                var delimiter = GetDelimiterChar();
                var dataTable = new DataTable();

                // Skip rows (for comments/headers at top of file)
                var dataLines = lines.Skip(SkipRows).ToList();
                if (dataLines.Count == 0)
                {
                    PreviewData = null;
                    RecordCount = 0;
                    return;
                }

                // Parse first data line to determine column count
                var firstLine = dataLines[0];
                var firstFields = ParseLine(firstLine, delimiter);
                var columnCount = firstFields.Length;

                // Create columns
                ColumnMappings.Clear();
                for (int i = 0; i < columnCount; i++)
                {
                    var columnName = UseFirstRowAsHeader ? firstFields[i] : string.Format("Column {0}", i + 1);
                    if (string.IsNullOrWhiteSpace(columnName))
                        columnName = string.Format("Column {0}", i + 1);

                    dataTable.Columns.Add(columnName);

                    var mapping = new ColumnMapping
                    {
                        ColumnIndex = i,
                        HeaderText = columnName,
                        SelectedField = AvailableFields[0]
                    };

                    // Auto-detect field based on column name
                    mapping.SelectedField = DetectField(columnName);
                    ColumnMappings.Add(mapping);
                }

                // Parse data rows
                int startRow = UseFirstRowAsHeader ? 1 : 0;
                int maxPreviewRows = 100;
                int rowCount = 0;

                for (int i = startRow; i < dataLines.Count && rowCount < maxPreviewRows; i++)
                {
                    var fields = ParseLine(dataLines[i], delimiter);
                    if (fields.Length == 0 || (fields.Length == 1 && string.IsNullOrWhiteSpace(fields[0])))
                        continue;

                    var row = dataTable.NewRow();
                    for (int j = 0; j < Math.Min(fields.Length, columnCount); j++)
                    {
                        row[j] = fields[j];
                    }
                    dataTable.Rows.Add(row);
                    rowCount++;
                }

                PreviewData = dataTable;
                RecordCount = UseFirstRowAsHeader ? dataLines.Count - 1 : dataLines.Count;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error parsing file: " + ex.Message, "Parse Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                PreviewData = null;
                RecordCount = 0;
            }
        }

        private string[] ParseLine(string line, char delimiter)
        {
            if (MergeDelimiters)
            {
                return line.Split(new[] { delimiter }, StringSplitOptions.RemoveEmptyEntries);
            }
            return line.Split(delimiter);
        }

        private ImportField DetectField(string columnName)
        {
            if (string.IsNullOrEmpty(columnName))
                return AvailableFields[0];

            columnName = columnName.ToLowerInvariant().Trim();

            // Reference designator (for PNP)
            if (columnName.Contains("ref") || columnName == "designator" || columnName == "part")
                return FindField("Reference");

            // X position
            if (columnName == "x" || columnName.Contains("pos x") || columnName.Contains("x pos") ||
                columnName == "center-x" || columnName == "mid x" || columnName == "posx")
                return FindField("X");

            // Y position
            if (columnName == "y" || columnName.Contains("pos y") || columnName.Contains("y pos") ||
                columnName == "center-y" || columnName == "mid y" || columnName == "posy")
                return FindField("Y");

            // Rotation
            if (columnName.Contains("rot") || columnName.Contains("angle") || columnName.Contains("orient"))
                return FindField("Rotation");

            // Side
            if (columnName.Contains("side") || columnName.Contains("layer") || columnName == "tb" ||
                columnName == "top/bot")
                return FindField("Side");

            // Part number
            if ((columnName.Contains("part") && columnName.Contains("num")) ||
                columnName == "pn" || columnName == "p/n" || columnName == "mfr pn" ||
                columnName == "internal pn" || columnName == "ipn")
                return FindField("PartNumber");

            // Package
            if (columnName.Contains("package") || columnName.Contains("footprint") ||
                columnName.Contains("case") || columnName.Contains("pattern"))
                return FindField("PackageName");

            // Value
            if (columnName == "value" || columnName == "val")
                return FindField("Value");

            // Description
            if (columnName.Contains("desc"))
                return FindField("Description");

            // Manufacturer
            if (columnName.Contains("manuf") || columnName == "mfr" || columnName == "mfg")
                return FindField("Manufacturer");

            // MPN
            if (columnName == "mpn" || columnName.Contains("mfr part") || columnName.Contains("mfg part"))
                return FindField("MPN");

            // Quantity
            if (columnName.Contains("qty") || columnName.Contains("quantity") || columnName == "count")
                return FindField("Quantity");

            // References (for BOM)
            if (columnName.Contains("references") || columnName.Contains("designators") ||
                columnName.Contains("ref des") || columnName == "refs")
                return FindField("References");

            return AvailableFields[0]; // Skip
        }

        private ImportField FindField(string fieldName)
        {
            return AvailableFields.FirstOrDefault(f => f.FieldName == fieldName) ?? AvailableFields[0];
        }

        private void ExecuteBrowse()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Text Files (*.txt;*.csv;*.tsv)|*.txt;*.csv;*.tsv|All Files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                FilePath = dialog.FileName;
            }
        }

        private void ExecuteImport()
        {
            var window = Application.Current.Windows.OfType<Views.TextImportDialog>().FirstOrDefault();
            if (window != null)
            {
                window.DialogResult = true;
                window.Close();
            }
        }

        private void ExecuteSaveMapping()
        {
            MessageBox.Show("Save mapping functionality coming soon.", "Save Mapping",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Gets placements from the imported data with unit conversion
        /// </summary>
        public List<Placement> GetPlacements()
        {
            var placements = new List<Placement>();
            if (PreviewData == null) return placements;

            var refIndex = GetMappedColumnIndex("Reference");
            var xIndex = GetMappedColumnIndex("X");
            var yIndex = GetMappedColumnIndex("Y");
            var rotIndex = GetMappedColumnIndex("Rotation");
            var sideIndex = GetMappedColumnIndex("Side");
            var partIndex = GetMappedColumnIndex("PartNumber");
            var pkgIndex = GetMappedColumnIndex("PackageName");
            var valIndex = GetMappedColumnIndex("Value");

            var inputUnit = GetSelectedUnit();
            var targetUnit = LengthUnit.Millimeters; // App default

            foreach (DataRow row in PreviewData.Rows)
            {
                var placement = new Placement();

                if (refIndex >= 0) placement.Reference = row[refIndex].ToString().Trim();

                // Parse and convert coordinates
                if (xIndex >= 0)
                {
                    double x = ParseDouble(row[xIndex].ToString());
                    placement.X = UnitConverter.Convert(x, inputUnit, targetUnit);
                }

                if (yIndex >= 0)
                {
                    double y = ParseDouble(row[yIndex].ToString());
                    placement.Y = UnitConverter.Convert(y, inputUnit, targetUnit);
                }

                // Parse rotation
                if (rotIndex >= 0)
                {
                    double rot = ParseDouble(row[rotIndex].ToString());
                    if (AngleFormat == "Radians")
                    {
                        rot = rot * 180.0 / Math.PI; // Convert to degrees
                    }
                    placement.Rotation = rot;
                }

                // Parse side
                if (sideIndex >= 0)
                {
                    var sideStr = row[sideIndex].ToString().ToLower();
                    placement.Side = (sideStr.Contains("bot") || sideStr == "b" || sideStr == "bottom")
                        ? BoardSide.Bottom : BoardSide.Top;
                }
                else
                {
                    placement.Side = this.BoardSide == "Bottom"
                        ? BoardSide.Bottom
                        : BoardSide.Top;
                }

                // Optional fields
                if (partIndex >= 0) placement.PartNumber = row[partIndex].ToString().Trim();
                if (pkgIndex >= 0) placement.PackageName = row[pkgIndex].ToString().Trim();

                placements.Add(placement);
            }

            return placements;
        }

        /// <summary>
        /// Gets BOM data from the imported data with reference expansion
        /// </summary>
        public BomImportResult GetBomData()
        {
            var result = new BomImportResult();
            if (PreviewData == null) return result;

            var partIndex = GetMappedColumnIndex("PartNumber");
            var refsIndex = GetMappedColumnIndex("References");
            var valIndex = GetMappedColumnIndex("Value");
            var pkgIndex = GetMappedColumnIndex("PackageName");
            var mfrIndex = GetMappedColumnIndex("Manufacturer");
            var mpnIndex = GetMappedColumnIndex("MPN");
            var descIndex = GetMappedColumnIndex("Description");
            var qtyIndex = GetMappedColumnIndex("Quantity");

            foreach (DataRow row in PreviewData.Rows)
            {
                var bomLine = new BomLine();

                if (partIndex >= 0) bomLine.PartNumber = row[partIndex].ToString().Trim();
                if (valIndex >= 0) bomLine.Value = row[valIndex].ToString().Trim();
                if (pkgIndex >= 0) bomLine.PackageName = row[pkgIndex].ToString().Trim();
                if (mfrIndex >= 0) bomLine.Manufacturer = row[mfrIndex].ToString().Trim();
                if (mpnIndex >= 0) bomLine.ManufacturerPartNumber = row[mpnIndex].ToString().Trim();
                if (descIndex >= 0) bomLine.Description = row[descIndex].ToString().Trim();

                // Expand references
                if (refsIndex >= 0)
                {
                    string refsStr = row[refsIndex].ToString();
                    var expansion = ReferenceExpander.ExpandReferences(refsStr);

                    bomLine.References.AddRange(expansion.References);
                    result.TotalReferences += expansion.References.Count;

                    if (expansion.Warnings.Count > 0)
                    {
                        foreach (var warning in expansion.Warnings)
                        {
                            result.Warnings.Add(string.Format("Line '{0}': {1}", bomLine.PartNumber, warning));
                        }
                    }
                }

                // Parse quantity (optional - can be derived from reference count)
                if (qtyIndex >= 0)
                {
                    if (int.TryParse(row[qtyIndex].ToString(), out int qty))
                    {
                        bomLine.Quantity = qty;
                    }
                }
                else
                {
                    bomLine.Quantity = bomLine.References.Count;
                }

                result.BomLines.Add(bomLine);
            }

            return result;
        }

        private int GetMappedColumnIndex(string fieldName)
        {
            var mapping = ColumnMappings.FirstOrDefault(m =>
                m.SelectedField != null && m.SelectedField.FieldName == fieldName);
            return mapping?.ColumnIndex ?? -1;
        }

        private double ParseDouble(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            value = value.Trim();

            // Remove any unit suffixes
            value = value.Replace("mm", "").Replace("mil", "").Replace("in", "").Trim();

            // Handle common formats
            if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double result))
            {
                return result;
            }

            // Try current culture
            if (double.TryParse(value, out result))
            {
                return result;
            }

            return 0;
        }
    }
}
