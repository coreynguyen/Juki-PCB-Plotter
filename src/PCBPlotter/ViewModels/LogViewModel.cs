using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using PCBPlotter.Core.Events;

namespace PCBPlotter.ViewModels
{
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Type { get; set; }
        public string Message { get; set; }
        public string Details { get; set; }
    }

    public class LogViewModel : ViewModelBase
    {
        private ObservableCollection<LogEntry> _logEntries;
        private ICollectionView _logEntriesView;
        private LogEntry _selectedLogEntry;
        private string _filterText;

        public ObservableCollection<LogEntry> LogEntries
        {
            get { return _logEntries; }
            set { SetProperty(ref _logEntries, value); }
        }

        public ICollectionView LogEntriesView
        {
            get { return _logEntriesView; }
            set { SetProperty(ref _logEntriesView, value); }
        }

        public LogEntry SelectedLogEntry
        {
            get { return _selectedLogEntry; }
            set { SetProperty(ref _selectedLogEntry, value); }
        }

        public string FilterText
        {
            get { return _filterText; }
            set
            {
                if (SetProperty(ref _filterText, value))
                {
                    LogEntriesView?.Refresh();
                }
            }
        }

        public int TotalEntries
        {
            get { return LogEntries?.Count ?? 0; }
        }

        public ICommand CopyAllCommand { get; private set; }
        public ICommand ClearLogCommand { get; private set; }

        public LogViewModel()
        {
            LogEntries = new ObservableCollection<LogEntry>();
            LogEntriesView = CollectionViewSource.GetDefaultView(LogEntries);
            LogEntriesView.Filter = FilterLogEntry;

            InitializeCommands();
            SubscribeToEvents();

            // Add welcome message
            AddInfo("PCB Plotter ready. Import PNP or BOM files to get started.");
        }

        private void InitializeCommands()
        {
            CopyAllCommand = new RelayCommand(ExecuteCopyAll, () => LogEntries.Count > 0);
            ClearLogCommand = new RelayCommand(ExecuteClearLog, () => LogEntries.Count > 0);
        }

        private void SubscribeToEvents()
        {
            Subscribe<StatusMessageEvent>(OnStatusMessage);
            Subscribe<LogMessageEvent>(OnLogMessage);
        }

        private bool FilterLogEntry(object obj)
        {
            var entry = obj as LogEntry;
            if (entry == null) return false;

            if (!string.IsNullOrEmpty(FilterText))
            {
                var text = FilterText.ToLower();
                return (entry.Message ?? "").ToLower().Contains(text) ||
                       (entry.Type ?? "").ToLower().Contains(text) ||
                       (entry.Details ?? "").ToLower().Contains(text);
            }

            return true;
        }

        private void ExecuteCopyAll()
        {
            var text = string.Join("\n",
                LogEntries.Select(e => string.Format("{0:HH:mm:ss} [{1}] {2}",
                    e.Timestamp, e.Type, e.Message)));

            try
            {
                Clipboard.SetText(text);
            }
            catch { }
        }

        private void ExecuteClearLog()
        {
            LogEntries.Clear();
            OnPropertyChanged(nameof(TotalEntries));
        }

        public void AddInfo(string message, string details = null)
        {
            AddEntry("Info", message, details);
        }

        public void AddSuccess(string message, string details = null)
        {
            AddEntry("Success", message, details);
        }

        public void AddWarning(string message, string details = null)
        {
            AddEntry("Warning", message, details);
        }

        public void AddError(string message, string details = null)
        {
            AddEntry("Error", message, details);
        }

        private void AddEntry(string type, string message, string details)
        {
            // Ensure we're on the UI thread
            if (!Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(() => AddEntry(type, message, details));
                return;
            }

            LogEntries.Add(new LogEntry
            {
                Timestamp = DateTime.Now,
                Type = type,
                Message = message,
                Details = details
            });

            OnPropertyChanged(nameof(TotalEntries));
        }

        private void OnStatusMessage(StatusMessageEvent e)
        {
            switch (e.Type)
            {
                case StatusMessageType.Error:
                    AddError(e.Message);
                    break;
                case StatusMessageType.Warning:
                    AddWarning(e.Message);
                    break;
                case StatusMessageType.Success:
                    AddSuccess(e.Message);
                    break;
                default:
                    AddInfo(e.Message);
                    break;
            }
        }

        private void OnLogMessage(LogMessageEvent e)
        {
            AddEntry(e.Type, e.Message, e.Details);
        }
    }
}
