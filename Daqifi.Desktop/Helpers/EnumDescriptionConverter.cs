using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace Daqifi.Desktop.Helpers;

public class EnumDescriptionConverter : IValueConverter
{
    private string GetEnumDescription(Enum enumObj)
    {
        // Undefined/out-of-range enum values are not named members, so GetField returns null.
        // Fall back to the value's string form (its numeric representation) rather than crashing.
        var fieldInfo = enumObj.GetType().GetField(enumObj.ToString());
        if (fieldInfo == null)
        {
            return enumObj.ToString();
        }

        // Select the DescriptionAttribute specifically: a member may carry other attributes,
        // and GetCustomAttributes order is not guaranteed. Fall back to the member name when absent.
        var descriptionAttribute = fieldInfo
            .GetCustomAttributes(typeof(DescriptionAttribute), false)
            .OfType<DescriptionAttribute>()
            .FirstOrDefault();

        return descriptionAttribute?.Description ?? enumObj.ToString();
    }

    object IValueConverter.Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
        {
            return string.Empty;
        }

        if (value is not Enum myEnum)
        {
            return value.ToString();
        }

        var description = GetEnumDescription(myEnum);
        return description;
    }

    object IValueConverter.ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.Empty;
    }
}