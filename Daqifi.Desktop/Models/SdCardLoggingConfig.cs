using System.ComponentModel;

namespace Daqifi.Desktop.Models
{
    /// <summary>
    /// Configuration model for SD card logging settings
    /// </summary>
    public class SdCardLoggingConfig : INotifyPropertyChanged
    {
        private bool _isEnabled;
        private string _dataFormat;
        private LoggingMode _loggingMode;
        private string _fileNamePattern;

        /// <summary>
        /// Gets or sets whether SD card is enabled
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged(nameof(IsEnabled));
                }
            }
        }

        /// <summary>
        /// Gets or sets the data format for SD card logging (JSON/Protobuf)
        /// </summary>
        public string DataFormat
        {
            get => _dataFormat;
            set
            {
                if (_dataFormat != value)
                {
                    _dataFormat = value;
                    OnPropertyChanged(nameof(DataFormat));
                }
            }
        }

        /// <summary>
        /// Gets or sets the current logging mode
        /// </summary>
        public LoggingMode LoggingMode
        {
            get => _loggingMode;
            set
            {
                if (_loggingMode != value)
                {
                    _loggingMode = value;
                    OnPropertyChanged(nameof(LoggingMode));
                }
            }
        }

        /// <summary>
        /// Gets or sets the filename pattern for logged files
        /// </summary>
        public string FileNamePattern
        {
            get => _fileNamePattern;
            set
            {
                if (_fileNamePattern != value)
                {
                    _fileNamePattern = value;
                    OnPropertyChanged(nameof(FileNamePattern));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public SdCardLoggingConfig()
        {
            // Set default values
            DataFormat = "Protobuf";  // Default to Protobuf format
            FileNamePattern = "LOG_%Y%m%d_%H%M%S.dat";  // Default filename pattern with timestamp
            LoggingMode = LoggingMode.Stream;  // Default to streaming mode
        }
    }
} 