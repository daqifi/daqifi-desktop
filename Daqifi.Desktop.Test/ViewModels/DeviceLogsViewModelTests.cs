using Daqifi.Core.Device.SdCard;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Models;
using Daqifi.Desktop.ViewModels;
using Moq;

namespace Daqifi.Desktop.Test.ViewModels;

[TestClass]
public class DeviceLogsViewModelTests
{
    private Mock<IStreamingDevice> _mockDevice;
    private DeviceLogsViewModel _viewModel;

    [TestInitialize]
    public void Setup()
    {
        _mockDevice = new Mock<IStreamingDevice>();
        _mockDevice.Setup(d => d.ConnectionType).Returns(ConnectionType.Usb);
        _mockDevice.Setup(d => d.DeviceSerialNo).Returns("DAQ-TEST-001");
        _mockDevice.Setup(d => d.DeviceVersion).Returns("1.0.0");
        _mockDevice.Setup(d => d.SdCardFiles).Returns(new List<SdCardFile>().AsReadOnly());
        _mockDevice.Setup(d => d.DeviceDisplayName).Returns("DAQ-TEST-001");

        _viewModel = new DeviceLogsViewModel();
        _viewModel.SelectedDevice = _mockDevice.Object;
    }

    [TestMethod]
    public async Task RefreshFiles_Success_SetsSdCardStateOk()
    {
        _mockDevice.Setup(d => d.RefreshSdCardFiles()); // no-op

        await _viewModel.RefreshFilesAsync();

        Assert.AreEqual(SdCardState.Ok, _viewModel.SdCardState);
    }

    [TestMethod]
    public async Task RefreshFiles_Success_WithFiles_HasFilesIsTrue()
    {
        var files = new List<SdCardFile>
        {
            new SdCardFile { FileName = "LOG001.bin", CreatedDate = DateTime.UtcNow }
        };
        _mockDevice.Setup(d => d.SdCardFiles).Returns(files.AsReadOnly());

        await _viewModel.RefreshFilesAsync();

        Assert.AreEqual(SdCardState.Ok, _viewModel.SdCardState);
        Assert.IsTrue(_viewModel.HasFiles);
        Assert.IsFalse(_viewModel.HasNoFiles);
        Assert.IsFalse(_viewModel.HasSdCardError);
        Assert.IsFalse(_viewModel.HasSdCardNotPresent);
    }

    [TestMethod]
    public async Task RefreshFiles_Success_EmptyCard_HasNoFilesIsTrue()
    {
        _mockDevice.Setup(d => d.SdCardFiles).Returns(new List<SdCardFile>().AsReadOnly());

        await _viewModel.RefreshFilesAsync();

        Assert.AreEqual(SdCardState.Ok, _viewModel.SdCardState);
        Assert.IsTrue(_viewModel.HasNoFiles);
        Assert.IsFalse(_viewModel.HasFiles);
        Assert.IsFalse(_viewModel.HasSdCardError);
        Assert.IsFalse(_viewModel.HasSdCardNotPresent);
    }

    [TestMethod]
    public async Task RefreshFiles_SdCardNotPresent_SetsSdCardStateNotPresent()
    {
        _mockDevice
            .Setup(d => d.RefreshSdCardFiles())
            .Throws(new SdCardNotPresentException(new List<string>(), "No card"));

        await _viewModel.RefreshFilesAsync();

        Assert.AreEqual(SdCardState.NotPresent, _viewModel.SdCardState);
        Assert.IsTrue(_viewModel.HasSdCardNotPresent);
        Assert.IsFalse(_viewModel.HasSdCardError);
        Assert.IsFalse(_viewModel.HasNoFiles);
        Assert.IsFalse(_viewModel.HasFiles);
    }

    [TestMethod]
    public async Task RefreshFiles_SdCardFilesystemException_SetsSdCardStateError()
    {
        const string deviceMessage = "FS corrupt";
        _mockDevice
            .Setup(d => d.RefreshSdCardFiles())
            .Throws(new SdCardFilesystemException(new List<string>(), "Filesystem error", deviceMessage));

        await _viewModel.RefreshFilesAsync();

        Assert.AreEqual(SdCardState.Error, _viewModel.SdCardState);
        Assert.IsTrue(_viewModel.HasSdCardError);
        Assert.AreEqual(deviceMessage, _viewModel.SdCardErrorMessage);
        Assert.IsFalse(_viewModel.HasSdCardNotPresent);
        Assert.IsFalse(_viewModel.HasNoFiles);
        Assert.IsFalse(_viewModel.HasFiles);
    }

