using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace Daqifi.Desktop.Converters;

/// <summary>
/// Converts a list/collection to a comma-separated string
/// </summary>
public class ListToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return string.Empty;

        if (value is IEnumerable enumerable)
        {
            var items = enumerable.Cast<object>().ToList();
            if (items.Count == 0)
                return string.Empty;

            // Handle different formatting based on parameter
            var format = parameter?.ToString() ?? "default";
            
            switch (format.ToLower())
            {
                case "brackets":
                    return $"[{string.Join(", ", items)}]";
                case "short":
                    // Limit to first 3 items for short display
                    var shortItems = items.Take(3).ToList();
                    var result = string.Join(",", shortItems);
                    if (items.Count > 3)
                        result += "...";
                    return result;
                default:
                    return string.Join(", ", items);
            }
        }

        return value.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException("ListToStringConverter does not support ConvertBack");
    }
}
