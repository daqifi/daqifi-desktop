using Daqifi.Desktop.Models;
using Daqifi.Desktop.Services;

namespace Daqifi.Desktop.Test.Services;

[TestClass]
public class FileTypeDetectorTests
{
    #region Test Setup
    private FileTypeDetector _detector;

    [TestInitialize]
    public void Setup()
    {
        _detector = new FileTypeDetector();
    }
    #endregion

    #region Extension-Based Detection Tests
    [TestMethod]
    public void DetectFileType_BinExtension_ShouldReturnProtobuf()
    {
        // Arrange
        var fileName = "log_20241105_120000.bin";

        // Act
        var result = _detector.DetectFileType(fileName);

        // Assert
        Assert.AreEqual(SdCardFileType.Protobuf, result);
    }

    [TestMethod]
    public void DetectFileType_JsonExtension_ShouldReturnJson()
    {
        // Arrange
        var fileName = "data.json";

        // Act
        var result = _detector.DetectFileType(fileName);

        // Assert
        Assert.AreEqual(SdCardFileType.Json, result);
    }

    [TestMethod]
    public void DetectFileType_CsvExtension_ShouldReturnCsv()
    {
        // Arrange
        var fileName = "data.csv";

        // Act
        var result = _detector.DetectFileType(fileName);

        // Assert
        Assert.AreEqual(SdCardFileType.Csv, result);
    }

    [TestMethod]
    public void DetectFileType_ProtoExtension_ShouldReturnProtobuf()
    {
        // Arrange
        var fileName = "message.proto";

        // Act
        var result = _detector.DetectFileType(fileName);

        // Assert
        Assert.AreEqual(SdCardFileType.Protobuf, result);
    }

    [TestMethod]
    public void DetectFileType_PbExtension_ShouldReturnProtobuf()
    {
        // Arrange
        var fileName = "message.pb";

        // Act
        var result = _detector.DetectFileType(fileName);

        // Assert
        Assert.AreEqual(SdCardFileType.Protobuf, result);
    }

    [TestMethod]
    public void DetectFileType_UnknownExtension_ShouldReturnUnknown()
    {
        // Arrange
        var fileName = "data.txt";

        // Act
        var result = _detector.DetectFileType(fileName);

        // Assert
        Assert.AreEqual(SdCardFileType.Unknown, result);
    }

    [TestMethod]
    public void DetectFileType_EmptyFileName_ShouldReturnUnknown()
    {
        // Arrange
        var fileName = "";

        // Act
        var result = _detector.DetectFileType(fileName);

        // Assert
        Assert.AreEqual(SdCardFileType.Unknown, result);
    }

    [TestMethod]
    public void DetectFileType_NullFileName_ShouldReturnUnknown()
    {
        // Arrange
        string fileName = null;

        // Act
        var result = _detector.DetectFileType(fileName);

        // Assert
        Assert.AreEqual(SdCardFileType.Unknown, result);
    }

    [TestMethod]
    public void DetectFileType_CaseInsensitive_ShouldDetectCorrectly()
    {
        // Arrange
        var fileNameUpper = "DATA.JSON";
        var fileNameLower = "data.json";
        var fileNameMixed = "Data.JSON";

        // Act
        var resultUpper = _detector.DetectFileType(fileNameUpper);
        var resultLower = _detector.DetectFileType(fileNameLower);
        var resultMixed = _detector.DetectFileType(fileNameMixed);

        // Assert
        Assert.AreEqual(SdCardFileType.Json, resultUpper);
        Assert.AreEqual(SdCardFileType.Json, resultLower);
        Assert.AreEqual(SdCardFileType.Json, resultMixed);
    }
    #endregion

    #region Content-Based Detection Tests
    [TestMethod]
    public void DetectFileType_JsonContent_ShouldReturnJson()
    {
        // Arrange
        var fileName = "unknown.dat";
        var content = System.Text.Encoding.UTF8.GetBytes("{\"key\": \"value\"}");

        // Act
        var result = _detector.DetectFileType(fileName, content);

        // Assert
        Assert.AreEqual(SdCardFileType.Json, result);
    }

    [TestMethod]
    public void DetectFileType_JsonArrayContent_ShouldReturnJson()
    {
        // Arrange
        var fileName = "unknown.dat";
        var content = System.Text.Encoding.UTF8.GetBytes("[1, 2, 3]");

        // Act
        var result = _detector.DetectFileType(fileName, content);

        // Assert
        Assert.AreEqual(SdCardFileType.Json, result);
    }

    [TestMethod]
    public void DetectFileType_CsvContent_ShouldReturnCsv()
    {
        // Arrange
        var fileName = "unknown.dat";
        var content = System.Text.Encoding.UTF8.GetBytes("Time,Channel1,Channel2\r\n0.0,1.5,2.3\r\n0.1,1.6,2.4\r\n");

        // Act
        var result = _detector.DetectFileType(fileName, content);

        // Assert
        Assert.AreEqual(SdCardFileType.Csv, result);
    }

    [TestMethod]
    public void DetectFileType_BinaryContent_ShouldReturnProtobuf()
    {
        // Arrange
        var fileName = "unknown.dat";
        // Create content that looks like protobuf (with field tags)
        var content = new byte[] { 0x08, 0x96, 0x01, 0x12, 0x04, 0x74, 0x65, 0x73, 0x74, 0x1a, 0x03, 0x01, 0x02, 0x03 };

        // Act
        var result = _detector.DetectFileType(fileName, content);

        // Assert
        Assert.AreEqual(SdCardFileType.Protobuf, result);
    }

    [TestMethod]
    public void DetectFileType_EmptyContent_ShouldUseExtension()
    {
        // Arrange
        var fileName = "data.csv";
        var content = Array.Empty<byte>();

        // Act
        var result = _detector.DetectFileType(fileName, content);

        // Assert
        Assert.AreEqual(SdCardFileType.Csv, result);
    }

    [TestMethod]
    public void DetectFileType_ContentOverridesUnknownExtension_ShouldReturnJson()
    {
        // Arrange
        var fileName = "data.unknown";
        var content = System.Text.Encoding.UTF8.GetBytes("{\"test\": true}");

        // Act
        var result = _detector.DetectFileType(fileName, content);

        // Assert
        Assert.AreEqual(SdCardFileType.Json, result);
    }
    #endregion

    #region Edge Cases
    [TestMethod]
    public void DetectFileType_FileNameWithPath_ShouldDetectCorrectly()
    {
        // Arrange
        var fileName = "/path/to/file/data.csv";

        // Act
        var result = _detector.DetectFileType(fileName);

        // Assert
        Assert.AreEqual(SdCardFileType.Csv, result);
    }

    [TestMethod]
    public void DetectFileType_FileNameWithMultipleDots_ShouldUseLastExtension()
    {
        // Arrange
        var fileName = "my.backup.data.json";

        // Act
        var result = _detector.DetectFileType(fileName);

        // Assert
        Assert.AreEqual(SdCardFileType.Json, result);
    }

    [TestMethod]
    public void DetectFileType_NoExtension_ShouldReturnUnknown()
    {
        // Arrange
        var fileName = "datafile";

        // Act
        var result = _detector.DetectFileType(fileName);

        // Assert
        Assert.AreEqual(SdCardFileType.Unknown, result);
    }
    #endregion
}
