using System.Globalization;
using System.Windows.Data;
using Daqifi.Desktop.Helpers;

namespace Daqifi.Desktop.Test.Helpers;

[TestClass]
public class BooleanToInverseBoolConverterTests
{
    private BooleanToInverseBoolConverter _converter = null!;

    [TestInitialize]
    public void Setup()
    {
        _converter = new BooleanToInverseBoolConverter();
    }

    [TestMethod]
    public void Convert_True_ReturnsFalse()
    {
        // Arrange
        object value = true;

        // Act
        var result = _converter.Convert(value, typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(false, result);
    }

    [TestMethod]
    public void Convert_False_ReturnsTrue()
    {
        // Arrange
        object value = false;

        // Act
        var result = _converter.Convert(value, typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(true, result);
    }

    [TestMethod]
    public void Convert_NonBoolean_ReturnsFalse()
    {
        // Arrange
        var nonBooleanValues = new object[] { null!, "true", 1, 0 };

        foreach (var value in nonBooleanValues)
        {
            // Act
            var result = _converter.Convert(value, typeof(bool), null!, CultureInfo.InvariantCulture);

            // Assert
            Assert.AreEqual(false, result, $"Failed for value: {value ?? "null"}");
        }
    }

    [TestMethod]
    public void ConvertBack_True_ReturnsFalse()
    {
        // Arrange
        object value = true;

        // Act
        var result = _converter.ConvertBack(value, typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(false, result);
    }

    [TestMethod]
    public void ConvertBack_False_ReturnsTrue()
    {
        // Arrange
        object value = false;

        // Act
        var result = _converter.ConvertBack(value, typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(true, result);
    }

    [TestMethod]
    public void ConvertBack_NonBoolean_ReturnsFalse()
    {
        // Arrange
        object value = "not a bool";

        // Act
        var result = _converter.ConvertBack(value, typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(false, result);
    }

    [TestMethod]
    public void Convert_RoundTripsThroughConvertBack()
    {
        // Arrange
        object original = true;

        // Act
        var forward = _converter.Convert(original, typeof(bool), null!, CultureInfo.InvariantCulture);
        var back = _converter.ConvertBack(forward, typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(true, back);
    }
}
