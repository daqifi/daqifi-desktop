using System.Globalization;
using System.Windows;
using Daqifi.Desktop.Converters;

namespace Daqifi.Desktop.Test.Converters;

[TestClass]
public class NotNullToVisibilityConverterTests
{
    private NotNullToVisibilityConverter _converter = null!;

    [TestInitialize]
    public void Setup()
    {
        _converter = new NotNullToVisibilityConverter();
    }

    [TestMethod]
    public void Convert_NonNullValue_ReturnsVisible()
    {
        // Arrange
        object value = "something";

        // Act
        var result = _converter.Convert(value, typeof(Visibility), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(Visibility.Visible, result);
    }

    [TestMethod]
    public void Convert_NullValue_ReturnsCollapsed()
    {
        // Arrange
        object value = null!;

        // Act
        var result = _converter.Convert(value, typeof(Visibility), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(Visibility.Collapsed, result);
    }

    [TestMethod]
    public void ConvertBack_Always_ThrowsNotImplemented()
    {
        // Arrange
        var value = Visibility.Visible;

        // Act & Assert
        Assert.ThrowsExactly<NotImplementedException>(() =>
            _converter.ConvertBack(value, typeof(object), null!, CultureInfo.InvariantCulture));
    }
}
