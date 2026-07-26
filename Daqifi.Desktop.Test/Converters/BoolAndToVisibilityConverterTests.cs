using System.Globalization;
using System.Windows;
using Daqifi.Desktop.Converters;

namespace Daqifi.Desktop.Test.Converters;

[TestClass]
public class BoolAndToVisibilityConverterTests
{
    private BoolAndToVisibilityConverter _converter = null!;

    [TestInitialize]
    public void Setup()
    {
        _converter = new BoolAndToVisibilityConverter();
    }

    [TestMethod]
    public void Convert_AllTrue_ReturnsVisible()
    {
        // Arrange
        var values = new object[] { true, true, true };

        // Act
        var result = _converter.Convert(values, typeof(Visibility), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(Visibility.Visible, result);
    }

    [TestMethod]
    public void Convert_AnyFalse_ReturnsCollapsed()
    {
        // Arrange
        var values = new object[] { true, false, true };

        // Act
        var result = _converter.Convert(values, typeof(Visibility), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(Visibility.Collapsed, result);
    }

    [TestMethod]
    public void Convert_NonBooleanValue_ReturnsCollapsed()
    {
        // Arrange
        var values = new object[] { true, "true", true };

        // Act
        var result = _converter.Convert(values, typeof(Visibility), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(Visibility.Collapsed, result);
    }

    [TestMethod]
    public void Convert_EmptyValues_ReturnsCollapsed()
    {
        // Arrange
        var values = Array.Empty<object>();

        // Act
        var result = _converter.Convert(values, typeof(Visibility), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(Visibility.Collapsed, result);
    }

    [TestMethod]
    public void Convert_NullValues_ReturnsCollapsed()
    {
        // Arrange
        object[] values = null!;

        // Act
        var result = _converter.Convert(values, typeof(Visibility), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(Visibility.Collapsed, result);
    }

    [TestMethod]
    public void ConvertBack_Always_ThrowsNotSupported()
    {
        // Arrange
        var value = Visibility.Visible;

        // Act & Assert
        Assert.ThrowsExactly<NotSupportedException>(() =>
            _converter.ConvertBack(value, new[] { typeof(bool) }, null!, CultureInfo.InvariantCulture));
    }
}
