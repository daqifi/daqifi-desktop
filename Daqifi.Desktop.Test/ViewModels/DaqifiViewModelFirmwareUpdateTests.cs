using Daqifi.Core.Communication.Messages;
using Daqifi.Core.Communication.Producers;
using Daqifi.Core.Communication.Transport;
using Daqifi.Core.Device;
using Daqifi.Core.Firmware;
using Daqifi.Desktop.Device.SerialDevice;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Daqifi.Desktop.Test.ViewModels;

[TestClass]
public class DaqifiViewModelFirmwareUpdateTests
{
    private readonly List<string> _tempFiles = [];
    private readonly List<string> _tempDirectories = [];

    [TestCleanup]
    public void TestCleanup()
    {
        foreach (var tempFile in _tempFiles)
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }

        foreach (var tempDirectory in _tempDirectories)
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task UploadFirmware_ManualPackage_UsesConnectedCoreDeviceForPic32Only()
    {
        var dialogService = new Mock<IDialogService>();
        var firmwareUpdateService = new Mock<IFirmwareUpdateService>();
        var firmwareDownloadService = new Mock<IFirmwareDownloadService>();

        using var coreDevice = new TestCoreStreamingDevice("DAQiFi Core");
        coreDevice.Connect();

        var serialDevice = CreateSerialDeviceWithCoreDevice("COM7", coreDevice);
        var firmwareFilePath = CreateTempFile(".hex");

        Daqifi.Core.Device.IStreamingDevice? pic32Device = null;
        var wifiFactoryCalls = 0;

        firmwareUpdateService
            .Setup(service => service.UpdateFirmwareAsync(
                It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
                firmwareFilePath,
                It.IsAny<IProgress<FirmwareUpdateProgress>>(),
                It.IsAny<CancellationToken>()))
            .Callback<Daqifi.Core.Device.IStreamingDevice, string, IProgress<FirmwareUpdateProgress>?, CancellationToken>(
                (device, _, _, _) => pic32Device = device)
            .Returns(Task.CompletedTask);

        var viewModel = new DaqifiViewModel(
            dialogService.Object,
            firmwareUpdateService.Object,
            firmwareDownloadService.Object,
            NullLogger<FirmwareUpdateService>.Instance,
            (_, _) =>
            {
                wifiFactoryCalls++;
                return Mock.Of<IFirmwareUpdateService>();
            })
        {
            SelectedDevice = serialDevice,
            FirmwareFilePath = firmwareFilePath
        };

        await InvokeUploadFirmwareAsync(viewModel);

        Assert.AreSame(coreDevice, pic32Device);
        Assert.AreEqual(0, wifiFactoryCalls);
        Assert.IsTrue(viewModel.IsUploadComplete);
        Assert.IsFalse(viewModel.HasErrorOccured);

        firmwareUpdateService.Verify(service => service.UpdateFirmwareAsync(
            It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
            firmwareFilePath,
            It.IsAny<IProgress<FirmwareUpdateProgress>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        firmwareUpdateService.Verify(service => service.CheckWifiFirmwareStatusAsync(
            It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
            It.IsAny<CancellationToken>()), Times.Never);
        firmwareDownloadService.Verify(service => service.DownloadLatestFirmwareAsync(
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<IProgress<int>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task UploadFirmware_DownloadedPackage_UsesConnectedCoreDeviceForPic32AndWifi()
    {
        var dialogService = new Mock<IDialogService>();
        var pic32FirmwareUpdateService = new Mock<IFirmwareUpdateService>();
        var wifiFirmwareUpdateService = new Mock<IFirmwareUpdateService>();
        var firmwareDownloadService = new Mock<IFirmwareDownloadService>();

        using var coreDevice = new TestCoreStreamingDevice("DAQiFi Core");
        coreDevice.Connect();

        var serialDevice = CreateSerialDeviceWithCoreDevice("COM9", coreDevice);
        var pic32FirmwarePath = CreateTempFile(".hex");
        var wifiPackageDirectory = CreateTempDirectory();

        Daqifi.Core.Device.IStreamingDevice? pic32Device = null;
        Daqifi.Core.Device.IStreamingDevice? wifiDevice = null;
        string? wifiVersion = null;
        string? wifiPort = null;

        pic32FirmwareUpdateService
            .Setup(service => service.UpdateFirmwareAsync(
                It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
                pic32FirmwarePath,
                It.IsAny<IProgress<FirmwareUpdateProgress>>(),
                It.IsAny<CancellationToken>()))
            .Callback<Daqifi.Core.Device.IStreamingDevice, string, IProgress<FirmwareUpdateProgress>?, CancellationToken>(
                (device, _, _, _) => pic32Device = device)
            .Returns(Task.CompletedTask);

        wifiFirmwareUpdateService
            .Setup(service => service.UpdateWifiModuleAsync(
                It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
                wifiPackageDirectory,
                It.IsAny<IProgress<FirmwareUpdateProgress>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .Callback<Daqifi.Core.Device.IStreamingDevice, string, IProgress<FirmwareUpdateProgress>?, CancellationToken, bool>(
                (device, _, _, _, _) => wifiDevice = device)
            .Returns(Task.CompletedTask);

        firmwareDownloadService
            .Setup(service => service.DownloadLatestFirmwareAsync(
                It.IsAny<string>(),
                true,
                It.IsAny<IProgress<int>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(pic32FirmwarePath);

        pic32FirmwareUpdateService
            .Setup(service => service.CheckWifiFirmwareStatusAsync(
                It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateWifiStatus(WifiFirmwareStatusReason.UpdateAvailable, "19.3.0", "v19.7.0"));

        firmwareDownloadService
            .Setup(service => service.DownloadWifiFirmwareAsync(
                It.IsAny<string>(),
                It.IsAny<IProgress<int>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((wifiPackageDirectory, "v19.7.0"));

        var viewModel = new DaqifiViewModel(
            dialogService.Object,
            pic32FirmwareUpdateService.Object,
            firmwareDownloadService.Object,
            NullLogger<FirmwareUpdateService>.Instance,
            (version, port) =>
            {
                wifiVersion = version;
                wifiPort = port;
                return wifiFirmwareUpdateService.Object;
            })
        {
            SelectedDevice = serialDevice
        };

        await InvokeUploadFirmwareAsync(viewModel);

        Assert.AreSame(coreDevice, pic32Device);
        Assert.AreSame(coreDevice, wifiDevice);
        Assert.AreEqual("19.7.0", wifiVersion);
        Assert.AreEqual("COM9", wifiPort);
        Assert.IsTrue(viewModel.IsUploadComplete);
        Assert.IsFalse(viewModel.HasErrorOccured);

        AssertCommandSent(coreDevice, ScpiMessageProducer.TurnDeviceOn);
        AssertCommandSent(coreDevice, ScpiMessageProducer.SetLanFirmwareUpdateMode);
        AssertCommandSent(coreDevice, ScpiMessageProducer.SetUsbTransparencyMode(0));
        AssertCommandSent(coreDevice, ScpiMessageProducer.EnableNetworkLan);
        AssertCommandSent(coreDevice, ScpiMessageProducer.ApplyNetworkLan);
        AssertCommandSent(coreDevice, ScpiMessageProducer.SaveNetworkLan);

        pic32FirmwareUpdateService.Verify(service => service.UpdateFirmwareAsync(
            It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
            pic32FirmwarePath,
            It.IsAny<IProgress<FirmwareUpdateProgress>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        pic32FirmwareUpdateService.Verify(service => service.CheckWifiFirmwareStatusAsync(
            coreDevice,
            It.IsAny<CancellationToken>()), Times.Once);
        wifiFirmwareUpdateService.Verify(service => service.UpdateWifiModuleAsync(
            It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
            wifiPackageDirectory,
            It.IsAny<IProgress<FirmwareUpdateProgress>>(),
            It.IsAny<CancellationToken>(),
            true), Times.Once);
        firmwareDownloadService.Verify(service => service.GetLatestWifiReleaseAsync(
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task UploadFirmware_RunTwiceInSession_UpdatesWifiOnEachRun()
    {
        // Regression test for issue #599. The auto-update path used to write the downloaded
        // package path into the bound FirmwareFilePath property. Because the manual-vs-auto
        // decision is derived from FirmwareFilePath being non-empty, the SECOND in-session
        // update was misclassified as a manual upload and silently skipped the WiFi-module
        // flash (only an app restart restored correct behavior). Each auto-update run must
        // download firmware and flash the WiFi module independently, and FirmwareFilePath must
        // stay empty so subsequent runs remain auto-updates.
        var dialogService = new Mock<IDialogService>();
        var pic32FirmwareUpdateService = new Mock<IFirmwareUpdateService>();
        var wifiFirmwareUpdateService = new Mock<IFirmwareUpdateService>();
        var firmwareDownloadService = new Mock<IFirmwareDownloadService>();

        using var coreDevice = new TestCoreStreamingDevice("DAQiFi Core");
        coreDevice.Connect();

        var serialDevice = CreateSerialDeviceWithCoreDevice("COM9", coreDevice);
        var pic32FirmwarePath = CreateTempFile(".hex");
        var wifiPackageDirectory = CreateTempDirectory();
        var wifiFactoryCalls = 0;

        pic32FirmwareUpdateService
            .Setup(service => service.UpdateFirmwareAsync(
                It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
                pic32FirmwarePath,
                It.IsAny<IProgress<FirmwareUpdateProgress>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        pic32FirmwareUpdateService
            .Setup(service => service.CheckWifiFirmwareStatusAsync(
                It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateWifiStatus(WifiFirmwareStatusReason.UpdateAvailable, "19.3.0", "v19.7.0"));

        wifiFirmwareUpdateService
            .Setup(service => service.UpdateWifiModuleAsync(
                It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
                wifiPackageDirectory,
                It.IsAny<IProgress<FirmwareUpdateProgress>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        firmwareDownloadService
            .Setup(service => service.DownloadLatestFirmwareAsync(
                It.IsAny<string>(),
                true,
                It.IsAny<IProgress<int>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(pic32FirmwarePath);

        firmwareDownloadService
            .Setup(service => service.DownloadWifiFirmwareAsync(
                It.IsAny<string>(),
                It.IsAny<IProgress<int>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((wifiPackageDirectory, "v19.7.0"));

        var viewModel = new DaqifiViewModel(
            dialogService.Object,
            pic32FirmwareUpdateService.Object,
            firmwareDownloadService.Object,
            NullLogger<FirmwareUpdateService>.Instance,
            (_, _) =>
            {
                wifiFactoryCalls++;
                return wifiFirmwareUpdateService.Object;
            })
        {
            SelectedDevice = serialDevice
        };

        // First in-session update.
        await InvokeUploadFirmwareAsync(viewModel);
        Assert.IsTrue(viewModel.IsUploadComplete);
        Assert.IsFalse(viewModel.HasErrorOccured);
        Assert.IsTrue(
            string.IsNullOrWhiteSpace(viewModel.FirmwareFilePath),
            "Auto-update must not populate FirmwareFilePath; doing so reclassifies the next run as a manual upload.");

        // Second in-session update WITHOUT restarting the app.
        await InvokeUploadFirmwareAsync(viewModel);
        Assert.IsTrue(viewModel.IsUploadComplete);
        Assert.IsFalse(viewModel.HasErrorOccured);
        Assert.IsTrue(
            string.IsNullOrWhiteSpace(viewModel.FirmwareFilePath),
            "Auto-update must not populate FirmwareFilePath; doing so reclassifies the next run as a manual upload.");

        // The WiFi module must be flashed on BOTH runs, not just the first.
        Assert.AreEqual(2, wifiFactoryCalls);
        // The main (PIC32) firmware flash is not gated by the auto/manual classification, so it
        // must run on both passes; counting it guards against a future regression that gates it.
        pic32FirmwareUpdateService.Verify(service => service.UpdateFirmwareAsync(
            It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
            pic32FirmwarePath,
            It.IsAny<IProgress<FirmwareUpdateProgress>>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        firmwareDownloadService.Verify(service => service.DownloadLatestFirmwareAsync(
            It.IsAny<string>(),
            true,
            It.IsAny<IProgress<int>>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        pic32FirmwareUpdateService.Verify(service => service.CheckWifiFirmwareStatusAsync(
            It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        wifiFirmwareUpdateService.Verify(service => service.UpdateWifiModuleAsync(
            It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
            wifiPackageDirectory,
            It.IsAny<IProgress<FirmwareUpdateProgress>>(),
            It.IsAny<CancellationToken>(),
            true), Times.Exactly(2));
    }

    [TestMethod]
    public async Task UploadFirmware_ManualThenAuto_FlashesWifiOnAutoRun()
    {
        // Regression test for the symmetric case of issue #599. A manual .hex upload is
        // intentionally PIC32-only (no WiFi), and the auto/manual decision is derived from
        // FirmwareFilePath being non-empty. If a manual selection persisted across runs, the
        // NEXT (intended auto) update would be misclassified as manual and silently skip the
        // WiFi-module flash until the app restart. UploadFirmware must consume the manual
        // selection so a subsequent run defaults to a full auto-update.
        var dialogService = new Mock<IDialogService>();
        var pic32FirmwareUpdateService = new Mock<IFirmwareUpdateService>();
        var wifiFirmwareUpdateService = new Mock<IFirmwareUpdateService>();
        var firmwareDownloadService = new Mock<IFirmwareDownloadService>();

        using var coreDevice = new TestCoreStreamingDevice("DAQiFi Core");
        coreDevice.Connect();

        var serialDevice = CreateSerialDeviceWithCoreDevice("COM9", coreDevice);
        var manualFirmwarePath = CreateTempFile(".hex");
        var downloadedFirmwarePath = CreateTempFile(".hex");
        var wifiPackageDirectory = CreateTempDirectory();
        var wifiFactoryCalls = 0;

        pic32FirmwareUpdateService
            .Setup(service => service.UpdateFirmwareAsync(
                It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
                It.IsAny<string>(),
                It.IsAny<IProgress<FirmwareUpdateProgress>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        pic32FirmwareUpdateService
            .Setup(service => service.CheckWifiFirmwareStatusAsync(
                It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateWifiStatus(WifiFirmwareStatusReason.UpdateAvailable, "19.3.0", "v19.7.0"));

        wifiFirmwareUpdateService
            .Setup(service => service.UpdateWifiModuleAsync(
                It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
                wifiPackageDirectory,
                It.IsAny<IProgress<FirmwareUpdateProgress>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        firmwareDownloadService
            .Setup(service => service.DownloadLatestFirmwareAsync(
                It.IsAny<string>(),
                true,
                It.IsAny<IProgress<int>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(downloadedFirmwarePath);

        firmwareDownloadService
            .Setup(service => service.DownloadWifiFirmwareAsync(
                It.IsAny<string>(),
                It.IsAny<IProgress<int>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((wifiPackageDirectory, "v19.7.0"));

        var viewModel = new DaqifiViewModel(
            dialogService.Object,
            pic32FirmwareUpdateService.Object,
            firmwareDownloadService.Object,
            NullLogger<FirmwareUpdateService>.Instance,
            (_, _) =>
            {
                wifiFactoryCalls++;
                return wifiFirmwareUpdateService.Object;
            })
        {
            SelectedDevice = serialDevice,
            FirmwareFilePath = manualFirmwarePath
        };

        // First run: manual upload (PIC32 only, no WiFi, no download).
        await InvokeUploadFirmwareAsync(viewModel);
        Assert.IsTrue(viewModel.IsUploadComplete);
        Assert.IsFalse(viewModel.HasErrorOccured);
        Assert.AreEqual(0, wifiFactoryCalls);
        Assert.IsTrue(
            string.IsNullOrWhiteSpace(viewModel.FirmwareFilePath),
            "A manual upload must be consumed so the next run is not silently trapped in manual mode.");

        // Second run WITHOUT restarting the app and without re-selecting a file: must be a full
        // auto-update that downloads firmware and flashes the WiFi module.
        await InvokeUploadFirmwareAsync(viewModel);
        Assert.IsTrue(viewModel.IsUploadComplete);
        Assert.IsFalse(viewModel.HasErrorOccured);
        Assert.AreEqual(1, wifiFactoryCalls);

        pic32FirmwareUpdateService.Verify(service => service.UpdateFirmwareAsync(
            It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
            manualFirmwarePath,
            It.IsAny<IProgress<FirmwareUpdateProgress>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        firmwareDownloadService.Verify(service => service.DownloadLatestFirmwareAsync(
            It.IsAny<string>(),
            true,
            It.IsAny<IProgress<int>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        pic32FirmwareUpdateService.Verify(service => service.UpdateFirmwareAsync(
            It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
            downloadedFirmwarePath,
            It.IsAny<IProgress<FirmwareUpdateProgress>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        wifiFirmwareUpdateService.Verify(service => service.UpdateWifiModuleAsync(
            It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
            wifiPackageDirectory,
            It.IsAny<IProgress<FirmwareUpdateProgress>>(),
            It.IsAny<CancellationToken>(),
            true), Times.Once);
    }

    [TestMethod]
    public async Task UploadFirmware_WifiFirmwareUpToDate_SkipsWifiFlash()
    {
        var dialogService = new Mock<IDialogService>();
        var pic32FirmwareUpdateService = new Mock<IFirmwareUpdateService>();
        var firmwareDownloadService = new Mock<IFirmwareDownloadService>();

        using var coreDevice = new TestCoreStreamingDevice("DAQiFi Core");
        coreDevice.Connect();

        var serialDevice = CreateSerialDeviceWithCoreDevice("COM9", coreDevice);
        var pic32FirmwarePath = CreateTempFile(".hex");
        var wifiFactoryCalls = 0;

        pic32FirmwareUpdateService
            .Setup(service => service.UpdateFirmwareAsync(
                It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
                pic32FirmwarePath,
                It.IsAny<IProgress<FirmwareUpdateProgress>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        pic32FirmwareUpdateService
            .Setup(service => service.CheckWifiFirmwareStatusAsync(
                It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateWifiStatus(WifiFirmwareStatusReason.UpToDate, "19.7.0", "v19.7.0"));

        firmwareDownloadService
            .Setup(service => service.DownloadLatestFirmwareAsync(
                It.IsAny<string>(),
                true,
                It.IsAny<IProgress<int>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(pic32FirmwarePath);

        var viewModel = new DaqifiViewModel(
            dialogService.Object,
            pic32FirmwareUpdateService.Object,
            firmwareDownloadService.Object,
            NullLogger<FirmwareUpdateService>.Instance,
            (_, _) =>
            {
                wifiFactoryCalls++;
                return Mock.Of<IFirmwareUpdateService>();
            })
        {
            SelectedDevice = serialDevice
        };

        await InvokeUploadFirmwareAsync(viewModel);

        Assert.AreEqual(0, wifiFactoryCalls);
        Assert.IsTrue(viewModel.IsUploadComplete);
        Assert.IsFalse(viewModel.HasErrorOccured);
        Assert.AreEqual(100, viewModel.UploadWiFiProgress);
        Assert.AreEqual("WiFi firmware already up to date (19.7.0).", viewModel.FirmwareUpdateStatusText);

        firmwareDownloadService.Verify(service => service.DownloadWifiFirmwareAsync(
            It.IsAny<string>(),
            It.IsAny<IProgress<int>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    [DataRow(WifiFirmwareStatusReason.ChipInfoUnavailable, true)]
    [DataRow(WifiFirmwareStatusReason.DeviceDoesNotSupportLanQuery, true)]
    [DataRow(WifiFirmwareStatusReason.LatestReleaseUnavailable, false)]
    [DataRow(WifiFirmwareStatusReason.VersionUnparseable, true)]
    public async Task UploadFirmware_WifiFirmwareStatusUnknown_StillFlashesWifi(
        WifiFirmwareStatusReason reason,
        bool expectsUnavailableStatusText)
    {
        await AssertWifiFlashProceedsAsync(
            pic32Service => pic32Service
                .Setup(service => service.CheckWifiFirmwareStatusAsync(
                    It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateUnknownWifiStatus(reason)),
            expectsUnavailableStatusText);
    }

    [TestMethod]
    public async Task UploadFirmware_WifiStatusCheckThrows_StillFlashesWifi()
    {
        await AssertWifiFlashProceedsAsync(
            pic32Service => pic32Service
                .Setup(service => service.CheckWifiFirmwareStatusAsync(
                    It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("WiFi status check failed")),
            expectsUnavailableStatusText: true);
    }

    /// <summary>
    /// Drives a full non-manual firmware upload where the WiFi status check yields no usable
    /// verdict (inconclusive reason or thrown exception) and asserts the WiFi flash still runs
    /// with skipVersionCheck: true.
    /// </summary>
    private async Task AssertWifiFlashProceedsAsync(
        Action<Mock<IFirmwareUpdateService>> configureStatusCheck,
        bool expectsUnavailableStatusText)
    {
        var dialogService = new Mock<IDialogService>();
        var pic32FirmwareUpdateService = new Mock<IFirmwareUpdateService>();
        var wifiFirmwareUpdateService = new Mock<IFirmwareUpdateService>();
        var firmwareDownloadService = new Mock<IFirmwareDownloadService>();

        using var coreDevice = new TestCoreStreamingDevice("DAQiFi Core");
        coreDevice.Connect();

        var serialDevice = CreateSerialDeviceWithCoreDevice("COM9", coreDevice);
        var pic32FirmwarePath = CreateTempFile(".hex");
        var wifiPackageDirectory = CreateTempDirectory();

        pic32FirmwareUpdateService
            .Setup(service => service.UpdateFirmwareAsync(
                It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
                pic32FirmwarePath,
                It.IsAny<IProgress<FirmwareUpdateProgress>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        configureStatusCheck(pic32FirmwareUpdateService);

        wifiFirmwareUpdateService
            .Setup(service => service.UpdateWifiModuleAsync(
                It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
                wifiPackageDirectory,
                It.IsAny<IProgress<FirmwareUpdateProgress>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        firmwareDownloadService
            .Setup(service => service.DownloadLatestFirmwareAsync(
                It.IsAny<string>(),
                true,
                It.IsAny<IProgress<int>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(pic32FirmwarePath);

        firmwareDownloadService
            .Setup(service => service.DownloadWifiFirmwareAsync(
                It.IsAny<string>(),
                It.IsAny<IProgress<int>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((wifiPackageDirectory, "v19.7.0"));

        var viewModel = new DaqifiViewModel(
            dialogService.Object,
            pic32FirmwareUpdateService.Object,
            firmwareDownloadService.Object,
            NullLogger<FirmwareUpdateService>.Instance,
            (_, _) => wifiFirmwareUpdateService.Object)
        {
            SelectedDevice = serialDevice
        };

        var statusTextHistory = new List<string>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DaqifiViewModel.FirmwareUpdateStatusText))
            {
                statusTextHistory.Add(viewModel.FirmwareUpdateStatusText);
            }
        };

        await InvokeUploadFirmwareAsync(viewModel);

        Assert.IsTrue(viewModel.IsUploadComplete);
        Assert.IsFalse(viewModel.HasErrorOccured);
        Assert.AreEqual(
            expectsUnavailableStatusText,
            statusTextHistory.Contains("WiFi firmware version unavailable; continuing with update."));

        wifiFirmwareUpdateService.Verify(service => service.UpdateWifiModuleAsync(
            It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
            wifiPackageDirectory,
            It.IsAny<IProgress<FirmwareUpdateProgress>>(),
            It.IsAny<CancellationToken>(),
            true), Times.Once);
    }

    private static SerialStreamingDevice CreateSerialDeviceWithCoreDevice(string portName, TestCoreStreamingDevice coreDevice)
    {
        return new SerialStreamingDevice(portName, coreDevice);
    }

    private static WifiFirmwareStatus CreateWifiStatus(
        WifiFirmwareStatusReason reason,
        string deviceVersion,
        string latestTag)
    {
        return new WifiFirmwareStatus
        {
            CurrentChipInfo = CreateLanChipInfo(deviceVersion),
            LatestRelease = new FirmwareReleaseInfo
            {
                Version = new FirmwareVersion(19, 7, 0, null, 0),
                TagName = latestTag,
                IsPreRelease = false
            },
            IsUpToDate = reason == WifiFirmwareStatusReason.UpToDate,
            Reason = reason
        };
    }

    /// <summary>
    /// Builds an inconclusive <see cref="WifiFirmwareStatus"/> mirroring Core's contract:
    /// IsUpToDate is false and CurrentChipInfo/LatestRelease are populated only as far
    /// as the check got before failing.
    /// </summary>
    private static WifiFirmwareStatus CreateUnknownWifiStatus(WifiFirmwareStatusReason reason)
    {
        return reason switch
        {
            WifiFirmwareStatusReason.LatestReleaseUnavailable => new WifiFirmwareStatus
            {
                CurrentChipInfo = CreateLanChipInfo("19.3.0"),
                IsUpToDate = false,
                Reason = reason
            },
            WifiFirmwareStatusReason.VersionUnparseable => new WifiFirmwareStatus
            {
                CurrentChipInfo = CreateLanChipInfo("WINC-19.3"),
                LatestRelease = new FirmwareReleaseInfo
                {
                    Version = new FirmwareVersion(19, 7, 0, null, 0),
                    TagName = "v19.7.0",
                    IsPreRelease = false
                },
                IsUpToDate = false,
                Reason = reason
            },
            _ => new WifiFirmwareStatus
            {
                IsUpToDate = false,
                Reason = reason
            }
        };
    }

    private static LanChipInfo CreateLanChipInfo(string firmwareVersion)
    {
        return new LanChipInfo
        {
            ChipId = 0x1503,
            FwVersion = firmwareVersion,
            BuildDate = "2026-03-01"
        };
    }

    private static async Task InvokeUploadFirmwareAsync(DaqifiViewModel viewModel)
    {
        await viewModel.UploadFirmwareCommand.ExecuteAsync(null);
    }

    private string CreateTempFile(string extension)
    {
        var tempFile = Path.GetTempFileName();
        var path = Path.ChangeExtension(tempFile, extension);
        File.Delete(tempFile);
        File.WriteAllText(path, ":00000001FF");
        _tempFiles.Add(path);
        return path;
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"daqifi-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }

    private static void AssertCommandSent(
        TestCoreStreamingDevice coreDevice,
        IOutboundMessage<string> expectedCommand)
    {
        var expectedCommandText = expectedCommand.Data?.ToString() ?? string.Empty;
        Assert.IsTrue(
            coreDevice.SentCommands.Any(command => command.Contains(expectedCommandText, StringComparison.Ordinal)),
            $"Expected command '{expectedCommandText}' to be sent.");
    }

    private sealed class TestCoreStreamingDevice : DaqifiStreamingDevice, ILanChipInfoProvider
    {
        public TestCoreStreamingDevice(string name)
            : base(name, new MockStreamTransport())
        {
        }

        public List<string> SentCommands { get; } = [];

        public override void Send<T>(IOutboundMessage<T> message)
        {
            SentCommands.Add(message.Data?.ToString() ?? string.Empty);
        }

        // Shadows the base implementation so no test path ever runs a real SCPI
        // exchange against the mock transport. The WiFi status check itself is
        // mocked at the IFirmwareUpdateService level.
        Task<LanChipInfo?> ILanChipInfoProvider.GetLanChipInfoAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<LanChipInfo?>(null);
        }
    }

    private sealed class MockStreamTransport : IStreamTransport
    {
        private readonly MemoryStream _stream = new();
        private bool _isConnected;
        private bool _disposed;

        public Stream Stream => _disposed ? throw new ObjectDisposedException(nameof(MockStreamTransport)) : _stream;
        public bool IsConnected => _isConnected && !_disposed;
        public string ConnectionInfo => _disposed ? "Disposed" : (_isConnected ? "Mock: Connected" : "Mock: Disconnected");

        public event EventHandler<TransportStatusEventArgs>? StatusChanged;

        public Task ConnectAsync()
        {
            return ConnectAsync(null);
        }

        public Task ConnectAsync(ConnectionRetryOptions? retryOptions)
        {
            ObjectDisposedException.ThrowIf(_disposed, nameof(MockStreamTransport));

            _isConnected = true;
            StatusChanged?.Invoke(this, new TransportStatusEventArgs(true, ConnectionInfo));
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            _isConnected = false;
            StatusChanged?.Invoke(this, new TransportStatusEventArgs(false, ConnectionInfo));
            return Task.CompletedTask;
        }

        public void Connect() => ConnectAsync().GetAwaiter().GetResult();

        public void Disconnect() => DisconnectAsync().GetAwaiter().GetResult();

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _isConnected = false;
            _disposed = true;
            _stream.Dispose();
        }
    }
}
