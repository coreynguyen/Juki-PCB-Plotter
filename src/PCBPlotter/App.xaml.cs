using System;
using System.Windows;
using PCBPlotter.Services;

namespace PCBPlotter
{
    /// <summary>
    /// Application entry point
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Apply theme based on settings/system preference
            ApplyTheme();

            // Set up global exception handling
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            DispatcherUnhandledException += OnDispatcherUnhandledException;
        }

        private void ApplyTheme()
        {
            bool useDarkTheme = AppSettings.Instance.ShouldUseDarkTheme();

            // Get the Controls.xaml resource dictionary
            var controlsDict = Resources.MergedDictionaries.Count > 0
                ? Resources.MergedDictionaries[0]
                : null;

            if (controlsDict == null) return;

            // Update the theme colors based on dark/light mode
            if (useDarkTheme)
            {
                // Dark theme colors (already default in Controls.xaml)
                // These are the existing dark theme values
            }
            else
            {
                // Light theme colors - override the dark defaults
                controlsDict["BackgroundDarkBrush"] = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(240, 240, 240));
                controlsDict["BackgroundMediumBrush"] = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(250, 250, 250));
                controlsDict["BackgroundLightBrush"] = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(255, 255, 255));
                controlsDict["ForegroundPrimaryBrush"] = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(30, 30, 30));
                controlsDict["ForegroundSecondaryBrush"] = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(80, 80, 80));
                controlsDict["ForegroundDisabledBrush"] = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(160, 160, 160));
                controlsDict["BorderDarkBrush"] = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(200, 200, 200));
                controlsDict["BorderLightBrush"] = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(220, 220, 220));
            }
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            MessageBox.Show(
                "An unexpected error occurred:\n\n" + (exception?.Message ?? "Unknown error"),
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }

        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                "An unexpected error occurred:\n\n" + e.Exception.Message,
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            e.Handled = true;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Cleanup
            Services.WindowService.Instance.CloseAllFloatingWindows();
            base.OnExit(e);
        }
    }
}
