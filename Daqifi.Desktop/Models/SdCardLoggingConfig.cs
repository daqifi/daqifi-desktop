using CommunityToolkit.Mvvm.ComponentModel;

namespace Daqifi.Desktop.Models;

/// <summary>
/// Configuration model for SD card logging settings
/// </summary>
public partial class SdCardLoggingConfig : ObservableObject
{
    [ObservableProperty]
    private bool _isEnabled;
    [ObservableProperty]
    private LoggingMode _loggingMode;
    [ObservableProperty]
    private string _fileNamePattern;

    public SdCardLoggingConfig()
    {
        // Set default values
        FileNamePattern = "LOG_%Y%m%d_%H%M%S.dat";  // Default filename pattern with timestamp
        LoggingMode = LoggingMode.Stream;  // Default to streaming mode
    }
}