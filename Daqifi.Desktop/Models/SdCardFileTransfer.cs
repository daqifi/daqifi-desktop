using System;
using System.ComponentModel;

namespace Daqifi.Desktop.Models
{
    /// <summary>
    /// Model for tracking SD card file transfer operations
    /// </summary>
    public class SdCardFileTransfer : INotifyPropertyChanged
    {
        private SdCardFile _file;
        private int _progress;
        private string _status;
        private bool _isCompleted;
        private bool _hasError;
        private string _errorMessage;
        private DateTime _startTime;
        private DateTime? _endTime;

        /// <summary>
        /// Gets or sets the file being transferred
        /// </summary>
        public SdCardFile File
        {
            get => _file;
            set
            {
                if (_file != value)
                {
                    _file = value;
                    OnPropertyChanged(nameof(File));
                }
            }
        }

        /// <summary>
        /// Gets or sets the transfer progress (0-100)
        /// </summary>
        public int Progress
        {
            get => _progress;
            set
            {
                if (_progress != value)
                {
                    _progress = value;
                    OnPropertyChanged(nameof(Progress));
                }
            }
        }

        /// <summary>
        /// Gets or sets the current transfer status
        /// </summary>
        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                }
            }
        }

        /// <summary>
        /// Gets or sets whether the transfer is completed
        /// </summary>
        public bool IsCompleted
        {
            get => _isCompleted;
            set
            {
                if (_isCompleted != value)
                {
                    _isCompleted = value;
                    if (value)
                    {
                        EndTime = DateTime.Now;
                    }
                    OnPropertyChanged(nameof(IsCompleted));
                }
            }
        }

        /// <summary>
        /// Gets or sets whether an error occurred during transfer
        /// </summary>
        public bool HasError
        {
            get => _hasError;
            set
            {
                if (_hasError != value)
                {
                    _hasError = value;
                    OnPropertyChanged(nameof(HasError));
                }
            }
        }

        /// <summary>
        /// Gets or sets the error message if an error occurred
        /// </summary>
        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                if (_errorMessage != value)
                {
                    _errorMessage = value;
                    OnPropertyChanged(nameof(ErrorMessage));
                }
            }
        }

        /// <summary>
        /// Gets or sets the transfer start time
        /// </summary>
        public DateTime StartTime
        {
            get => _startTime;
            set
            {
                if (_startTime != value)
                {
                    _startTime = value;
                    OnPropertyChanged(nameof(StartTime));
                }
            }
        }

        /// <summary>
        /// Gets or sets the transfer end time
        /// </summary>
        public DateTime? EndTime
        {
            get => _endTime;
            set
            {
                if (_endTime != value)
                {
                    _endTime = value;
                    OnPropertyChanged(nameof(EndTime));
                    OnPropertyChanged(nameof(Duration));
                }
            }
        }

        /// <summary>
        /// Gets the duration of the transfer
        /// </summary>
        public TimeSpan? Duration => EndTime.HasValue ? EndTime.Value - StartTime : null;

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public SdCardFileTransfer(SdCardFile file)
        {
            File = file;
            StartTime = DateTime.Now;
            Status = "Initializing";
            Progress = 0;
        }
    }
} 