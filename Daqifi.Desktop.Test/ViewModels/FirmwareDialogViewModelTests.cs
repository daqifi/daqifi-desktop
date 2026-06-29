using System.Diagnostics;
using Daqifi.Core.Device;
using Daqifi.Core.Firmware;
using Daqifi.Desktop.ViewModels;
using Moq;

namespace Daqifi.Desktop.Test.ViewModels;

/// <summary>
/// Covers the bootloader firmware dialog's "download the latest DAQiFi firmware" path: the dropdown
/// is populated from the latest release, the Upload command is gated until there's something to flash,
/// a no-file upload downloads the latest .hex before flashing, and a browsed .hex takes precedence
/// (no download).
/// </summary>
[TestClass]
public class FirmwareDialogViewModelTests
{
    private string _tempHexPath = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        // A real file so the dialog's File.Exists guard passes; the flash service is mocked.
        _tempHexPath = Path.Combine(Path.GetTempPath(), $"daqifi-fw-{Guid.NewGuid():N}.hex");
        File.WriteAllText(_tempHexPath, ":00000001FF");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_tempHexPath)) { File.Delete(_tempHexPath); }
    }

    private static Mock<IFirmwareDownloadService> DownloadServiceReturning(FirmwareReleaseInfo? latest)
    {
        var download = new Mock<IFirmwareDownloadService>();
        download
            .Setup(s => s.GetLatestReleaseAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(latest);
        return download;
    }

    private static FirmwareReleaseInfo Release(int major, int minor, int patch, string tag) => new()
    {
        Version = new FirmwareVersion(major, minor, patch, null, 0),
        TagName = tag,
        IsPreRelease = false
    };

    /// <summary>Waits for a fire-and-forget continuation to settle (the constructor's option load).</summary>
    private static void WaitUntil(Func<bool> condition, int timeoutMs = 2000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
        {
            Thread.Sleep(10);
        }
    }

    [TestMethod]
    public void Constructor_LoadsLatestFirmwareIntoDropdown()
    {
        var update = new Mock<IFirmwareUpdateService>();
        var download = DownloadServiceReturning(Release(3, 6, 1, "v3.6.1"));

        var vm = new FirmwareDialogViewModel("TestHid", firmwareUpdateService: update.Object, firmwareDownloadService: download.Object);

        WaitUntil(() => vm.AvailableFirmwares.Count > 0);
        Assert.AreEqual(1, vm.AvailableFirmwares.Count);
        Assert.IsNotNull(vm.SelectedFirmware);
        Assert.AreEqual("3.6.1", vm.SelectedFirmware!.Version);
        Assert.AreEqual("DAQiFi — 3.6.1", vm.SelectedFirmware.Display);
    }

    [TestMethod]
    public void Constructor_NoRelease_LeavesDropdownEmpty()
    {
        var update = new Mock<IFirmwareUpdateService>();
        var download = DownloadServiceReturning(null);

        var vm = new FirmwareDialogViewModel("TestHid", firmwareUpdateService: update.Object, firmwareDownloadService: download.Object);

        // Nothing to select; the upload command must be disabled until the user browses a file.
        Assert.AreEqual(0, vm.AvailableFirmwares.Count);
        Assert.IsNull(vm.SelectedFirmware);
        Assert.IsFalse(vm.UploadFirmwareCommand.CanExecute(null));
    }

    [TestMethod]
    public void CanUploadFirmware_TrueOnceAFileIsBrowsed()
    {
        var update = new Mock<IFirmwareUpdateService>();
        var download = DownloadServiceReturning(null);
        var vm = new FirmwareDialogViewModel("TestHid", firmwareUpdateService: update.Object, firmwareDownloadService: download.Object);

        Assert.IsFalse(vm.UploadFirmwareCommand.CanExecute(null), "Disabled with no file and no dropdown selection.");

        vm.FirmwareFilePath = _tempHexPath;

        Assert.IsTrue(vm.UploadFirmwareCommand.CanExecute(null), "Enabled once a .hex is browsed.");
    }

    [TestMethod]
    public async Task Upload_NoFileBrowsed_DownloadsLatestThenFlashes()
    {
        var update = new Mock<IFirmwareUpdateService>();
        update
            .Setup(s => s.UpdateFirmwareAsync(
                It.IsAny<IStreamingDevice>(),
                It.IsAny<string>(),
                It.IsAny<IProgress<FirmwareUpdateProgress>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var download = DownloadServiceReturning(Release(3, 6, 1, "v3.6.1"));
        download
            .Setup(s => s.DownloadLatestFirmwareAsync(
                It.IsAny<string>(), true, It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_tempHexPath);

        var vm = new FirmwareDialogViewModel("TestHid", firmwareUpdateService: update.Object, firmwareDownloadService: download.Object);
        // Stand in for the async dropdown load so we exercise the no-file path deterministically.
        vm.SelectedFirmware = new Models.FirmwareOption { DeviceModel = "DAQiFi", Version = "3.6.1" };

        await vm.UploadFirmwareCommand.ExecuteAsync(null);

        download.Verify(s => s.DownloadLatestFirmwareAsync(
            It.IsAny<string>(), true, It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()), Times.Once);
        update.Verify(s => s.UpdateFirmwareAsync(
            It.IsAny<IStreamingDevice>(), _tempHexPath, It.IsAny<IProgress<FirmwareUpdateProgress>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.IsTrue(vm.IsUploadComplete);
        Assert.IsFalse(vm.HasErrorOccured);
    }

    [TestMethod]
    public async Task Upload_FileBrowsed_TakesPrecedenceAndDoesNotDownload()
    {
        var update = new Mock<IFirmwareUpdateService>();
        update
            .Setup(s => s.UpdateFirmwareAsync(
                It.IsAny<IStreamingDevice>(),
                It.IsAny<string>(),
                It.IsAny<IProgress<FirmwareUpdateProgress>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var download = DownloadServiceReturning(Release(3, 6, 1, "v3.6.1"));

        var vm = new FirmwareDialogViewModel("TestHid", firmwareUpdateService: update.Object, firmwareDownloadService: download.Object)
        {
            FirmwareFilePath = _tempHexPath
        };

        await vm.UploadFirmwareCommand.ExecuteAsync(null);

        download.Verify(s => s.DownloadLatestFirmwareAsync(
            It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IProgress<int>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        update.Verify(s => s.UpdateFirmwareAsync(
            It.IsAny<IStreamingDevice>(), _tempHexPath, It.IsAny<IProgress<FirmwareUpdateProgress>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.IsTrue(vm.IsUploadComplete);
    }
}
