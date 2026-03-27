using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace Daqifi.Desktop.Services;

public interface IMessageBoxService
{
    MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon);
}

public class WpfMessageBoxService : IMessageBoxService
{
    public MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
    {
        return MessageBox.Show(messageBoxText, caption, button, icon);
    }
}