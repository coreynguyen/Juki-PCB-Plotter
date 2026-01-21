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
        private bool _useFirstRowAsHeader = false;
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
        private bool _clearExistingOnImport = false;

        // Source selection
        private bool _useFileSource = true;
        private bool _useClipboardSource = false;
        private string _clipboardStatus = "";
        private string _clipboardData = null;

        // Parse mode selection
        private bool _useDelimitedMode = true;
        private bool _useFixedWidthMode = false;
        private string _columnWidths = "";
        private string _sampleLine = "";
        private string _characterRuler = "";
        private ObservableCollection<double> _columnBreakPositions;
        private List<string> _rawLines = new List<string>();

        /// <summary>
        /// If true, clear existing BOM data before importing. Only applies to BOM import.
        /// </summary>
        public bool ClearExistingOnImport
        {
            get { return _clearExistingOnImport; }
            set { SetProperty(ref _clearExistingOnImport, value); }
        }

        #region Source Selection Properties

        public bool UseFileSource
        {
            get { return _useFileSource; }
            set
            {
                if (SetProperty(ref _useFileSource, value))
                {
                    if (value)
                    {
                        _useClipboardSource = false;
                        OnPropertyChanged(nameof(UseClipboardSource));
                        ParseData();
                    }
                }
            }
        }

        public bool UseClipboardSource
        {
            get { return _useClipboardSource; }
            set
            {
                if (SetProperty(ref _useClipboardSource, value))
                {
                    if (value)
                    {
                        _useFileSource = false;
                        OnPropertyChanged(nameof(UseFileSource));
                        ParseData();
                    }
                }
            }
        }

        public string ClipboardStatus
        {
            get { return _clipboardStatus; }
            set { SetProperty(ref _clipboardStatus, value); }
        }

        #endregion

        #region Parse Mode Properties

        public bool UseDelimitedMode
        {
            get { return _useDelimitedMode; }
            set
            {
                if (SetProperty(ref _useDelimitedMode, value))
                {
                    if (value)
                    {
                        _useFixedWidthMode = false;
                        OnPropertyChanged(nameof(UseFixedWidthMode));
                        ParseData();
                    }
                }
            }
        }

        public bool UseFixedWidthMode
        {
            get { return _useFixedWidthMode; }
            set
            {
                if (SetProperty(ref _useFixedWidthMode, value))
                {
                    if (value)
                    {
                        _useDelimitedMode = false;
                        OnPropertyChanged(nameof(UseDelimitedMode));
                        UpdateFixedWidthPreview();
                        ParseData();
                    }
                }
            }
        }

        public string ColumnWidths
        {
            get { return _columnWidths; }
            set
            {
                if (SetProperty(ref _columnWidths, value))
                {
                    UpdateColumnBreaksFromWidths();
                    ParseData();
                }
            }
        }

        public string SampleLine
        {
            get { return _sampleLine; }
            set { SetProperty(ref _sampleLine, value); }
        }

        public string CharacterRuler
        {
            get { return _characterRuler; }
            set { SetProperty(ref _characterRuler, value); }
        }

        public ObservableCollection<double> ColumnBreakPositions
        {
            get { return _columnBreakPositions; }
            set { SetProperty(ref _columnBreakPositions, value); }
        }

        #endregion

        public string FilePath
        {
            get { return _filePath; }
            set
            {
                if (SetProperty(ref _filePath, value))
                {
                    if (_useFileSource)
                    {
                        ParseData();
                    }
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
                    ParseData();
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
                    ParseData();
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
                    ParseData();
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
                    ParseData();
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
                    ParseData();
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
                    RefreshSavedMappingNames();
                    _selectedMappingName = null;
                    OnPropertyChanged(nameof(SelectedMappingName));
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
        public ICommand DeleteMappingCommand { get; private set; }
        public ICommand PasteClipboardCommand { get; private set; }
        public ICommand AutoDetectWidthsCommand { get; private set; }

        private string _selectedMappingName;
        public string SelectedMappingName
        {
            get { return _selectedMappingName; }
            set
            {
                if (SetProperty(ref _selectedMappingName, value) && !string.IsNullOrEmpty(value))
                {
                    LoadMapping(value);
                }
            }
        }

        public ObservableCollection<string> SavedMappingNames { get; } = new ObservableCollection<string>();

        public TextImportViewModel()
        {
            ColumnMappings = new ObservableCollection<ColumnMapping>();
            AvailableFields = new ObservableCollection<ImportField>();
            ColumnBreakPositions = new ObservableCollection<double>();
            UpdateAvailableFields();
            InitializeCommands();
            RefreshSavedMappingNames();
            GenerateCharacterRuler(100);
        }

        private void InitializeCommands()
        {
            BrowseCommand = new RelayCommand(ExecuteBrowse);
            ImportCommand = new RelayCommand(ExecuteImport, () => PreviewData != null && PreviewData.Rows.Count > 0);
            SaveMappingCommand = new RelayCommand(ExecuteSaveMapping);
            DeleteMappingCommand = new RelayCommand(ExecuteDeleteMapping, () => !string.IsNullOrEmpty(SelectedMappingName));
            PasteClipboardCommand = new RelayCommand(ExecutePasteClipboard);
            AutoDetectWidthsCommand = new RelayCommand(ExecuteAutoDetectWidths, () => _rawLines.Count > 0);
        }

        private void RefreshSavedMappingNames()
        {
            SavedMappingNames.Clear();
            bool isBom = (ImportType == "BOM");
            foreach (var name in Services.AppSettings.Instance.GetSavedMappingNames(isBom))
            {
                SavedMappingNames.Add(name);
            }
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

        private void ExecutePasteClipboard()
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    _clipboardData = Clipboard.GetText();
                    var lineCount = _clipboardData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
                    ClipboardStatus = string.Format("Pasted {0} lines", lineCount);
                    ParseData();
                }
                else
                {
                    ClipboardStatus = "Clipboard is empty or contains no text";
                }
            }
            catch (Exception ex)
            {
                ClipboardStatus = "Error: " + ex.Message;
            }
        }

        private void ExecuteAutoDetectWidths()
        {
            if (_rawLines.Count == 0) return;

            try
            {
                // Analyze lines to detect column boundaries based on whitespace patterns
                var widths = DetectColumnWidths(_rawLines);
                if (widths.Count > 0)
                {
                    ColumnWidths = string.Join(",", widths);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not auto-detect column widths: " + ex.Message,
                    "Auto-Detect", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private List<int> DetectColumnWidths(List<string> lines)
        {
            var widths = new List<int>();
            if (lines.Count == 0) return widths;

            // Find the longest line
            int maxLen = lines.Max(l => l.Length);
            if (maxLen == 0) return widths;

            // Create a histogram of whitespace positions
            var whitespaceCount = new int[maxLen];
            int lineCount = Math.Min(lines.Count, 20); // Sample first 20 lines

            for (int i = 0; i < lineCount; i++)
            {
                var line = lines[i];
                for (int j = 0; j < line.Length; j++)
                {
                    if (char.IsWhiteSpace(line[j]) && j > 0 && !char.IsWhiteSpace(line[j - 1]))
                    {
                        // Transition from non-whitespace to whitespace
                        whitespaceCount[j]++;
                    }
                }
            }

            // Find positions where most lines have whitespace transitions
            int threshold = lineCount / 2;
            int lastBreak = 0;

            for (int i = 1; i < maxLen; i++)
            {
                if (whitespaceCount[i] >= threshold && i - lastBreak >= 3)
                {
                    widths.Add(i - lastBreak);
                    lastBreak = i;
                }
            }

            // Add final column width
            if (lastBreak < maxLen)
            {
                widths.Add(maxLen - lastBreak);
            }

            return widths;
        }

        private void GenerateCharacterRuler(int length)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < length; i++)
            {
                if (i % 10 == 0)
                    sb.Append((i / 10) % 10);
                else if (i % 5 == 0)
                    sb.Append('+');
                else
                    sb.Append('-');
            }
            CharacterRuler = sb.ToString();
        }

        private void UpdateFixedWidthPreview()
        {
            if (_rawLines.Count > 0)
            {
                // Use first non-skipped line as sample
                int skipCount = Math.Min(SkipRows, _rawLines.Count - 1);
                SampleLine = _rawLines.Count > skipCount ? _rawLines[skipCount] : "";
                GenerateCharacterRuler(Math.Max(100, SampleLine.Length + 10));
            }
        }

        private void UpdateColumnBreaksFromWidths()
        {
            ColumnBreakPositions.Clear();

            if (string.IsNullOrWhiteSpace(_columnWidths)) return;

            var widthStrings = _columnWidths.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int position = 0;
            const double charWidth = 7.0; // Approximate pixel width per character

            foreach (var ws in widthStrings)
            {
                if (int.TryParse(ws.Trim(), out int width))
                {
                    position += width;
                    ColumnBreakPositions.Add(position * charWidth);
                }
            }
        }

        public void ToggleColumnBreak(int charPosition)
        {
            const double charWidth = 7.0;
            double pixelPosition = charPosition * charWidth;

            // Check if there's already a break near this position
            var existing = ColumnBreakPositions.FirstOrDefault(p => Math.Abs(p - pixelPosition) < charWidth * 2);
            if (existing > 0)
            {
                ColumnBreakPositions.Remove(existing);
            }
            else
            {
                // Add new break
                var sortedPositions = ColumnBreakPositions.ToList();
                sortedPositions.Add(pixelPosition);
                sortedPositions.Sort();
                ColumnBreakPositions.Clear();
                foreach (var p in sortedPositions)
                {
                    ColumnBreakPositions.Add(p);
                }
            }

            // Update column widths string from breaks
            UpdateWidthsFromBreaks();
            ParseData();
        }

        private void UpdateWidthsFromBreaks()
        {
            const double charWidth = 7.0;
            var widths = new List<int>();
            int lastPos = 0;

            foreach (var pos in ColumnBreakPositions.OrderBy(p => p))
            {
                int charPos = (int)(pos / charWidth);
                if (charPos > lastPos)
                {
                    widths.Add(charPos - lastPos);
                    lastPos = charPos;
                }
            }

            // Don't add trailing column automatically - let it capture rest of line
            _columnWidths = string.Join(",", widths);
            OnPropertyChanged(nameof(ColumnWidths));
        }

        private List<int> GetColumnWidthsList()
        {
            var widths = new List<int>();
            if (string.IsNullOrWhiteSpace(_columnWidths)) return widths;

            var parts = _columnWidths.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                if (int.TryParse(p.Trim(), out int w) && w > 0)
                {
                    widths.Add(w);
                }
            }
            return widths;
        }

        private void ParseData()
        {
            // Load raw lines from source
            _rawLines.Clear();

            if (_useFileSource)
            {
                if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath))
                {
                    PreviewData = null;
                    RecordCount = 0;
                    return;
                }

                try
                {
                    _rawLines = File.ReadAllLines(FilePath, GetEncoding()).ToList();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error reading file: " + ex.Message, "Read Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    PreviewData = null;
                    RecordCount = 0;
                    return;
                }
            }
            else if (_useClipboardSource)
            {
                if (string.IsNullOrEmpty(_clipboardData))
                {
                    PreviewData = null;
                    RecordCount = 0;
                    return;
                }

                _rawLines = _clipboardData.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).ToList();
            }

            if (_rawLines.Count == 0)
            {
                PreviewData = null;
                RecordCount = 0;
                return;
            }

            // Update fixed-width preview if in that mode
            if (_useFixedWidthMode)
            {
                UpdateFixedWidthPreview();
            }

            // Parse based on mode
            if (_useDelimitedMode)
            {
                ParseDelimited();
            }
            else
            {
                ParseFixedWidth();
            }
        }

        private void ParseDelimited()
        {
            try
            {
                var delimiter = GetDelimiterChar();
                var dataTable = new DataTable();

                // Skip rows (for comments/headers at top of file)
                var dataLines = _rawLines.Skip(SkipRows).ToList();
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

                // Create columns - use consistent 0-indexed naming for DataGrid binding
                ColumnMappings.Clear();
                for (int i = 0; i < columnCount; i++)
                {
                    var displayName = UseFirstRowAsHeader ? firstFields[i] : "";
                    if (string.IsNullOrWhiteSpace(displayName))
                        displayName = string.Format("Column {0}", i + 1);

                    // Always use Column0, Column1, etc. for DataTable column names (for consistent binding)
                    // The display name will be shown in tooltips
                    dataTable.Columns.Add(string.Format("Column{0}", i));

                    var mapping = new ColumnMapping
                    {
                        ColumnIndex = i,
                        HeaderText = displayName,
                        SelectedField = AvailableFields[0]
                    };

                    // Auto-detect field based on header name
                    mapping.SelectedField = DetectField(displayName);
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
                MessageBox.Show("Error parsing data: " + ex.Message, "Parse Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                PreviewData = null;
                RecordCount = 0;
            }
        }

        private void ParseFixedWidth()
        {
            try
            {
                var widths = GetColumnWidthsList();
                if (widths.Count == 0)
                {
                    // No widths defined - show a single column with all data
                    widths.Add(1000); // Large enough to capture entire line
                }

                var dataTable = new DataTable();

                // Skip rows (for comments/headers at top of file)
                var dataLines = _rawLines.Skip(SkipRows).ToList();
                if (dataLines.Count == 0)
                {
                    PreviewData = null;
                    RecordCount = 0;
                    return;
                }

                // Parse first data line to get column headers (if using first row as header)
                var firstLine = dataLines[0];
                var firstFields = SplitByWidths(firstLine, widths);
                var columnCount = firstFields.Length;

                // Create columns
                ColumnMappings.Clear();
                for (int i = 0; i < columnCount; i++)
                {
                    var displayName = UseFirstRowAsHeader ? firstFields[i].Trim() : "";
                    if (string.IsNullOrWhiteSpace(displayName))
                        displayName = string.Format("Column {0}", i + 1);

                    dataTable.Columns.Add(string.Format("Column{0}", i));

                    var mapping = new ColumnMapping
                    {
                        ColumnIndex = i,
                        HeaderText = displayName,
                        SelectedField = AvailableFields[0]
                    };

                    mapping.SelectedField = DetectField(displayName);
                    ColumnMappings.Add(mapping);
                }

                // Parse data rows
                int startRow = UseFirstRowAsHeader ? 1 : 0;
                int maxPreviewRows = 100;
                int rowCount = 0;

                for (int i = startRow; i < dataLines.Count && rowCount < maxPreviewRows; i++)
                {
                    var fields = SplitByWidths(dataLines[i], widths);
                    if (fields.Length == 0 || (fields.Length == 1 && string.IsNullOrWhiteSpace(fields[0])))
                        continue;

                    var row = dataTable.NewRow();
                    for (int j = 0; j < Math.Min(fields.Length, columnCount); j++)
                    {
                        row[j] = fields[j].Trim();
                    }
                    dataTable.Rows.Add(row);
                    rowCount++;
                }

                PreviewData = dataTable;
                RecordCount = UseFirstRowAsHeader ? dataLines.Count - 1 : dataLines.Count;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error parsing fixed-width data: " + ex.Message, "Parse Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                PreviewData = null;
                RecordCount = 0;
            }
        }

        private string[] SplitByWidths(string line, List<int> widths)
        {
            var fields = new List<string>();
            int position = 0;

            for (int i = 0; i < widths.Count; i++)
            {
                int width = widths[i];
                if (position >= line.Length)
                {
                    fields.Add("");
                }
                else if (position + width > line.Length)
                {
                    fields.Add(line.Substring(position));
                }
                else
                {
                    fields.Add(line.Substring(position, width));
                }
                position += width;
            }

            // If there's remaining text after the last defined width, capture it
            if (position < line.Length)
            {
                fields.Add(line.Substring(position));
            }

            return fields.ToArray();
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
            bool isBom = (ImportType == "BOM");

            // Use a dialog to get the name or default if none provided
            string defaultName = SelectedMappingName;
            if (string.IsNullOrWhiteSpace(defaultName))
            {
                defaultName = Path.GetFileNameWithoutExtension(FilePath ?? "Mapping");
            }

            // Prompt for name - set owner to the active window (TextImportDialog)
            var inputDialog = new Views.InputDialog("Save Mapping", "Enter a name for this mapping:", defaultName);
            inputDialog.Owner = System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().FirstOrDefault(w => w.IsActive);
            if (inputDialog.ShowDialog() != true)
                return;

            var mappingName = inputDialog.Value;
            if (string.IsNullOrWhiteSpace(mappingName))
                return;

            // Collect the field names from current mappings
            var fieldNames = ColumnMappings
                .Select(m => m.SelectedField?.FieldName ?? "")
                .ToList();

            // Save to settings
            Services.AppSettings.Instance.SaveColumnMapping(mappingName, fieldNames, isBom);

            // Refresh dropdown and select the saved mapping
            RefreshSavedMappingNames();
            _selectedMappingName = mappingName;
            OnPropertyChanged(nameof(SelectedMappingName));
        }

        private void ExecuteDeleteMapping()
        {
            if (string.IsNullOrEmpty(SelectedMappingName))
                return;

            bool isBom = (ImportType == "BOM");
            Services.AppSettings.Instance.DeleteColumnMapping(SelectedMappingName, isBom);

            RefreshSavedMappingNames();
            _selectedMappingName = null;
            OnPropertyChanged(nameof(SelectedMappingName));
        }

        public void LoadMapping(string name)
        {
            bool isBom = (ImportType == "BOM");
            var fieldNames = Services.AppSettings.Instance.LoadColumnMapping(name, isBom);
            if (fieldNames == null) return;

            for (int i = 0; i < Math.Min(fieldNames.Count, ColumnMappings.Count); i++)
            {
                var field = AvailableFields.FirstOrDefault(f => f.FieldName == fieldNames[i]);
                if (field != null)
                {
                    ColumnMappings[i].SelectedField = field;
                }
            }
        }

        /// <summary>
        /// Gets all data rows (not just preview), parsed according to current settings
        /// </summary>
        private DataTable GetFullDataTable()
        {
            if (_rawLines.Count == 0) return null;

            var dataTable = new DataTable();
            var dataLines = _rawLines.Skip(SkipRows).ToList();
            if (dataLines.Count == 0) return null;

            // Get column count from mappings
            int columnCount = ColumnMappings.Count;
            if (columnCount == 0) return null;

            for (int i = 0; i < columnCount; i++)
            {
                dataTable.Columns.Add(string.Format("Column{0}", i));
            }

            int startRow = UseFirstRowAsHeader ? 1 : 0;

            if (_useDelimitedMode)
            {
                var delimiter = GetDelimiterChar();
                for (int i = startRow; i < dataLines.Count; i++)
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
                }
            }
            else
            {
                var widths = GetColumnWidthsList();
                if (widths.Count == 0) widths.Add(1000);

                for (int i = startRow; i < dataLines.Count; i++)
                {
                    var fields = SplitByWidths(dataLines[i], widths);
                    if (fields.Length == 0 || (fields.Length == 1 && string.IsNullOrWhiteSpace(fields[0])))
                        continue;

                    var row = dataTable.NewRow();
                    for (int j = 0; j < Math.Min(fields.Length, columnCount); j++)
                    {
                        row[j] = fields[j].Trim();
                    }
                    dataTable.Rows.Add(row);
                }
            }

            return dataTable;
        }

        /// <summary>
        /// Gets placements from the imported data with unit conversion
        /// </summary>
        public List<Placement> GetPlacements()
        {
            var placements = new List<Placement>();

            // Get full data, not just preview
            var fullData = GetFullDataTable();
            if (fullData == null || fullData.Rows.Count == 0) return placements;

            var refIndex = GetMappedColumnIndex("Reference");
            var xIndex = GetMappedColumnIndex("X");
            var yIndex = GetMappedColumnIndex("Y");
            var rotIndex = GetMappedColumnIndex("Rotation");
            var sideIndex = GetMappedColumnIndex("Side");

            var inputUnit = GetSelectedUnit();
            var targetUnit = LengthUnit.Millimeters; // App default

            // Smart side detection: collect unique side values first
            // First unique value = Top (Side 1), Second unique value = Bottom (Side 2)
            string side1Value = null;
            string side2Value = null;
            if (sideIndex >= 0)
            {
                foreach (DataRow row in fullData.Rows)
                {
                    var sideStr = row[sideIndex].ToString().Trim().ToLowerInvariant();
                    if (string.IsNullOrEmpty(sideStr)) continue;

                    if (side1Value == null)
                    {
                        side1Value = sideStr;
                    }
                    else if (side2Value == null && sideStr != side1Value)
                    {
                        side2Value = sideStr;
                        break; // Found both sides
                    }
                }
            }

            foreach (DataRow row in fullData.Rows)
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

                // Smart side detection: first unique value = Top, second = Bottom
                if (sideIndex >= 0)
                {
                    var sideStr = row[sideIndex].ToString().Trim().ToLowerInvariant();
                    if (sideStr == side2Value)
                    {
                        placement.Side = Core.Models.BoardSide.Bottom;
                    }
                    else
                    {
                        placement.Side = Core.Models.BoardSide.Top;
                    }
                }
                else
                {
                    // No side column - default to Top
                    placement.Side = Core.Models.BoardSide.Top;
                }

                // Note: PartNumber and PackageName are stored in Component/Package objects
                // These will be linked via BOM import or manually after placement import

                // Validate: skip placements with empty reference or no valid coordinates
                if (string.IsNullOrWhiteSpace(placement.Reference))
                    continue;

                // Skip if both X and Y are exactly 0 and there's no reference (likely empty row)
                if (placement.X == 0 && placement.Y == 0 && refIndex < 0)
                    continue;

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

            // Get full data, not just preview
            var fullData = GetFullDataTable();
            if (fullData == null || fullData.Rows.Count == 0) return result;

            var partIndex = GetMappedColumnIndex("PartNumber");
            var refsIndex = GetMappedColumnIndex("References");
            var valIndex = GetMappedColumnIndex("Value");
            var pkgIndex = GetMappedColumnIndex("PackageName");
            var mfrIndex = GetMappedColumnIndex("Manufacturer");
            var mpnIndex = GetMappedColumnIndex("MPN");
            var descIndex = GetMappedColumnIndex("Description");
            var qtyIndex = GetMappedColumnIndex("Quantity");

            foreach (DataRow row in fullData.Rows)
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
