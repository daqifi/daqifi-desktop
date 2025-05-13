using System;
using System.Globalization;
using System.Windows.Data;

namespace Daqifi.Desktop.Converters
{
    public class MonitoringButtonContentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool enabled)
            {
                return enabled ? "Stop Monitoring" : "Start Monitoring";
            }
            return "Start Monitoring";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 