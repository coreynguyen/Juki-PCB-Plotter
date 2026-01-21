using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

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

    /// <summary>
    /// Converts a list of strings to a comma-separated string
    /// </summary>
    public class ListToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return string.Empty;

            var separator = parameter as string ?? ", ";

            if (value is IEnumerable<string> stringList)
            {
                return string.Join(separator, stringList);
            }

            if (value is IEnumerable enumerable)
            {
                var items = new List<string>();
                foreach (var item in enumerable)
                {
                    if (item != null)
                        items.Add(item.ToString());
                }
                return string.Join(separator, items);
            }

            return value.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return new List<string>();

            var separator = parameter as string ?? ", ";
            var str = value.ToString();

            return new List<string>(str.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries));
        }
    }

    /// <summary>
    /// Inverts a boolean value
    /// </summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool && !(bool)value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool && !(bool)value;
        }
    }

    /// <summary>
    /// Inverts boolean and converts to Visibility
    /// </summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = value is bool && (bool)value;
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility && (Visibility)value == Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Converts a Color to a SolidColorBrush
    /// </summary>
    public class ColorToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Color color)
            {
                return new SolidColorBrush(color);
            }

            if (value is string colorString)
            {
                try
                {
                    var convertedColor = (Color)ColorConverter.ConvertFromString(colorString);
                    return new SolidColorBrush(convertedColor);
                }
                catch
                {
                    return Brushes.Transparent;
                }
            }

            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SolidColorBrush brush)
            {
                return brush.Color;
            }

            return Colors.Transparent;
        }
    }
}
