using CommunityToolkit.Mvvm.ComponentModel;

namespace Daqifi.Desktop.ViewModels;

public partial class ErrorDialogViewModel : ObservableObject
{
    #region Private Variables
    [ObservableProperty]
    private string _errorMessage;
    #endregion

    #region Constructor
    public ErrorDialogViewModel(string errorMessage)
    {
        ErrorMessage = errorMessage;
    }
    #endregion
}