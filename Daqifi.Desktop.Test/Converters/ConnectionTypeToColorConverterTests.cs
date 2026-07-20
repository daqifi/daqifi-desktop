using System.Globalization;
using System.Windows.Media;
using Daqifi.Desktop.Converters;
using Daqifi.Desktop.Device;

namespace Daqifi.Desktop.Test.Converters;

[TestClass]
public class ConnectionTypeToColorConverterTests
{
    private ConnectionTypeToColorConverter _converter;

    [TestInitialize]
    public void Setup()
    {
        _converter = new ConnectionTypeToColorConverter();
    }

    [TestMethod]
    public void Convert_Usb_ReturnsGreenBrush()
    {
        // Arrange
        var value = ConnectionType.Usb;

        // Act
        var result = _converter.Convert(value, typeof(Brush), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.IsInstanceOfType<SolidColorBrush>(result);
        Assert.AreEqual(Colors.Green, ((SolidColorBrush)result).Color);
    }

    [TestMethod]
    public void Convert_Wifi_ReturnsOrangeBrush()
    {
        // Arrange
        var value = ConnectionType.Wifi;

        // Act
        var result = _converter.Convert(value, typeof(Brush), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.IsInstanceOfType<SolidColorBrush>(result);
        Assert.AreEqual(Colors.Orange, ((SolidColorBrush)result).Color);
    }

    [TestMethod]
    public void Convert_NonConnectionTypeValue_ReturnsGrayBrush()
    {
        // Arrange
        object value = "not a connection type";

        // Act
        var result = _converter.Convert(value, typeof(Brush), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.IsInstanceOfType<SolidColorBrush>(result);
        Assert.AreEqual(Colors.Gray, ((SolidColorBrush)result).Color);
    }

    [TestMethod]
    public void ConvertBack_Always_ThrowsNotImplemented()
    {
        // Arrange
        var value = new SolidColorBrush(Colors.Green);

        // Act & Assert
        Assert.ThrowsExactly<NotImplementedException>(() =>
            _converter.ConvertBack(value, typeof(ConnectionType), null, CultureInfo.InvariantCulture));
    }
}
