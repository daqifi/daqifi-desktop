﻿using System.Globalization;
using System.Windows.Data;

namespace Daqifi.Desktop.Helpers;

public  class MyMultiValueConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        return values.Clone();
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        // Implement if you need to convert back
        throw new NotImplementedException();
    }
}