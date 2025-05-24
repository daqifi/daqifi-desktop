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
        var plotView = sender as OxyPlot.Wpf.PlotView;
        var viewModel = DataContext as DaqifiViewModel;

        if (plotView == null || viewModel == null || plotView.ActualModel == null)
        {
            return;
        }

        var wpfPoint = e.GetPosition(plotView);
        var screenPoint = new ScreenPoint(wpfPoint.X, wpfPoint.Y);

        var oxyArgs = new OxyMouseDownEventArgs
        {
            ChangedButton = WpfOxyInputExtensions.ToOxyMouseButton(e.ChangedButton),
            ClickCount = e.ClickCount,
            ModifierKeys = WpfOxyInputExtensions.ToOxyModifierKeys(System.Windows.Input.Keyboard.Modifiers),
            Position = screenPoint
        };
        oxyArgs.HitTestResult = plotView.ActualModel.HitTest(new HitTestArguments(screenPoint, plotView.SelectionTolerance));
        
        viewModel.OnMinimapMouseDown(plotView, oxyArgs);
    }

    private void OnMinimapMouseMove_FromXaml(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var plotView = sender as OxyPlot.Wpf.PlotView;
        var viewModel = DataContext as DaqifiViewModel;

        if (plotView == null || viewModel == null) // ActualModel not strictly needed for mouse move if not hit testing
        {
            return;
        }

        var wpfPoint = e.GetPosition(plotView);
        var screenPoint = new ScreenPoint(wpfPoint.X, wpfPoint.Y);

        var oxyArgs = new OxyMouseEventArgs
        {
            ModifierKeys = WpfOxyInputExtensions.ToOxyModifierKeys(System.Windows.Input.Keyboard.Modifiers),
            Position = screenPoint
        };
        
        viewModel.OnMinimapMouseMove(plotView, oxyArgs);
    }

    private void OnMinimapMouseUp_FromXaml(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var plotView = sender as OxyPlot.Wpf.PlotView;
        var viewModel = DataContext as DaqifiViewModel;

        if (plotView == null || viewModel == null) // ActualModel not strictly needed for mouse up if not hit testing
        {
            return;
        }
        
        var wpfPoint = e.GetPosition(plotView);
        var screenPoint = new ScreenPoint(wpfPoint.X, wpfPoint.Y);

        // DaqifiViewModel.OnMinimapMouseUp expects OxyMouseEventArgs
        var oxyArgs = new OxyMouseEventArgs 
        {
            ModifierKeys = WpfOxyInputExtensions.ToOxyModifierKeys(System.Windows.Input.Keyboard.Modifiers),
            Position = screenPoint
        };
        
        viewModel.OnMinimapMouseUp(plotView, oxyArgs);
    }
}

// Helper class for converting WPF input to OxyPlot input types
public static class WpfOxyInputExtensions
{
    public static OxyMouseButton ToOxyMouseButton(this System.Windows.Input.MouseButton button)
    {
        switch (button)
        {
            case System.Windows.Input.MouseButton.Left: return OxyMouseButton.Left;
            case System.Windows.Input.MouseButton.Middle: return OxyMouseButton.Middle;
            case System.Windows.Input.MouseButton.Right: return OxyMouseButton.Right;
            case System.Windows.Input.MouseButton.XButton1: return OxyMouseButton.XButton1;
            case System.Windows.Input.MouseButton.XButton2: return OxyMouseButton.XButton2;
        }
        return OxyMouseButton.None;
    }

    public static OxyModifierKeys ToOxyModifierKeys(this System.Windows.Input.ModifierKeys keys)
    {
        var oxyKeys = OxyModifierKeys.None;
        if ((keys & System.Windows.Input.ModifierKeys.Alt) == System.Windows.Input.ModifierKeys.Alt) oxyKeys |= OxyModifierKeys.Alt;
        if ((keys & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control) oxyKeys |= OxyModifierKeys.Control;
        if ((keys & System.Windows.Input.ModifierKeys.Shift) == System.Windows.Input.ModifierKeys.Shift) oxyKeys |= OxyModifierKeys.Shift;
        // Note: System.Windows.Input.ModifierKeys.Windows is not mapped in OxyPlot's OxyModifierKeys
        return oxyKeys;
    }
}