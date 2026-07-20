using System.Globalization;
using System.Windows;
using Daqifi.Desktop.Converters;

namespace Daqifi.Desktop.Test.Converters;

[TestClass]
public class InvertedBoolToVisibilityConverterTests
{
    private InvertedBoolToVisibilityConverter _converter;

    [TestInitialize]
    public void Setup()
    {
        _converter = new InvertedBoolToVisibilityConverter();
    }

    [TestMethod]
    public void Convert_True_ReturnsCollapsed()
    {
        // Arrange
        const bool value = true;

        // Act
        var result = _converter.Convert(value, typeof(Visibility), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(Visibility.Collapsed, result);
    }

    [TestMethod]
    public void Convert_False_ReturnsVisible()
    {
        // Arrange
        const bool value = false;

        // Act
        var result = _converter.Convert(value, typeof(Visibility), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(Visibility.Visible, result);
    }

    [TestMethod]
    public void Convert_NonBooleanValue_ReturnsCollapsed()
    {
        // Arrange
        object value = "true";

        // Act
        var result = _converter.Convert(value, typeof(Visibility), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(Visibility.Collapsed, result);
    }

    [TestMethod]
    public void ConvertBack_Collapsed_ReturnsTrue()
    {
        // Arrange
        var value = Visibility.Collapsed;

        // Act
        var result = _converter.ConvertBack(value, typeof(bool), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(true, result);
    }

    [TestMethod]
    public void ConvertBack_Visible_ReturnsFalse()
    {
        // Arrange
        var value = Visibility.Visible;

        // Act
        var result = _converter.ConvertBack(value, typeof(bool), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(false, result);
    }

    [TestMethod]
    public void ConvertBack_NonVisibilityValue_ReturnsFalse()
    {
        // Arrange
        object value = 42;

        // Act
        var result = _converter.ConvertBack(value, typeof(bool), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(false, result);
    }
}
