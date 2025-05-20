using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Device;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Linq;
using System.Timers;
using System.Windows;

namespace Daqifi.Desktop.Logger;

/// <summary>
/// Provides performance metrics for data acquisition.
/// </summary>
public partial class PerformanceMonitorViewModel : ObservableObject, ILogger
{
    // Constants for thresholds (in milliseconds)
    private const int TIMER_INTERVAL_MS = 1000; // 1 second interval

    #region "Private Data"

    internal class ChannelBuffer
    {
        public ChannelBuffer()
        {

        }

        /// <summary>
        /// The number of samples seen
        /// </summary>
        public int SampleCount { get; set; }

        /// <summary>
        /// The total elapsed time
        /// </summary>
        public long FirstSampleTicks { get; set; }

        /// <summary>
        /// The total elapsed time
        /// </summary>
        public long LastSampleTicks { get; set; }

        /// <summary>
        /// The frequency sample rate for this channel
        /// </summary>
        public double SampleRate
        {
            get
            {
                // FirstSampleTicks is measured from the end of the sample, so we need to drop the first sample
                var delta = new TimeSpan(LastSampleTicks - FirstSampleTicks);
                return delta.Ticks > 0 ? (SampleCount - 1) / delta.TotalSeconds : 0.0;
            }
        }

        public void Reset()
        {
            SampleCount = 0;
            FirstSampleTicks = 0;
            LastSampleTicks = 0;
        }
    }


    /// <summary>
    /// Summary results object for a window of data.
    /// </summary>
    private class SummaryBuffer
    {
        public SummaryBuffer()
        {
            Channels = new Dictionary<string, ChannelBuffer>(64);
        }

        /// <summary>
        /// The number of device messages seen in this window (used for buffer fullness).
        /// </summary>
        public int SampleCount { get; set; } // This was for overall device messages

        /// <summary>
        /// The channels seen.
        /// </summary>
        public Dictionary<string, ChannelBuffer> Channels { get; set; }

        public void Reset()
        {
            SampleCount = 0;
            foreach (var pair in Channels)
            {
                pair.Value.Reset();
            }
        }
    }

    [ObservableProperty]
    private int _sampleSize;

    [ObservableProperty]
    private bool _enabled;

    /// <summary>
    /// The in-progress sample set
    /// </summary>
    private SummaryBuffer _buffer;

    /// <summary>
    /// The last completed sample set
    /// </summary>
    private SummaryBuffer _current;

    private System.Timers.Timer _performanceTimer;

    #endregion

    #region "Properties"
    #endregion

    #region "Performance Monitor Properties"

    /// <summary>
    /// The current measured sample rate (samples/sec)
    /// </summary>
    [ObservableProperty]
    private double _actualSampleRate;

    /// <summary>
    /// The target sample rate (samples/sec)
    /// </summary>
    [ObservableProperty]
    private double _targetSampleRate;

    /// <summary>
    /// Sampling efficiency percentage (0-100)
    /// </summary>
    [ObservableProperty]
    private double _samplingEfficiencyPercentage;

    /// <summary>
    /// Sampling efficiency status (Good, Warning, Critical)
    /// </summary>
    [ObservableProperty]
    private string _samplingEfficiencyStatus = "Good";

    /// <summary>
    /// Overall system status (Healthy, Warning, Critical)
    /// </summary>
    [ObservableProperty]
    private string _overallSystemStatus = "Healthy";

    /// <summary>
    /// Overall system status message (System Healthy, Performance Warning, Performance Critical)
    /// </summary>
    [ObservableProperty]
    private string _overallSystemStatusMessage = "System Healthy";

    #endregion

    #region "Constructor"

    public PerformanceMonitorViewModel()
    {
        _sampleSize = 1000;
        _buffer = new SummaryBuffer();
        _current = new SummaryBuffer();
        InitializeTimer();
    }

    /// <summary>
    /// Creates a new instance
    /// </summary>
    /// <param name="sampleSize">The size of the sample set (no longer used for swap timing)</param>
    public PerformanceMonitorViewModel(int sampleSize)
    {
        _sampleSize = sampleSize;
        _buffer = new SummaryBuffer();
        _current = new SummaryBuffer();
        _enabled = true;
        InitializeTimer();
        Start();
    }

