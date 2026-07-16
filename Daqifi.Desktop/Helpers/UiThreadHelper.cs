using Daqifi.Desktop.Common.Loggers;

namespace Daqifi.Desktop.Helpers;

/// <summary>
/// Marshals actions onto the WPF UI thread from background-thread event handlers (e.g. Core's
/// <c>StatusChanged</c>/<c>ConnectionLost</c>, which can fire from a transport thread).
/// </summary>
internal static class UiThreadHelper
{
    /// <summary>
    /// Runs <paramref name="action"/> on the WPF UI thread. Runs inline when there is no
    /// dispatcher (unit tests — <c>Application.Current</c> is null) or the caller is already on
    /// it. Uses the non-blocking <c>BeginInvoke</c> so a background-thread caller can never block
    /// on the UI thread; failures during app/dispatcher shutdown are swallowed since there is
    /// nothing left to update.
    /// </summary>
    /// <param name="action">The action to run on the UI thread.</param>
    /// <param name="failureLogMessage">
    /// When non-null, logged as a warning if dispatching fails (e.g. dispatcher shutting down).
    /// </param>
    public static void InvokeOnUiThread(Action action, string? failureLogMessage = null)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        if (dispatcher.HasShutdownStarted)
        {
            if (failureLogMessage != null)
            {
                AppLogger.Instance.Warning(failureLogMessage);
            }
            return;
        }

        try
        {
            dispatcher.BeginInvoke(action);
        }
        catch (Exception ex)
        {
            if (failureLogMessage != null)
            {
                AppLogger.Instance.Warning(ex, failureLogMessage);
            }
        }
    }
}
