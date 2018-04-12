using Daqifi.Desktop.Loggers;
using GalaSoft.MvvmLight;

namespace Daqifi.Desktop.ViewModels
{
    public class ErrorDialogViewModel : ViewModelBase
    {
        #region Private Variables
        private string _errorMessage;
        #endregion

        #region Properties
        public AppLogger AppLogger = AppLogger.Instance;

        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                _errorMessage=value;
                RaisePropertyChanged();
            }
        }

        #endregion

        #region Constructor
        public ErrorDialogViewModel(string errorMessage)
        {
            ErrorMessage = errorMessage;
        }
        #endregion
    }
}
