namespace Daqifi.Desktop.Models
{
    /// <summary>
    /// Configuration model for SD card logging settings
    /// </summary>
    public class SdCardLoggingConfig : ObservableObject
    {
        private bool _isEnabled;
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
                    NotifyPropertyChanged(nameof(IsEnabled));
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
                    NotifyPropertyChanged(nameof(LoggingMode));
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
                    NotifyPropertyChanged(nameof(FileNamePattern));
                }
            }
        }

        public SdCardLoggingConfig()
        {
            // Set default values
            FileNamePattern = "LOG_%Y%m%d_%H%M%S.dat";  // Default filename pattern with timestamp
            LoggingMode = LoggingMode.Stream;  // Default to streaming mode
        }
    }
} 