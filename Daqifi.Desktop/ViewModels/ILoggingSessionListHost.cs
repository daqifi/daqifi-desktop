using System.Collections.ObjectModel;
using Daqifi.Desktop.Logger;

namespace Daqifi.Desktop.ViewModels;

/// <summary>
/// Narrow seam the <see cref="LoggingSessionListViewModel"/> uses to read the bound logged-data
/// state and route plot/database, dialog, and notification side effects back to the host view model.
/// <para>
/// Implemented by <c>DaqifiViewModel</c> so its existing bound members (<c>LoggingSessions</c>,
/// <c>HasLoggingSessions</c>, <c>SelectedLoggingSession</c>, <c>IsLoggedDataBusy</c>,
/// <c>LoggedDataBusyReason</c>) remain the binding source — the logged-data pane XAML and its
/// command bindings are unchanged — while the display/export/delete orchestration and the
/// session-collection plumbing live in the list view model and are unit-testable against a
/// lightweight fake. Only logging-session-relevant members are exposed; the list view model never
/// reaches into desktop singletons (<c>LoggingManager.Instance</c>, <c>ConnectionManager.Instance</c>,
/// <c>App.ServiceProvider</c>) or touches WPF (<c>Application.Current</c>, a dispatcher) directly.
/// </para>
/// </summary>
public interface ILoggingSessionListHost
{
    #region Bound state
    /// <summary>
    /// The logging session the logged-data pane is currently acting on. Set by the list view model
    /// before it displays, exports, or deletes a session so the bound selection stays in sync.
    /// </summary>
    LoggingSession SelectedLoggingSession { set; }

    /// <summary>True while a long-running logged-data operation (load/delete) is in progress.</summary>
    bool IsLoggedDataBusy { set; }

    /// <summary>The status line shown over the logged-data pane while it is busy.</summary>
    string LoggedDataBusyReason { set; }

    /// <summary>
    /// The shared, bound logging-session collection (the <c>LoggingManager</c>-owned list in
    /// production). The list view model reads its count, removes/clears entries, and subscribes to
    /// its change notifications through this property so it never touches <c>LoggingManager.Instance</c>.
    /// </summary>
    ObservableCollection<LoggingSession> LoggingSessions { get; }

    /// <summary>True while a logging session is active. Gates the "delete all" path.</summary>
    bool IsLoggingActive { get; }
    #endregion

    #region Notifications
    /// <summary>
    /// Re-raises change notifications for <c>LoggingSessions</c> / <c>HasLoggingSessions</c> and
    /// refreshes the "export all" / "delete all" command CanExecute state. The host marshals this to
    /// the UI thread when called off it, so the list view model never references a dispatcher.
    /// </summary>
    void NotifyLoggingSessionsChanged();
    #endregion

    #region Plot / database operations
    /// <summary>Loads the given session onto the logged-data plot (routes to <c>DbLogger</c>).</summary>
    void DisplaySessionOnPlot(LoggingSession session);

    /// <summary>Deletes the given session's samples from the database (routes to <c>DbLogger</c>).</summary>
    void DeleteSessionFromDatabase(LoggingSession session);

    /// <summary>Clears the logged-data plot (routes to <c>DbLogger</c>).</summary>
    void ClearPlot();

    /// <summary>Suspends the database consumer thread before a bulk storage purge (routes to <c>DbLogger</c>).</summary>
    void SuspendConsumer();

    /// <summary>Resumes the database consumer thread after a bulk storage purge (routes to <c>DbLogger</c>).</summary>
    void ResumeConsumer();

    /// <summary>Clears the pending sample buffer before a bulk storage purge (routes to <c>DbLogger</c>).</summary>
    void ClearBuffer();
    #endregion

    #region Dialogs
    /// <summary>
    /// Presents the export dialog for a single session. Dialog construction (which resolves services
    /// from the desktop container) and presentation are view concerns, so the list view model
    /// delegates them here rather than newing up the dialog view model itself.
    /// </summary>
    Task ShowExportDialogForSessionAsync(int sessionId);

    /// <summary>Presents the export dialog for a batch of sessions ("export all").</summary>
    Task ShowExportDialogForSessionsAsync(IReadOnlyList<LoggingSession> sessions);

    /// <summary>
    /// Shows the in-pane confirm overlay and returns true when the user accepts. Routed through the
    /// host so the confirm-overlay state stays on the view model (its extraction is a separate PR).
    /// </summary>
    Task<bool> ShowConfirmAsync(string title, string message, string affirmativeLabel, bool isDestructive);

    /// <summary>Shows an informational message dialog (e.g. the "stop logging before delete all" guard).</summary>
    Task ShowMessageAsync(string title, string message);
    #endregion
}
