using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PCBPlotter.Services
{
    public enum AppTheme
    {
        System,
        Dark,
        Light
    }

    public class ThemeService
    {
        private static ThemeService _instance;
        public static ThemeService Instance => _instance ?? (_instance = new ThemeService());

        public event EventHandler<bool> SystemThemeChanged;

        private AppTheme _currentPreference = AppTheme.System;
        public AppTheme CurrentPreference
        {
            get => _currentPreference;
            set
            {
                if (_currentPreference != value)
                {
                    _currentPreference = value;
                    ApplyTheme();
                }
            }
        }

        public bool IsSystemDarkTheme => GetWindowsTheme();

        public bool IsDarkThemeActive
        {
            get
            {
                switch (_currentPreference)
                {
                    case AppTheme.Dark:
                        return true;
                    case AppTheme.Light:
                        return false;
                    case AppTheme.System:
                    default:
                        return IsSystemDarkTheme;
                }
            }
        }

        private ThemeService()
        {
            // Listen for Windows theme changes
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General)
            {
                // Windows theme may have changed
                if (_currentPreference == AppTheme.System)
                {
                    ApplyTheme();
                }
                SystemThemeChanged?.Invoke(this, IsSystemDarkTheme);
            }
        }

        private bool GetWindowsTheme()
        {
            try
            {
                // Check Windows 10/11 dark mode setting
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        var value = key.GetValue("AppsUseLightTheme");
                        if (value != null)
                        {
                            return (int)value == 0; // 0 = dark, 1 = light
                        }
                    }
                }
            }
            catch
            {
                // Fallback for older Windows versions or permission issues
            }

            // Default to dark theme if detection fails
            return true;
        }

        public void ApplyTheme()
        {
            var app = System.Windows.Application.Current;
            if (app == null) return;

            var mergedDicts = app.Resources.MergedDictionaries;

            // Remove existing theme dictionaries
            for (int i = mergedDicts.Count - 1; i >= 0; i--)
            {
                var dict = mergedDicts[i];
                if (dict.Source != null &&
                    (dict.Source.OriginalString.Contains("DarkTheme") ||
                     dict.Source.OriginalString.Contains("LightTheme")))
                {
                    mergedDicts.RemoveAt(i);
                }
            }

            // Add the appropriate theme
            var themeUri = IsDarkThemeActive
                ? new Uri("Themes/DarkTheme.xaml", UriKind.Relative)
                : new Uri("Themes/LightTheme.xaml", UriKind.Relative);

            try
            {
                var themeDictionary = new System.Windows.ResourceDictionary { Source = themeUri };
                mergedDicts.Insert(0, themeDictionary);
            }
            catch
            {
                // If light theme doesn't exist yet, keep dark theme
                if (!IsDarkThemeActive)
                {
                    var darkUri = new Uri("Themes/DarkTheme.xaml", UriKind.Relative);
                    var darkDictionary = new System.Windows.ResourceDictionary { Source = darkUri };
                    mergedDicts.Insert(0, darkDictionary);
                }
            }
        }

        public void Cleanup()
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }
    }
}
