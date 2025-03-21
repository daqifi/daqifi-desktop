namespace Daqifi.Desktop.Commands;

public static class HostCommands
{
    private static readonly CompositeCommand Shutdown = new CompositeCommand();

    public static CompositeCommand ShutdownCommand => Shutdown;
}