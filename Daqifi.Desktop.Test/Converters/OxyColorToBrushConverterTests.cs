using System.Globalization;
using System.Windows.Media;
using Daqifi.Desktop.Converters;
using OxyPlot;

namespace Daqifi.Desktop.Test.Converters;

[TestClass]
public class OxyColorToBrushConverterTests
{
    private OxyColorToBrushConverter _converter;

    [TestInitialize]
    public void Setup()
    {
        _converter = new OxyColorToBrushConverter();
    }

    [TestMethod]
    public void Convert_OxyColor_ReturnsMatchingSolidColorBrush()
    {
        // Arrange
        var oxyColor = OxyColor.FromArgb(200, 10, 20, 30);

        // Act
        var result = _converter.Convert(oxyColor, typeof(Brush), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.IsInstanceOfType<SolidColorBrush>(result);
        var color = ((SolidColorBrush)result).Color;
        Assert.AreEqual(Color.FromArgb(200, 10, 20, 30), color);
    }

    [TestMethod]
    public void Convert_NonOxyColorValue_ReturnsTransparentBrush()
    {
        // Arrange
        object value = "not a color";

        // Act
        var result = _converter.Convert(value, typeof(Brush), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreSame(Brushes.Transparent, result);
    }

    [TestMethod]
    public void ConvertBack_Always_ThrowsNotImplemented()
    {
        // Arrange
        var value = new SolidColorBrush(Colors.Red);

        // Act & Assert
        Assert.ThrowsExactly<NotImplementedException>(() =>
            _converter.ConvertBack(value, typeof(OxyColor), null, CultureInfo.InvariantCulture));
    }
}
