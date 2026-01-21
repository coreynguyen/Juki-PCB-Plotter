using System.Windows;

namespace PCBPlotter.Views
{
    public partial class InputDialog : Window
    {
        public string Value { get; private set; }

        public InputDialog(string title, string prompt, string defaultValue = "")
        {
            InitializeComponent();
            Title = title;
            PromptText.Text = prompt;
            InputTextBox.Text = defaultValue;
            InputTextBox.SelectAll();
            InputTextBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Value = InputTextBox.Text;
            DialogResult = true;
            Close();
        }
    }
}
