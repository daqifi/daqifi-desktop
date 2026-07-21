using Daqifi.Desktop.Device;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.ViewModels;
using Moq;
using System.Reflection;

namespace Daqifi.Desktop.Test.ViewModels;

/// <summary>
/// Verifies the connection dialog suspends its serial + WiFi discovery while a firmware update is in
/// progress (issue #738): the per-cycle SerialDeviceFinder opens every DAQiFi VID/PID COM port each
/// pass, and a probe landing in Core's post-flash JumpingToApp reconnect window steals the
/// re-enumerating port and strands the update in a timeout. The dialog gates its <c>Start*Discovery</c>
/// on <see cref="ConnectionManager.IsFirmwareUpdateInProgress"/> so a dialog opened mid-flash never
/// starts a finder.
/// </summary>
[TestClass]
public class ConnectionDialogViewModelFirmwareGateTests
{
    private Func<DuplicateDeviceCheckResult, DuplicateDeviceAction>? _originalDuplicateDeviceHandler;

    [TestInitialize]
    public void TestInitialize()
    {
        _originalDuplicateDeviceHandler = ConnectionManager.Instance.DuplicateDeviceHandler;
        ConnectionManager.Instance.DuplicateDeviceHandler = null;
        ConnectionManager.Instance.DeviceBeingUpdated = null;
    }

    [TestCleanup]
    public void TestCleanup()
    {
        ConnectionManager.Instance.DeviceBeingUpdated = null;
        ConnectionManager.Instance.DuplicateDeviceHandler = _originalDuplicateDeviceHandler;
    }

    [TestMethod]
    public void StartSerialDiscovery_DoesNotStartFinder_WhileFirmwareUpdateInProgress()
    {
        using var viewModel = CreateViewModel();
        ConnectionManager.Instance.DeviceBeingUpdated = CreateUsbDevice();

        InvokePrivate(viewModel, "StartSerialDiscovery");

        // No finder created => no COM ports opened/probed during the flash.
        Assert.IsNull(GetPrivateField(viewModel, "_serialFinder"),
            "Serial discovery must not start while a firmware update is in progress.");
    }

    [TestMethod]
    public void StartWiFiDiscovery_DoesNotStartFinder_WhileFirmwareUpdateInProgress()
    {
        using var viewModel = CreateViewModel();
        ConnectionManager.Instance.DeviceBeingUpdated = CreateUsbDevice();

        InvokePrivate(viewModel, "StartWiFiDiscovery");

        Assert.IsNull(GetPrivateField(viewModel, "_wifiFinder"),
            "WiFi discovery must not start while a firmware update is in progress.");
    }

    [TestMethod]
    public void StartConnectionFinders_StartsNothing_WhileFirmwareUpdateInProgress()
    {
        // The user opens the connection dialog mid-flash (the issue #738 breadcrumb sequence): the
        // public entry point that kicks off discovery must create neither finder.
        using var viewModel = CreateViewModel();
        ConnectionManager.Instance.DeviceBeingUpdated = CreateUsbDevice();

        viewModel.StartConnectionFinders();

        Assert.IsNull(GetPrivateField(viewModel, "_serialFinder"));
        Assert.IsNull(GetPrivateField(viewModel, "_wifiFinder"));
    }

    private static IStreamingDevice CreateUsbDevice()
    {
        var device = new Mock<IStreamingDevice>();
        device.SetupGet(d => d.ConnectionType).Returns(ConnectionType.Usb);
        device.SetupGet(d => d.Name).Returns("UpdatingDevice");
        return device.Object;
    }

    private static ConnectionDialogViewModel CreateViewModel()
    {
        var dialogService = new Mock<IDialogService>();
        return new ConnectionDialogViewModel(dialogService.Object);
    }

    private static void InvokePrivate(ConnectionDialogViewModel viewModel, string methodName)
    {
        var method = typeof(ConnectionDialogViewModel).GetMethod(
            methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method, $"{methodName} not found.");
        method.Invoke(viewModel, null);
    }

    private static object? GetPrivateField(ConnectionDialogViewModel viewModel, string fieldName)
    {
        var field = typeof(ConnectionDialogViewModel).GetField(
            fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, $"{fieldName} not found.");
        return field.GetValue(viewModel);
    }
}
