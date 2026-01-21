using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using PCBPlotter.Core.Models;

namespace PCBPlotter.Views
{
    public partial class TranslateDialog : Window
    {
        public double OffsetX { get; private set; }
        public double OffsetY { get; private set; }
        public List<Placement> Placements { get; set; }

        public TranslateDialog()
        {
            InitializeComponent();
            OffsetXTextBox.Focus();
            OffsetXTextBox.SelectAll();
        }

        public TranslateDialog(List<Placement> placements) : this()
        {
            Placements = placements;
            CountLabel.Text = string.Format("{0} placement(s) selected", placements?.Count ?? 0);
        }

        private void TranslateButton_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(OffsetXTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) &&
                double.TryParse(OffsetYTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
            {
                OffsetX = x;
                OffsetY = y;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Please enter valid numeric values for X and Y offsets.",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
