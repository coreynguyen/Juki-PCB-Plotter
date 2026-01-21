using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace PCBPlotter.Services
{
    /// <summary>
    /// Application settings that persist between sessions
    /// </summary>
    [DataContract]
    public class AppSettings
    {
        private static readonly string SettingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PCBPlotter");

        private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");
        private static readonly string MappingsFile = Path.Combine(SettingsFolder, "columnmappings.json");

        private static AppSettings _instance;
        public static AppSettings Instance => _instance ?? (_instance = Load());

        // General settings
        [DataMember] public bool StartOnStartScreen { get; set; } = true;
        [DataMember] public bool RememberLastProject { get; set; } = true;
        [DataMember] public bool CheckForUpdates { get; set; } = false;
        [DataMember] public string LastProjectPath { get; set; }
        [DataMember] public List<string> RecentProjects { get; set; } = new List<string>();

        // Theme settings
        [DataMember] public string Theme { get; set; } = "Dark"; // Dark, Light, System

        // Units settings
        [DataMember] public string DefaultLengthUnit { get; set; } = "mm";
        [DataMember] public string DefaultAngleUnit { get; set; } = "Degrees";
        [DataMember] public int DecimalPlaces { get; set; } = 3;

        // Export settings
        [DataMember] public string DefaultExportFormat { get; set; } = "Juki JX-100";
        [DataMember] public bool IncludeCsvHeaders { get; set; } = true;
        [DataMember] public bool AutoOpenExportFolder { get; set; } = false;
        [DataMember] public string DefaultExportPath { get; set; } = "";

        // Display settings
        [DataMember] public bool ShowGridByDefault { get; set; } = true;
        [DataMember] public bool ShowLabelsByDefault { get; set; } = true;
        [DataMember] public double DefaultGridSpacing { get; set; } = 1.0;

        // Column mappings for text import
        [DataMember] public Dictionary<string, SavedColumnMapping> SavedMappings { get; set; }
            = new Dictionary<string, SavedColumnMapping>();

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                    using (var stream = File.OpenRead(SettingsFile))
                    {
                        return (AppSettings)serializer.ReadObject(stream);
                    }
                }
            }
            catch (Exception)
            {
                // If loading fails, return default settings
            }

            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                // Ensure directory exists
                if (!Directory.Exists(SettingsFolder))
                {
                    Directory.CreateDirectory(SettingsFolder);
                }

                var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                using (var stream = File.Create(SettingsFile))
                {
                    serializer.WriteObject(stream, this);
                }
            }
            catch (Exception)
            {
                // Silently fail if we can't save settings
            }
        }

        public void AddRecentProject(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            // Remove if already exists
            RecentProjects.Remove(path);

            // Add to front
            RecentProjects.Insert(0, path);

            // Keep only last 10
            if (RecentProjects.Count > 10)
            {
                RecentProjects.RemoveRange(10, RecentProjects.Count - 10);
            }

            LastProjectPath = path;
            Save();
        }

        public void SaveColumnMapping(string name, List<string> fieldNames)
        {
            SavedMappings[name] = new SavedColumnMapping
            {
                Name = name,
                FieldNames = fieldNames,
                SavedDate = DateTime.Now
            };
            Save();
        }

        public List<string> LoadColumnMapping(string name)
        {
            if (SavedMappings.TryGetValue(name, out var mapping))
            {
                return mapping.FieldNames;
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

    [DataContract]
    public class SavedColumnMapping
    {
        [DataMember] public string Name { get; set; }
        [DataMember] public List<string> FieldNames { get; set; }
        [DataMember] public DateTime SavedDate { get; set; }
    }
}
