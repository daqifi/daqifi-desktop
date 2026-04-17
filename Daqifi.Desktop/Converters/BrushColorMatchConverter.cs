using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Daqifi.Desktop.Converters;

public class BrushColorMatchConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return false;
        if (values[0] is SolidColorBrush a && values[1] is SolidColorBrush b)
        {
            return a.Color == b.Color;
        }
        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
