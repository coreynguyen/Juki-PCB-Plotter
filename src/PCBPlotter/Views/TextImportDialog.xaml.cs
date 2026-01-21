using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PCBPlotter.ViewModels;

namespace PCBPlotter.Views
{
    public partial class TextImportDialog : Window
    {
        public TextImportDialog()
        {
            InitializeComponent();
        }

        public TextImportDialog(string filePath) : this()
        {
            var vm = DataContext as TextImportViewModel;
            if (vm != null)
            {
                vm.FilePath = filePath;
            }
        }

        private void PreviewDataGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            var vm = DataContext as TextImportViewModel;
            if (vm == null) return;

            // Get the column index from the column name (Column0, Column1, etc.)
            int columnIndex;
            string columnName = e.PropertyName;
            if (columnName.StartsWith("Column") && int.TryParse(columnName.Substring(6), out columnIndex))
            {
                // Ensure we have a mapping for this column
                while (vm.ColumnMappings.Count <= columnIndex)
                {
                    vm.ColumnMappings.Add(new ColumnMapping());
                }

                // Create a ComboBox for the header
                var comboBox = new ComboBox
                {
                    ItemsSource = vm.AvailableFields,
                    DisplayMemberPath = "DisplayName",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0)
                };

                // Bind the selected item to the column mapping
                var binding = new Binding("SelectedField")
                {
                    Source = vm.ColumnMappings[columnIndex],
                    Mode = BindingMode.TwoWay
                };
                comboBox.SetBinding(ComboBox.SelectedItemProperty, binding);

                // Set the header to the ComboBox
                e.Column.Header = comboBox;
                e.Column.MinWidth = 80;
            }
        }
    }
}
