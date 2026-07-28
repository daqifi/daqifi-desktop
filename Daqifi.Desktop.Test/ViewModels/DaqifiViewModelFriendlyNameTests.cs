using Daqifi.Desktop.Device;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.ViewModels;
using Moq;

namespace Daqifi.Desktop.Test.ViewModels;

/// <summary>
/// Covers the failure contract of the Devices drawer's NAME field: which exceptions
/// <see cref="DaqifiViewModel.SetFriendlyName"/> turns into inline feedback, and which message
/// the user sees for each.
/// </summary>
[TestClass]
public class DaqifiViewModelFriendlyNameTests
{
    private const string GENERIC_FAILURE_MESSAGE = "Failed to set the device name. See the application log for details.";

    [TestMethod]
    public async Task SetFriendlyName_WhenDeviceThrowsArgumentException_ShowsValidationMessageVerbatim()
    {
        // Arrange — validation failures are user-fixable, so the device's own message is shown.
        var device = CreateConnectedDevice();
        device.Setup(d => d.SetFriendlyName(It.IsAny<string>()))
            .Throws(new ArgumentException("Device name must be 1-31 printable ASCII characters.", "name"));
        var viewModel = CreateViewModel(device.Object);

        // Act
        await viewModel.SetFriendlyName();

        // Assert
        StringAssert.StartsWith(
            viewModel.FriendlyNameError, "Device name must be 1-31 printable ASCII characters.");
        Assert.IsFalse(viewModel.FriendlyNameApplied);
    }

    [TestMethod]
    public async Task SetFriendlyName_WhenDeviceThrowsUnexpectedException_ShowsGenericErrorWithoutEscaping()
    {
        // Arrange — anything other than a validation failure (a mid-write transport fault, say)
        // must not escape the command: an unhandled exception out of an async RelayCommand takes
        // down the app instead of surfacing in the drawer.
        var device = CreateConnectedDevice();
        device.Setup(d => d.SetFriendlyName(It.IsAny<string>()))
            .Throws(new InvalidOperationException("Device is not connected."));
        var viewModel = CreateViewModel(device.Object);

        // Act
        await viewModel.SetFriendlyName();

        // Assert
        Assert.AreEqual(GENERIC_FAILURE_MESSAGE, viewModel.FriendlyNameError);
        Assert.IsFalse(viewModel.FriendlyNameApplied);
    }

    private static Mock<IStreamingDevice> CreateConnectedDevice()
    {
        var device = new Mock<IStreamingDevice>();
        device.SetupGet(d => d.IsConnected).Returns(true);
        device.SetupGet(d => d.DeviceDisplayName).Returns("12345");
        return device;
    }

    private static DaqifiViewModel CreateViewModel(IStreamingDevice device)
    {
        var dialogService = new Mock<IDialogService>();
        var viewModel = new DaqifiViewModel(dialogService.Object)
        {
            SelectedDevice = device,
            PendingFriendlyName = "Bench Rig 3"
        };
        return viewModel;
    }
}
