using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Daqifi.Desktop.Converters;

namespace Daqifi.Desktop.Test.Converters;

[TestClass]
public class BrushColorMatchConverterTests
{
    private BrushColorMatchConverter _converter = null!;

    [TestInitialize]
    public void Setup()
    {
        _converter = new BrushColorMatchConverter();
    }

    [TestMethod]
    public void Convert_SameColorDifferentInstances_TargetBool_ReturnsTrue()
    {
        // Arrange
        var values = new object[] { new SolidColorBrush(Colors.Red), new SolidColorBrush(Colors.Red) };

        // Act
        var result = _converter.Convert(values, typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(true, result);
    }

    [TestMethod]
    public void Convert_DifferentColors_TargetBool_ReturnsFalse()
    {
        // Arrange
        var values = new object[] { new SolidColorBrush(Colors.Red), new SolidColorBrush(Colors.Blue) };

        // Act
        var result = _converter.Convert(values, typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(false, result);
    }

    [TestMethod]
    public void Convert_SameColor_TargetVisibility_ReturnsVisible()
    {
        // Arrange
        var values = new object[] { new SolidColorBrush(Colors.Green), new SolidColorBrush(Colors.Green) };

        // Act
        var result = _converter.Convert(values, typeof(Visibility), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(Visibility.Visible, result);
    }

    [TestMethod]
    public void Convert_DifferentColors_TargetVisibility_ReturnsCollapsed()
    {
        // Arrange
        var values = new object[] { new SolidColorBrush(Colors.Green), new SolidColorBrush(Colors.Yellow) };

        // Act
        var result = _converter.Convert(values, typeof(Visibility), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(Visibility.Collapsed, result);
    }

    [TestMethod]
    public void Convert_FewerThanTwoValues_ReturnsFalse()
    {
        // Arrange
        var values = new object[] { new SolidColorBrush(Colors.Red) };

        // Act
        var result = _converter.Convert(values, typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(false, result);
    }

    [TestMethod]
    public void Convert_NonBrushValues_ReturnsFalse()
    {
        // Arrange
        var values = new object[] { "red", "red" };

        // Act
        var result = _converter.Convert(values, typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(false, result);
    }

    [TestMethod]
    public void ConvertBack_Always_ThrowsNotImplemented()
    {
        // Arrange
        var value = Visibility.Visible;

        // Act & Assert
        Assert.ThrowsExactly<NotImplementedException>(() =>
            _converter.ConvertBack(value, new[] { typeof(SolidColorBrush) }, null!, CultureInfo.InvariantCulture));
    }
}
