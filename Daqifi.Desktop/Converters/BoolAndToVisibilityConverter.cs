using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace Daqifi.Desktop.Converters;

/// <summary>
/// Multi-value converter that returns <see cref="Visibility.Visible"/> only when every bound value is
/// a <c>true</c> boolean; otherwise <see cref="Visibility.Collapsed"/>. Used to gate UI on the AND of
/// several conditions — e.g. "the device has a WINC WiFi module" AND "debug mode is enabled".
/// </summary>
public class BoolAndToVisibilityConverter : IMultiValueConverter
{
    /// <summary>
    /// Returns <see cref="Visibility.Visible"/> when every bound value is boolean <c>true</c>;
    /// otherwise <see cref="Visibility.Collapsed"/>.
    /// </summary>
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var allTrue = values != null && values.Length > 0 && values.All(v => v is true);
        return allTrue ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Not supported — this converter is one-way (bindings are read-only Visibility).</summary>
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
