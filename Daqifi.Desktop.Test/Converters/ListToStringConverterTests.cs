using System.Globalization;
using Daqifi.Desktop.Converters;

namespace Daqifi.Desktop.Test.Converters;

[TestClass]
public class ListToStringConverterTests
{
    private ListToStringConverter _converter;

    [TestInitialize]
    public void Setup()
    {
        _converter = new ListToStringConverter();
    }

    [TestMethod]
    public void Convert_NullValue_ReturnsEmptyString()
    {
        // Arrange
        object value = null;

        // Act
        var result = _converter.Convert(value, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(string.Empty, result);
    }

    [TestMethod]
    public void Convert_EmptyList_ReturnsEmptyString()
    {
        // Arrange
        var value = new List<string>();

        // Act
        var result = _converter.Convert(value, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual(string.Empty, result);
    }

    [TestMethod]
    public void Convert_DefaultFormat_JoinsWithCommaSpace()
    {
        // Arrange
        var value = new List<string> { "a", "b", "c" };

        // Act
        var result = _converter.Convert(value, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual("a, b, c", result);
    }

    [TestMethod]
    public void Convert_BracketsFormat_WrapsInBrackets()
    {
        // Arrange
        var value = new List<string> { "a", "b" };

        // Act
        var result = _converter.Convert(value, typeof(string), "brackets", CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual("[a, b]", result);
    }

    [TestMethod]
    public void Convert_ShortFormat_WithinThreeItems_JoinsWithoutEllipsis()
    {
        // Arrange
        var value = new List<string> { "a", "b", "c" };

        // Act
        var result = _converter.Convert(value, typeof(string), "short", CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual("a,b,c", result);
    }

    [TestMethod]
    public void Convert_ShortFormat_MoreThanThreeItems_TruncatesWithEllipsis()
    {
        // Arrange
        var value = new List<string> { "a", "b", "c", "d", "e" };

        // Act
        var result = _converter.Convert(value, typeof(string), "short", CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual("a,b,c...", result);
    }

    [TestMethod]
    public void Convert_FormatParameterIsCaseInsensitive()
    {
        // Arrange
        var value = new List<string> { "a", "b" };

        // Act
        var result = _converter.Convert(value, typeof(string), "BRACKETS", CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual("[a, b]", result);
    }

    [TestMethod]
    public void Convert_NonEnumerableValue_ReturnsToString()
    {
        // Arrange
        object value = 42;

        // Act
        var result = _converter.Convert(value, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.AreEqual("42", result);
    }

    [TestMethod]
    public void ConvertBack_Always_ThrowsNotImplemented()
    {
        // Arrange
        object value = "a, b, c";

        // Act & Assert
        Assert.ThrowsExactly<NotImplementedException>(() =>
            _converter.ConvertBack(value, typeof(object), null, CultureInfo.InvariantCulture));
    }
}
