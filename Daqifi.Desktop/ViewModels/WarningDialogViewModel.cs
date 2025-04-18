using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Daqifi.Desktop.Commands;

namespace Daqifi.Desktop.ViewModels;

public partial class WarningDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _message;

    public ICommand OkCommand { get; }

    public WarningDialogViewModel(string message)
    {
        _message = message;
        OkCommand = new DelegateCommand(Close);
    }

    private void Close(object parameter)
    {
        var window = parameter as System.Windows.Window;
        window?.Close();
    }
}