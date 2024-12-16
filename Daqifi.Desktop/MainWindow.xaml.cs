using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.Reflection;

namespace Daqifi.Desktop
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private readonly AppLogger _appLogger = AppLogger.Instance;

        #region Constructor / Initialization
        public MainWindow()
        {
            try
            {
                InitializeComponent();

                var version = Assembly.GetExecutingAssembly().GetName().Version;
                this.Title = $"DAQiFi v{version.Major}.{version.Minor}.{version.Build}";

                Closing += (sender, e) =>
                {
                    if (HostCommands.ShutdownCommand.CanExecute(e))
                    {
                        HostCommands.ShutdownCommand.Execute(e);
                    }
                };
            }
            catch (Exception ex)
            {  
                _appLogger.Error(ex, "Error in MainWindow");
            }

           this.DataContext =new  DaqifiViewModel();
        }
        #endregion

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;

        }

    }

}
