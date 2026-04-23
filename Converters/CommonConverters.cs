using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Grpc.Core;
using GrpcInvokeTool.Models;

namespace GrpcInvokeTool.Converters
{
    public class MethodTypeToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is MethodType methodType)
            {
                // Nordic Light Aurora/Frost palette
                return methodType switch
                {
                    MethodType.Unary => "#5E81AC",           // Frost 1 - Primary Blue
                    MethodType.ServerStreaming => "#A3BE8C", // Aurora Green
                    MethodType.ClientStreaming => "#EBCB8B", // Aurora Yellow
                    MethodType.DuplexStreaming => "#BF616A", // Aurora Red
                    _ => "#81A1C1"                            // Frost 2
                };
            }
            return "#81A1C1";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class MethodTypeToTagConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is MethodType methodType)
            {
                return methodType switch
                {
                    MethodType.Unary => "U",
                    MethodType.ServerStreaming => "SS",
                    MethodType.ClientStreaming => "CS",
                    MethodType.DuplexStreaming => "DS",
                    _ => "?"
                };
            }
            return "?";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? Visibility.Visible : Visibility.Collapsed;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility v)
                return v == Visibility.Visible;
            return false;
        }
    }

    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? Visibility.Collapsed : Visibility.Visible;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return !b;
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class IsErrorToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isError)
                return isError ? "#BF616A" : "#2E3440";
            return "#2E3440";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

}
