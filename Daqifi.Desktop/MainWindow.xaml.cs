using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.ViewModels;
using System.Diagnostics;
using System.Reflection;
using OxyPlot; // Added for OxyPlot event args
using OxyPlot.Wpf; // Added for specific WPF event args if needed, though OxyPlot core should be enough for args

namespace Daqifi.Desktop;

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
    #endregion

    private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    // Bridge event handlers for Minimap PlotView
    private void OnMinimapMouseDown_FromXaml(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is DaqifiViewModel viewModel && sender is PlotView plotView)
        {
            // Convert WPF MouseButtonEventArgs to OxyMouseDownEventArgs
            // PlotView.TransformMouseDown is a helper that can do this.
            // Or, we can manually construct it if needed, but usually, the PlotView itself
            // provides these events in the correct OxyPlot format if we subscribe to its own events.
            // Since we are directly handling WPF events, we need to pass appropriate args or convert.
            // For simplicity, let's assume the ViewModel methods can adapt or we pass basic info.
            // The error messages suggest the ViewModel expects OxyPlot args.
            // The PlotView itself raises OxyPlot events.
            // Let's assume the XAML wires to PlotView's internal OxyPlot events if possible,
            // or we find a way to convert. The error was that PlotController doesn't have them.
            // The PlotView directly has these events.
            
            // The XAML should be <oxy:PlotView MouseDown="OnMinimapMouseDown_FromXaml" ... />
            // The 'sender' will be the PlotView.
            // The 'e' will be System.Windows.Input.MouseButtonEventArgs.
            // The ViewModel expects OxyMouseDownEventArgs.
            // We need to create an OxyMouseDownEventArgs from System.Windows.Input.MouseButtonEventArgs.

            var oxyArgs = plotView.CreateMouseDownEventArgs(e, null); // null for hit-test result, may need adjustment
            viewModel.OnMinimapMouseDown(sender, oxyArgs);
        }
    }

    private void OnMinimapMouseMove_FromXaml(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (DataContext is DaqifiViewModel viewModel && sender is PlotView plotView)
        {
            var oxyArgs = plotView.CreateMouseEventArgs(e, null);
            viewModel.OnMinimapMouseMove(sender, oxyArgs);
        }
    }

    private void OnMinimapMouseUp_FromXaml(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is DaqifiViewModel viewModel && sender is PlotView plotView)
        {
            var oxyArgs = plotView.CreateMouseUpEventArgs(e, null);
            viewModel.OnMinimapMouseUp(sender, oxyArgs);
        }
    }
}