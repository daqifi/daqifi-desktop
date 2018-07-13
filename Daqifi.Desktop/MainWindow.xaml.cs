using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Common.Loggers;
using System;

namespace Daqifi.Desktop
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        public AppLogger AppLogger = AppLogger.Instance;

        #region Constructor / Initialization
        public MainWindow()
        {
            try
            {
                InitializeComponent();

                Closing += (sender, e) =>
                {
                    if (HostCommands.ShutdownCommand.CanExecute(e))
                        HostCommands.ShutdownCommand.Execute(e);
                };
            }
            catch(Exception ex)
            {
                AppLogger.Error(ex, "Error in MainWindow");
            }  
        }
        #endregion
    }
}
