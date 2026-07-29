using System.ComponentModel;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.ViewModels;
using Moq;

namespace Daqifi.Desktop.Test.ViewModels;

/// <summary>
/// Covers the Devices drawer's NAME field: the failure contract of
/// <see cref="DaqifiViewModel.SetFriendlyName"/> (which exceptions become inline feedback, and the
/// message the user sees for each), and the edit buffer's behaviour when the device reports a name
/// of its own from a background thread (issue #778).
/// </summary>
[TestClass]
public class DaqifiViewModelFriendlyNameTests
{
    private const string GENERIC_FAILURE_MESSAGE =
        "Failed to set the device name. See the application log for details.";

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

    [TestMethod]
    public async Task SetFriendlyName_WhenSelectionChangesDuringWrite_LeavesTheNewDrawerAlone()
    {
        // Arrange — the write runs off the UI thread, so the user can open another device's drawer
        // before it returns. FriendlyNameError is shared drawer state that OpenSettings clears on
        // selection, so a late failure from the previous device must not land on its successor.
        var successor = CreateConnectedDevice().Object;
        var device = CreateConnectedDevice();
        DaqifiViewModel? viewModel = null;
        device.Setup(d => d.SetFriendlyName(It.IsAny<string>()))
            .Callback(() =>
            {
                viewModel!.SelectedDevice = successor;
                viewModel.PendingFriendlyName = "Successor Rig";
            })
            .Throws(new InvalidOperationException("Device is not connected."));
        viewModel = CreateViewModel(device.Object);

        // Act
        await viewModel.SetFriendlyName();

        // Assert
        Assert.IsNull(viewModel.FriendlyNameError, "A stale failure must not surface on the newly selected device.");
    }

    [TestMethod]
    public async Task SetFriendlyName_WhenSelectionChangesDuringWrite_DoesNotSeedTheNewDevicesField()
    {
        // Arrange — same race on the success path: seeding would overwrite the successor's NAME
        // field with the name just written to the device the user navigated away from.
        var successor = CreateConnectedDevice().Object;
        var device = CreateConnectedDevice();
        DaqifiViewModel? viewModel = null;
        device.Setup(d => d.SetFriendlyName(It.IsAny<string>()))
            .Callback(() =>
            {
                viewModel!.SelectedDevice = successor;
                viewModel.PendingFriendlyName = "Successor Rig";
            });
        viewModel = CreateViewModel(device.Object);

        // Act
        await viewModel.SetFriendlyName();

        // Assert
        Assert.AreEqual("Successor Rig", viewModel.PendingFriendlyName);
        Assert.IsFalse(viewModel.FriendlyNameApplied, "The applied banner belongs to the drawer that is now closed.");
    }

    [TestMethod]
    public void DeviceReportsName_FromBackgroundThread_UpdatesTheDrawerField()
    {
        // Arrange — the device's FriendlyName PropertyChanged is raised on Core's inbound-message
        // consumer thread (a dedicated background Thread in Core's StreamMessageConsumer), never on
        // the UI thread. The handler marshals through UiThreadHelper, which runs inline in this
        // WPF-runtime-free test host (Application.Current is null); the point of raising from a real
        // background thread is that the hop must neither block nor silently drop the update there.
        var device = CreateNotifyingDevice("Bench Rig 7");
        var viewModel = CreateViewModel(device.Object);
        viewModel.SeedPendingFriendlyName("Bench Rig 3");

        // Act
        RaiseFriendlyNameChangedOnBackgroundThread(device);

        // Assert
        Assert.AreEqual("Bench Rig 7", viewModel.PendingFriendlyName);
    }

    [TestMethod]
    public void DeviceReportsName_WhileUserIsTyping_DoesNotClobberTheInProgressEdit()
    {
        // Arrange — issue #778: a late-duplicate SYSTem:SYSInfoPB? status response (Core re-sends
        // the request while waiting for channels to populate, so a stale one can land after
        // Connect() returns and the drawer is open) must not seed over what the user is typing.
        var device = CreateNotifyingDevice("Name From Device");
        var viewModel = CreateViewModel(device.Object);
        viewModel.SeedPendingFriendlyName("Bench Rig 3");
        viewModel.PendingFriendlyName = "Bench Rig 4";  // user edit — marks the buffer dirty

        // Act
        RaiseFriendlyNameChangedOnBackgroundThread(device);

        // Assert
        Assert.AreEqual("Bench Rig 4", viewModel.PendingFriendlyName);
    }

