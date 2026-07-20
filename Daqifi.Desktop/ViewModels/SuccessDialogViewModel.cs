
using CommunityToolkit.Mvvm.ComponentModel;

namespace Daqifi.Desktop.ViewModels;

/// <summary>
/// View model backing the success dialog; exposes the success message shown to the user.
/// </summary>
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