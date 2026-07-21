using System.Collections.ObjectModel;
using Daqifi.Core.Communication.Messages;
using Daqifi.Core.Communication.Producers;
using Daqifi.Core.Communication.Transport;
using Daqifi.Core.Device;
using Daqifi.Core.Firmware;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device.Firmware;
using Daqifi.Desktop.Device.SerialDevice;
using Daqifi.Desktop.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Daqifi.Desktop.Test.ViewModels;

/// <summary>
/// Behavior contract for <see cref="FirmwareUpdateCoordinator"/>. These tests originally drove the
/// firmware flow through <c>DaqifiViewModel</c>; after the coordinator extraction (issue #592) they
/// target the coordinator directly through a lightweight <see cref="FakeFirmwareUpdateHost"/>, with
/// no WPF dependency. The asserted behavior (PIC32/WiFi flash sequencing, the auto-vs-manual rule,
/// the WiFi version-check verdicts, and progress/status state) is unchanged.
/// </summary>
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
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<
                Daqifi.Core.Device.IStreamingDevice, string, IProgress<FirmwareUpdateProgress>?,
                string?, string?, CancellationToken>(
                (device, _, _, _, _, _) => pic32Device = device)
            .Returns(Task.CompletedTask);

        var host = new FakeFirmwareUpdateHost
        {
            SelectedDevice = serialDevice,
            FirmwareFilePath = firmwareFilePath
        };

        var coordinator = CreateCoordinator(
            host,
            firmwareUpdateService.Object,
            firmwareDownloadService.Object,
            (_, _) =>
            {
                wifiFactoryCalls++;
                return Mock.Of<IFirmwareUpdateService>();
            });

        await coordinator.UploadFirmwareAsync();

        Assert.AreSame(coreDevice, pic32Device);
        Assert.AreEqual(0, wifiFactoryCalls);
        Assert.IsTrue(host.IsUploadComplete);
        Assert.IsFalse(host.HasErrorOccured);

        firmwareUpdateService.Verify(service => service.UpdateFirmwareAsync(
            It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
            firmwareFilePath,
            It.IsAny<IProgress<FirmwareUpdateProgress>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
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
    public async Task UploadFirmware_PassesConnectedDeviceLocationKeyAsTargetLocationKey()
    {
        // issue #655: the coordinator must resolve the connected device's USB physical location
        // BEFORE it reboots into the bootloader (the future device path doesn't exist yet) and pass
        // it through as targetLocationKey, with no targetDevicePath (the bootloader hasn't appeared
        // yet either), so Core's post-reboot search can find the exact physical device.
        var firmwareUpdateService = new Mock<IFirmwareUpdateService>();
        var firmwareDownloadService = new Mock<IFirmwareDownloadService>();

        using var coreDevice = new TestCoreStreamingDevice("DAQiFi Core");
        coreDevice.Connect();

        var serialDevice = CreateSerialDeviceWithCoreDevice("COM7", coreDevice);
        serialDevice.LocationKey = "Port_#0001.Hub_#0001";
        var firmwareFilePath = CreateTempFile(".hex");

        firmwareUpdateService
            .Setup(service => service.UpdateFirmwareAsync(
                It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
                firmwareFilePath,
                It.IsAny<IProgress<FirmwareUpdateProgress>>(),
                null,
                "Port_#0001.Hub_#0001",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var host = new FakeFirmwareUpdateHost
        {
            SelectedDevice = serialDevice,
            FirmwareFilePath = firmwareFilePath
        };

        var coordinator = CreateCoordinator(
            host,
            firmwareUpdateService.Object,
            firmwareDownloadService.Object,
            (_, _) => Mock.Of<IFirmwareUpdateService>());

        await coordinator.UploadFirmwareAsync();

        Assert.IsTrue(host.IsUploadComplete);
        Assert.IsFalse(host.HasErrorOccured);
        firmwareUpdateService.Verify(service => service.UpdateFirmwareAsync(
            It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
            firmwareFilePath,
            It.IsAny<IProgress<FirmwareUpdateProgress>>(),
            null,
            "Port_#0001.Hub_#0001",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task UploadFirmware_DownloadedPackage_UsesConnectedCoreDeviceForPic32AndWifi()
    {
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
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<
                Daqifi.Core.Device.IStreamingDevice, string, IProgress<FirmwareUpdateProgress>?,
                string?, string?, CancellationToken>(
                (device, _, _, _, _, _) => pic32Device = device)
            .Returns(Task.CompletedTask);

        wifiFirmwareUpdateService
            .Setup(service => service.UpdateWifiModuleAsync(
                It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
                wifiPackageDirectory,
                It.IsAny<IProgress<FirmwareUpdateProgress>>(),
                It.IsAny<CancellationToken>()))
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

        // The coordinator's WiFi version check now reads chip info via the serial device's
        // ILanChipInfoProvider. Leaving it null models an unreadable module, which the coordinator
        // treats as out-of-date and proceeds to flash.
        coreDevice.LanChipInfoResult = null;

        firmwareDownloadService
            .Setup(service => service.DownloadWifiFirmwareAsync(
                It.IsAny<string>(),
                It.IsAny<IProgress<int>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((wifiPackageDirectory, "v19.7.0"));

        var host = new FakeFirmwareUpdateHost
        {
            SelectedDevice = serialDevice
        };

        var coordinator = CreateCoordinator(
            host,
            pic32FirmwareUpdateService.Object,
            firmwareDownloadService.Object,
            (version, port) =>
            {
                wifiVersion = version;
                wifiPort = port;
                return wifiFirmwareUpdateService.Object;
            });

        await coordinator.UploadFirmwareAsync();

        Assert.AreSame(coreDevice, pic32Device);
        // Unlike the PIC32 flash, the WiFi flash releases the managed serial connection and runs the
        // WINC tool against a no-op device (the tool reaches the board over raw serial via bridge
        // activation) — that's what frees the COM port so the tool can open it. So the WiFi flash does
        // NOT reuse the live core device.
        Assert.IsInstanceOfType<Daqifi.Desktop.Device.Firmware.BootloaderSessionStreamingDeviceAdapter>(wifiDevice);
        Assert.AreEqual("19.7.0", wifiVersion);
        Assert.AreEqual("COM9", wifiPort);
        Assert.IsTrue(host.IsUploadComplete);
        Assert.IsFalse(host.HasErrorOccured);
        // The connect-time WiFi probe must be quiesced before the device enters update mode, so a
        // stray POWer:STATe 1 / GETChipInfo? can't land on the bridging WINC and brick it.
        Assert.IsTrue(host.QuiesceWifiFirmwareProbeCallCount >= 1,
            "Coordinator must quiesce the WiFi probe before flashing.");

        // LAN-update-mode prep goes over the managed connection before it's released.
        AssertCommandSent(coreDevice, ScpiMessageProducer.TurnDeviceOn);
        AssertCommandSent(coreDevice, ScpiMessageProducer.SetLanFirmwareUpdateMode);
        // The post-flash reset (SetTransparentMode 0 / EnableNetworkLan / Apply / Save) no longer
        // travels over the managed connection — it's released before the tool runs, so the transparent-
        // mode exit happens via the raw ExitWifiTransparentModeRaw path (and the device's reboot).

        pic32FirmwareUpdateService.Verify(service => service.UpdateFirmwareAsync(
            It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
            pic32FirmwarePath,
            It.IsAny<IProgress<FirmwareUpdateProgress>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
        wifiFirmwareUpdateService.Verify(service => service.UpdateWifiModuleAsync(
            It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
            wifiPackageDirectory,
            It.IsAny<IProgress<FirmwareUpdateProgress>>(),
            It.IsAny<CancellationToken>()), Times.Once);
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

        // Arrange
        var pic32FirmwareUpdateService = new Mock<IFirmwareUpdateService>();
        var wifiFirmwareUpdateService = new Mock<IFirmwareUpdateService>();
        var firmwareDownloadService = new Mock<IFirmwareDownloadService>();

        // The WiFi-flash cleanup (ExitWifiTransparentModeRaw) tears down the managed connection when it
        // can't open the COM port for the raw transparent-mode exit — which it can't against a mock
        // transport. On real hardware the device reboots and the app reconnects with a fresh transport
        // after a flash; model that here with a separate connected device per run so each in-session
        // auto-update runs against a live connection.
        using var coreDevice = new TestCoreStreamingDevice("DAQiFi Core");
        coreDevice.Connect();
        using var coreDevice2 = new TestCoreStreamingDevice("DAQiFi Core");
        coreDevice2.Connect();

        var serialDevice = CreateSerialDeviceWithCoreDevice("COM9", coreDevice);
        var serialDevice2 = CreateSerialDeviceWithCoreDevice("COM9", coreDevice2);
        var pic32FirmwarePath = CreateTempFile(".hex");
        var wifiPackageDirectory = CreateTempDirectory();
        var wifiFactoryCalls = 0;

        pic32FirmwareUpdateService
            .Setup(service => service.UpdateFirmwareAsync(
                It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
                pic32FirmwarePath,
                It.IsAny<IProgress<FirmwareUpdateProgress>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Unreadable WiFi chip info -> coordinator treats the module as out-of-date and flashes.
        coreDevice.LanChipInfoResult = null;

        wifiFirmwareUpdateService
            .Setup(service => service.UpdateWifiModuleAsync(
                It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
                wifiPackageDirectory,
                It.IsAny<IProgress<FirmwareUpdateProgress>>(),
                It.IsAny<CancellationToken>()))
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

        var host = new FakeFirmwareUpdateHost
        {
            SelectedDevice = serialDevice
        };

        var coordinator = CreateCoordinator(
            host,
            pic32FirmwareUpdateService.Object,
            firmwareDownloadService.Object,
            (_, _) =>
            {
                wifiFactoryCalls++;
                return wifiFirmwareUpdateService.Object;
            });

        // Act: two consecutive in-session auto-updates with no app restart between them. The second
        // run uses the freshly reconnected device (see the per-run device note above).
        await coordinator.UploadFirmwareAsync();
        host.SelectedDevice = serialDevice2;
        await coordinator.UploadFirmwareAsync();

        // Assert: each run performed a full auto-update and the bound path was never populated.
        // (The per-run state is proven by the Times.Exactly(2) counts below: had run 1 left
        // FirmwareFilePath set, run 2 would be classified manual and these would be Times.Once.)
        Assert.IsTrue(host.IsUploadComplete);
        Assert.IsFalse(host.HasErrorOccured);
        Assert.IsTrue(
            string.IsNullOrWhiteSpace(host.FirmwareFilePath),
            "Auto-update must not populate FirmwareFilePath; doing so reclassifies the next run as a manual upload.");
        // The WiFi module must be flashed on BOTH runs, not just the first.
        Assert.AreEqual(2, wifiFactoryCalls);
        // The main (PIC32) firmware flash is not gated by the auto/manual classification, so it
        // must run on both passes; counting it guards against a future regression that gates it.
        pic32FirmwareUpdateService.Verify(service => service.UpdateFirmwareAsync(
            It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
            pic32FirmwarePath,
            It.IsAny<IProgress<FirmwareUpdateProgress>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        firmwareDownloadService.Verify(service => service.DownloadLatestFirmwareAsync(
            It.IsAny<string>(),
            true,
            It.IsAny<IProgress<int>>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        wifiFirmwareUpdateService.Verify(service => service.UpdateWifiModuleAsync(
            It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
            wifiPackageDirectory,
            It.IsAny<IProgress<FirmwareUpdateProgress>>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
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

        // Arrange
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
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Unreadable WiFi chip info -> coordinator treats the module as out-of-date and flashes.
        coreDevice.LanChipInfoResult = null;

        wifiFirmwareUpdateService
            .Setup(service => service.UpdateWifiModuleAsync(
                It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
                wifiPackageDirectory,
                It.IsAny<IProgress<FirmwareUpdateProgress>>(),
                It.IsAny<CancellationToken>()))
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

        var host = new FakeFirmwareUpdateHost
        {
            SelectedDevice = serialDevice,
            FirmwareFilePath = manualFirmwarePath
        };

        var coordinator = CreateCoordinator(
            host,
            pic32FirmwareUpdateService.Object,
            firmwareDownloadService.Object,
            (_, _) =>
            {
                wifiFactoryCalls++;
                return wifiFirmwareUpdateService.Object;
            });

        // Arrange (cont.): drive a manual upload so the session is left in the post-manual state.
        // A manual upload is PIC32-only and must consume the selection; these guards confirm the
        // precondition the Act below depends on.
        await coordinator.UploadFirmwareAsync();
        Assert.AreEqual(0, wifiFactoryCalls, "A manual upload must not flash the WiFi module.");
        Assert.IsTrue(
            string.IsNullOrWhiteSpace(host.FirmwareFilePath),
            "A manual upload must be consumed so the next run is not silently trapped in manual mode.");

        // Act: the next in-session update, without restarting the app and without re-selecting a
        // file. With the selection consumed it must default to a full auto-update.
        await coordinator.UploadFirmwareAsync();

        // Assert: the second run downloaded firmware and flashed the WiFi module.
        Assert.IsTrue(host.IsUploadComplete);
        Assert.IsFalse(host.HasErrorOccured);
        Assert.AreEqual(1, wifiFactoryCalls);

        pic32FirmwareUpdateService.Verify(service => service.UpdateFirmwareAsync(
            It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
            manualFirmwarePath,
            It.IsAny<IProgress<FirmwareUpdateProgress>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
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
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
        wifiFirmwareUpdateService.Verify(service => service.UpdateWifiModuleAsync(
            It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
            wifiPackageDirectory,
            It.IsAny<IProgress<FirmwareUpdateProgress>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task UploadFirmware_WifiFirmwareUpToDate_SkipsWifiFlash()
    {
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
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // A WiFi module already at/above the minimum supported version. The coordinator reads chip
        // info via the serial device's ILanChipInfoProvider and skips the flash when it's current.
        coreDevice.LanChipInfoResult = CreateLanChipInfo(FirmwareUpdateCoordinator.MinimumWifiFirmwareVersion);

        firmwareDownloadService
            .Setup(service => service.DownloadLatestFirmwareAsync(
                It.IsAny<string>(),
                true,
                It.IsAny<IProgress<int>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(pic32FirmwarePath);

        var host = new FakeFirmwareUpdateHost
        {
            SelectedDevice = serialDevice
        };

        var coordinator = CreateCoordinator(
            host,
            pic32FirmwareUpdateService.Object,
            firmwareDownloadService.Object,
            (_, _) =>
            {
                wifiFactoryCalls++;
                return Mock.Of<IFirmwareUpdateService>();
            });

        await coordinator.UploadFirmwareAsync();

        Assert.AreEqual(0, wifiFactoryCalls);
        Assert.IsTrue(host.IsUploadComplete);
        Assert.IsFalse(host.HasErrorOccured);
        Assert.AreEqual(100, host.UploadWiFiProgress);
        Assert.AreEqual(
            $"WiFi firmware already up to date ({FirmwareUpdateCoordinator.MinimumWifiFirmwareVersion}).",
            host.FirmwareUpdateStatusText);

        firmwareDownloadService.Verify(service => service.DownloadWifiFirmwareAsync(
            It.IsAny<string>(),
            It.IsAny<IProgress<int>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task UploadFirmware_WifiChipInfoUnreadable_StillFlashesWifi()
    {
        // GETChipInfo? returns nothing (module not answering): the coordinator can't read a version,
        // surfaces the "unavailable" status, and proceeds with the flash so a dead module is reflashed.
        await AssertWifiFlashProceedsAsync(
            coreDevice => coreDevice.LanChipInfoResult = null,
            expectsUnavailableStatusText: true);
    }

    [TestMethod]
    public async Task UploadFirmware_WifiVersionUnparseable_StillFlashesWifi()
    {
        // Chip info came back but its version string can't be parsed: treated as needing a flash.
        // The version is reported (not "unavailable"), so the unavailable status is not shown.
        await AssertWifiFlashProceedsAsync(
            coreDevice => coreDevice.LanChipInfoResult = CreateLanChipInfo("WINC-19.3"),
            expectsUnavailableStatusText: false);
    }

    /// <summary>
    /// Drives a full non-manual firmware upload where the WiFi version check yields no usable
    /// verdict (chip info unreadable or its version unparseable) and asserts the WiFi flash still runs.
    /// </summary>
    private async Task AssertWifiFlashProceedsAsync(
        Action<TestCoreStreamingDevice> configureChipInfo,
        bool expectsUnavailableStatusText)
    {
        var pic32FirmwareUpdateService = new Mock<IFirmwareUpdateService>();
        var wifiFirmwareUpdateService = new Mock<IFirmwareUpdateService>();
        var firmwareDownloadService = new Mock<IFirmwareDownloadService>();

        using var coreDevice = new TestCoreStreamingDevice("DAQiFi Core");
        coreDevice.Connect();
        configureChipInfo(coreDevice);

        var serialDevice = CreateSerialDeviceWithCoreDevice("COM9", coreDevice);
        var pic32FirmwarePath = CreateTempFile(".hex");
        var wifiPackageDirectory = CreateTempDirectory();

        pic32FirmwareUpdateService
            .Setup(service => service.UpdateFirmwareAsync(
                It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
                pic32FirmwarePath,
                It.IsAny<IProgress<FirmwareUpdateProgress>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        wifiFirmwareUpdateService
            .Setup(service => service.UpdateWifiModuleAsync(
                It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
                wifiPackageDirectory,
                It.IsAny<IProgress<FirmwareUpdateProgress>>(),
                It.IsAny<CancellationToken>()))
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

        var host = new FakeFirmwareUpdateHost
        {
            SelectedDevice = serialDevice
        };

        var coordinator = CreateCoordinator(
            host,
            pic32FirmwareUpdateService.Object,
            firmwareDownloadService.Object,
            (_, _) => wifiFirmwareUpdateService.Object);

        await coordinator.UploadFirmwareAsync();

        Assert.IsTrue(host.IsUploadComplete);
        Assert.IsFalse(host.HasErrorOccured);
        Assert.AreEqual(
            expectsUnavailableStatusText,
            host.StatusTextHistory.Contains("WiFi firmware version unavailable; continuing with update."));

        wifiFirmwareUpdateService.Verify(service => service.UpdateWifiModuleAsync(
            It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
            wifiPackageDirectory,
            It.IsAny<IProgress<FirmwareUpdateProgress>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task RefreshFirmwareUpdates_NoConnectedDevices_DoesNotQueryReleases()
    {
        var firmwareUpdateService = new Mock<IFirmwareUpdateService>();
        var firmwareDownloadService = new Mock<IFirmwareDownloadService>();

        var host = new FakeFirmwareUpdateHost();
        var coordinator = CreateCoordinator(host, firmwareUpdateService.Object, firmwareDownloadService.Object);

        await coordinator.RefreshFirmwareUpdatesAsync();

        Assert.AreEqual(string.Empty, coordinator.LatestFirmwareVersion);
        firmwareDownloadService.Verify(service => service.GetLatestReleaseAsync(
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task RefreshFirmwareUpdates_OutdatedDevice_FlagsDeviceAndAddsNotification()
    {
        var firmwareUpdateService = new Mock<IFirmwareUpdateService>();
        var firmwareDownloadService = new Mock<IFirmwareDownloadService>();

        var latestRelease = new FirmwareReleaseInfo
        {
            Version = new FirmwareVersion(2, 0, 0, null, 0),
            TagName = "v2.0.0",
            IsPreRelease = false
        };
        firmwareDownloadService
            .Setup(service => service.GetLatestReleaseAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(latestRelease);
        firmwareDownloadService
            .Setup(service => service.CheckForUpdateAsync(It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FirmwareUpdateCheckResult { UpdateAvailable = true });

        var device = CreateDeviceMock("SN-OUTDATED", "1.0.0");
        var host = new FakeFirmwareUpdateHost { ConnectedDevices = [device.Object] };
        var coordinator = CreateCoordinator(host, firmwareUpdateService.Object, firmwareDownloadService.Object);

        await coordinator.RefreshFirmwareUpdatesAsync();

        Assert.AreEqual(latestRelease.Version.ToString(), coordinator.LatestFirmwareVersion);
        Assert.IsTrue(device.Object.IsFirmwareOutdated);
        Assert.AreEqual(1, host.Notifications.Count);
        Assert.IsTrue(host.Notifications[0].IsFirmwareUpdate);
        Assert.AreEqual("SN-OUTDATED", host.Notifications[0].DeviceSerialNo);
        Assert.AreEqual(1, host.NotificationCount);
    }

    [TestMethod]
    public async Task RefreshFirmwareUpdates_UpToDateDevice_ClearsFlagAndRemovesNotification()
    {
        var firmwareUpdateService = new Mock<IFirmwareUpdateService>();
        var firmwareDownloadService = new Mock<IFirmwareDownloadService>();

        firmwareDownloadService
            .Setup(service => service.GetLatestReleaseAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FirmwareReleaseInfo
            {
                Version = new FirmwareVersion(2, 0, 0, null, 0),
                TagName = "v2.0.0",
                IsPreRelease = false
            });
        firmwareDownloadService
            .Setup(service => service.CheckForUpdateAsync(It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FirmwareUpdateCheckResult { UpdateAvailable = false });

        var device = CreateDeviceMock("SN-CURRENT", "2.0.0");
        device.Object.IsFirmwareOutdated = true;
        var host = new FakeFirmwareUpdateHost { ConnectedDevices = [device.Object] };
        // Pre-existing stale notification for this device that the refresh must clear.
        host.Notifications.Add(new Notifications
        {
            DeviceSerialNo = "SN-CURRENT",
            Message = "stale",
            IsFirmwareUpdate = true
        });

        var coordinator = CreateCoordinator(host, firmwareUpdateService.Object, firmwareDownloadService.Object);

        await coordinator.RefreshFirmwareUpdatesAsync();

        Assert.IsFalse(device.Object.IsFirmwareOutdated);
        Assert.AreEqual(0, host.Notifications.Count);
    }

    [TestMethod]
    public void WifiFirmwareNeedsFlash_NullChipInfo_NeedsFlashAndReportsUnknown()
    {
        var needsFlash = FirmwareUpdateCoordinator.WifiFirmwareNeedsFlash(null, out var reportedVersion);

        Assert.IsTrue(needsFlash);
        Assert.AreEqual("Unknown", reportedVersion);
    }

    [TestMethod]
    public void WifiFirmwareNeedsFlash_EmptyChipVersion_NeedsFlashAndReportsUnknown()
    {
        var chipInfo = CreateLanChipInfo(string.Empty);

        var needsFlash = FirmwareUpdateCoordinator.WifiFirmwareNeedsFlash(chipInfo, out var reportedVersion);

        Assert.IsTrue(needsFlash);
        Assert.AreEqual("Unknown", reportedVersion);
    }

    [TestMethod]
    [DataRow("19.7.7", false)]   // exactly the minimum supported version
    [DataRow("19.8.0", false)]   // newer than the minimum
    [DataRow("20.0.0", false)]
    [DataRow("19.6.1", true)]    // older than the minimum
    [DataRow("19.3.0", true)]
    [DataRow("not-a-version", true)] // unparseable -> can't be trusted, flash
    public void WifiFirmwareNeedsFlash_ComparesReportedVersionAgainstMinimum(string reportedFwVersion, bool expectedNeedsFlash)
    {
        var chipInfo = CreateLanChipInfo(reportedFwVersion);

        var needsFlash = FirmwareUpdateCoordinator.WifiFirmwareNeedsFlash(chipInfo, out var reportedVersion);

        Assert.AreEqual(expectedNeedsFlash, needsFlash);
        Assert.AreEqual(reportedFwVersion, reportedVersion);
    }

    /// <summary>
    /// Builds a coordinator with no-op test loggers and a throwaway firmware data directory.
    /// Dialog/flyout host operations are captured by <see cref="FakeFirmwareUpdateHost"/>, so no
    /// dialog service is needed; the WiFi factory defaults to null (the WiFi path is only reached by
    /// tests that pass one explicitly).
    /// </summary>
    private FirmwareUpdateCoordinator CreateCoordinator(
        FakeFirmwareUpdateHost host,
        IFirmwareUpdateService firmwareUpdateService,
        IFirmwareDownloadService firmwareDownloadService,
        Func<string, string, IFirmwareUpdateService>? wifiFirmwareUpdateServiceFactory = null)
    {
        return new FirmwareUpdateCoordinator(
            host,
            firmwareUpdateService,
            firmwareDownloadService,
            NullLogger<FirmwareUpdateService>.Instance,
            Mock.Of<IAppLogger>(),
            CreateTempDirectory(),
            wifiFirmwareUpdateServiceFactory,
            // Collapse the WiFi-update-mode settle wait so these unit tests stay fast and
            // deterministic. (Production timing is exercised by the integration gate.) Flash
            // success/failure is now verified by Core from the tool output, so there is no
            // desktop-side duration guard to configure here.
            wifiUpdateModeSettleDelay: TimeSpan.Zero);
    }

    private static Mock<Daqifi.Desktop.Device.IStreamingDevice> CreateDeviceMock(string serialNo, string version)
    {
        var device = new Mock<Daqifi.Desktop.Device.IStreamingDevice>();
        device.SetupGet(d => d.DeviceSerialNo).Returns(serialNo);
        device.SetupGet(d => d.DeviceVersion).Returns(version);
        device.SetupProperty(d => d.IsFirmwareOutdated);
        return device;
    }

    private static SerialStreamingDevice CreateSerialDeviceWithCoreDevice(string portName, TestCoreStreamingDevice coreDevice)
    {
        // These tests exercise the WiFi-module flash, so model a Nyquist (WINC1500) device — that's
        // what HasWincWifiModule keys on, which the coordinator now requires before a WiFi update.
        return new SerialStreamingDevice(portName, coreDevice)
        {
            DeviceType = Daqifi.Core.Device.DeviceType.Nyquist1
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

    /// <summary>
    /// In-memory <see cref="IFirmwareUpdateHost"/> for driving the coordinator without WPF.
    /// Captures the progress/status writes (and a full history of status text) the firmware
    /// flow pushes, and exposes the inputs the coordinator reads.
    /// </summary>
    private sealed class FakeFirmwareUpdateHost : IFirmwareUpdateHost
    {
        private string _firmwareUpdateStatusText = string.Empty;

        public Daqifi.Desktop.Device.IStreamingDevice? SelectedDevice { get; set; }

        public IReadOnlyList<Daqifi.Desktop.Device.IStreamingDevice> ConnectedDevices { get; set; } = [];

        public string FirmwareFilePath { get; set; } = string.Empty;

        public bool SelectedDeviceSupportsFirmwareUpdate { get; set; }

        public bool IsFirmwareUploading { get; set; }

        public bool IsUploadComplete { get; set; }

        public bool HasErrorOccured { get; set; }

        public int UploadFirmwareProgress { get; set; }

        public int UploadWiFiProgress { get; set; }

        public string FirmwareUpdateStatusText
        {
            get => _firmwareUpdateStatusText;
            set
            {
                _firmwareUpdateStatusText = value;
                StatusTextHistory.Add(value);
            }
        }

        /// <summary>Every value written to <see cref="FirmwareUpdateStatusText"/>, in order.</summary>
        public List<string> StatusTextHistory { get; } = [];

        public ObservableCollection<Notifications> Notifications { get; } = [];

        public Daqifi.Desktop.Device.IStreamingDevice? DeviceBeingUpdated { get; set; }

        public int NotificationCount { get; private set; }

        /// <summary>Messages passed to <see cref="ShowFirmwareError"/>, in order.</summary>
        public List<string> FirmwareErrors { get; } = [];

        /// <summary>Number of times <see cref="ShowFirmwareUpdateSucceeded"/> was invoked.</summary>
        public int SucceededCallCount { get; private set; }

        public void RefreshNotificationCount() => NotificationCount = Notifications.Count;

        public void ShowFirmwareError(string message) => FirmwareErrors.Add(message);

        public void ShowFirmwareUpdateSucceeded() => SucceededCallCount++;

        public Task QuiesceWifiFirmwareProbeAsync(CancellationToken cancellationToken = default)
        {
            QuiesceWifiFirmwareProbeCallCount++;
            return Task.CompletedTask;
        }

        /// <summary>Number of times <see cref="QuiesceWifiFirmwareProbeAsync"/> was invoked.</summary>
        public int QuiesceWifiFirmwareProbeCallCount { get; private set; }
    }

    private sealed class TestCoreStreamingDevice : DaqifiStreamingDevice, ILanChipInfoProvider
    {
        public TestCoreStreamingDevice(string name)
            : base(name, new MockStreamTransport())
        {
        }

        public List<string> SentCommands { get; } = [];

        /// <summary>
        /// Chip info returned by the shadowed <see cref="ILanChipInfoProvider.GetLanChipInfoAsync"/>.
        /// Null (the default) models a WiFi module whose chip info cannot be read — which the
        /// coordinator treats as "needs flash"; set it to a populated value to model an
        /// up-to-date / specific-version module.
        /// </summary>
        public LanChipInfo? LanChipInfoResult { get; set; }

        public override void Send<T>(IOutboundMessage<T> message)
        {
            SentCommands.Add(message.Data?.ToString() ?? string.Empty);
        }

        // Shadows the base implementation so no test path ever runs a real SCPI exchange against
        // the mock transport. The coordinator's WiFi version check reads this via the serial
        // device's ILanChipInfoProvider; LanChipInfoResult lets each test pick the verdict.
        Task<LanChipInfo?> ILanChipInfoProvider.GetLanChipInfoAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(LanChipInfoResult);
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
