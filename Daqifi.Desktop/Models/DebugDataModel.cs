using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Daqifi.Desktop.Models;

/// <summary>
/// Model for holding real-time debug information about device data flow
/// </summary>
public partial class DebugDataModel : ObservableObject
{
    [ObservableProperty]
    private DateTime _timestamp;

    [ObservableProperty]
    private string _deviceId = string.Empty;

    [ObservableProperty]
    private int _analogDataCount;

    [ObservableProperty]
    private List<int> _rawAnalogValues = new();

    [ObservableProperty]
    private List<double> _scaledAnalogValues = new();

    [ObservableProperty]
    private string _channelEnableMask = string.Empty;

    [ObservableProperty]
    private string _channelEnableBinary = string.Empty;

    [ObservableProperty]
    private List<string> _activeChannelNames = new();

    [ObservableProperty]
    private List<int> _activeChannelIndices = new();

    [ObservableProperty]
    private List<string> _dataFlowMapping = new();

    [ObservableProperty]
    private bool _hasDigitalData;

    [ObservableProperty]
    private string _messageType = string.Empty;

    [ObservableProperty]
    private string _deviceResponse = string.Empty;

    /// <summary>
    /// Creates a formatted string showing the data flow visualization
    /// </summary>
    public string DataFlowVisualization
    {
        get
        {
            if (RawAnalogValues.Count == 0)
                return "No analog data";

            var lines = new List<string>
            {
                $"Device Sends: [{string.Join(", ", RawAnalogValues)}]",
                "Data Mapping:"
            };

            for (int i = 0; i < Math.Min(RawAnalogValues.Count, ActiveChannelNames.Count); i++)
            {
                var channelName = i < ActiveChannelNames.Count ? ActiveChannelNames[i] : "Unknown";
                var channelIndex = i < ActiveChannelIndices.Count ? ActiveChannelIndices[i] : -1;
                var rawValue = RawAnalogValues[i];
                var scaledValue = i < ScaledAnalogValues.Count ? ScaledAnalogValues[i] : 0.0;
                
                lines.Add($"  data[{i}] = {rawValue} → {channelName}(idx:{channelIndex}) = {scaledValue:F3}V");
            }

            return string.Join("\n", lines);
        }
    }

    /// <summary>
    /// Creates a summary string for logging
    /// </summary>
    public string LogSummary => 
        $"[DEBUG] {Timestamp:HH:mm:ss.fff} | Device:{DeviceId} | " +
        $"Channels:{string.Join(",", ActiveChannelNames)} | " +
        $"Raw:[{string.Join(",", RawAnalogValues)}] | " +
        $"Scaled:[{string.Join(",", ScaledAnalogValues.ConvertAll(v => v.ToString("F3")))}] | " +
        $"Mask:{ChannelEnableMask}({ChannelEnableBinary})";
}

/// <summary>
/// Collection to hold recent debug data entries
/// </summary>
public partial class DebugDataCollection : ObservableObject
{
    private const int MaxEntries = 100;

    [ObservableProperty]
    private ObservableCollection<DebugDataModel> _entries = new();

    [ObservableProperty]
    private DebugDataModel? _latestEntry;

    public void AddEntry(DebugDataModel entry)
    {
        // Ensure we're on the UI thread
        if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == false)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => AddEntry(entry));
            return;
        }

        Entries.Insert(0, entry);

        // Keep only the most recent entries
        while (Entries.Count > MaxEntries)
        {
            Entries.RemoveAt(Entries.Count - 1);
        }

        LatestEntry = entry;
    }

    public void Clear()
    {
        // Ensure we're on the UI thread
        if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == false)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => Clear());
            return;
        }

        Entries.Clear();
        LatestEntry = null;
    }
}