using CommunityToolkit.Mvvm.ComponentModel;
using Daqifi.Desktop.Channel;
using System.ComponentModel.DataAnnotations.Schema;

namespace Daqifi.Desktop.Logger;

public class LoggingSession : ObservableObject
{
    #region Private Data
    private string _name;
    private long? _sampleCount;
    #endregion

    #region Properties
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int ID { get; set; }
    public DateTime SessionStart { get; set; }

    /// <summary>
    /// Total number of samples persisted for this session, populated when the
    /// session ends and lazy-backfilled on first list load for legacy sessions.
    /// Null only for sessions that have not been counted yet. Setter notifies
    /// dependent computed properties so the list view refreshes if the count
    /// is updated after the entity is already bound.
    /// </summary>
    public long? SampleCount
    {
        get => _sampleCount;
        set
        {
            if (SetProperty(ref _sampleCount, value))
            {
                OnPropertyChanged(nameof(SampleCountDisplay));
                OnPropertyChanged(nameof(HasSampleCount));
            }
        }
    }
    public virtual ICollection<Channel.Channel> Channels { get; set; } = new List<Channel.Channel>();
    public virtual ICollection<DataSample> DataSamples { get; set; } = new List<DataSample>();
    public virtual ICollection<SessionDeviceMetadata> DeviceMetadata { get; set; } = new List<SessionDeviceMetadata>();

    public string Name
    {
        get => string.IsNullOrWhiteSpace(_name) ? "Session " + ID : _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>
    /// A compact, human-readable summary of each device's sampling frequency
    /// for this session. Empty for legacy sessions that lack metadata.
    /// Single device: "10 Hz". Multi device: "...4106: 10 Hz · ...5678: 1 kHz".
    /// </summary>
    [NotMapped]
    public string FrequencyDisplay
    {
        get
        {
            if (DeviceMetadata == null || DeviceMetadata.Count == 0)
            {
                return string.Empty;
            }

            if (DeviceMetadata.Count == 1)
            {
                var only = DeviceMetadata.First();
                return DeviceLegendGroup.FormatFrequency(only.SamplingFrequencyHz);
            }

            var parts = new List<string>(DeviceMetadata.Count);
            foreach (var entry in DeviceMetadata.OrderBy(m => m.DeviceSerialNo, StringComparer.Ordinal))
            {
                var formatted = DeviceLegendGroup.FormatFrequency(entry.SamplingFrequencyHz);
                if (string.IsNullOrEmpty(formatted)) { continue; }
                var serialTail = !string.IsNullOrEmpty(entry.DeviceSerialNo) && entry.DeviceSerialNo.Length > 4
                    ? "..." + entry.DeviceSerialNo[^4..]
                    : entry.DeviceSerialNo ?? string.Empty;
                parts.Add($"{serialTail}: {formatted}");
            }
            return string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// Number of distinct devices participating in this session, derived from
    /// device metadata. Zero for legacy sessions without metadata.
    /// </summary>
    [NotMapped]
    public int DeviceCount => DeviceMetadata?.Count ?? 0;

    /// <summary>True when <see cref="FrequencyDisplay"/> has content to show.</summary>
    [NotMapped]
    public bool HasFrequencyDisplay => !string.IsNullOrEmpty(FrequencyDisplay);

    /// <summary>True when more than one device participated in the session.</summary>
    [NotMapped]
    public bool HasMultipleDevices => DeviceCount > 1;

    /// <summary>
    /// Sample count formatted with thousands separators (e.g. "16,000"),
    /// or empty string when the count has not been recorded yet.
    /// </summary>
    [NotMapped]
    public string SampleCountDisplay => SampleCount.HasValue
        ? SampleCount.Value.ToString("N0", System.Globalization.CultureInfo.CurrentCulture)
        : string.Empty;

    /// <summary>True when <see cref="SampleCount"/> is available.</summary>
    [NotMapped]
    public bool HasSampleCount => SampleCount.HasValue;
    #endregion

    #region Constructors
    public LoggingSession() { }

    public LoggingSession(int id, string name)
    {
        ID = id;
        SessionStart = DateTime.Now;
        Name = name;
    }
    #endregion

    #region Object Overrides
    public override bool Equals(object obj)
    {
        return obj is LoggingSession sessionObj && sessionObj.ID == ID;
    }

    public override int GetHashCode()
    {
        return ID.GetHashCode();
    }
    #endregion
}
