using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using OxyPlot;

namespace Daqifi.Desktop.Converters
{
    public class OxyColorToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is OxyColor oxyColor)
            {
                return new SolidColorBrush(Color.FromArgb(oxyColor.A, oxyColor.R, oxyColor.G, oxyColor.B));
            }
            return Brushes.Transparent; // Default or fallback
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
