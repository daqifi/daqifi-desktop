using System.Collections.ObjectModel;
using System.Windows;

namespace Daqifi.Desktop.DialogService
{
    /// <summary>
    /// Abstracts ViewModels from Views
    /// </summary>
    public interface IDialogService
    {
        /// <summary>
        /// Gets the registerd views.
        /// </summary>
        ReadOnlyCollection<FrameworkElement> Views { get; }

        /// <summary>
        /// Registers a view.
        /// </summary>
        /// <param name="view">The View to register.</param>
        void Register(FrameworkElement view);

        /// <summary>
        /// Unregisters a View.
        /// </summary>
        /// <param name="view">The View to unregister.</param>
        void Unregister(FrameworkElement view);

        /// <summary>
        /// Shows a dialog.
        /// </summary>
        /// <param name="ownerViewModel">The ViewModel that is the owner of the dialog.</param>
        /// <param name="viewModel">The ViewModel of the dialog itself.</param>
        /// <returns></returns>
        bool? ShowDialog(object ownerViewModel, object viewModel);

        /// <summary>
        /// Shows a dialog.
        /// </summary>
        /// <typeparam name="T">The type of the dialog to show.</typeparam>
        /// <param name="ownerViewModel">The ViewModel that is the owner of the dialog.</param>
        /// <param name="viewModel">The ViewModwl of the dialog itself</param>
        /// <returns></returns>
        bool? ShowDialog<T>(object ownerViewModel, object viewModel) where T : Window;

        MessageBoxResult ShowMessageBox(
            object ownerViewModel,
            string messageBoxText,
            string caption,
            MessageBoxButton button,
            MessageBoxImage icon);
    }
}
