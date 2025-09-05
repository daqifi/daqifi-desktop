namespace Daqifi.Desktop.Commands;

public static class HostCommands
{
    private static readonly CompositeCommand Shutdown = new();

    public static CompositeCommand ShutdownCommand => Shutdown;
}