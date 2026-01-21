using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace PCBPlotter.Services
{
    /// <summary>
    /// Application settings that persist between sessions using simple INI-style storage
    /// </summary>
    public class AppSettings
    {
        private static readonly string SettingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PCBPlotter");

        private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.ini");
        private static readonly string MappingsFile = Path.Combine(SettingsFolder, "mappings.ini");

        private static AppSettings _instance;
        public static AppSettings Instance => _instance ?? (_instance = Load());

        // General settings
        public bool StartOnStartScreen { get; set; } = true;
        public bool RememberLastProject { get; set; } = true;
        public bool CheckForUpdates { get; set; } = false;
        public string LastProjectPath { get; set; } = "";
        public List<string> RecentProjects { get; set; } = new List<string>();

        // Theme settings
        public string Theme { get; set; } = "System"; // Dark, Light, System

        // Units settings
        public string DefaultLengthUnit { get; set; } = "mm";
        public string DefaultAngleUnit { get; set; } = "Degrees";
        public int DecimalPlaces { get; set; } = 3;

        // Export settings
        public string DefaultExportFormat { get; set; } = "Juki JX-100";
        public bool IncludeCsvHeaders { get; set; } = true;
        public bool AutoOpenExportFolder { get; set; } = false;
        public string DefaultExportPath { get; set; } = "";

        // Display settings
        public bool ShowGridByDefault { get; set; } = true;
        public bool ShowLabelsByDefault { get; set; } = true;
        public double DefaultGridSpacing { get; set; } = 1.0;

        // Column mappings for text import
        public Dictionary<string, List<string>> SavedMappings { get; set; } = new Dictionary<string, List<string>>();

        /// <summary>
        /// Detects if Windows is using dark mode
        /// </summary>
        public static bool IsSystemDarkMode()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        var value = key.GetValue("AppsUseLightTheme");
                        if (value != null)
                        {
                            return (int)value == 0; // 0 = dark mode, 1 = light mode
                        }
                    }
                }
            }
            catch
            {
                // Default to dark if we can't read the registry
            }
            return true; // Default to dark mode
        }

        /// <summary>
        /// Returns true if dark theme should be used based on settings
        /// </summary>
        public bool ShouldUseDarkTheme()
        {
            if (Theme == "Dark") return true;
            if (Theme == "Light") return false;
            // "System" - detect from Windows
            return IsSystemDarkMode();
        }

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var lines = File.ReadAllLines(SettingsFile);
                    foreach (var line in lines)
                    {
                        var parts = line.Split(new[] { '=' }, 2);
                        if (parts.Length != 2) continue;

                        var key = parts[0].Trim();
                        var value = parts[1].Trim();

                        switch (key)
                        {
                            case "StartOnStartScreen": settings.StartOnStartScreen = value == "true"; break;
                            case "RememberLastProject": settings.RememberLastProject = value == "true"; break;
                            case "CheckForUpdates": settings.CheckForUpdates = value == "true"; break;
                            case "LastProjectPath": settings.LastProjectPath = value; break;
                            case "Theme": settings.Theme = value; break;
                            case "DefaultLengthUnit": settings.DefaultLengthUnit = value; break;
                            case "DefaultAngleUnit": settings.DefaultAngleUnit = value; break;
                            case "DecimalPlaces": int.TryParse(value, out int dp); settings.DecimalPlaces = dp; break;
                            case "DefaultExportFormat": settings.DefaultExportFormat = value; break;
                            case "IncludeCsvHeaders": settings.IncludeCsvHeaders = value == "true"; break;
                            case "AutoOpenExportFolder": settings.AutoOpenExportFolder = value == "true"; break;
                            case "DefaultExportPath": settings.DefaultExportPath = value; break;
                            case "ShowGridByDefault": settings.ShowGridByDefault = value == "true"; break;
                            case "ShowLabelsByDefault": settings.ShowLabelsByDefault = value == "true"; break;
                            case "DefaultGridSpacing": double.TryParse(value, out double gs); settings.DefaultGridSpacing = gs; break;
                            case "RecentProjects": settings.RecentProjects = value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).ToList(); break;
                        }
                    }
                }

                // Load mappings
                if (File.Exists(MappingsFile))
                {
                    var lines = File.ReadAllLines(MappingsFile);
                    foreach (var line in lines)
                    {
                        var parts = line.Split(new[] { '=' }, 2);
                        if (parts.Length == 2)
                        {
                            var name = parts[0].Trim();
                            var fields = parts[1].Split(new[] { '|' }, StringSplitOptions.None).ToList();
                            settings.SavedMappings[name] = fields;
                        }
                    }
                }
            }
            catch
            {
                // Return default settings on error
            }

            return settings;
        }

        public void Save()
        {
            try
            {
                if (!Directory.Exists(SettingsFolder))
                {
                    Directory.CreateDirectory(SettingsFolder);
                }

                var sb = new StringBuilder();
                sb.AppendLine($"StartOnStartScreen={StartOnStartScreen.ToString().ToLower()}");
                sb.AppendLine($"RememberLastProject={RememberLastProject.ToString().ToLower()}");
                sb.AppendLine($"CheckForUpdates={CheckForUpdates.ToString().ToLower()}");
                sb.AppendLine($"LastProjectPath={LastProjectPath}");
                sb.AppendLine($"Theme={Theme}");
                sb.AppendLine($"DefaultLengthUnit={DefaultLengthUnit}");
                sb.AppendLine($"DefaultAngleUnit={DefaultAngleUnit}");
                sb.AppendLine($"DecimalPlaces={DecimalPlaces}");
                sb.AppendLine($"DefaultExportFormat={DefaultExportFormat}");
                sb.AppendLine($"IncludeCsvHeaders={IncludeCsvHeaders.ToString().ToLower()}");
                sb.AppendLine($"AutoOpenExportFolder={AutoOpenExportFolder.ToString().ToLower()}");
                sb.AppendLine($"DefaultExportPath={DefaultExportPath}");
                sb.AppendLine($"ShowGridByDefault={ShowGridByDefault.ToString().ToLower()}");
                sb.AppendLine($"ShowLabelsByDefault={ShowLabelsByDefault.ToString().ToLower()}");
                sb.AppendLine($"DefaultGridSpacing={DefaultGridSpacing}");
                sb.AppendLine($"RecentProjects={string.Join("|", RecentProjects)}");

                File.WriteAllText(SettingsFile, sb.ToString());

                // Save mappings
                var mappingSb = new StringBuilder();
                foreach (var kvp in SavedMappings)
                {
                    mappingSb.AppendLine($"{kvp.Key}={string.Join("|", kvp.Value)}");
                }
                File.WriteAllText(MappingsFile, mappingSb.ToString());
            }
            catch
            {
                // Silently fail
            }
        }

        public void AddRecentProject(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            RecentProjects.Remove(path);
            RecentProjects.Insert(0, path);

            if (RecentProjects.Count > 10)
            {
                RecentProjects.RemoveRange(10, RecentProjects.Count - 10);
            }

            LastProjectPath = path;
            Save();
        }

        public void SaveColumnMapping(string name, List<string> fieldNames)
        {
            SavedMappings[name] = fieldNames;
            Save();
        }

        public List<string> LoadColumnMapping(string name)
        {
            if (SavedMappings.TryGetValue(name, out var mapping))
            {
                return mapping;
            }
            return null;
        }

        public void DeleteColumnMapping(string name)
        {
            if (SavedMappings.Remove(name))
            {
                Save();
            }
        }
    }
}
