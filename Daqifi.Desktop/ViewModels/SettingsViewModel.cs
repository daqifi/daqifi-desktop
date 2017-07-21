using Daqifi.Desktop.Models;

namespace Daqifi.Desktop.ViewModels
{
    public class SettingsViewModel
    {
        #region Private Data
        private DaqifiSettings _settings = DaqifiSettings.Instance;
        #endregion

        #region Properties
        public bool CanReportErrors
        {
            get { return _settings.CanReportErrors; }
            set { _settings.CanReportErrors = value; }
        }
        #endregion
    }
}
