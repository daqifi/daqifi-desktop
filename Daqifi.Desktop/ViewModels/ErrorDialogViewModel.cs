using Daqifi.Desktop.Loggers;

namespace Daqifi.Desktop.ViewModels
{
    public class ErrorDialogViewModel : ObservableObject
    {
        #region Private Variables
        private string _errorMessage;
        #endregion

        #region Properties
        public AppLogger AppLogger = AppLogger.Instance;

        public string ErrorMessage
        {
            get { return _errorMessage;}
            set
            {
                _errorMessage=value;
                NotifyPropertyChanged("ErrorMessage");
            }
        }

        #endregion

        #region Constructor
        public ErrorDialogViewModel(string errorMessage)
        {
           ErrorMessage= errorMessage;
        }
        #endregion
    }
}
