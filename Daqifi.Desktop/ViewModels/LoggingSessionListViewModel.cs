using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Logger;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Daqifi.Desktop.ViewModels;

/// <summary>
/// Owns the logged-data pane's session-list actions — displaying a session on the plot, exporting
/// one or all sessions, deleting one or all sessions, and keeping the bound session collection's
/// change notifications wired.
/// <para>
/// Extracted from <c>DaqifiViewModel</c> (issue #592). Every collaborator is constructor-injected
/// (the logging-context factory resolver, the database file path, the application logger) and all
/// plot/database, dialog, and notification side effects are reached through the
/// <see cref="ILoggingSessionListHost"/> seam — so this class has no dependency on WPF or on desktop
/// singletons (<c>LoggingManager.Instance</c>, <c>App.ServiceProvider</c>, <c>App.DatabasePath</c>)
/// and is unit-testable in isolation. The legacy <c>BackgroundWorker</c> the display and delete
/// paths used has been replaced with the file's async/await + busy-flag pattern.
/// </para>
/// </summary>
public class LoggingSessionListViewModel
{
    #region Private Fields
    private readonly ILoggingSessionListHost _host;
    private readonly Func<IDbContextFactory<LoggingContext>> _loggingContextFactoryResolver;
    private readonly string _databasePath;
    private readonly IAppLogger _appLogger;
    private ObservableCollection<LoggingSession>? _observedLoggingSessions;
    #endregion

