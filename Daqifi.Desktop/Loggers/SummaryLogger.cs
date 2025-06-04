using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Device;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Daqifi.Desktop.Logger;

/// <summary>
/// Provaides summary data for incoming samples
/// </summary>
public partial class SummaryLogger : ObservableObject, ILogger
{

    #region "Private Data"

    /// <summary>
    /// Summary results object
    /// </summary>
    private class SummaryBuffer
    {
        public SummaryBuffer()
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

        public void Reset()
        {
            SampleCount = 0;
            FirstSampleTicks = 0;
            LastSampleTicks = 0;
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
    
    private double _elapsedTime;
    private DateTime _lastUpdate;
    private double _sampleRate;

    #endregion

    #region "Properties"
    /// <summary>
    /// The frequency sample rate
    /// </summary>
    public double SampleRate
    {
        get
        {
            var delta = new TimeSpan(_current.LastSampleTicks - _current.FirstSampleTicks);
            return delta.Ticks > 0 ? (_current.SampleCount - 1) / delta.TotalSeconds : 0.0;
        }
    }

    /// <summary>
    /// The target samples per second.
    /// </summary>
    private int _targetSampleRate;
    public int TargetSampleRate => _targetSampleRate;

    public void UpdateTargetSampleRate(int targetRate)
    {
        _targetSampleRate = targetRate;
        OnPropertyChanged(nameof(TargetSampleRate));
    }

    #endregion

    #region "Constructor"

    public SummaryLogger()
    {
        _sampleSize = 1000;
        _buffer = new SummaryBuffer();
        _current = new SummaryBuffer();
    }

    /// <summary>
    /// Creates a new instance
    /// </summary>
    /// <param name="sampleSize">The size of the sample set</param>
    public SummaryLogger(int sampleSize)
    {
        _sampleSize = sampleSize;
        _buffer = new SummaryBuffer();
        _current = new SummaryBuffer();
        _enabled = true;
        Start();
    }

    #endregion

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
            if (_buffer.SampleCount == 0)
            {
                _buffer.FirstSampleTicks = dataSample.AppTicks;
            }
            _buffer.LastSampleTicks = dataSample.AppTicks;

            ++_buffer.SampleCount;
            if (_buffer.SampleCount == _sampleSize)
            {
                lock (_current)
                {
                    SwapBuffer();
                }
            }
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
        OnPropertyChanged(nameof(SampleRate));
        OnPropertyChanged(nameof(TargetSampleRate));
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
            _enabled = true;
            OnPropertyChanged("Enabled");
        }
    }

    private void Stop()
    {
        lock (_buffer)
        {
            _enabled = false;
            OnPropertyChanged("Enabled");
        }
    }

    [RelayCommand]
    private void Reset()
    {
        lock(_buffer)
        {
            lock (_current)
            {
                Enabled = false;
                _buffer.Reset();
                _current.Reset();
                NotifyResultsChanged();
            }
        }
    }
}