    [TestMethod]
    public void SelectionMovingOn_DetachesThePreviousDevicesNameUpdates()
    {
        // Arrange — the first line of defence against a name from a device the user has navigated
        // away from landing in the successor's drawer: OnSelectedDeviceChanged unsubscribes the
        // outgoing device. (The apply-time `device != SelectedDevice` guard in the handler covers
        // the residual case this cannot reach — an update already in flight when the selection
        // changes, which needs a real dispatcher queue to stage and so is not simulated here.)
        var device = CreateNotifyingDevice("Name From Old Device");
        var viewModel = CreateViewModel(device.Object);
        viewModel.SelectedDevice = CreateConnectedDevice().Object;
        viewModel.SeedPendingFriendlyName("Successor Rig");

        // Act
        RaiseFriendlyNameChangedOnBackgroundThread(device);

        // Assert
        Assert.AreEqual("Successor Rig", viewModel.PendingFriendlyName);
    }

    [TestMethod]
    public void DeviceReportsEmptyName_FromBackgroundThread_ClearsTheDrawerField()
    {
        // Arrange — the second reachability path in issue #778: transport instances are reused
        // across reconnects on the same COM port, and AbstractStreamingDevice.OnStatusMessageReceived
        // assigns friendly_device_name unconditionally, so a device with no name set reports empty.
        // A clean buffer must follow that clear rather than keep the previous device's name.
        var device = CreateNotifyingDevice(string.Empty);
        var viewModel = CreateViewModel(device.Object);
        viewModel.SeedPendingFriendlyName("Previous Device Name");

        // Act
        RaiseFriendlyNameChangedOnBackgroundThread(device);

        // Assert
        Assert.AreEqual(string.Empty, viewModel.PendingFriendlyName);
    }

    [TestMethod]
    public void DeviceReportsUnrelatedProperty_LeavesTheDrawerFieldAlone()
    {
        // Arrange — a streaming device raises other change notifications too (AbstractStreamingDevice
        // raises IsConnected from its Core status handler, for one); only FriendlyName may touch the
        // edit buffer, and only it should pay for the dispatcher hop.
        var device = CreateNotifyingDevice("Name From Device");
        var viewModel = CreateViewModel(device.Object);
        viewModel.SeedPendingFriendlyName("Bench Rig 3");

        // Act
        device.As<INotifyPropertyChanged>().Raise(
            d => d.PropertyChanged += null, new PropertyChangedEventArgs(nameof(IStreamingDevice.IsConnected)));

        // Assert
        Assert.AreEqual("Bench Rig 3", viewModel.PendingFriendlyName);
    }

    [TestMethod]
    public void DeviceReportsName_DoesNotMutateTheBufferOnTheEventThread()
    {
        // Arrange — the guard against this PR silently regressing. The other tests here run in a
        // host with no Application.Current, where UiThreadHelper deliberately falls back to running
        // inline, so they would still pass if the handler went back to mutating the UI-bound buffer
        // straight from the message-consumer thread. Swapping in an invoker that captures the action
        // instead of running it makes the hop itself observable: nothing may reach the buffer until
        // the marshalled action is actually run.
        var device = CreateNotifyingDevice("Name From Device");
        var viewModel = CreateViewModel(device.Object);
        viewModel.SeedPendingFriendlyName("Bench Rig 3");
        Action? marshalled = null;
        viewModel.UiInvoker = (action, _) => marshalled = action;

        // Act
        RaiseFriendlyNameChangedOnBackgroundThread(device);

        // Assert
        Assert.IsNotNull(marshalled, "The update was not handed to the UI-thread invoker at all.");
        Assert.AreEqual(
            "Bench Rig 3", viewModel.PendingFriendlyName,
            "The edit buffer was mutated on the event thread instead of being marshalled.");
    }

