using System;
using System.Globalization;
using System.Windows.Data;

namespace PCBPlotter.Converters
{
    /// <summary>
    /// Converts an enum value to a boolean based on the parameter
    /// </summary>
    public class EnumToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            string enumValue = value.ToString();
            string targetValue = parameter.ToString();

            return enumValue.Equals(targetValue, StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return Binding.DoNothing;

            bool isChecked = (bool)value;
            if (!isChecked)
                return Binding.DoNothing;

            string targetValue = parameter.ToString();
            return Enum.Parse(targetType, targetValue, true);
        }
    }

    /// <summary>
    /// Converts an enum value to its inverse boolean based on the parameter
    /// </summary>
    public class EnumToInverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return true;

            string enumValue = value.ToString();
            string targetValue = parameter.ToString();

            return !enumValue.Equals(targetValue, StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
