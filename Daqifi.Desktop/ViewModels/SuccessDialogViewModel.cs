
using CommunityToolkit.Mvvm.ComponentModel;

namespace Daqifi.Desktop.ViewModels;

public partial class SuccessDialogViewModel : ObservableObject
{
    #region Properties
    [ObservableProperty]
    private string _successMessage;
    #endregion

    public SuccessDialogViewModel(string successmessage)
    {
        SuccessMessage = successmessage;
    }
}