using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Daqifi.Desktop.Helpers;

namespace Daqifi.Desktop.Test.Helpers;

[TestClass]
public class IntToVisibilityConverterTests
{
    private IValueConverter _converter;

    [TestInitialize]
    public void Setup()
    {
        _converter = new IntToVisibilityConverter();
    }

    [TestMethod]
    public void Convert_PositiveCount_ReturnsVisible()
    {
        // Arrange
        var positiveCounts = new object[] { 1, 5, int.MaxValue };

        foreach (var value in positiveCounts)
        {
            // Act
            var result = _converter.Convert(value, typeof(Visibility), null, CultureInfo.InvariantCulture);

            // Assert
            Assert.AreEqual(Visibility.Visible, result, $"Failed for value: {value}");
        }
    }

    [TestMethod]
    public void Convert_Zero_ReturnsCollapsed()
    {
        // Arrange
        object value = 0;

        // Act
        var result = _converter.Convert(value, typeof(Visibility), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(Visibility.Collapsed, result);
    }

    [TestMethod]
    public void Convert_NegativeCount_ReturnsCollapsed()
    {
        // Arrange
        var negativeCounts = new object[] { -1, int.MinValue };

        foreach (var value in negativeCounts)
        {
            // Act
            var result = _converter.Convert(value, typeof(Visibility), null, CultureInfo.InvariantCulture);

            // Assert
            Assert.AreEqual(Visibility.Collapsed, result, $"Failed for value: {value}");
        }
    }

    [TestMethod]
    public void Convert_NonInteger_ReturnsCollapsed()
    {
        // Arrange — anything not boxed as int (including null and other numeric types).
        var nonIntegers = new object[] { null, "5", 5.0, 5L, true };

        foreach (var value in nonIntegers)
        {
            // Act
            var result = _converter.Convert(value, typeof(Visibility), null, CultureInfo.InvariantCulture);

            // Assert
            Assert.AreEqual(Visibility.Collapsed, result, $"Failed for value: {value ?? "null"}");
        }
    }

    [TestMethod]
    public void ConvertBack_Always_ThrowsNotImplementedException()
    {
        // Arrange
        object value = Visibility.Visible;

        // Act & Assert
        Assert.ThrowsExactly<NotImplementedException>(() =>
            _converter.ConvertBack(value, typeof(int), null, CultureInfo.InvariantCulture));
    }
}
