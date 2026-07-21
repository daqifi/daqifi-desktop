using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.Models;
using Daqifi.Desktop.ViewModels;
using Moq;

namespace Daqifi.Desktop.Test.ViewModels;

/// <summary>
/// Regression coverage for the notification-pruning behaviour in <see cref="DaqifiViewModel"/>.
/// The "update available" notification is added with a null device serial (it has no owning
/// device). <see cref="DaqifiViewModel.RemoveNotification"/> runs at the end of every UpdateUi
/// pass, and previously pruned every notification whose serial didn't match a connected device —
/// which silently removed the app-update notice on the same pass that added it, so it never
/// appeared to the user. These tests lock in that the app-update notice (null serial) survives
/// while device-owned notifications for disconnected devices are still pruned.
/// </summary>
[TestClass]
public class DaqifiViewModelNotificationTests
{
    [TestInitialize]
    public void ResetConnectedDevices()
    {
        // RemoveNotification() reads the ConnectionManager singleton to decide pruning. Clear it so
        // these tests don't depend on connected devices left behind by other test classes.
        ConnectionManager.Instance.ConnectedDevices.Clear();
    }

    [TestMethod]
    public void RemoveNotification_KeepsAppUpdateNotice_WithNullDeviceSerial()
    {
        var viewModel = CreateViewModel();
        viewModel.NotificationList.Add(new Notifications
        {
            IsFirmwareUpdate = false,
            DeviceSerialNo = null!,
            Message = "Please update latest application version:  3.3.0",
            Link = "https://github.com/daqifi/daqifi-desktop/releases"
        });

        viewModel.RemoveNotification();

        Assert.AreEqual(1, viewModel.NotificationList.Count,
            "The app-update notice (null device serial) must not be pruned by device-disconnect cleanup.");
        Assert.AreEqual(1, viewModel.NotificationCount,
            "The badge count must reflect the surviving app-update notice.");
    }

    [TestMethod]
    public void RemoveNotification_PrunesDeviceNotification_WithEmptyDeviceSerial()
    {
        var viewModel = CreateViewModel();
        viewModel.NotificationList.Add(new Notifications
        {
            // An empty serial is NOT the app-update sentinel (that is a null serial). A device-owned
            // notification with an empty serial must still be pruned when no such device is connected,
            // otherwise it would keep the badge count stale forever.
            DeviceSerialNo = string.Empty,
            Message = "Device notice with empty serial"
        });

        viewModel.RemoveNotification();

        Assert.AreEqual(0, viewModel.NotificationList.Count,
            "Only a null serial is app-level; an empty-serial device notice must still be pruned.");
    }

    [TestMethod]
    public void RemoveNotification_PrunesNotification_ForDisconnectedDevice()
    {
        var viewModel = CreateViewModel();
        viewModel.NotificationList.Add(new Notifications
        {
            // A serial that is not among ConnectionManager's connected devices (cleared in setup), so
            // this device-owned notification is "orphaned" and should be pruned.
            DeviceSerialNo = "SN-not-connected-regression",
            Message = "Device firmware notice"
        });

        viewModel.RemoveNotification();

        Assert.AreEqual(0, viewModel.NotificationList.Count,
            "Device-owned notifications whose device is not connected must still be pruned.");
    }

    private static DaqifiViewModel CreateViewModel()
    {
        var dialogService = new Mock<IDialogService>();
        return new DaqifiViewModel(dialogService.Object);
    }
}
