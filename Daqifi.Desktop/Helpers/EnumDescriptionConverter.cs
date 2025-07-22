using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;

namespace Daqifi.Desktop.Helpers;

public class EnumDescriptionConverter : IValueConverter
{
    private string GetEnumDescription(Enum enumObj)
    {
        var fieldInfo = enumObj.GetType().GetField(enumObj.ToString());

        var attribArray = fieldInfo.GetCustomAttributes(false);

        if (attribArray.Length == 0)
        {
            return enumObj.ToString();
        }

        var attrib = attribArray[0] as DescriptionAttribute;
        return attrib.Description;

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