    #region Constructor
    /// <summary>
    /// Creates the list view model. The composition root (the view model / DI container) is
    /// responsible for resolving the production collaborators so this class never reaches into
    /// singletons.
    /// </summary>
    /// <param name="host">The host view-model surface the list reads state from and pushes effects to.</param>
    /// <param name="loggingContextFactoryResolver">
    /// Resolves the logging-context factory used by the "delete all" storage purge. A resolver
    /// (rather than the factory itself) preserves the host's lazy resolution + clear failure when no
    /// factory is available.
    /// </param>
    /// <param name="databasePath">Absolute path to the SQLite database file (and its -wal/-shm siblings).</param>
    /// <param name="appLogger">Application logger used for diagnostics.</param>
    public LoggingSessionListViewModel(
        ILoggingSessionListHost host,
        Func<IDbContextFactory<LoggingContext>> loggingContextFactoryResolver,
        string databasePath,
        IAppLogger appLogger)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _loggingContextFactoryResolver = loggingContextFactoryResolver ?? throw new ArgumentNullException(nameof(loggingContextFactoryResolver));
        _databasePath = databasePath ?? throw new ArgumentNullException(nameof(databasePath));
        _appLogger = appLogger ?? throw new ArgumentNullException(nameof(appLogger));
    }
    #endregion

    #region Display
    /// <summary>
    /// Loads the selected session onto the logged-data plot. A null session clears the plot. The
    /// blocking <c>DbLogger</c> load runs off the UI thread under the busy flag (replacing the legacy
    /// <c>BackgroundWorker</c>); load failures are logged and never propagate.
    /// </summary>
    public async Task DisplaySessionAsync(LoggingSession? session)
    {
        if (session == null)
        {
            _host.ClearPlot();
            return;
        }

        _host.SelectedLoggingSession = session;
        _host.IsLoggedDataBusy = true;
        _host.LoggedDataBusyReason = "Loading " + session.Name;
        try
        {
            await Task.Run(() =>
            {
                try
                {
                    _host.DisplaySessionOnPlot(session);
                }
                catch (Exception ex)
                {
                    _appLogger.Error(ex, $"Failed to display logging session {session.ID}.");
                }
            });
        }
        finally
        {
            _host.IsLoggedDataBusy = false;
            _host.LoggedDataBusyReason = string.Empty;
        }
    }
    #endregion

    #region Export
    /// <summary>Exports a single session via the export dialog.</summary>
    public async Task ExportSessionAsync(LoggingSession? session)
    {
        if (session == null)
        {
            _appLogger.Error("Error exporting logging session");
            return;
        }

        _host.SelectedLoggingSession = session;
        await _host.ShowExportDialogForSessionAsync(session.ID);
    }

    /// <summary>Exports every session via the export dialog.</summary>
    public async Task ExportAllSessionsAsync()
    {
        if (_host.LoggingSessions.Count == 0)
        {
            _appLogger.Error("Error exporting all logging sessions");
            return;
        }

        await _host.ShowExportDialogForSessionsAsync(_host.LoggingSessions.ToList());
    }
    #endregion

    #region Delete
    /// <summary>
    /// Deletes a single session after confirmation. The blocking <c>DbLogger</c> delete runs off the
    /// UI thread under the busy flag (replacing the legacy <c>BackgroundWorker</c>); the session is
    /// removed from the bound collection only when the database delete succeeds.
    /// </summary>
    public async Task DeleteSessionAsync(LoggingSession? session)
    {
        try
        {
            if (session == null)
            {
                _appLogger.Error("Error deleting logging session: Invalid object provided.");
                return;
            }

            _host.SelectedLoggingSession = session;

            var confirmed = await _host.ShowConfirmAsync(
                "Delete Confirmation",
                $"Are you sure you want to delete {session.Name}?",
                affirmativeLabel: "DELETE",
                isDestructive: true);
            if (!confirmed)
            {
                return;
            }

            _host.IsLoggedDataBusy = true;
            _host.LoggedDataBusyReason = $"Deleting Logging Session #{session.ID}";
            try
            {
                // Remove the bound row only when the database delete reports success — the gating is
                // preserved verbatim from the pre-extraction DaqifiViewModel. Note the production host
                // routes DeleteSessionFromDatabase to DatabaseLogger.DeleteLoggingSession, which today
                // logs and swallows its own exceptions, so this catch is currently a latent guard
                // rather than a live failure path. Surfacing delete failures (so a failed delete keeps
                // the row) belongs to the separate DatabaseLogger extraction tracked in #592; this
                // contract is unit-tested here so it stays correct once the host propagates failure.
                var deleteSucceeded = false;
                try
                {
                    await Task.Run(() => _host.DeleteSessionFromDatabase(session));
                    deleteSucceeded = true;
                }
                catch (Exception dbEx)
                {
                    _appLogger.Error(dbEx, $"Failed to delete session {session.ID} from database.");
                }

                if (deleteSucceeded)
                {
                    _host.LoggingSessions.Remove(session);
                    _host.NotifyLoggingSessionsChanged();
                }
            }
            finally
            {
                _host.IsLoggedDataBusy = false;
                _host.LoggedDataBusyReason = string.Empty;
            }
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Error initiating logging session deletion");
        }
    }

    /// <summary>
    /// Deletes every session. Refuses to run while logging is active, confirms the destructive
    /// action, then purges the database storage off the UI thread under the busy flag.
    /// </summary>
    public async Task DeleteAllSessionsAsync()
    {
        try
        {
            if (_host.LoggingSessions.Count == 0)
            {
                return;
            }

            if (_host.IsLoggingActive)
            {
                await _host.ShowMessageAsync(
                    "Cannot Delete",
                    "Please stop logging before deleting all sessions.");
                return;
            }

            var confirmed = await _host.ShowConfirmAsync(
                "Delete Confirmation",
                "Are you sure you want to delete all logging sessions? This cannot be undone.",
                affirmativeLabel: "DELETE ALL",
                isDestructive: true);
            if (!confirmed)
            {
                return;
            }

            _host.IsLoggedDataBusy = true;
            _host.LoggedDataBusyReason = "Deleting All Logging Sessions";

            try
            {
                var contextFactory = _loggingContextFactoryResolver();
                await Task.Run(() => DeleteAllLoggingSessionsFromStorage(contextFactory));
                _host.LoggingSessions.Clear();
                _host.NotifyLoggingSessionsChanged();
                _host.ClearPlot();
            }
            catch (IOException ioEx)
            {
                _appLogger.Error(ioEx, "Database file is in use. Please try again.");
            }
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Error during deletion of all logging sessions");
        }
        finally
        {
            _host.IsLoggedDataBusy = false;
            _host.LoggedDataBusyReason = string.Empty;
        }
    }

    private void DeleteAllLoggingSessionsFromStorage(IDbContextFactory<LoggingContext> contextFactory)
    {
        _host.SuspendConsumer();
        try
        {
            _host.ClearBuffer();

            // Release all pooled SQLite connections so the file is not locked.
            SqliteConnection.ClearAllPools();

            DeleteFileIfExists(_databasePath);
            DeleteFileIfExists(_databasePath + "-wal");
            DeleteFileIfExists(_databasePath + "-shm");

            // Recreate the database schema. Constructing a context does not
            // create tables — only Migrate() (or EnsureCreated) does. Without
            // this, the next session-start query against Samples/Sessions
            // throws "no such table: Samples".
            using var context = contextFactory.CreateDbContext();
            context.Database.Migrate();
        }
        finally
        {
            _host.ResumeConsumer();
        }
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
    #endregion

    #region Collection plumbing
    /// <summary>
    /// Subscribes to the supplied session collection's change notifications, replacing any prior
    /// subscription. The host re-supplies the collection whenever the logging manager swaps it out.
    /// </summary>
    public void AttachCollection(ObservableCollection<LoggingSession> loggingSessions)
    {
        if (ReferenceEquals(_observedLoggingSessions, loggingSessions))
        {
            return;
        }

        if (_observedLoggingSessions != null)
        {
            _observedLoggingSessions.CollectionChanged -= OnLoggingSessionsCollectionChanged;
        }

        _observedLoggingSessions = loggingSessions;
        _observedLoggingSessions.CollectionChanged += OnLoggingSessionsCollectionChanged;
    }

    private void OnLoggingSessionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // The host owns the UI-thread marshalling so this class stays dispatcher-free.
        _host.NotifyLoggingSessionsChanged();
    }
    #endregion
}
