using System.Windows;
using PCBPlotter.Services;
using PCBPlotter.ViewModels;

namespace PCBPlotter.Views
{
    /// <summary>
    /// Main application window
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Register with window service
            WindowService.Instance.SetMainWindow(this);

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Create a new empty project on startup
            var vm = DataContext as MainViewModel;
            vm?.NewProjectCommand.Execute(null);
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // TODO: Check for unsaved changes
            WindowService.Instance.CloseAllFloatingWindows();
        }
    }
}
