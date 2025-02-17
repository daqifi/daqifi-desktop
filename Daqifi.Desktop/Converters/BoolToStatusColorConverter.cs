using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Daqifi.Desktop.Converters
{
    public class BoolToStatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isConnected)
            {
                return new SolidColorBrush(isConnected ? Colors.LightGreen : Colors.IndianRed);
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 