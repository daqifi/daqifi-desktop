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
        _loggingContext = App.ServiceProvider.GetRequiredService<IDbContextFactory<LoggingContext>>();
        Unloaded += (_, _) =>
        {
            _renameSessionCts?.Cancel();
            _renameSessionCts?.Dispose();
            _renameSessionCts = null;
        };
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
        var trimmed = textBox.Text?.Trim();
        var newName = string.IsNullOrEmpty(trimmed) ? null : trimmed;
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
