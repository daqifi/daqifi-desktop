namespace Daqifi.Desktop.Common.Loggers;

public interface IAppLogger
{
    void Information(string message);
    void Warning(string message);
    void Error(string message);
    void Error(Exception ex, string message);

    /// <summary>
    /// Records a Sentry breadcrumb so the event timeline shows what happened before a crash.
    /// </summary>
    void AddBreadcrumb(string category, string message, BreadcrumbLevel level = BreadcrumbLevel.Info);

    /// <summary>
    /// Sets DAQiFi-specific device context tags on the Sentry scope so errors can be
    /// filtered and grouped by hardware environment.
    /// </summary>
    void SetDeviceContext(string model, string firmwareVersion, string connectionType, int activeChannels);

    /// <summary>
    /// Clears DAQiFi device context tags from the Sentry scope on disconnect.
    /// </summary>
    void ClearDeviceContext();

    /// <summary>
    /// Flushes pending error reports and releases logging resources.
    /// </summary>
    void Shutdown();
}

/// <summary>
/// Breadcrumb severity levels matching Sentry's BreadcrumbLevel.
/// </summary>
public enum BreadcrumbLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Critical
}