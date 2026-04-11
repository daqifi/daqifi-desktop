using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Daqifi.Desktop.Logger;

/// <summary>
/// Groups legend items by device for compact display in the legend panel.
/// </summary>
public partial class DeviceLegendGroup : ObservableObject
{
    /// <summary>
    /// Full device serial number.
    /// </summary>
    public string DeviceSerialNo { get; }

    /// <summary>
    /// Truncated serial for display (e.g., "...4104").
    /// </summary>
    public string TruncatedSerialNo => DeviceSerialNo?.Length > 4
        ? $"...{DeviceSerialNo[^4..]}"
        : DeviceSerialNo ?? string.Empty;

    /// <summary>
    /// Channel legend items belonging to this device.
    /// </summary>
    public ObservableCollection<LoggedSeriesLegendItem> Channels { get; } = new();

    /// <summary>
    /// Initializes a new device legend group.
    /// </summary>
    /// <param name="deviceSerialNo">The device serial number for this group.</param>
    public DeviceLegendGroup(string deviceSerialNo)
    {
        DeviceSerialNo = deviceSerialNo;
    }
}
