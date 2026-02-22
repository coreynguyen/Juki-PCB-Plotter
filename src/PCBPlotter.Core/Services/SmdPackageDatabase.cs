using System;
using System.Collections.Generic;
using System.Linq;
using PCBPlotter.Core.Models;

namespace PCBPlotter.Core.Services
{
    /// <summary>
    /// Mount type classification for components.
    /// </summary>
    public enum MountType
    {
        Unknown,
        SMD,        // Surface Mount Device
        ThroughHole // Through-hole / PTH
    }

    /// <summary>
    /// Standard SMD package definition with dimensions.
    /// Dimensions are in micrometers (um) for precision matching.
    /// </summary>
    public class SmdPackageDefinition
    {
        public string Name { get; set; }
        public string Category { get; set; }    // Chip, SOT, SOIC, QFP, BGA, etc.
        public int WidthUm { get; set; }        // Body width in micrometers
        public int LengthUm { get; set; }       // Body length in micrometers
        public int HeightUm { get; set; }       // Body height in micrometers
        public int PinCount { get; set; }       // Number of pins (0 = variable)
        public double PitchMm { get; set; }     // Pin pitch in mm (0 = N/A)
        public string[] Aliases { get; set; }   // Alternative names

        // Computed properties in mm
        public double WidthMm => WidthUm / 1000.0;
        public double LengthMm => LengthUm / 1000.0;
        public double HeightMm => HeightUm / 1000.0;
    }

    /// <summary>
    /// Database of standard SMD package types with dimensions.
    /// Used for matching detected pad patterns to known packages.
    /// </summary>
    public static class SmdPackageDatabase
    {
        private static readonly List<SmdPackageDefinition> _packages;
        private static readonly Dictionary<string, SmdPackageDefinition> _byName;

        static SmdPackageDatabase()
        {
            _packages = InitializePackages();
            _byName = _packages.ToDictionary(p => p.Name.ToUpperInvariant(), p => p);

            // Add aliases
            foreach (var pkg in _packages)
            {
                if (pkg.Aliases != null)
                {
                    foreach (var alias in pkg.Aliases)
                    {
                        var key = alias.ToUpperInvariant();
                        if (!_byName.ContainsKey(key))
                            _byName[key] = pkg;
                    }
                }
            }
        }

        /// <summary>
        /// Find a package by name (case-insensitive).
        /// </summary>
        public static SmdPackageDefinition FindByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            _byName.TryGetValue(name.ToUpperInvariant(), out var pkg);
            return pkg;
        }

        /// <summary>
        /// Find packages matching given body dimensions within tolerance.
        /// </summary>
        /// <param name="widthMm">Body width in mm</param>
        /// <param name="lengthMm">Body length in mm</param>
        /// <param name="tolerancePct">Tolerance as percentage (e.g., 0.15 = 15%)</param>
        public static List<SmdPackageDefinition> FindByDimensions(
            double widthMm, double lengthMm, double tolerancePct = 0.20)
        {
            var matches = new List<SmdPackageDefinition>();

            foreach (var pkg in _packages)
            {
                // Check both orientations (rotated 90 degrees)
                bool matchNormal = IsWithinTolerance(widthMm, pkg.WidthMm, tolerancePct) &&
                                   IsWithinTolerance(lengthMm, pkg.LengthMm, tolerancePct);
                bool matchRotated = IsWithinTolerance(widthMm, pkg.LengthMm, tolerancePct) &&
                                    IsWithinTolerance(lengthMm, pkg.WidthMm, tolerancePct);

                if (matchNormal || matchRotated)
                    matches.Add(pkg);
            }

            // Sort by closest match
            return matches.OrderBy(p =>
                Math.Min(
                    Math.Abs(p.WidthMm - widthMm) + Math.Abs(p.LengthMm - lengthMm),
                    Math.Abs(p.LengthMm - widthMm) + Math.Abs(p.WidthMm - lengthMm)
                )).ToList();
        }

