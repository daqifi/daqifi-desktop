using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Daqifi.Desktop.Converters;

/// <summary>
/// Multi-value converter that compares two <see cref="SolidColorBrush"/> values by their
/// underlying <see cref="Color"/>. Two brushes constructed from the same hex still count as
/// different references, which breaks equality on the brush object — the Color value is the
/// semantically correct comparison. Returns <see cref="Visibility"/> when the binding target
/// is a Visibility (for direct use on Visibility properties), otherwise a boolean.
/// </summary>
public class BrushColorMatchConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var match = values.Length >= 2
            && values[0] is SolidColorBrush a
            && values[1] is SolidColorBrush b
            && a.Color == b.Color;

        if (targetType == typeof(Visibility))
        {
            return match ? Visibility.Visible : Visibility.Collapsed;
        }
        return match;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
