using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Daqifi.Desktop.Device;

namespace Daqifi.Desktop.ViewModels;

/// <summary>
/// Wraps a connected device for the new-profile creation form so each item
/// carries its own IsSelected state and a nested channel list.
/// </summary>
public partial class NewProfileDeviceItem : ObservableObject
{
    #region Observable Properties
    [ObservableProperty] private bool _isSelected;
    #endregion

    #region Public Properties
    /// <summary>
    /// The connected streaming device this row represents.
    /// </summary>
    public required IStreamingDevice Device { get; init; }

    /// <summary>
    /// Display name of the underlying device.
    /// </summary>
    public string Name => Device.Name;

    /// <summary>
    /// Serial number of the underlying device (empty string when unknown).
    /// </summary>
    public string SerialNo => Device.DeviceSerialNo ?? string.Empty;

    /// <summary>
    /// Child channels available for selection under this device in the
    /// new-profile form. Populated when <see cref="IsSelected"/> becomes true.
    /// </summary>
    public ObservableCollection<NewProfileChannelItem> ChannelItems { get; } = [];
    #endregion
}