        /// <summary>
        /// Find chip packages (0201, 0402, etc.) by pad-to-pad distance.
        /// </summary>
        public static SmdPackageDefinition FindChipByPadDistance(double padDistanceMm)
        {
            // Standard chip sizes by approximate pad center distance
            if (padDistanceMm < 0.5) return FindByName("0201");
            if (padDistanceMm < 0.8) return FindByName("0402");
            if (padDistanceMm < 1.3) return FindByName("0603");
            if (padDistanceMm < 1.8) return FindByName("0805");
            if (padDistanceMm < 2.8) return FindByName("1206");
            if (padDistanceMm < 4.0) return FindByName("1210");
            if (padDistanceMm < 5.5) return FindByName("1812");
            if (padDistanceMm < 7.0) return FindByName("2010");
            return FindByName("2512");
        }

        /// <summary>
        /// Get all packages in a category.
        /// </summary>
        public static List<SmdPackageDefinition> GetByCategory(string category)
        {
            return _packages.Where(p =>
                p.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private static bool IsWithinTolerance(double value, double target, double tolerancePct)
        {
            if (target < 0.001) return value < 0.001;
            double diff = Math.Abs(value - target);
            return diff <= target * tolerancePct;
        }

        private static List<SmdPackageDefinition> InitializePackages()
        {
            return new List<SmdPackageDefinition>
            {
                // ========== CHIP RESISTORS / CAPACITORS ==========
                new SmdPackageDefinition { Name = "0201", Category = "Chip", WidthUm = 6000, LengthUm = 3000, HeightUm = 2500, PinCount = 2, Aliases = new[] { "0603M", "01005" } },
                new SmdPackageDefinition { Name = "0402", Category = "Chip", WidthUm = 10000, LengthUm = 5000, HeightUm = 4000, PinCount = 2, Aliases = new[] { "1005M" } },
                new SmdPackageDefinition { Name = "0603", Category = "Chip", WidthUm = 16000, LengthUm = 8000, HeightUm = 4500, PinCount = 2, Aliases = new[] { "1608M" } },
                new SmdPackageDefinition { Name = "0805", Category = "Chip", WidthUm = 20000, LengthUm = 12500, HeightUm = 6000, PinCount = 2, Aliases = new[] { "2012M" } },
                new SmdPackageDefinition { Name = "1206", Category = "Chip", WidthUm = 32000, LengthUm = 16000, HeightUm = 6000, PinCount = 2, Aliases = new[] { "3216M" } },
                new SmdPackageDefinition { Name = "1210", Category = "Chip", WidthUm = 32000, LengthUm = 25000, HeightUm = 6000, PinCount = 2, Aliases = new[] { "3225M" } },
                new SmdPackageDefinition { Name = "1812", Category = "Chip", WidthUm = 45000, LengthUm = 32000, HeightUm = 6000, PinCount = 2, Aliases = new[] { "4532M" } },
                new SmdPackageDefinition { Name = "2010", Category = "Chip", WidthUm = 50800, LengthUm = 25400, HeightUm = 6000, PinCount = 2, Aliases = new[] { "5025M" } },
                new SmdPackageDefinition { Name = "2512", Category = "Chip", WidthUm = 63500, LengthUm = 32000, HeightUm = 6500, PinCount = 2, Aliases = new[] { "6332M" } },

                // ========== TANTALUM CAPACITORS ==========
                new SmdPackageDefinition { Name = "TANT-A", Category = "Tantalum", WidthUm = 32000, LengthUm = 16000, HeightUm = 16000, PinCount = 2, Aliases = new[] { "3216", "EIA-A" } },
                new SmdPackageDefinition { Name = "TANT-B", Category = "Tantalum", WidthUm = 35000, LengthUm = 28000, HeightUm = 19000, PinCount = 2, Aliases = new[] { "3528", "EIA-B" } },
                new SmdPackageDefinition { Name = "TANT-C", Category = "Tantalum", WidthUm = 60000, LengthUm = 32000, HeightUm = 25000, PinCount = 2, Aliases = new[] { "6032", "EIA-C" } },
                new SmdPackageDefinition { Name = "TANT-D", Category = "Tantalum", WidthUm = 73000, LengthUm = 43000, HeightUm = 28000, PinCount = 2, Aliases = new[] { "7343", "EIA-D" } },

                // ========== MELF (METAL ELECTRODE LEADLESS FACE) ==========
                new SmdPackageDefinition { Name = "MINIMELF", Category = "MELF", WidthUm = 35000, LengthUm = 16000, HeightUm = 16000, PinCount = 2, Aliases = new[] { "SOD80", "LL-34", "DO-213AA" } },
                new SmdPackageDefinition { Name = "MELF", Category = "MELF", WidthUm = 50000, LengthUm = 25000, HeightUm = 25000, PinCount = 2, Aliases = new[] { "DO-213AB", "LL-41" } },
                new SmdPackageDefinition { Name = "MICROMELF", Category = "MELF", WidthUm = 20000, LengthUm = 10000, HeightUm = 10000, PinCount = 2 },

                // ========== SOT (SMALL OUTLINE TRANSISTOR) ==========
                new SmdPackageDefinition { Name = "SOT23", Category = "SOT", WidthUm = 29000, LengthUm = 13000, HeightUm = 9500, PinCount = 3, PitchMm = 0.95, Aliases = new[] { "TO-236AB", "SC59" } },
                new SmdPackageDefinition { Name = "SOT23-5", Category = "SOT", WidthUm = 29000, LengthUm = 16000, HeightUm = 11000, PinCount = 5, PitchMm = 0.95 },
                new SmdPackageDefinition { Name = "SOT23-6", Category = "SOT", WidthUm = 29000, LengthUm = 16000, HeightUm = 11000, PinCount = 6, PitchMm = 0.95 },
                new SmdPackageDefinition { Name = "SOT89", Category = "SOT", WidthUm = 45000, LengthUm = 25000, HeightUm = 15000, PinCount = 3, PitchMm = 1.5, Aliases = new[] { "TO-243AA", "SC62" } },
                new SmdPackageDefinition { Name = "SOT223", Category = "SOT", WidthUm = 65000, LengthUm = 36000, HeightUm = 16000, PinCount = 4, PitchMm = 2.3, Aliases = new[] { "TO-261AA", "SC73" } },
                new SmdPackageDefinition { Name = "SOT323", Category = "SOT", WidthUm = 20000, LengthUm = 12500, HeightUm = 9000, PinCount = 3, PitchMm = 0.65, Aliases = new[] { "SC70" } },
                new SmdPackageDefinition { Name = "SOT363", Category = "SOT", WidthUm = 20000, LengthUm = 12500, HeightUm = 9000, PinCount = 6, PitchMm = 0.65, Aliases = new[] { "SC88" } },
                new SmdPackageDefinition { Name = "SC90", Category = "SOT", WidthUm = 16000, LengthUm = 8000, HeightUm = 7000, PinCount = 3, PitchMm = 0.5 },

                // ========== DPAK / D2PAK ==========
                new SmdPackageDefinition { Name = "DPAK", Category = "DPAK", WidthUm = 65000, LengthUm = 60000, HeightUm = 22300, PinCount = 3, Aliases = new[] { "TO-252" } },
                new SmdPackageDefinition { Name = "D2PAK", Category = "DPAK", WidthUm = 105000, LengthUm = 91900, HeightUm = 44000, PinCount = 3, Aliases = new[] { "TO-263", "DDPAK" } },
                new SmdPackageDefinition { Name = "D3PAK", Category = "DPAK", WidthUm = 160000, LengthUm = 140000, HeightUm = 47000, PinCount = 3, Aliases = new[] { "TO-268" } },

                // ========== DIODES (SOD) ==========
                new SmdPackageDefinition { Name = "SOD123", Category = "Diode", WidthUm = 27000, LengthUm = 15000, HeightUm = 13500, PinCount = 2 },
                new SmdPackageDefinition { Name = "SOD323", Category = "Diode", WidthUm = 17000, LengthUm = 12000, HeightUm = 10000, PinCount = 2 },
                new SmdPackageDefinition { Name = "SOD523", Category = "Diode", WidthUm = 16000, LengthUm = 8000, HeightUm = 6000, PinCount = 2 },
                new SmdPackageDefinition { Name = "SMA", Category = "Diode", WidthUm = 42500, LengthUm = 26000, HeightUm = 22000, PinCount = 2, Aliases = new[] { "DO-214AC" } },
                new SmdPackageDefinition { Name = "SMB", Category = "Diode", WidthUm = 42500, LengthUm = 35000, HeightUm = 22900, PinCount = 2, Aliases = new[] { "DO-214AA" } },
                new SmdPackageDefinition { Name = "SMC", Category = "Diode", WidthUm = 81300, LengthUm = 62200, HeightUm = 26200, PinCount = 2, Aliases = new[] { "DO-214AB" } },

                // ========== SOIC / SOP ==========
                new SmdPackageDefinition { Name = "SOIC8", Category = "SOIC", WidthUm = 50000, LengthUm = 50000, HeightUm = 16500, PinCount = 8, PitchMm = 1.27, Aliases = new[] { "SO8", "SOP8" } },
                new SmdPackageDefinition { Name = "SOIC14", Category = "SOIC", WidthUm = 86900, LengthUm = 60000, HeightUm = 16500, PinCount = 14, PitchMm = 1.27, Aliases = new[] { "SO14" } },
                new SmdPackageDefinition { Name = "SOIC16", Category = "SOIC", WidthUm = 96900, LengthUm = 60000, HeightUm = 16500, PinCount = 16, PitchMm = 1.27, Aliases = new[] { "SO16" } },
                new SmdPackageDefinition { Name = "SOIC18", Category = "SOIC", WidthUm = 116000, LengthUm = 76000, HeightUm = 22000, PinCount = 18, PitchMm = 1.27 },
                new SmdPackageDefinition { Name = "SOIC20", Category = "SOIC", WidthUm = 128000, LengthUm = 76000, HeightUm = 22000, PinCount = 20, PitchMm = 1.27 },
                new SmdPackageDefinition { Name = "SOIC24", Category = "SOIC", WidthUm = 154000, LengthUm = 103000, HeightUm = 25000, PinCount = 24, PitchMm = 1.27 },
                new SmdPackageDefinition { Name = "SOIC28", Category = "SOIC", WidthUm = 180000, LengthUm = 103000, HeightUm = 25000, PinCount = 28, PitchMm = 1.27 },

                // ========== SSOP / TSSOP / MSOP ==========
                new SmdPackageDefinition { Name = "SSOP8", Category = "SSOP", WidthUm = 30000, LengthUm = 40000, HeightUm = 13000, PinCount = 8, PitchMm = 0.65 },
                new SmdPackageDefinition { Name = "SSOP14", Category = "SSOP", WidthUm = 62000, LengthUm = 78000, HeightUm = 19000, PinCount = 14, PitchMm = 0.65 },
                new SmdPackageDefinition { Name = "SSOP16", Category = "SSOP", WidthUm = 62000, LengthUm = 78000, HeightUm = 19000, PinCount = 16, PitchMm = 0.65 },
                new SmdPackageDefinition { Name = "SSOP20", Category = "SSOP", WidthUm = 72000, LengthUm = 78000, HeightUm = 19000, PinCount = 20, PitchMm = 0.65 },
                new SmdPackageDefinition { Name = "SSOP24", Category = "SSOP", WidthUm = 82000, LengthUm = 78000, HeightUm = 19000, PinCount = 24, PitchMm = 0.65 },
                new SmdPackageDefinition { Name = "SSOP28", Category = "SSOP", WidthUm = 102000, LengthUm = 78000, HeightUm = 19000, PinCount = 28, PitchMm = 0.65 },
                new SmdPackageDefinition { Name = "TSSOP8", Category = "TSSOP", WidthUm = 31000, LengthUm = 65000, HeightUm = 12000, PinCount = 8, PitchMm = 0.65 },
                new SmdPackageDefinition { Name = "TSSOP14", Category = "TSSOP", WidthUm = 50000, LengthUm = 64000, HeightUm = 11000, PinCount = 14, PitchMm = 0.65 },
                new SmdPackageDefinition { Name = "TSSOP16", Category = "TSSOP", WidthUm = 50000, LengthUm = 64000, HeightUm = 11000, PinCount = 16, PitchMm = 0.65 },
                new SmdPackageDefinition { Name = "TSSOP20", Category = "TSSOP", WidthUm = 65000, LengthUm = 64000, HeightUm = 12000, PinCount = 20, PitchMm = 0.65 },
                new SmdPackageDefinition { Name = "TSSOP24", Category = "TSSOP", WidthUm = 78000, LengthUm = 64000, HeightUm = 12000, PinCount = 24, PitchMm = 0.65 },
                new SmdPackageDefinition { Name = "TSSOP28", Category = "TSSOP", WidthUm = 97000, LengthUm = 64000, HeightUm = 12000, PinCount = 28, PitchMm = 0.65 },
                new SmdPackageDefinition { Name = "MSOP8", Category = "MSOP", WidthUm = 30000, LengthUm = 50000, HeightUm = 10000, PinCount = 8, PitchMm = 0.65 },
                new SmdPackageDefinition { Name = "MSOP10", Category = "MSOP", WidthUm = 30000, LengthUm = 50000, HeightUm = 10000, PinCount = 10, PitchMm = 0.5 },

                // ========== QFP / LQFP / TQFP ==========
                new SmdPackageDefinition { Name = "LQFP32", Category = "QFP", WidthUm = 70000, LengthUm = 70000, HeightUm = 14000, PinCount = 32, PitchMm = 0.8 },
                new SmdPackageDefinition { Name = "LQFP44", Category = "QFP", WidthUm = 128000, LengthUm = 128000, HeightUm = 14000, PinCount = 44, PitchMm = 0.8 },
                new SmdPackageDefinition { Name = "LQFP48", Category = "QFP", WidthUm = 90000, LengthUm = 90000, HeightUm = 14000, PinCount = 48, PitchMm = 0.5 },
                new SmdPackageDefinition { Name = "LQFP64", Category = "QFP", WidthUm = 110000, LengthUm = 110000, HeightUm = 14000, PinCount = 64, PitchMm = 0.5 },
                new SmdPackageDefinition { Name = "LQFP80", Category = "QFP", WidthUm = 140000, LengthUm = 140000, HeightUm = 14000, PinCount = 80, PitchMm = 0.5 },
                new SmdPackageDefinition { Name = "LQFP100", Category = "QFP", WidthUm = 160000, LengthUm = 160000, HeightUm = 14000, PinCount = 100, PitchMm = 0.5 },
                new SmdPackageDefinition { Name = "LQFP144", Category = "QFP", WidthUm = 226000, LengthUm = 226000, HeightUm = 14000, PinCount = 144, PitchMm = 0.5 },
                new SmdPackageDefinition { Name = "LQFP176", Category = "QFP", WidthUm = 266000, LengthUm = 266000, HeightUm = 14000, PinCount = 176, PitchMm = 0.5 },
                new SmdPackageDefinition { Name = "LQFP208", Category = "QFP", WidthUm = 306000, LengthUm = 306000, HeightUm = 14000, PinCount = 208, PitchMm = 0.5 },
                new SmdPackageDefinition { Name = "TQFP32", Category = "QFP", WidthUm = 90000, LengthUm = 90000, HeightUm = 10000, PinCount = 32, PitchMm = 0.8 },
                new SmdPackageDefinition { Name = "TQFP44", Category = "QFP", WidthUm = 128000, LengthUm = 128000, HeightUm = 10000, PinCount = 44, PitchMm = 0.8 },
                new SmdPackageDefinition { Name = "TQFP48", Category = "QFP", WidthUm = 90000, LengthUm = 90000, HeightUm = 10000, PinCount = 48, PitchMm = 0.5 },
                new SmdPackageDefinition { Name = "TQFP64", Category = "QFP", WidthUm = 120000, LengthUm = 120000, HeightUm = 10000, PinCount = 64, PitchMm = 0.5 },
                new SmdPackageDefinition { Name = "TQFP100", Category = "QFP", WidthUm = 160000, LengthUm = 160000, HeightUm = 10000, PinCount = 100, PitchMm = 0.5 },

                // ========== QFN / DFN ==========
                new SmdPackageDefinition { Name = "QFN8", Category = "QFN", WidthUm = 30000, LengthUm = 30000, HeightUm = 8000, PinCount = 8, PitchMm = 0.65 },
                new SmdPackageDefinition { Name = "QFN12", Category = "QFN", WidthUm = 30000, LengthUm = 30000, HeightUm = 8000, PinCount = 12, PitchMm = 0.5 },
                new SmdPackageDefinition { Name = "QFN16", Category = "QFN", WidthUm = 40000, LengthUm = 40000, HeightUm = 9000, PinCount = 16, PitchMm = 0.5 },
                new SmdPackageDefinition { Name = "QFN20", Category = "QFN", WidthUm = 50000, LengthUm = 50000, HeightUm = 9000, PinCount = 20, PitchMm = 0.5 },
                new SmdPackageDefinition { Name = "QFN24", Category = "QFN", WidthUm = 50000, LengthUm = 50000, HeightUm = 9000, PinCount = 24, PitchMm = 0.5 },
                new SmdPackageDefinition { Name = "QFN28", Category = "QFN", WidthUm = 50000, LengthUm = 50000, HeightUm = 8000, PinCount = 28, PitchMm = 0.4 },
                new SmdPackageDefinition { Name = "QFN32", Category = "QFN", WidthUm = 50000, LengthUm = 50000, HeightUm = 9000, PinCount = 32, PitchMm = 0.4 },
                new SmdPackageDefinition { Name = "QFN48", Category = "QFN", WidthUm = 70000, LengthUm = 70000, HeightUm = 9000, PinCount = 48, PitchMm = 0.5 },
                new SmdPackageDefinition { Name = "QFN64", Category = "QFN", WidthUm = 100000, LengthUm = 100000, HeightUm = 10000, PinCount = 64, PitchMm = 0.5 },
                new SmdPackageDefinition { Name = "DFN6", Category = "DFN", WidthUm = 20000, LengthUm = 20000, HeightUm = 8000, PinCount = 6, PitchMm = 0.65 },
                new SmdPackageDefinition { Name = "DFN8", Category = "DFN", WidthUm = 30000, LengthUm = 20000, HeightUm = 8000, PinCount = 8, PitchMm = 0.5 },

                // ========== BGA ==========
                new SmdPackageDefinition { Name = "BGA49", Category = "BGA", WidthUm = 50000, LengthUm = 50000, HeightUm = 10000, PinCount = 49, PitchMm = 0.8 },
                new SmdPackageDefinition { Name = "BGA64", Category = "BGA", WidthUm = 80000, LengthUm = 80000, HeightUm = 10000, PinCount = 64, PitchMm = 0.8 },
                new SmdPackageDefinition { Name = "BGA100", Category = "BGA", WidthUm = 100000, LengthUm = 100000, HeightUm = 12000, PinCount = 100, PitchMm = 0.8 },
                new SmdPackageDefinition { Name = "BGA144", Category = "BGA", WidthUm = 100000, LengthUm = 100000, HeightUm = 12000, PinCount = 144, PitchMm = 0.65 },
                new SmdPackageDefinition { Name = "BGA169", Category = "BGA", WidthUm = 140000, LengthUm = 140000, HeightUm = 14000, PinCount = 169, PitchMm = 1.0 },
                new SmdPackageDefinition { Name = "BGA225", Category = "BGA", WidthUm = 170000, LengthUm = 170000, HeightUm = 14000, PinCount = 225, PitchMm = 1.0 },
                new SmdPackageDefinition { Name = "BGA256", Category = "BGA", WidthUm = 170000, LengthUm = 170000, HeightUm = 14000, PinCount = 256, PitchMm = 1.0 },
                new SmdPackageDefinition { Name = "BGA324", Category = "BGA", WidthUm = 230000, LengthUm = 230000, HeightUm = 16000, PinCount = 324, PitchMm = 1.0 },
                new SmdPackageDefinition { Name = "BGA400", Category = "BGA", WidthUm = 210000, LengthUm = 210000, HeightUm = 10000, PinCount = 400, PitchMm = 1.0 },
                new SmdPackageDefinition { Name = "BGA484", Category = "BGA", WidthUm = 230000, LengthUm = 230000, HeightUm = 16000, PinCount = 484, PitchMm = 1.0 },

                // ========== PLCC ==========
                new SmdPackageDefinition { Name = "PLCC20", Category = "PLCC", WidthUm = 90000, LengthUm = 90000, HeightUm = 34000, PinCount = 20, PitchMm = 1.27 },
                new SmdPackageDefinition { Name = "PLCC28", Category = "PLCC", WidthUm = 124500, LengthUm = 124500, HeightUm = 43000, PinCount = 28, PitchMm = 1.27 },
                new SmdPackageDefinition { Name = "PLCC32", Category = "PLCC", WidthUm = 130800, LengthUm = 105400, HeightUm = 31500, PinCount = 32, PitchMm = 1.27 },
                new SmdPackageDefinition { Name = "PLCC44", Category = "PLCC", WidthUm = 175300, LengthUm = 175300, HeightUm = 43000, PinCount = 44, PitchMm = 1.27 },
                new SmdPackageDefinition { Name = "PLCC52", Category = "PLCC", WidthUm = 201000, LengthUm = 201000, HeightUm = 44000, PinCount = 52, PitchMm = 1.27 },
                new SmdPackageDefinition { Name = "PLCC68", Category = "PLCC", WidthUm = 251500, LengthUm = 251500, HeightUm = 43000, PinCount = 68, PitchMm = 1.27 },
                new SmdPackageDefinition { Name = "PLCC84", Category = "PLCC", WidthUm = 302300, LengthUm = 302300, HeightUm = 43000, PinCount = 84, PitchMm = 1.27 },

                // ========== ALUMINUM ELECTROLYTIC CAPACITORS ==========
                new SmdPackageDefinition { Name = "ALCAP-4X5", Category = "AlCap", WidthUm = 43000, LengthUm = 43000, HeightUm = 55000, PinCount = 2, Aliases = new[] { "4x5.5mm" } },
                new SmdPackageDefinition { Name = "ALCAP-5X5", Category = "AlCap", WidthUm = 53000, LengthUm = 53000, HeightUm = 55000, PinCount = 2, Aliases = new[] { "5x5.5mm" } },
                new SmdPackageDefinition { Name = "ALCAP-6X5", Category = "AlCap", WidthUm = 66000, LengthUm = 66000, HeightUm = 55000, PinCount = 2, Aliases = new[] { "6.3x5.5mm" } },
                new SmdPackageDefinition { Name = "ALCAP-6X8", Category = "AlCap", WidthUm = 66000, LengthUm = 66000, HeightUm = 80000, PinCount = 2, Aliases = new[] { "6.3x8mm" } },
                new SmdPackageDefinition { Name = "ALCAP-8X10", Category = "AlCap", WidthUm = 83000, LengthUm = 83000, HeightUm = 105000, PinCount = 2, Aliases = new[] { "8x10.5mm" } },
                new SmdPackageDefinition { Name = "ALCAP-10X10", Category = "AlCap", WidthUm = 103000, LengthUm = 103000, HeightUm = 105000, PinCount = 2, Aliases = new[] { "10x10.5mm" } },

                // ========== LED ==========
                new SmdPackageDefinition { Name = "LED0603", Category = "LED", WidthUm = 16000, LengthUm = 8000, HeightUm = 6500, PinCount = 2, Aliases = new[] { "LED1608" } },
                new SmdPackageDefinition { Name = "LED0805", Category = "LED", WidthUm = 20000, LengthUm = 12500, HeightUm = 10000, PinCount = 2, Aliases = new[] { "LED2012" } },
                new SmdPackageDefinition { Name = "LED1206", Category = "LED", WidthUm = 32000, LengthUm = 16000, HeightUm = 10000, PinCount = 2, Aliases = new[] { "LED3216" } },

                // ========== INDUCTORS ==========
                new SmdPackageDefinition { Name = "IND0402", Category = "Inductor", WidthUm = 10000, LengthUm = 5000, HeightUm = 5000, PinCount = 2 },
                new SmdPackageDefinition { Name = "IND0603", Category = "Inductor", WidthUm = 16000, LengthUm = 8000, HeightUm = 8000, PinCount = 2 },
                new SmdPackageDefinition { Name = "IND0805", Category = "Inductor", WidthUm = 20000, LengthUm = 12500, HeightUm = 12500, PinCount = 2 },
                new SmdPackageDefinition { Name = "IND1008", Category = "Inductor", WidthUm = 25000, LengthUm = 20000, HeightUm = 18000, PinCount = 2 },
                new SmdPackageDefinition { Name = "IND1206", Category = "Inductor", WidthUm = 32000, LengthUm = 16000, HeightUm = 16000, PinCount = 2 },
                new SmdPackageDefinition { Name = "IND1210", Category = "Inductor", WidthUm = 32000, LengthUm = 25000, HeightUm = 22000, PinCount = 2 },

                // ========== CRYSTAL / OSCILLATOR ==========
                new SmdPackageDefinition { Name = "XTAL-3215", Category = "Crystal", WidthUm = 32000, LengthUm = 15000, HeightUm = 9000, PinCount = 2 },
                new SmdPackageDefinition { Name = "XTAL-5032", Category = "Crystal", WidthUm = 50000, LengthUm = 32000, HeightUm = 16000, PinCount = 4 },
                new SmdPackageDefinition { Name = "XTAL-7050", Category = "Crystal", WidthUm = 70000, LengthUm = 50000, HeightUm = 17000, PinCount = 4 },
            };
        }

        /// <summary>
        /// Map part class to suggested designator prefix.
        /// </summary>
        public static string GetDesignatorPrefix(PartClass partClass, string packageName = null)
        {
            // Check for specific package hints
            if (!string.IsNullOrEmpty(packageName))
            {
                var upper = packageName.ToUpperInvariant();
                if (upper.Contains("RES") || upper.StartsWith("R")) return "R";
                if (upper.Contains("CAP") || upper.StartsWith("C")) return "C";
                if (upper.Contains("IND") || upper.StartsWith("L")) return "L";
                if (upper.Contains("LED")) return "LED";
                if (upper.Contains("DIODE") || upper.Contains("SOD") || upper.StartsWith("D")) return "D";
                if (upper.Contains("TANT")) return "C"; // Tantalum capacitors
                if (upper.Contains("XTAL") || upper.Contains("OSC")) return "Y";
                if (upper.Contains("FUSE")) return "F";
                if (upper.Contains("CONN") || upper.Contains("HEADER")) return "J";
            }

            switch (partClass)
            {
                case PartClass.Chip: return "R"; // Default to resistor
                case PartClass.SOT: return "Q";  // Transistor
                case PartClass.SOP:
                case PartClass.QFP:
                case PartClass.QFN:
                case PartClass.BGA: return "U"; // IC
                case PartClass.Connector: return "J";
                case PartClass.ElectrolyticCap: return "C";
                case PartClass.LED: return "LED";
                case PartClass.Crystal: return "Y";
                default: return "U";
            }
        }
    }
}
