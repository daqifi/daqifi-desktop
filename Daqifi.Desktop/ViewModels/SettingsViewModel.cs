using CommunityToolkit.Mvvm.ComponentModel;
using Daqifi.Desktop.Models;

namespace Daqifi.Desktop.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly DaqifiSettings _settings = DaqifiSettings.Instance;

    public string CsvDelimiter
    {
        get => _settings.CsvDelimiter;
        set
        {
            if (_settings.CsvDelimiter == value) return;
            _settings.CsvDelimiter = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCommaDelimiter));
            OnPropertyChanged(nameof(IsSemicolonDelimiter));
        }
    }

    public bool IsCommaDelimiter
    {
        get => CsvDelimiter == ",";
        set { if (value) CsvDelimiter = ","; }
    }

    public bool IsSemicolonDelimiter
    {
        get => CsvDelimiter == ";";
        set { if (value) CsvDelimiter = ";"; }
    }
}
