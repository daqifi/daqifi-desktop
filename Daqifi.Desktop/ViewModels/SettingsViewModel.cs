using System.Collections.ObjectModel;
using Daqifi.Desktop.Models;

namespace Daqifi.Desktop.ViewModels;

public class SettingsViewModel
{
    #region Private Data
    private readonly DaqifiSettings _settings = DaqifiSettings.Instance;
    #endregion

    #region Properties
    public bool CanReportErrors
    {
        get => _settings.CanReportErrors;
        set => _settings.CanReportErrors = value;
    }
        
    public ObservableCollection<string> CsvDelimiterOptions
    {
        get => _settings.CsvDelimiterOptions;
    }
        
    public string CsvDelimiter
    {
        get => _settings.CsvDelimiter;
        set => _settings.CsvDelimiter = value;
    }
    #endregion
}