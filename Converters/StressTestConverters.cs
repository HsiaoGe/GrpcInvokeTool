using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace GrpcInvokeTool.Converters
{
    /// <summary>
    /// Converts integer tab index to Visibility. Parameter specifies which tab index this instance matches.
    /// Usage: Converter={StaticResource IntToVisibilityConverter}, ConverterParameter=0
    /// </summary>
    public class IntToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int tabIndex && parameter is string param && int.TryParse(param, out var matchIndex))
            {
                return tabIndex == matchIndex ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts boolean to color: true = Success green, false = Danger red.
    /// Used for call result status display.
    /// </summary>
    public class StatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isSuccess)
            {
                return isSuccess
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A3BE8C"))  // Nordic Success
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#BF616A")); // Nordic Danger
            }
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4C566A"));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

}