    private void InitializeTimer()
    {
        _performanceTimer = new System.Timers.Timer(TIMER_INTERVAL_MS);
        _performanceTimer.Elapsed += PerformanceTimer_Elapsed;
        _performanceTimer.AutoReset = true;
    }

    private void PerformanceTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
    {
        lock (_current)
        {
            SwapBuffer();
        }
    }

    #endregion

    public void Log(DataSample dataSample)
    {
        if (!_enabled)
        {
            return;
        }

        lock(_buffer)
        {
            if (!_buffer.Channels.ContainsKey(dataSample.ChannelName))
            {
                _buffer.Channels[dataSample.ChannelName] = new ChannelBuffer();
            }

            var channelBuffer = _buffer.Channels[dataSample.ChannelName];

            if (channelBuffer.SampleCount == 0)
            {
                channelBuffer.FirstSampleTicks = dataSample.TimestampTicks;
            }
            channelBuffer.LastSampleTicks = dataSample.TimestampTicks;
            ++channelBuffer.SampleCount;
        }
    }

    /// <summary>
    /// Consumes a device message
    /// </summary>
    /// <param name="dataSample"></param>
    public void Log(DeviceMessage dataSample)
    {
        if (!_enabled)
        {
            return;
        }

        lock (_buffer)
        {
            _buffer.SampleCount++; // Increment for overall device message count for the window
        }
    }

    private void SwapBuffer()
    {
        lock(_buffer)
        {
            lock (_current)
            {
                (_current, _buffer) = (_buffer, _current);
                _buffer.Reset();

                NotifyResultsChanged();
            }
        }
    }

    private void NotifyResultsChanged()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            ActualSampleRate = _current.Channels.Values.Sum(c => c.SampleRate);

            if (TargetSampleRate > 0)
            {
                SamplingEfficiencyPercentage = Math.Max(0, (ActualSampleRate / TargetSampleRate) * 100.0);
            }
            else
            {
                SamplingEfficiencyPercentage = 100.0;
            }

            if (SamplingEfficiencyPercentage >= 95.0)
                SamplingEfficiencyStatus = "Good";
            else if (SamplingEfficiencyPercentage >= 80.0)
                SamplingEfficiencyStatus = "Warning";
            else
                SamplingEfficiencyStatus = "Critical";
            
            if (TargetSampleRate == 0) SamplingEfficiencyStatus = "Good";

            if (SamplingEfficiencyStatus == "Critical")
            {
                OverallSystemStatus = "Critical";
                OverallSystemStatusMessage = "Performance Critical";
            }
            else if (SamplingEfficiencyStatus == "Warning")
            {
                OverallSystemStatus = "Warning";
                OverallSystemStatusMessage = "Performance Warning";
            }
            else
            {
                OverallSystemStatus = "Healthy";
                OverallSystemStatusMessage = "System Healthy";
            }

            OnPropertyChanged(nameof(ActualSampleRate));
            OnPropertyChanged(nameof(TargetSampleRate));
            OnPropertyChanged(nameof(SamplingEfficiencyPercentage));
            OnPropertyChanged(nameof(SamplingEfficiencyStatus));
            OnPropertyChanged(nameof(OverallSystemStatus));
            OnPropertyChanged(nameof(OverallSystemStatusMessage));
        });
    }

    [RelayCommand]
    private void ToggleEnabled()
    {
        if (Enabled)
        {
            Stop();
        }
        else
        {
            Start();
        }
    }

    private void Start()
    {
        lock(_buffer)
        {
            _enabled = false;
            _buffer.Reset();
            _current.Reset();
            NotifyResultsChanged();
            _enabled = true;
            _performanceTimer.Start();
            OnPropertyChanged(nameof(Enabled));
        }
    }

    private void Stop()
    {
        lock (_buffer)
        {
            _performanceTimer.Stop();
            _enabled = false;
            OnPropertyChanged(nameof(Enabled));
        }
    }
} 