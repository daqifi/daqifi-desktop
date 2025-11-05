using Daqifi.Desktop.Device;
using Daqifi.Desktop.Models;
using Daqifi.Desktop.Services;
using Moq;

namespace Daqifi.Desktop.Test.Services;

[TestClass]
public class SdCardFileServiceTests
{
    #region Test Setup
    private Mock<IFileTypeDetector> _mockFileTypeDetector;
    private SdCardFileService _service;

    [TestInitialize]
    public void Setup()
    {
        _mockFileTypeDetector = new Mock<IFileTypeDetector>();
        _service = new SdCardFileService(_mockFileTypeDetector.Object);
    }
    #endregion

    #region SaveToFileAsync Tests
    [TestMethod]
    public async Task SaveToFileAsync_ValidInput_ShouldReturnTrue()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var content = new byte[] { 1, 2, 3, 4, 5 };

        try
        {
            // Act
            var result = await _service.SaveToFileAsync(tempFile, content);

            // Assert
            Assert.IsTrue(result);
            Assert.IsTrue(File.Exists(tempFile));
            var savedContent = await File.ReadAllBytesAsync(tempFile);
            CollectionAssert.AreEqual(content, savedContent);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [TestMethod]
    public async Task SaveToFileAsync_EmptyFilePath_ShouldReturnFalse()
    {
        // Arrange
        var filePath = "";
        var content = new byte[] { 1, 2, 3 };

        // Act
        var result = await _service.SaveToFileAsync(filePath, content);

        // Assert
        Assert.IsFalse(result);
    }

    [TestMethod]
    public async Task SaveToFileAsync_NullContent_ShouldReturnFalse()
    {
        // Arrange
        var filePath = Path.GetTempFileName();
        byte[] content = null;

        try
        {
            // Act
            var result = await _service.SaveToFileAsync(filePath, content);

            // Assert
            Assert.IsFalse(result);
        }
        finally
        {
            // Cleanup
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [TestMethod]
    public async Task SaveToFileAsync_EmptyContent_ShouldReturnFalse()
    {
        // Arrange
        var filePath = Path.GetTempFileName();
        var content = Array.Empty<byte>();

        try
        {
            // Act
            var result = await _service.SaveToFileAsync(filePath, content);

            // Assert
            Assert.IsFalse(result);
        }
        finally
        {
            // Cleanup
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [TestMethod]
    public async Task SaveToFileAsync_NonExistentDirectory_ShouldCreateDirectoryAndReturnTrue()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var filePath = Path.Combine(tempDir, "test.bin");
        var content = new byte[] { 1, 2, 3 };

        try
        {
            // Act
            var result = await _service.SaveToFileAsync(filePath, content);

            // Assert
            Assert.IsTrue(result);
            Assert.IsTrue(Directory.Exists(tempDir));
            Assert.IsTrue(File.Exists(filePath));
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [TestMethod]
    public async Task SaveToFileAsync_OverwriteExistingFile_ShouldSucceed()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var originalContent = new byte[] { 1, 2, 3 };
        var newContent = new byte[] { 4, 5, 6, 7, 8 };

        try
        {
            await File.WriteAllBytesAsync(tempFile, originalContent);

            // Act
            var result = await _service.SaveToFileAsync(tempFile, newContent);

            // Assert
            Assert.IsTrue(result);
            var savedContent = await File.ReadAllBytesAsync(tempFile);
            CollectionAssert.AreEqual(newContent, savedContent);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
    #endregion

    #region DownloadFileAsync Tests
    [TestMethod]
    public async Task DownloadFileAsync_NullDevice_ShouldReturnFailure()
    {
        // Arrange
        IStreamingDevice device = null;
        var fileName = "test.bin";

        // Act
        var result = await _service.DownloadFileAsync(device, fileName);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.AreEqual("Device is null", result.ErrorMessage);
    }

    [TestMethod]
    public async Task DownloadFileAsync_EmptyFileName_ShouldReturnFailure()
    {
        // Arrange
        var mockDevice = new Mock<IStreamingDevice>();
        var fileName = "";

        // Act
        var result = await _service.DownloadFileAsync(mockDevice.Object, fileName);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.AreEqual("File name is empty", result.ErrorMessage);
    }

    [TestMethod]
    public async Task DownloadFileAsync_NullFileName_ShouldReturnFailure()
    {
        // Arrange
        var mockDevice = new Mock<IStreamingDevice>();
        string fileName = null;

        // Act
        var result = await _service.DownloadFileAsync(mockDevice.Object, fileName);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.AreEqual("File name is empty", result.ErrorMessage);
    }
    #endregion

    #region FileDownloadResult Tests
    [TestMethod]
    public void FileDownloadResult_CreateSuccess_ShouldHaveSuccessTrue()
    {
        // Arrange
        var content = new byte[] { 1, 2, 3 };
        var fileType = SdCardFileType.Protobuf;

        // Act
        var result = FileDownloadResult.CreateSuccess(content, fileType);

        // Assert
        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.Content);
        CollectionAssert.AreEqual(content, result.Content);
        Assert.AreEqual(fileType, result.FileType);
        Assert.IsNull(result.ErrorMessage);
    }

    [TestMethod]
    public void FileDownloadResult_CreateFailure_ShouldHaveSuccessFalse()
    {
        // Arrange
        var errorMessage = "Test error";

        // Act
        var result = FileDownloadResult.CreateFailure(errorMessage);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsNull(result.Content);
        Assert.AreEqual(SdCardFileType.Unknown, result.FileType);
        Assert.AreEqual(errorMessage, result.ErrorMessage);
    }
    #endregion

    #region Integration Tests
    [TestMethod]
    public async Task SaveAndReadFile_RoundTrip_ShouldPreserveContent()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var originalContent = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };

        try
        {
            // Act
            var saveResult = await _service.SaveToFileAsync(tempFile, originalContent);
            var readContent = await File.ReadAllBytesAsync(tempFile);

            // Assert
            Assert.IsTrue(saveResult);
            CollectionAssert.AreEqual(originalContent, readContent);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [TestMethod]
    public async Task SaveToFileAsync_CancellationRequested_ShouldThrowOperationCanceledException()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var content = new byte[] { 1, 2, 3 };
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        try
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () =>
            {
                await _service.SaveToFileAsync(tempFile, content, cts.Token);
            });
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
    #endregion
}
