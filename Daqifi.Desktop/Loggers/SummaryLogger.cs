using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Common.Loggers;
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

        /// <summary>
        /// Summary results object
        /// </summary>
        private class SummaryBuffer
        {
            public SummaryBuffer()
            {
                Devices = new HashSet<string>();
                Channels = new HashSet<string>();
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
            /// The devices seen
            /// </summary>
            public HashSet<string> Devices { get; set; }

            /// <summary>
            /// The channels seen
            /// </summary>
            public HashSet<string> Channels { get; set; }

            /// <summary>
            /// The maximum value of the incoming samples
            /// </summary>
            public double MaxValue { get; set; }

            /// <summary>
            /// The minimum value of the incoming samples
            /// </summary>
            public double MinValue { get; set; }

            /// <summary>
            /// The average value of the incoming samples
            /// </summary>
            public double AverageValue { get; set; }

            public void Reset()
            {
                SampleCount = 0;
                FirstSampleTicks = 0;
                LastSampleTicks = 0;
                AverageDeltaTicks = 0;
                MaxDeltaTicks = 0;
                MinDeltaTicks = 0;
                Devices.Clear();
                Channels.Clear();
                MaxValue = 0.0;
                MinValue = 0.0;
                AverageValue = 0.0;
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
        public TimeSpan ElapsedTime
        {
            get
            {
                return TimeSpan.FromTicks(_current.LastSampleTicks - _current.FirstSampleTicks);
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
                return _current.AverageDeltaTicks >0 ? 1.0 / TimeSpan.FromTicks((long)_current.AverageDeltaTicks).TotalSeconds : 0.0;
            }
        }

        /// <summary>
        /// The maximum time between samples
        /// </summary>
        public TimeSpan MaxDelta
        {
            get
            {
                return new TimeSpan(_current.MaxDeltaTicks);
            }
        }

        /// <summary>
        /// The minimum time between samples
        /// </summary>
        public TimeSpan MinDelta
        {
            get
            {
                return new TimeSpan(_current.MinDeltaTicks);
            }
        }

        /// <summary>
        /// The devices sthat reported data
        /// </summary>
        public string Devices
        {
            get
            {
                return _current.Devices.Any() ? _current.Devices.Aggregate((a, b) => String.Format("{0}, {1}", a, b)) : String.Empty;
            }
        }

        /// <summary>
        /// The channels seen
        /// </summary>
        public string Channels
        {
            get
            {
                return _current.Channels.Any() ? _current.Channels.Aggregate((a, b) => String.Format("{0}, {1}", a, b)) : String.Empty;
            }
        }

        /// <summary>
        /// The maximum value of the incoming samples
        /// </summary>
        public double MaxValue
        {
            get
            {
                return _current.MaxValue;
            }
        }

        /// <summary>
        /// The minimum value of the incoming samples
        /// </summary>
        public double MinValue
        {
            get
            {
                return _current.MinValue;
            }
        }

        /// <summary>
        /// The average value of the incoming samples
        /// </summary>
        public double AverageValue
        {
            get
            {
                return _current.AverageValue;
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
                if (_buffer.SampleCount == 0)
                {
                    _buffer.FirstSampleTicks = dataSample.TimestampTicks;
                    _buffer.MinValue = dataSample.Value;
                    _buffer.MaxValue = dataSample.Value;
                }
                else
                {
                    _buffer.MinValue = Math.Min(dataSample.Value, _buffer.MinValue);
                    _buffer.MaxValue = Math.Max(dataSample.Value, _buffer.MaxValue);
                }
                _buffer.AverageValue += dataSample.Value / _sampleSize;

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
                        _buffer.MaxDeltaTicks = Math.Max(_buffer.MinDeltaTicks, elapsed);
                    }
                    _buffer.AverageDeltaTicks += elapsed / (double)(_sampleSize - 1);
                }
                _buffer.LastSampleTicks = dataSample.TimestampTicks;

                _buffer.Devices.Add(dataSample.DeviceName);
                _buffer.Channels.Add(dataSample.ChannelName);

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
            NotifyPropertyChanged("Devices");
            NotifyPropertyChanged("Channels");
            NotifyPropertyChanged("MaxValue");
            NotifyPropertyChanged("MinValue");
            NotifyPropertyChanged("AverageValue");
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
