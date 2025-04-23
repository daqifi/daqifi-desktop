using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Daqifi.Desktop.Converters;

[ValueConversion(typeof(bool), typeof(Visibility))]
public class InvertedBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool booleanValue)
        {
            return booleanValue ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Collapsed; // Default to collapsed if input is not bool
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibilityValue)
        {
            return visibilityValue == Visibility.Collapsed;
        }
        return false; // Default or throw exception based on requirements
    }
}