using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;

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
    /// Configured sampling frequency in Hz for this device, captured at log start.
    /// Null when the frequency is unknown (e.g., legacy sessions logged before
    /// device metadata was persisted).
    /// </summary>
    [ObservableProperty]
    private int? _samplingFrequencyHz;

    /// <summary>
    /// User-facing frequency string (e.g., "100 Hz", "1 kHz", "1.5 kHz").
    /// Returns an empty string when the frequency is unknown.
    /// </summary>
    public string SamplingFrequencyDisplay => FormatFrequency(SamplingFrequencyHz);

    partial void OnSamplingFrequencyHzChanged(int? value)
    {
        OnPropertyChanged(nameof(SamplingFrequencyDisplay));
        OnPropertyChanged(nameof(HasSamplingFrequency));
    }

    /// <summary>
    /// Whether a frequency is known and should be shown in the UI.
    /// Bound by the legend XAML to hide the row for legacy sessions.
    /// </summary>
    public bool HasSamplingFrequency => SamplingFrequencyHz.HasValue && SamplingFrequencyHz.Value > 0;

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

    /// <summary>
    /// Formats a frequency value in Hz as a compact, user-friendly string.
    /// </summary>
    /// <param name="frequencyHz">Frequency in Hz, or null if unknown.</param>
    /// <returns>
    /// A formatted string like "100 Hz", "1 kHz", "1.5 kHz", or "2 MHz".
    /// Returns an empty string when <paramref name="frequencyHz"/> is null or non-positive.
    /// </returns>
    public static string FormatFrequency(int? frequencyHz)
    {
        if (!frequencyHz.HasValue || frequencyHz.Value <= 0)
        {
            return string.Empty;
        }

        var hz = frequencyHz.Value;

        if (hz >= 1_000_000)
        {
            var mhz = hz / 1_000_000.0;
            return mhz % 1 == 0
                ? string.Format(CultureInfo.InvariantCulture, "{0:0} MHz", mhz)
                : string.Format(CultureInfo.InvariantCulture, "{0:0.##} MHz", mhz);
        }

        if (hz >= 1_000)
        {
            var khz = hz / 1_000.0;
            return khz % 1 == 0
                ? string.Format(CultureInfo.InvariantCulture, "{0:0} kHz", khz)
                : string.Format(CultureInfo.InvariantCulture, "{0:0.##} kHz", khz);
        }

        return string.Format(CultureInfo.InvariantCulture, "{0} Hz", hz);
    }
}
