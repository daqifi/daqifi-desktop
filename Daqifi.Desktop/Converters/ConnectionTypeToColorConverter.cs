using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Daqifi.Desktop.Device;

namespace Daqifi.Desktop.Converters
{
    public class ConnectionTypeToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ConnectionType connectionType)
            {
                return connectionType switch
                {
                    ConnectionType.Usb => new SolidColorBrush(Colors.Green),
                    ConnectionType.Wifi => new SolidColorBrush(Colors.Orange),
                    _ => new SolidColorBrush(Colors.Gray)
                };
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 