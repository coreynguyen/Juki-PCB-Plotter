using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace PCBPlotter.Views
{
    public partial class InputDialog : Window
    {
        public string Value { get; private set; }

        /// <summary>
        /// Optional set of existing values to check for duplicates (case-insensitive).
        /// </summary>
        public HashSet<string> ExistingValues { get; set; }

        public InputDialog(string title, string prompt, string defaultValue = "")
        {
            InitializeComponent();
            Title = title;
            TitleText.Text = title;
            PromptText.Text = prompt;
            InputTextBox.Text = defaultValue;
            InputTextBox.SelectAll();
            InputTextBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var input = InputTextBox.Text?.Trim() ?? "";

            // Check for duplicates if validation set is provided
            if (ExistingValues != null && !string.IsNullOrEmpty(input))
            {
                if (ExistingValues.Contains(input.ToUpperInvariant()))
                {
                    ErrorText.Text = string.Format("Reference '{0}' already exists. Please enter a unique name.", input);
                    ErrorText.Visibility = Visibility.Visible;
                    InputTextBox.SelectAll();
                    InputTextBox.Focus();
                    return;
                }
            }

            Value = input;
            DialogResult = true;
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
                DragMove();
        }
    }
}
