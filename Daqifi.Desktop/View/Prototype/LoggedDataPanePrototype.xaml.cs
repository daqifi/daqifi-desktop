using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Daqifi.Desktop.View.Prototype;

/// <summary>
/// Interaction logic for LoggedDataPanePrototype.xaml
/// </summary>
public partial class LoggedDataPanePrototype
{
    private readonly AppLogger _logger = AppLogger.Instance;
    private readonly IDbContextFactory<LoggingContext> _loggingContext;
    private CancellationTokenSource? _renameSessionCts;

    public LoggedDataPanePrototype()
    {
        InitializeComponent();

        // Disable UI virtualization only under the UI-test harness. Virtualization leaves
        // off-screen rows out of the UIA tree, which the harness needs realized to reach a row's
        // per-session buttons by position. Production keeps virtualization on — the session list
        // grows unbounded over a device's lifetime — and screen readers realize rows on navigation,
        // so the per-row accessible name (AutomationProperties.Name) still works there.
        if (Daqifi.Desktop.Common.AppDataPaths.IsTestMode)
        {
            System.Windows.Controls.VirtualizingStackPanel.SetIsVirtualizing(SessionList, false);
        }

        _loggingContext = App.ServiceProvider.GetRequiredService<IDbContextFactory<LoggingContext>>();
        Unloaded += (_, _) =>
        {
            _renameSessionCts?.Cancel();
            _renameSessionCts?.Dispose();
            _renameSessionCts = null;
        };
    }

    /// <summary>
    /// Moves keyboard focus to the Cancel button when the confirm overlay becomes visible,
    /// so Enter/Space activates the safe option by default. The TabNavigation="Cycle" on
    /// the overlay grid then traps subsequent Tab navigation inside the dialog.
    /// </summary>
    private void OnConfirmOverlayIsVisibleChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            // Defer until the visual tree has finished switching states; calling Focus
            // directly inside the change handler can race the visibility transition.
            Dispatcher.BeginInvoke(new Action(() => ConfirmCancelButton.Focus()),
                System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private async void OnSessionNameChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if ((DataContext as DaqifiViewModel)?.SelectedLoggingSession is not LoggingSession session ||
            sender is not System.Windows.Controls.TextBox textBox)
        {
            return;
        }

        // Treat blank input as "reset to default" so the DB and the Name getter
        // (which renders whitespace as "Session {ID}") stay consistent on reload.
        // An empty string is the reset sentinel: LoggingSession.Name renders null-or-whitespace as
        // "Session {ID}", so storing "" behaves exactly like storing NULL did.
        var newName = textBox.Text?.Trim() ?? string.Empty;
        session.Name = newName;

        _renameSessionCts?.Cancel();
        _renameSessionCts?.Dispose();

        var renameCts = new CancellationTokenSource();
        _renameSessionCts = renameCts;

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), renameCts.Token);

            await using var context = await _loggingContext.CreateDbContextAsync(renameCts.Token);
            var sessionToUpdate = await context.Sessions.FindAsync([session.ID], renameCts.Token);
            if (sessionToUpdate != null)
            {
                sessionToUpdate.Name = newName;
                await context.SaveChangesAsync(renameCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Failed to update logging session name for session {session.ID}.");
        }
        finally
        {
            if (ReferenceEquals(_renameSessionCts, renameCts))
            {
                _renameSessionCts = null;
            }

            renameCts.Dispose();
        }
    }
}
