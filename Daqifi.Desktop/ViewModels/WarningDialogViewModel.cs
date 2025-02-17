using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Daqifi.Desktop.Commands;

namespace Daqifi.Desktop.ViewModels
{
    public class WarningDialogViewModel : ObservableObject
    {
        private string _message;
        public string Message
        {
            get => _message;
            set => SetProperty(ref _message, value);
        }

        public ICommand OkCommand { get; }

        public WarningDialogViewModel(string message)
        {
            Message = message;
            OkCommand = new DelegateCommand(Close);
        }

        private void Close(object parameter)
        {
            var window = parameter as System.Windows.Window;
            window?.Close();
        }
    }
} 