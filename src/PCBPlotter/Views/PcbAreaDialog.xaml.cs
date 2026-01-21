using System.Globalization;
using System.Windows;
using PCBPlotter.Core.Models;

namespace PCBPlotter.Views
{
    public partial class PcbAreaDialog : Window
    {
        public double BoardWidth { get; private set; }
        public double BoardHeight { get; private set; }
        public double OriginX { get; private set; }
        public double OriginY { get; private set; }

        public PcbAreaDialog()
        {
            InitializeComponent();
            WidthTextBox.Focus();
            WidthTextBox.SelectAll();
        }

        public PcbAreaDialog(BoardDefinition board) : this()
        {
            if (board != null)
            {
                WidthTextBox.Text = board.Width.ToString(CultureInfo.InvariantCulture);
                HeightTextBox.Text = board.Height.ToString(CultureInfo.InvariantCulture);
                OriginXTextBox.Text = board.Origin.X.ToString(CultureInfo.InvariantCulture);
                OriginYTextBox.Text = board.Origin.Y.ToString(CultureInfo.InvariantCulture);
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(WidthTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double width) &&
                double.TryParse(HeightTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double height) &&
                double.TryParse(OriginXTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double originX) &&
                double.TryParse(OriginYTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double originY))
            {
                if (width <= 0 || height <= 0)
                {
                    MessageBox.Show("Width and Height must be positive values.",
                        "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                BoardWidth = width;
                BoardHeight = height;
                OriginX = originX;
                OriginY = originY;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Please enter valid numeric values.",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
