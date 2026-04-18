using Daqifi.Desktop;
using Daqifi.Desktop.Device.SerialDevice;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.ViewModels;
using Moq;

namespace Daqifi.Desktop.Test.ViewModels;

[TestClass]
public class ConnectionDialogViewModelCloseTests
{
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
    }

    [TestMethod]
    public async Task ConnectManualWifiCommand_WithBlankAddress_DoesNotRaiseCloseRequested()
    {
        var viewModel = CreateViewModel();
        viewModel.ManualIpAddress = "";
        var closeRaised = SubscribeToClose(viewModel);

        await viewModel.ConnectManualWifiCommand.ExecuteAsync(null);

        Assert.IsFalse(closeRaised(), "CloseRequested should not fire when the manual IP is blank.");
    }

    [TestMethod]
    public async Task ConnectManualWifiCommand_WithInvalidAddress_DoesNotRaiseCloseRequested()
    {
        var viewModel = CreateViewModel();
        viewModel.ManualIpAddress = "not a valid hostname or ip !@#";
        var closeRaised = SubscribeToClose(viewModel);

        await viewModel.ConnectManualWifiCommand.ExecuteAsync(null);

        Assert.IsFalse(closeRaised(), "CloseRequested should not fire when the manual endpoint fails to resolve.");
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
