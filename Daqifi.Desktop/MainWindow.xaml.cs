using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.ViewModels;
using System.Reflection;

namespace Daqifi.Desktop;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow
{
    private readonly AppLogger _appLogger = AppLogger.Instance;

    public MainWindow()
    {
        try
        {
            InitializeComponent();

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            Title = $"DAQiFi v{version.Major}.{version.Minor}.{version.Build}";

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

        DataContext = new DaqifiViewModel();
    }
}
