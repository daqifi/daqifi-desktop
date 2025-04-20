using CommunityToolkit.Mvvm.ComponentModel;
using Daqifi.Desktop.Common.Loggers;

namespace Daqifi.Desktop.ViewModels;

public class ErrorDialogViewModel : ObservableObject
{
    #region Private Variables
    private string _errorMessage;
    #endregion

    #region Properties
    public AppLogger AppLogger = AppLogger.Instance;

    public string ErrorMessage
    {
        get => _errorMessage;
        set
        {
            _errorMessage=value;
            OnPropertyChanged();
        }
    }

    #endregion

    #region Constructor
    public ErrorDialogViewModel(string errorMessage)
    {
        ErrorMessage = errorMessage;
    }
    #endregion
}