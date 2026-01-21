using System.Windows;

namespace PCBPlotter.Views
{
    /// <summary>
    /// Floating window for detached views
    /// </summary>
    public partial class FloatingWindow : Window
    {
        private FrameworkElement _originalContent;
        private FrameworkElement _currentContent;

        public FloatingWindow()
        {
            InitializeComponent();
        }

        public void SetContent(FrameworkElement content)
        {
            _originalContent = content;

            // Create a copy or move the content
            if (content.Parent is System.Windows.Controls.Panel panel)
            {
                panel.Children.Remove(content);
            }

            ContentHost.Children.Clear();
            ContentHost.Children.Add(content);
            _currentContent = content;
        }

        public FrameworkElement GetContent()
        {
            return _currentContent;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Return content to original location if needed
            if (_currentContent != null)
            {
                ContentHost.Children.Remove(_currentContent);
            }

            base.OnClosing(e);
        }
    }
}
