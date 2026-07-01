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
/// appeared to the user. These tests lock in that app-level notifications survive while
/// device-owned notifications for disconnected devices are still pruned.
/// </summary>
[TestClass]
public class DaqifiViewModelNotificationTests
{
    [TestMethod]
    public void RemoveNotification_KeepsAppLevelNotification_WithNullDeviceSerial()
    {
        var viewModel = CreateViewModel();
        viewModel.NotificationList.Add(new Notifications
        {
            IsFirmwareUpdate = false,
            DeviceSerialNo = null,
            Message = "Please update latest application version:  3.3.0",
            Link = "https://github.com/daqifi/daqifi-desktop/releases"
        });

        viewModel.RemoveNotification();

        Assert.AreEqual(1, viewModel.NotificationList.Count,
            "App-level notifications (null device serial) must not be pruned by device-disconnect cleanup.");
        Assert.AreEqual(1, viewModel.NotificationCount,
            "The badge count must reflect the surviving app-level notification.");
    }

    [TestMethod]
    public void RemoveNotification_KeepsAppLevelNotification_WithEmptyDeviceSerial()
    {
        var viewModel = CreateViewModel();
        viewModel.NotificationList.Add(new Notifications
        {
            DeviceSerialNo = string.Empty,
            Message = "App-level notice"
        });

        viewModel.RemoveNotification();

        Assert.AreEqual(1, viewModel.NotificationList.Count,
            "An empty (not just null) serial is also app-level and must survive pruning.");
    }

    [TestMethod]
    public void RemoveNotification_StillPrunesNotification_ForDisconnectedDevice()
    {
        var viewModel = CreateViewModel();
        viewModel.NotificationList.Add(new Notifications
        {
            // A serial that is not among ConnectionManager's connected devices, so this
            // device-owned notification is "orphaned" and should still be pruned.
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
