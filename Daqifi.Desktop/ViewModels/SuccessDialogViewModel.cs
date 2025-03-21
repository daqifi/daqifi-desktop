
namespace Daqifi.Desktop.ViewModels;

public class SuccessDialogViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    #region Private Variables
    private string _successMessage;
    #endregion

    #region Properties
        
    public string SuccessMessage
    {
        get => _successMessage;
        set
        {
            _successMessage = value;
            OnPropertyChanged();
        }
    }
    #endregion

    public SuccessDialogViewModel(string successmessage)
    {
        SuccessMessage = successmessage;
    }
}