    [TestMethod]
    public async Task RefreshFiles_SdCardOperationException_SetsSdCardStateError()
    {
        const string scpiError = "-200,\"Execution error\"";
        _mockDevice
            .Setup(d => d.RefreshSdCardFiles())
            .Throws(new SdCardOperationException("SCPI error", new List<string>(), scpiError, null));

        await _viewModel.RefreshFilesAsync();

        Assert.AreEqual(SdCardState.Error, _viewModel.SdCardState);
        Assert.IsTrue(_viewModel.HasSdCardError);
        Assert.AreEqual(scpiError, _viewModel.SdCardErrorMessage);
        Assert.IsFalse(_viewModel.HasSdCardNotPresent);
    }

    [TestMethod]
    public async Task RefreshFiles_GenericException_SetsSdCardStateError()
    {
        const string errorMessage = "Connection lost";
        _mockDevice
            .Setup(d => d.RefreshSdCardFiles())
            .Throws(new InvalidOperationException(errorMessage));

        await _viewModel.RefreshFilesAsync();

        Assert.AreEqual(SdCardState.Error, _viewModel.SdCardState);
        Assert.IsTrue(_viewModel.HasSdCardError);
        Assert.AreEqual(errorMessage, _viewModel.SdCardErrorMessage);
    }

    [TestMethod]
    public async Task RefreshFiles_AfterError_SuccessResetsErrorState()
    {
        _mockDevice
            .Setup(d => d.RefreshSdCardFiles())
            .Throws(new SdCardNotPresentException(new List<string>(), "No card"));

        await _viewModel.RefreshFilesAsync();
        Assert.AreEqual(SdCardState.NotPresent, _viewModel.SdCardState);

        _mockDevice.Setup(d => d.RefreshSdCardFiles()); // no-op on second call
        await _viewModel.RefreshFilesAsync();

        Assert.AreEqual(SdCardState.Ok, _viewModel.SdCardState);
        Assert.IsFalse(_viewModel.HasSdCardNotPresent);
        Assert.IsFalse(_viewModel.HasSdCardError);
    }

    [TestMethod]
    public async Task RefreshFiles_IsBusyIsFalseAfterCompletion()
    {
        _mockDevice.Setup(d => d.RefreshSdCardFiles());

        await _viewModel.RefreshFilesAsync();

        Assert.IsFalse(_viewModel.IsBusy);
        Assert.IsTrue(string.IsNullOrEmpty(_viewModel.BusyMessage));
    }

    [TestMethod]
    public async Task RefreshFiles_IsBusyIsFalseAfterException()
    {
        _mockDevice
            .Setup(d => d.RefreshSdCardFiles())
            .Throws(new SdCardFilesystemException(new List<string>(), "error", "FS error"));

        await _viewModel.RefreshFilesAsync();

        Assert.IsFalse(_viewModel.IsBusy);
        Assert.IsTrue(string.IsNullOrEmpty(_viewModel.BusyMessage));
    }

    [TestMethod]
    public void SdCardStatusLine_WhenOkWithFiles_ShowsFileCount()
    {
        _viewModel.SdCardState = SdCardState.Ok;
        _viewModel.DeviceFiles.Add(new SdCardFile { FileName = "LOG001.bin" });
        _viewModel.DeviceFiles.Add(new SdCardFile { FileName = "LOG002.bin" });

        Assert.IsTrue(_viewModel.SdCardStatusLine.Contains("2 files"), $"Expected '2 files' in '{_viewModel.SdCardStatusLine}'");
    }

    [TestMethod]
    public void SdCardStatusLine_WhenNotPresent_ShowsMessage()
    {
        _viewModel.SdCardState = SdCardState.NotPresent;

        Assert.IsTrue(_viewModel.SdCardStatusLine.Contains("No SD card"));
    }

    [TestMethod]
    public void SdCardStatusLine_WhenError_IncludesErrorMessage()
    {
        _viewModel.SdCardState = SdCardState.Error;
        _viewModel.SdCardErrorMessage = "-200 Execution error";

        Assert.IsTrue(_viewModel.SdCardStatusLine.Contains("-200 Execution error"));
    }

    [TestMethod]
    public void RefreshFiles_SkipsWhenNoDeviceSelected()
    {
        _viewModel.SelectedDevice = null;

        // Should not throw; state stays Unknown
        var task = _viewModel.RefreshFilesAsync();
        Assert.AreEqual(SdCardState.Unknown, _viewModel.SdCardState);
    }

    [TestMethod]
    public void CanAccessSdCard_WhenUsbConnected_IsTrue()
    {
        Assert.IsTrue(_viewModel.CanAccessSdCard);
    }

    [TestMethod]
    public void CanAccessSdCard_WhenWifiConnected_IsFalse()
    {
        _mockDevice.Setup(d => d.ConnectionType).Returns(ConnectionType.Wifi);
        _viewModel.SelectedDevice = _mockDevice.Object;

        Assert.IsFalse(_viewModel.CanAccessSdCard);
    }
}
