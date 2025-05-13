using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Daqifi.Desktop.Converters
{
    public class OverallStatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string status)
            {
                switch (status)
                {
                    case "Healthy":
                        return new SolidColorBrush(Colors.LimeGreen);
                    case "Warning":
                        return new SolidColorBrush(Colors.Gold);
                    case "Critical":
                        return new SolidColorBrush(Colors.Red);
                }
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 