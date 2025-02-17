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

        /// <summary>
        /// Gets or sets whether SD card logging is enabled
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

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 