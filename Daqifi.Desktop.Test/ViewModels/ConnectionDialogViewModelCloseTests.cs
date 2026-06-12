using Daqifi.Desktop.Device.SerialDevice;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.ViewModels;
using Moq;

namespace Daqifi.Desktop.Test.ViewModels;

[TestClass]
public class ConnectionDialogViewModelCloseTests
{
    // A name guaranteed to be absent from SerialPort.GetPortNames() on every CI runner —
    // Windows uses COM1..COMn, macOS/Linux use /dev/tty.* paths.
    private const string NONEXISTENT_PORT_NAME = "COM_DOES_NOT_EXIST_524";

    private Func<DuplicateDeviceCheckResult, DuplicateDeviceAction>? _originalDuplicateDeviceHandler;

    [TestInitialize]
    public void TestInitialize()
    {
        _originalDuplicateDeviceHandler = ConnectionManager.Instance.DuplicateDeviceHandler;
        ConnectionManager.Instance.DuplicateDeviceHandler = null;
    }

    [TestCleanup]
    public void TestCleanup()
    {
        ConnectionManager.Instance.DuplicateDeviceHandler = _originalDuplicateDeviceHandler;
    }

    [TestMethod]
    public async Task ConnectCommand_WithEmptySelection_DoesNotRaiseCloseRequested()
    {
        var viewModel = CreateViewModel();
        var closeRaised = SubscribeToClose(viewModel);

        await viewModel.ConnectCommand.ExecuteAsync(new List<object>());

        Assert.IsFalse(closeRaised(), "CloseRequested should not fire when no devices are selected.");
    }

    [TestMethod]
    public async Task ConnectCommand_WithNullParameter_DoesNotRaiseCloseRequested()
    {
        var viewModel = CreateViewModel();
        var closeRaised = SubscribeToClose(viewModel);

        await viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.IsFalse(closeRaised(), "CloseRequested should not fire when command parameter is null.");
    }

    [TestMethod]
    public async Task ConnectSerialCommand_WithEmptySelection_DoesNotRaiseCloseRequested()
    {
        var viewModel = CreateViewModel();
        var closeRaised = SubscribeToClose(viewModel);

        await viewModel.ConnectSerialCommand.ExecuteAsync(Array.Empty<SerialStreamingDevice>());

        Assert.IsFalse(closeRaised(), "CloseRequested should not fire when no serial devices are selected.");
    }

    [TestMethod]
    public async Task ConnectManualSerialCommand_WithBlankPort_DoesNotRaiseCloseRequested()
    {
        var viewModel = CreateViewModel();
        viewModel.ManualPortName = "   ";
        var closeRaised = SubscribeToClose(viewModel);

        await viewModel.ConnectManualSerialCommand.ExecuteAsync(null);

        Assert.IsFalse(closeRaised(), "CloseRequested should not fire when the manual port is blank.");
        Assert.IsNull(viewModel.ManualPortError,
            "Blank input should not surface the missing-port error message.");
    }

    [TestMethod]
    public async Task ConnectManualSerialCommand_WithNonexistentPort_SetsManualPortError()
    {
        var viewModel = CreateViewModel();
        viewModel.ManualPortName = NONEXISTENT_PORT_NAME;
        var closeRaised = SubscribeToClose(viewModel);

        await viewModel.ConnectManualSerialCommand.ExecuteAsync(null);

        Assert.IsFalse(closeRaised(),
            "CloseRequested should not fire when the entered port is not present on the system.");
        Assert.IsNotNull(viewModel.ManualPortError,
            "ManualPortError should be set when the entered port is not present on the system.");
        StringAssert.Contains(viewModel.ManualPortError, NONEXISTENT_PORT_NAME,
            "Error message should mention the offending port name.");
    }

    [TestMethod]
    public void ManualPortError_ClearsWhenManualPortNameChanges()
    {
        var viewModel = CreateViewModel();
        viewModel.ManualPortError = "stale validation message";

        viewModel.ManualPortName = "COM1";

        Assert.IsNull(viewModel.ManualPortError,
            "Editing the manual port name should clear any prior validation error.");
    }

    /// <summary>
    /// Blank manual-WiFi input is a no-op: no close, and no validation error surfaced.
    /// </summary>
    [TestMethod]
    public async Task ConnectManualWifiCommand_WithBlankAddress_DoesNotRaiseCloseRequested()
    {
        var viewModel = CreateViewModel();
        viewModel.ManualIpAddress = "";
        var closeRaised = SubscribeToClose(viewModel);

        await viewModel.ConnectManualWifiCommand.ExecuteAsync(null);

        Assert.IsFalse(closeRaised(), "CloseRequested should not fire when the manual IP is blank.");
        Assert.IsNull(viewModel.ManualWifiError,
            "Blank input should not surface a validation error message.");
    }

    /// <summary>
    /// An endpoint that fails to resolve must keep the dialog open and surface the
    /// inline <c>ManualWifiError</c> message (issue #517).
    /// </summary>
    [TestMethod]
    public async Task ConnectManualWifiCommand_WithInvalidAddress_SetsManualWifiError()
    {
        var viewModel = CreateViewModel();
        viewModel.ManualIpAddress = "not a valid hostname or ip !@#";
        var closeRaised = SubscribeToClose(viewModel);

        await viewModel.ConnectManualWifiCommand.ExecuteAsync(null);

        Assert.IsFalse(closeRaised(), "CloseRequested should not fire when the manual endpoint fails to resolve.");
        Assert.IsNotNull(viewModel.ManualWifiError,
            "ManualWifiError should be set when the entered endpoint fails to resolve.");
    }

    /// <summary>
    /// Editing the manual IP address clears a stale validation error
    /// (the <c>OnManualIpAddressChanged</c> clear-on-edit behavior).
    /// </summary>
    [TestMethod]
    public void ManualWifiError_ClearsWhenManualIpAddressChanges()
    {
        var viewModel = CreateViewModel();
        viewModel.ManualWifiError = "stale validation message";

        viewModel.ManualIpAddress = "192.168.1.50";

        Assert.IsNull(viewModel.ManualWifiError,
            "Editing the manual IP address should clear any prior validation error.");
    }

    [TestMethod]
    public void Close_CalledMultipleTimes_IsIdempotent()
    {
        var viewModel = CreateViewModel();

        viewModel.Close();
        viewModel.Close();
        viewModel.Close();
        // Must not throw — discovery finders are only disposed once.
    }

    private static ConnectionDialogViewModel CreateViewModel()
    {
        var dialogService = new Mock<IDialogService>();
        return new ConnectionDialogViewModel(dialogService.Object);
    }

    private static Func<bool> SubscribeToClose(ConnectionDialogViewModel viewModel)
    {
        var raised = false;
        viewModel.CloseRequested += (_, _) => raised = true;
        return () => raised;
    }
}
