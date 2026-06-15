using System.Collections.ObjectModel;
using Daqifi.Desktop.Models;

namespace Daqifi.Desktop.Device.Firmware;

/// <summary>
/// Narrow seam the <see cref="FirmwareUpdateCoordinator"/> uses to read user input and push
/// progress/status/result state back to the host view model.
/// <para>
/// Implemented by <c>DaqifiViewModel</c> so its existing bound <c>[ObservableProperty]</c> fields
/// remain the binding source (firmware XAML is unchanged), while the firmware orchestration and
/// version-check logic live in the coordinator and are unit-testable against a lightweight fake.
/// Only firmware-relevant members are exposed here — the coordinator never reaches into desktop
/// singletons (<c>ConnectionManager.Instance</c> etc.) directly.
/// </para>
/// </summary>
public interface IFirmwareUpdateHost
{
    #region Inputs
    /// <summary>The device the firmware flyout is currently acting on.</summary>
    IStreamingDevice? SelectedDevice { get; }

    /// <summary>
    /// Devices currently connected. Sourced from the host so the coordinator never reaches into
    /// <c>ConnectionManager.Instance</c>; used by the firmware-update version check.
    /// </summary>
    IReadOnlyList<IStreamingDevice> ConnectedDevices { get; }

    /// <summary>
    /// Path to a user-selected <c>.hex</c> file. A non-empty value selects a manual (PIC32-only)
    /// upload; the coordinator clears it after each run so the next run defaults to a full
    /// auto-update (issue #599).
    /// </summary>
    string FirmwareFilePath { get; set; }
    #endregion

    #region Bound progress / status state
    /// <summary>Whether the selected device supports the firmware-update path (USB-connected).</summary>
    bool SelectedDeviceSupportsFirmwareUpdate { set; }

    /// <summary>True while an upload is in progress. Read for the re-entrancy and cancel guards.</summary>
    bool IsFirmwareUploading { get; set; }

    /// <summary>True once an upload has finished successfully.</summary>
    bool IsUploadComplete { set; }

    /// <summary>True when the most recent upload failed.</summary>
    bool HasErrorOccured { set; }

    /// <summary>PIC32 flash progress, 0–100.</summary>
    int UploadFirmwareProgress { set; }

    /// <summary>WiFi-module flash progress, 0–100.</summary>
    int UploadWiFiProgress { set; }

    /// <summary>Status line shown beneath the firmware progress bars.</summary>
    string FirmwareUpdateStatusText { set; }
    #endregion

    #region Collaborators
    /// <summary>The shared, bound notification list. The coordinator adds/removes firmware notifications.</summary>
    ObservableCollection<Notifications> Notifications { get; }

    /// <summary>
    /// The device being flashed, surfaced so the connection manager can suppress reconnect churn
    /// while an update runs. Set to the device at start and back to <c>null</c> when finished.
    /// </summary>
    IStreamingDevice? DeviceBeingUpdated { set; }

    /// <summary>Re-syncs the notification badge count after the coordinator mutates the list.</summary>
    void RefreshNotificationCount();

    /// <summary>
    /// Presents a firmware error dialog. Dialog presentation (and its UI-thread marshalling) is a
    /// view concern, so the coordinator delegates it here rather than touching WPF directly.
    /// </summary>
    /// <param name="message">The error message to display.</param>
    void ShowFirmwareError(string message);

    /// <summary>
    /// Presents the firmware-update success dialog and closes the firmware flyout. Kept on the host
    /// so the coordinator stays free of WPF dependencies.
    /// </summary>
    void ShowFirmwareUpdateSucceeded();
    #endregion
}
