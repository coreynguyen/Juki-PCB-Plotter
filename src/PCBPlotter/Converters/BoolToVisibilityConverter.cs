using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PCBPlotter.Converters
{
    /// <summary>
    /// Converts boolean to Visibility
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = value is bool && (bool)value;

            // Parameter can invert the logic
            if (parameter != null && parameter.ToString().ToLower() == "invert")
            {
                boolValue = !boolValue;
            }

            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool result = value is Visibility && (Visibility)value == Visibility.Visible;

            if (parameter != null && parameter.ToString().ToLower() == "invert")
            {
                result = !result;
            }

            return result;
        }
    }

    /// <summary>
    /// Converts boolean to Visibility.Hidden instead of Collapsed
    /// </summary>
    public class BoolToVisibilityHiddenConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = value is bool && (bool)value;
            return boolValue ? Visibility.Visible : Visibility.Hidden;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility && (Visibility)value == Visibility.Visible;
        }
    }
}
