using System.Globalization;
using System.Windows.Data;

namespace Daqifi.Desktop.Converters;

/// <summary>
/// Converts a string to its rightmost characters.
/// </summary>
public class StringRightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string str || string.IsNullOrEmpty(str))
        {
            return string.Empty;
        }

        if (parameter is not string paramStr || !int.TryParse(paramStr, out int length))
        {
            return str;
        }

        return str.Length <= length ? str : str.Substring(str.Length - length);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
