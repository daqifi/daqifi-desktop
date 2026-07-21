using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Daqifi.Desktop.Helpers;

namespace Daqifi.Desktop.Test.Helpers;

[TestClass]
public class BooleanToVisibilityConverterTests
{
    private BooleanToVisibilityConverter _converter = null!;

    [TestInitialize]
    public void Setup()
    {
        _converter = new BooleanToVisibilityConverter();
    }

    [TestMethod]
    public void Convert_True_ReturnsVisible()
    {
        // Arrange
        object value = true;

        // Act
        var result = _converter.Convert(value, typeof(Visibility), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(Visibility.Visible, result);
    }

    [TestMethod]
    public void Convert_False_ReturnsCollapsed()
    {
        // Arrange
        object value = false;

        // Act
        var result = _converter.Convert(value, typeof(Visibility), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(Visibility.Collapsed, result);
    }

    [TestMethod]
    public void Convert_NonBoolean_ReturnsCollapsed()
    {
        // Arrange
        var nonBooleanValues = new object[] { null!, "true", 1 };

        foreach (var value in nonBooleanValues)
        {
            // Act
            var result = _converter.Convert(value, typeof(Visibility), null!, CultureInfo.InvariantCulture);

            // Assert
            Assert.AreEqual(Visibility.Collapsed, result, $"Failed for value: {value ?? "null"}");
        }
    }

    [TestMethod]
    public void ConvertBack_Visible_ReturnsTrue()
    {
        // Arrange
        object value = Visibility.Visible;

        // Act
        var result = _converter.ConvertBack(value, typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(true, result);
    }

    [TestMethod]
    public void ConvertBack_Collapsed_ReturnsFalse()
    {
        // Arrange
        object value = Visibility.Collapsed;

        // Act
        var result = _converter.ConvertBack(value, typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(false, result);
    }

    [TestMethod]
    public void Convert_InvertedConfiguration_MapsTrueToCollapsed()
    {
        // Arrange — mirrors the App.xaml "InvertedBoolToVis" resource
        // (True="Collapsed" False="Visible").
        var inverted = new BooleanToVisibilityConverter
        {
            True = Visibility.Collapsed,
            False = Visibility.Visible
        };

        // Act
        var whenTrue = inverted.Convert(true, typeof(Visibility), null!, CultureInfo.InvariantCulture);
        var whenFalse = inverted.Convert(false, typeof(Visibility), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(Visibility.Collapsed, whenTrue);
        Assert.AreEqual(Visibility.Visible, whenFalse);
    }
}
