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

            // AssemblyName.Version is null only for an assembly built without a version; the build
            // sets AssemblyVersion repo-wide, so fall back rather than crash the main window.
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            Title = version != null
                ? $"DAQiFi v{version.Major}.{version.Minor}.{version.Build}"
                : "DAQiFi";

            Closing += (sender, e) =>
            {
                if (DataContext is DaqifiViewModel viewModel)
                {
                    viewModel.Dispose();
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
