using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
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

        private void ColumnBreakCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var vm = DataContext as TextImportViewModel;
            if (vm == null) return;

            // Get click position in character units (assuming ~7px per character for Consolas 10pt)
            var canvas = sender as Canvas;
            if (canvas == null) return;

            var position = e.GetPosition(canvas);
            int charPosition = (int)(position.X / 7.0); // Approximate character width

            vm.ToggleColumnBreak(charPosition);
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
                // Check if we have a mapping for this column
                if (columnIndex >= vm.ColumnMappings.Count)
                    return;

                var mapping = vm.ColumnMappings[columnIndex];

                // Create a StackPanel with column name and ComboBox
                var stackPanel = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Margin = new Thickness(2)
                };

                // Add original header text if available
                if (!string.IsNullOrEmpty(mapping.HeaderText) && !mapping.HeaderText.StartsWith("Column "))
                {
                    stackPanel.Children.Add(new TextBlock
                    {
                        Text = mapping.HeaderText,
                        FontWeight = FontWeights.SemiBold,
                        FontSize = 10,
                        Margin = new Thickness(0, 0, 0, 2)
                    });
                }

                // Create a ComboBox for field selection
                var comboBox = new ComboBox
                {
                    ItemsSource = vm.AvailableFields,
                    DisplayMemberPath = "DisplayName",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    MinWidth = 80
                };

                // Bind the selected item to the column mapping
                var binding = new Binding("SelectedField")
                {
                    Source = mapping,
                    Mode = BindingMode.TwoWay
                };
                comboBox.SetBinding(ComboBox.SelectedItemProperty, binding);

                stackPanel.Children.Add(comboBox);

                // Set the header to the StackPanel
                e.Column.Header = stackPanel;
                e.Column.MinWidth = 90;
            }
        }
    }
}
