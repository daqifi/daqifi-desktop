using System.Globalization;
using System.Windows.Data;
using Daqifi.Desktop.Helpers;

namespace Daqifi.Desktop.Test.Helpers;

[TestClass]
public class BooleanConverterTests
{
    private IValueConverter _converter = null!;

    [TestInitialize]
    public void Setup()
    {
        // "Y" for true, "N" for false — a simple concrete instantiation of the generic base.
        _converter = new BooleanConverter<string>("Y", "N");
    }

    [TestMethod]
    public void Convert_TrueValue_ReturnsTrueValue()
    {
        // Arrange
        object value = true;

        // Act
        var result = _converter.Convert(value, typeof(string), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual("Y", result);
    }

    [TestMethod]
    public void Convert_FalseValue_ReturnsFalseValue()
    {
        // Arrange
        object value = false;

        // Act
        var result = _converter.Convert(value, typeof(string), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual("N", result);
    }

    [TestMethod]
    public void Convert_NonBooleanValue_ReturnsFalseValue()
    {
        // Arrange
        var nonBooleanValues = new object[] { null!, "true", 1, 0, new object() };

        foreach (var value in nonBooleanValues)
        {
            // Act
            var result = _converter.Convert(value, typeof(string), null!, CultureInfo.InvariantCulture);

            // Assert
            Assert.AreEqual("N", result, $"Failed for value: {value ?? "null"}");
        }
    }

    [TestMethod]
    public void ConvertBack_TrueValue_ReturnsTrue()
    {
        // Arrange
        object value = "Y";

        // Act
        var result = _converter.ConvertBack(value, typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(true, result);
    }

    [TestMethod]
    public void ConvertBack_FalseValue_ReturnsFalse()
    {
        // Arrange
        object value = "N";

        // Act
        var result = _converter.ConvertBack(value, typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(false, result);
    }

    [TestMethod]
    public void ConvertBack_UnknownValue_ReturnsFalse()
    {
        // Arrange — a value of the right type but equal to neither True nor False.
        object value = "other";

        // Act
        var result = _converter.ConvertBack(value, typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(false, result);
    }

    [TestMethod]
    public void ConvertBack_WrongTypeValue_ReturnsFalse()
    {
        // Arrange — not a T (string); the `value is T` guard must fail.
        object value = 123;

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
        var forward = _converter.Convert(original, typeof(string), null!, CultureInfo.InvariantCulture);
        var back = _converter.ConvertBack(forward, typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(true, back);
    }
}
