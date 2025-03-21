using System.Globalization;
using System.Windows.Data;
using Daqifi.Desktop.Device;

namespace Daqifi.Desktop.Converters;

public class ConnectionTypeToUsbConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ConnectionType connectionType)
        {
            return connectionType == ConnectionType.Usb;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}