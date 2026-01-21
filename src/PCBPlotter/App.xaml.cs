using System;
using System.Windows;

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

            // Set up global exception handling
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            DispatcherUnhandledException += OnDispatcherUnhandledException;
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
