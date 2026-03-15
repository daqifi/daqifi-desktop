using System.Reflection;
using Daqifi.Core.Communication.Messages;
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
        firmwareDownloadService.Verify(service => service.DownloadLatestFirmwareAsync(
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<IProgress<int>>(),
            It.IsAny<CancellationToken>()), Times.Never);

        File.Delete(firmwareFilePath);
    }

    [TestMethod]
    public async Task UploadFirmware_DownloadedPackage_UsesConnectedCoreDeviceForPic32AndWifi()
    {
        var dialogService = new Mock<IDialogService>();
        var pic32FirmwareUpdateService = new Mock<IFirmwareUpdateService>();
        var wifiFirmwareUpdateService = new Mock<IFirmwareUpdateService>();
        var firmwareDownloadService = new Mock<IFirmwareDownloadService>();

        using var coreDevice = new TestCoreStreamingDevice("DAQiFi Core", new LanChipInfo
        {
            ChipId = 0x1503,
            FwVersion = "19.3.0",
            BuildDate = "2026-03-01"
        });
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
                It.IsAny<CancellationToken>()))
            .Callback<Daqifi.Core.Device.IStreamingDevice, string, IProgress<FirmwareUpdateProgress>?, CancellationToken>(
                (device, _, _, _) => wifiDevice = device)
            .Returns(Task.CompletedTask);

        firmwareDownloadService
            .Setup(service => service.DownloadLatestFirmwareAsync(
                It.IsAny<string>(),
                true,
                It.IsAny<IProgress<int>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(pic32FirmwarePath);

        firmwareDownloadService
            .Setup(service => service.GetLatestWifiReleaseAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FirmwareReleaseInfo
            {
                Version = new FirmwareVersion(19, 7, 0, null, 0),
                TagName = "v19.7.0",
                IsPreRelease = false
            });

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

        Assert.IsTrue(coreDevice.SentCommands.Any(command => command.Contains("SYSTem:POWer:STATe 1", StringComparison.Ordinal)));
        Assert.IsTrue(coreDevice.SentCommands.Any(command => command.Contains("SYSTem:COMMUnicate:LAN:FWUpdate", StringComparison.Ordinal)));
        Assert.IsTrue(coreDevice.SentCommands.Any(command => command.Contains("SYSTem:COMMUnicate:USB:TRANsparent 0", StringComparison.Ordinal)));
        Assert.IsTrue(coreDevice.SentCommands.Any(command => command.Contains("SYSTem:COMMunicate:LAN:ENAbled 1", StringComparison.Ordinal)));
        Assert.IsTrue(coreDevice.SentCommands.Any(command => command.Contains("SYSTem:COMMunicate:LAN:APPLY", StringComparison.Ordinal)));
        Assert.IsTrue(coreDevice.SentCommands.Any(command => command.Contains("SYSTem:COMMunicate:LAN:SAVE", StringComparison.Ordinal)));

        pic32FirmwareUpdateService.Verify(service => service.UpdateFirmwareAsync(
            It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
            pic32FirmwarePath,
            It.IsAny<IProgress<FirmwareUpdateProgress>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        wifiFirmwareUpdateService.Verify(service => service.UpdateWifiModuleAsync(
            It.IsAny<Daqifi.Core.Device.IStreamingDevice>(),
            wifiPackageDirectory,
            It.IsAny<IProgress<FirmwareUpdateProgress>>(),
            It.IsAny<CancellationToken>()), Times.Once);

        File.Delete(pic32FirmwarePath);
        Directory.Delete(wifiPackageDirectory, recursive: true);
    }

    private static SerialStreamingDevice CreateSerialDeviceWithCoreDevice(string portName, TestCoreStreamingDevice coreDevice)
    {
        var serialDevice = new SerialStreamingDevice(portName);
        var coreDeviceField = typeof(SerialStreamingDevice).GetField("_coreDevice", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.IsNotNull(coreDeviceField);
        coreDeviceField.SetValue(serialDevice, coreDevice);

        return serialDevice;
    }

    private static async Task InvokeUploadFirmwareAsync(DaqifiViewModel viewModel)
    {
        var method = typeof(DaqifiViewModel).GetMethod("UploadFirmware", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.IsNotNull(method);

        var task = method.Invoke(viewModel, null) as Task;
        Assert.IsNotNull(task);

        await task;
    }

    private static string CreateTempFile(string extension)
    {
        var tempFile = Path.GetTempFileName();
        var path = Path.ChangeExtension(tempFile, extension);
        File.Delete(tempFile);
        File.WriteAllText(path, ":00000001FF");
        return path;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"daqifi-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestCoreStreamingDevice : DaqifiStreamingDevice, ILanChipInfoProvider
    {
        private readonly LanChipInfo? _lanChipInfo;

        public TestCoreStreamingDevice(string name, LanChipInfo? lanChipInfo = null)
            : base(name, new MockStreamTransport())
        {
            _lanChipInfo = lanChipInfo;
        }

        public List<string> SentCommands { get; } = [];

        public override void Send<T>(IOutboundMessage<T> message)
        {
            SentCommands.Add(message.Data?.ToString() ?? string.Empty);
        }

        Task<LanChipInfo?> ILanChipInfoProvider.GetLanChipInfoAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_lanChipInfo);
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
