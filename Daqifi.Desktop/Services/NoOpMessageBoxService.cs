using System.Windows;

namespace Daqifi.Desktop.Services;

/// <summary>
/// An <see cref="IMessageBoxService"/> implementation that never displays a modal dialog.
/// Used in unattended/test launch mode (<see cref="App.IsTestMode"/>) so that UI automation
/// is never blocked by a message box. Always returns <see cref="MessageBoxResult.OK"/>.
/// </summary>
public class NoOpMessageBoxService : IMessageBoxService
{
    /// <summary>
    /// Suppresses the message box and returns <see cref="MessageBoxResult.OK"/> without displaying anything.
    /// </summary>
    public MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
    {
        return MessageBoxResult.OK;
    }
}
