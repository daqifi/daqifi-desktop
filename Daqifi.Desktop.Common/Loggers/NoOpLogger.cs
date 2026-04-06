namespace Daqifi.Desktop.Common.Loggers;

public class NoOpLogger : IAppLogger
{
    public void Information(string message) { }
    public void Warning(string message) { }
    public void Error(string message) { }
    public void Error(Exception ex, string message) { }
    public void AddBreadcrumb(string category, string message, BreadcrumbLevel level = BreadcrumbLevel.Info) { }
    public void SetDeviceContext(string model, string serialNumber, string firmwareVersion, string connectionType, int activeChannels) { }
    public void ClearDeviceContext() { }
    /// <inheritdoc />
    public void Shutdown() { }
}