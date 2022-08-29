using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Daqifi.Desktop.Logger
{
    /// <summary>
    /// Provaides summary data for incoming samples
    /// </summary>
    public class SummaryLogger : ObservableObject, ILogger
    {

        #region "Private Data"

        public class ChannelSummary
        {
            private ChannelBuffer _current;

            private string _name;

            internal ChannelSummary(string name, ChannelBuffer current)
            {
                _name = name;
                _current = current;
            }

            /// <summary>
            /// The frequency sample rate
            /// </summary>
            public string Name
            {
                get
                {
                    return _name;
                }
            }

            /// <summary>
            /// The number of samples seen
            /// </summary>
            public int SampleCount
            {
                get
                {
                    return _current.SampleCount;
                }
            }

            /// <summary>
            /// The time of the last sample
            /// </summary>
            public DateTime LastUpdate
            {
                get
                {
                    return new DateTime(_current.LastSampleTicks);
                }
            }

            /// <summary>
            /// The frequency sample rate
            /// </summary>
            public double SampleRate
            {
                get
                {
                    var delta = new TimeSpan(_current.LastSampleTicks - _current.FirstSampleTicks);
                    return delta.Ticks > 0 ? _current.SampleCount / delta.TotalSeconds : 0.0;
                }
            }

            /// <summary>
            /// The maximum time between samples
            /// </summary>
            public double MaxDelta
            {
                get
                {
                    return _current.MaxDeltaTicks;
                }
            }

            /// <summary>
            /// The average time between samples
            /// </summary>
            public double AverageDelta
            {
                get
                {
                    return _current.AverageDeltaTicks;
                }
            }

            /// <summary>
            /// The minimum time between samples
            /// </summary>
            public double MinDelta
            {
                get
                {
                    return _current.MinDeltaTicks;
                }
            }

            /// <summary>
            /// The maximum time between samples
            /// </summary>
            public double MaxValue
            {
                get
                {
                    return _current.MaxValue;
                }
            }

            /// <summary>
            /// The minimum time between samples
            /// </summary>
            public double MinValue
            {
                get
                {
                    return _current.MinValue;
                }
            }

            /// <summary>
            /// The minimum time between samples
            /// </summary>
            public double AverageValue
            {
                get
                {
                    return _current.AverageValue;
                }
            }
        }

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
            /// The average time between samples
            /// </summary>
            public double AverageDeltaTicks { get; set; }

            /// <summary>
            /// The maximum time between samples
            /// </summary>
            public long MaxDeltaTicks { get; set; }

            /// <summary>
            /// The minimum time between samples
            /// </summary>
            public long MinDeltaTicks { get; set; }

            /// <summary>
            /// The average value of these samples
            /// </summary>
            public double AverageValue { get; set; }

            /// <summary>
            /// The minimum value of these samples
            /// </summary>
            public double MaxValue { get; set; }

            /// <summary>
            /// The maximum value of these samples
            /// </summary>
            public double MinValue { get; set; }

            public void Reset()
            {
                SampleCount = 0;
                FirstSampleTicks = 0;
                LastSampleTicks = 0;
                AverageDeltaTicks = 0;
                MaxDeltaTicks = 0;
                MinDeltaTicks = 0;
                AverageValue = 0;
                MaxValue = 0;
                MinValue = 0;
            }
        }


        /// <summary>
        /// Summary results object
        /// </summary>
        private class SummaryBuffer
        {
            public SummaryBuffer()
            {
                Channels = new Dictionary<string, ChannelBuffer>(64);
                StatusList = new HashSet<int>();
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
            /// The average time between samples
            /// </summary>
            public double AverageDeltaTicks { get; set; }

            /// <summary>
            /// The maximum time between samples
            /// </summary>
            public long MaxDeltaTicks { get; set; }

            /// <summary>
            /// The minimum time between samples
            /// </summary>
            public long MinDeltaTicks { get; set; }

            /// <summary>
            /// The maximum time between when the board reported a message and when the app recieved it
            /// </summary>
            public long MaxLatencyTicks { get; set; }

            /// <summary>
            /// The inimum time between when the board reported a message and when the app recieved it
            /// </summary>
            public long MinLatencyTicks { get; set; }

            /// <summary>
            /// The minimum time between when the board reported a message and when the app recieved it
            /// </summary>
            public double AverageLatencyTicks { get; set; }

            /// <summary>
            /// The statuses seen
            /// </summary>
            public HashSet<int> StatusList { get; set; }

            /// <summary>
            /// Indicates whether the device timestamp rolled over in this sample set
            /// </summary>
            public bool HasRollover { get; set; }

            /// <summary>
            /// The channels seen
            /// </summary>
            public Dictionary<string, ChannelBuffer> Channels { get; set; }

            public void Reset()
            {
                SampleCount = 0;
                FirstSampleTicks = 0;
                LastSampleTicks = 0;
                AverageDeltaTicks = 0;
                MaxDeltaTicks = 0;
                MinDeltaTicks = 0;
                MaxLatencyTicks = 0;
                MinLatencyTicks = 0;
                AverageLatencyTicks = 0;
                StatusList.Clear();
                foreach (var pair in Channels)
                {
                    pair.Value.Reset();
                }
            }
        }

        private int _sampleSize;

        private bool _enabled;

        /// <summary>
        /// The in-progress sample set
        /// </summary>
        private SummaryBuffer _buffer;

        /// <summary>
        /// The last completed sample set
        /// </summary>
        private SummaryBuffer _current;

        /// <summary>
        /// Application logger
        /// </summary>
        public AppLogger AppLogger = AppLogger.Instance;
        #endregion

        #region "Properties"
        /// <summary>
        /// Indicates whether the logger is accepting data
        /// </summary>
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; NotifyPropertyChanged("Enabled"); }
        }

        /// <summary>
        /// The number of samples to evaluate
        /// </summary>
        public int SampleSize
        {
            get => _sampleSize;
            set { _sampleSize = value; NotifyPropertyChanged("SampleSize"); }
        }

        /// <summary>
        /// The total elapsed time
        /// </summary>
        public double ElapsedTime
        {
            get
            {
                return TimeSpan.FromTicks(_current.LastSampleTicks - _current.FirstSampleTicks).TotalMilliseconds;
            }
        }

        /// <summary>
        /// The time of the last sample
        /// </summary>
        public DateTime LastUpdate
        {
            get
            {
                return new DateTime(_current.LastSampleTicks);
            }
        }

        /// <summary>
        /// The frequency sample rate
        /// </summary>
        public double SampleRate
        {
            get
            {
                var delta = new TimeSpan(_current.LastSampleTicks - _current.FirstSampleTicks);
                return delta.Ticks > 0 ? _current.SampleCount / delta.TotalSeconds : 0.0;
            }
        }

        /// <summary>
        /// The maximum time between samples
        /// </summary>
        public double MaxDelta
        {
            get
            {
                return _current.MaxDeltaTicks;
            }
        }

        /// <summary>
        /// The minimum time between samples
        /// </summary>
        public double MinDelta
        {
            get
            {
                return _current.MinDeltaTicks;
            }
        }

        /// <summary>
        /// The average time between samples
        /// </summary>
        public double AverageDelta
        {
            get
            {
                return _current.AverageDeltaTicks;
            }
        }

        /// <summary>
        /// The maximum time between samples
        /// </summary>
        public double MaxLatency
        {
            get
            {
                return _current.MaxLatencyTicks;
            }
        }

        /// <summary>
        /// The minimum time between samples
        /// </summary>
        public double MinLatency
        {
            get
            {
                return _current.MinLatencyTicks;
            }
        }

        /// <summary>
        /// The minimum time between samples
        /// </summary>
        public double AverageLatency
        {
            get
            {
                return _current.AverageLatencyTicks;
            }
        }

        /// <summary>
        /// Display info for the channels
        /// </summary>
        public IEnumerable<ChannelSummary> Channels
        {
            get
            {
                var result = new List<ChannelSummary>();
                foreach (var item in _current.Channels)
                {
                    if (item.Value.SampleCount > 0)
                    {
                        result.Add(new ChannelSummary(item.Key, item.Value));
                    }
                }

                return result;
            }
        }

        public string StatusList
        {
            get
            {
                var builder = new StringBuilder();
                foreach (var status in _current.StatusList)
                {
                    builder.AppendFormat("%d, ", status);
                }

                if (_current.HasRollover)
                {
                    builder.Append("Rollover: Y");
                }
                else
                {
                    builder.Append("Rollover: N");
                }

                return builder.ToString();
            }
        }

        #endregion

        #region "Command Properties"

        /// <summary>
        /// Resets the summary logger
        /// </summary>
        public ICommand ResetCommand { get; }

        /// <summary>
        /// Starts or stops the summary logger
        /// </summary>
        public ICommand ToggleEnabledCommand { get; }

        #endregion

        #region "Constructor"

        public SummaryLogger()
        {
            _buffer = new SummaryBuffer();
            _current = new SummaryBuffer();

            _sampleSize = 1000;

            ResetCommand = new DelegateCommand(Reset);
            ToggleEnabledCommand = new DelegateCommand(ToggleEnabled);
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

                var buffer = _buffer.Channels[dataSample.ChannelName];

                if (buffer.SampleCount == 0)
                {
                    buffer.FirstSampleTicks = dataSample.TimestampTicks;
                    buffer.MinValue = buffer.MinValue;
                    buffer.MaxValue = buffer.MaxValue;
                }
                else
                {
                    buffer.MinValue = Math.Min(dataSample.Value, buffer.MinValue);
                    buffer.MaxValue = Math.Max(dataSample.Value, buffer.MaxValue);
                }

                buffer.AverageValue += dataSample.Value / _sampleSize;

                if (buffer.SampleCount > 0)
                {
                    var elapsed = dataSample.TimestampTicks - buffer.LastSampleTicks;
                    if (buffer.SampleCount == 1)
                    {
                        buffer.MinDeltaTicks = elapsed;
                        buffer.MaxDeltaTicks = elapsed;
                    }
                    else
                    {
                        buffer.MinDeltaTicks = Math.Min(buffer.MinDeltaTicks, elapsed);
                        buffer.MaxDeltaTicks = Math.Max(buffer.MaxDeltaTicks, elapsed);
                    }
                    buffer.AverageDeltaTicks += elapsed / (double)(_sampleSize - 1);
                }
                buffer.LastSampleTicks = dataSample.TimestampTicks;

                ++buffer.SampleCount;
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
                _buffer.StatusList.Add(dataSample.DeviceStatus);
                _buffer.HasRollover = dataSample.Rollover;

                var latency = dataSample.AppTicks - dataSample.TimestampTicks;
                if (_buffer.SampleCount == 0)
                {
                    _buffer.FirstSampleTicks = dataSample.TimestampTicks;
                    _buffer.MinLatencyTicks = latency;
                    _buffer.MaxLatencyTicks = latency;
                }
                else
                {
                    _buffer.MinLatencyTicks = Math.Min(latency, _buffer.MinLatencyTicks);
                    _buffer.MaxLatencyTicks = Math.Max(latency, _buffer.MaxLatencyTicks);
                }
                _buffer.AverageLatencyTicks += latency / (double)(_sampleSize - 1);

                if (_buffer.SampleCount > 0)
                {
                    var elapsed = dataSample.TimestampTicks - _buffer.LastSampleTicks;
                    if (_buffer.SampleCount == 1)
                    {
                        _buffer.MinDeltaTicks = elapsed;
                        _buffer.MaxDeltaTicks = elapsed;
                    }
                    else
                    {
                        _buffer.MinDeltaTicks = Math.Min(_buffer.MinDeltaTicks, elapsed);
                        _buffer.MaxDeltaTicks = Math.Max(_buffer.MaxDeltaTicks, elapsed);
                    }

                    _buffer.AverageDeltaTicks += elapsed / (double)(_sampleSize - 1);
                }
                _buffer.LastSampleTicks = dataSample.TimestampTicks;

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
            NotifyPropertyChanged("ElapsedTime");
            NotifyPropertyChanged("LastUpdate");
            NotifyPropertyChanged("SampleRate");
            NotifyPropertyChanged("MaxDelta");
            NotifyPropertyChanged("MinDelta");
            NotifyPropertyChanged("AverageDelta");
            NotifyPropertyChanged("MaxLatency");
            NotifyPropertyChanged("MinLatency");
            NotifyPropertyChanged("AverageLatency");
            NotifyPropertyChanged("StatusList");
            NotifyPropertyChanged("Channels");
        }

        private void ToggleEnabled(object o)
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
                NotifyPropertyChanged("Enabled");
            }
        }

        private void Stop()
        {
            lock (_buffer)
            {
                _enabled = false;
                NotifyPropertyChanged("Enabled");
            }
        }

        private void Reset(object o)
        {
            lock(_buffer)
            {
                lock (_current)
                {
                    Enabled = false;
                    SampleSize = 1000;
                    _buffer.Reset();
                    _current.Reset();
                    NotifyResultsChanged();
                }
            }
        }
    }
}
