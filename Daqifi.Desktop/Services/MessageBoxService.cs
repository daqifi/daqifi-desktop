using System.Windows;

namespace Daqifi.Desktop.Services;

public interface IMessageBoxService
{
    MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon);
}

public class WpfMessageBoxService : IMessageBoxService
{
    public MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
    {
        return System.Windows.MessageBox.Show(messageBoxText, caption, button, icon);
    }
}