    [TestMethod]
    public void DeviceReportsName_AppliesTheNameOnceTheMarshalledActionRuns()
    {
        // Arrange — the other half of the contract: the update must be deferred, not discarded.
        var device = CreateNotifyingDevice("Name From Device");
        var viewModel = CreateViewModel(device.Object);
        viewModel.SeedPendingFriendlyName("Bench Rig 3");
        Action? marshalled = null;
        viewModel.UiInvoker = (action, _) => marshalled = action;
        RaiseFriendlyNameChangedOnBackgroundThread(device);

        // Act — stand in for the dispatcher draining its queue on the UI thread.
        marshalled!();

        // Assert
        Assert.AreEqual("Name From Device", viewModel.PendingFriendlyName);
    }

    [TestMethod]
    public void Dispose_DetachesTheSelectedDevicesNameUpdates()
    {
        // Arrange — the drawer's NAME-field sync is subscribed on whichever device is SelectedDevice,
        // not on the _subscribedDevices set Dispose loops over, so it needs its own teardown. Left
        // attached, the selected device keeps the disposed view model rooted — exactly the leak the
        // rest of Dispose exists to prevent (issue #592) — and a late name update still runs against
        // a disposed view model.
        var device = CreateNotifyingDevice("Name From Device");
        var viewModel = CreateViewModel(device.Object);
        viewModel.SeedPendingFriendlyName("Bench Rig 3");

        // Act
        viewModel.Dispose();
        RaiseFriendlyNameChangedOnBackgroundThread(device);

        // Assert
        Assert.AreEqual(
            "Bench Rig 3", viewModel.PendingFriendlyName,
            "A device-reported name reached the edit buffer after the view model was disposed.");
    }

    [TestMethod]
    public void SelectingADeviceAfterDispose_DoesNotReattachTheNameUpdates()
    {
        // Arrange — Dispose runs from MainWindow's Closing handler, not Closed, so the window is
        // still alive when it returns: another Closing handler cancelling the close leaves a device
        // tile clickable, and that click assigns SelectedDevice. Re-attaching there would undo the
        // teardown and root the disposed view model on the newly selected device.
        var viewModel = CreateViewModel(CreateConnectedDevice().Object);
        viewModel.Dispose();
        var successor = CreateNotifyingDevice("Name From Successor");

        // Act
        viewModel.SelectedDevice = successor.Object;
        viewModel.SeedPendingFriendlyName("Bench Rig 3");
        RaiseFriendlyNameChangedOnBackgroundThread(successor);

        // Assert
        Assert.AreEqual(
            "Bench Rig 3", viewModel.PendingFriendlyName,
            "The disposed view model re-subscribed to the newly selected device.");
    }

    /// <summary>
    /// Raises the device's <see cref="IStreamingDevice.FriendlyName"/> change notification from a
    /// thread-pool thread and waits for it, mirroring how Core's message-consumer thread delivers
    /// status-driven name updates.
    /// </summary>
    private static void RaiseFriendlyNameChangedOnBackgroundThread(Mock<IStreamingDevice> device)
    {
        Task.Run(() => device.As<INotifyPropertyChanged>().Raise(
                d => d.PropertyChanged += null,
                new PropertyChangedEventArgs(nameof(IStreamingDevice.FriendlyName))))
            .GetAwaiter().GetResult();
    }

    private static Mock<IStreamingDevice> CreateConnectedDevice()
    {
        var device = new Mock<IStreamingDevice>();
        device.SetupGet(d => d.IsConnected).Returns(true);
        device.SetupGet(d => d.DeviceDisplayName).Returns("12345");
        return device;
    }

    /// <summary>
    /// Builds a connected device that also implements <see cref="INotifyPropertyChanged"/>, which is
    /// what <c>DaqifiViewModel</c> subscribes to when the selected device changes.
    /// </summary>
    private static Mock<IStreamingDevice> CreateNotifyingDevice(string friendlyName)
    {
        var device = CreateConnectedDevice();
        device.SetupGet(d => d.FriendlyName).Returns(friendlyName);
        device.As<INotifyPropertyChanged>();
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
