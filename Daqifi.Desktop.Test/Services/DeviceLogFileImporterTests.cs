using Daqifi.Desktop.Device;
using Daqifi.Desktop.Services;
using Moq;

namespace Daqifi.Desktop.Test.Services;

[TestClass]
public class DeviceLogFileImporterTests
{
    #region Test Setup
    private DeviceLogFileImporter _importer;
    private Mock<IStreamingDevice> _mockDevice;

    [TestInitialize]
    public void Setup()
    {
        _importer = new DeviceLogFileImporter();
        _mockDevice = new Mock<IStreamingDevice>();
        _mockDevice.Setup(d => d.Name).Returns("TestDevice");
        _mockDevice.Setup(d => d.DeviceSerialNo).Returns("12345");
    }
    #endregion

    #region ImportProtobufFileAsync Tests
    [TestMethod]
    public async Task ImportProtobufFileAsync_NullContent_ShouldReturnFailure()
    {
        // Arrange
        byte[] content = null;
        var fileName = "test.bin";

        // Act
        var result = await _importer.ImportProtobufFileAsync(content, fileName, _mockDevice.Object);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.AreEqual("File content is empty", result.ErrorMessage);
    }

    [TestMethod]
    public async Task ImportProtobufFileAsync_EmptyContent_ShouldReturnFailure()
    {
        // Arrange
        var content = Array.Empty<byte>();
        var fileName = "test.bin";

        // Act
        var result = await _importer.ImportProtobufFileAsync(content, fileName, _mockDevice.Object);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.AreEqual("File content is empty", result.ErrorMessage);
    }

    [TestMethod]
    public async Task ImportProtobufFileAsync_NullDevice_ShouldReturnFailure()
    {
        // Arrange
        var content = new byte[] { 1, 2, 3 };
        var fileName = "test.bin";
        IStreamingDevice device = null;

        // Act
        var result = await _importer.ImportProtobufFileAsync(content, fileName, device);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.AreEqual("Device is null", result.ErrorMessage);
    }

    [TestMethod]
    public async Task ImportProtobufFileAsync_InvalidProtobuf_ShouldReturnFailure()
    {
        // Arrange
        var content = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }; // Invalid protobuf data
        var fileName = "test.bin";

        // Act
        var result = await _importer.ImportProtobufFileAsync(content, fileName, _mockDevice.Object);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.ErrorMessage);
    }

    [TestMethod]
    public async Task ImportProtobufFileAsync_CancellationRequested_ShouldReturnFailure()
    {
        // Arrange
        var content = new byte[] { 1, 2, 3 };
        var fileName = "test.bin";
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act
        var result = await _importer.ImportProtobufFileAsync(content, fileName, _mockDevice.Object, cts.Token);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.AreEqual("Import was cancelled", result.ErrorMessage);
    }
    #endregion

    #region FileImportResult Tests
    [TestMethod]
    public void FileImportResult_CreateSuccess_ShouldHaveSuccessTrue()
    {
        // Arrange
        var sessionId = 42;
        var samplesImported = 100;

        // Act
        var result = FileImportResult.CreateSuccess(sessionId, samplesImported);

        // Assert
        Assert.IsTrue(result.Success);
        Assert.AreEqual(sessionId, result.LoggingSessionId);
        Assert.AreEqual(samplesImported, result.SamplesImported);
        Assert.IsNull(result.ErrorMessage);
    }

    [TestMethod]
    public void FileImportResult_CreateFailure_ShouldHaveSuccessFalse()
    {
        // Arrange
        var errorMessage = "Test error";

        // Act
        var result = FileImportResult.CreateFailure(errorMessage);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsNull(result.LoggingSessionId);
        Assert.AreEqual(0, result.SamplesImported);
        Assert.AreEqual(errorMessage, result.ErrorMessage);
    }
    #endregion

    #region Edge Cases
    [TestMethod]
    public async Task ImportProtobufFileAsync_EmptyFileName_ShouldStillAttemptImport()
    {
        // Arrange
        var content = new byte[] { 0x08, 0x01 }; // Minimal protobuf-like content
        var fileName = "";

        // Act
        var result = await _importer.ImportProtobufFileAsync(content, fileName, _mockDevice.Object);

        // Assert
        // Should attempt import even with empty filename
        Assert.IsNotNull(result);
    }

    [TestMethod]
    public async Task ImportProtobufFileAsync_LongFileName_ShouldHandleCorrectly()
    {
        // Arrange
        var content = new byte[] { 0x08, 0x01 };
        var fileName = new string('a', 255) + ".bin"; // Very long filename

        // Act
        var result = await _importer.ImportProtobufFileAsync(content, fileName, _mockDevice.Object);

        // Assert
        Assert.IsNotNull(result);
    }
    #endregion
}
