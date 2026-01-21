using System;

namespace PCBPlotter.Core.Utilities
{
    /// <summary>
    /// Units supported by the application
    /// </summary>
    public enum LengthUnit
    {
        Millimeters,
        Mils,       // 1/1000 inch
        Inches
    }

    /// <summary>
    /// Utility class for converting between measurement units
    /// </summary>
    public static class UnitConverter
    {
        // Conversion factors to millimeters
        private const double MilsToMm = 0.0254;      // 1 mil = 0.0254 mm
        private const double InchesToMm = 25.4;      // 1 inch = 25.4 mm

        /// <summary>
        /// Default unit for the application
        /// </summary>
        public static LengthUnit DefaultUnit { get; set; } = LengthUnit.Millimeters;

        /// <summary>
        /// Converts a value from one unit to another
        /// </summary>
        public static double Convert(double value, LengthUnit fromUnit, LengthUnit toUnit)
        {
            if (fromUnit == toUnit)
                return value;

            // Convert to mm first
            double mm = ToMillimeters(value, fromUnit);

            // Convert from mm to target unit
            return FromMillimeters(mm, toUnit);
        }

        /// <summary>
        /// Converts a value to millimeters
        /// </summary>
        public static double ToMillimeters(double value, LengthUnit fromUnit)
        {
            switch (fromUnit)
            {
                case LengthUnit.Millimeters:
                    return value;
                case LengthUnit.Mils:
                    return value * MilsToMm;
                case LengthUnit.Inches:
                    return value * InchesToMm;
                default:
                    throw new ArgumentException("Unknown unit: " + fromUnit);
            }
        }

        /// <summary>
        /// Converts from millimeters to another unit
        /// </summary>
        public static double FromMillimeters(double mm, LengthUnit toUnit)
        {
            switch (toUnit)
            {
                case LengthUnit.Millimeters:
                    return mm;
                case LengthUnit.Mils:
                    return mm / MilsToMm;
                case LengthUnit.Inches:
                    return mm / InchesToMm;
                default:
                    throw new ArgumentException("Unknown unit: " + toUnit);
            }
        }

        /// <summary>
        /// Converts to the application's default unit (typically mm)
        /// </summary>
        public static double ToDefault(double value, LengthUnit fromUnit)
        {
            return Convert(value, fromUnit, DefaultUnit);
        }

        /// <summary>
        /// Parses a unit string to LengthUnit enum
        /// </summary>
        public static LengthUnit ParseUnit(string unitString)
        {
            if (string.IsNullOrEmpty(unitString))
                return DefaultUnit;

            unitString = unitString.ToLowerInvariant().Trim();

            switch (unitString)
            {
                case "mm":
                case "millimeters":
                case "millimeter":
                    return LengthUnit.Millimeters;

                case "mil":
                case "mils":
                case "thou":
                    return LengthUnit.Mils;

                case "in":
                case "inch":
                case "inches":
                    return LengthUnit.Inches;

                default:
                    return DefaultUnit;
            }
        }

        /// <summary>
        /// Gets the display string for a unit
        /// </summary>
        public static string GetUnitString(LengthUnit unit)
        {
            switch (unit)
            {
                case LengthUnit.Millimeters:
                    return "mm";
                case LengthUnit.Mils:
                    return "mil";
                case LengthUnit.Inches:
                    return "in";
                default:
                    return "mm";
            }
        }

        /// <summary>
        /// Formats a value with its unit
        /// </summary>
        public static string Format(double value, LengthUnit unit, int decimals = 3)
        {
            string format = "F" + decimals;
            return value.ToString(format) + " " + GetUnitString(unit);
        }
    }
}
