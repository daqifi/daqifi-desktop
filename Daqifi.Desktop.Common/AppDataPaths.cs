using System.Security.Principal;

namespace Daqifi.Desktop.Common;

/// <summary>
/// Single source of truth for where DAQiFi stores its application data (database and logs).
///
/// Production (elevated) runs use the machine-wide <c>CommonApplicationData</c>
/// (<c>%ProgramData%</c>) location. Any un-elevated run — the UI-test harness
/// (<c>DAQIFI_TEST_MODE=1</c>) <b>or</b> a normal non-admin Debug launch (the Debug build
/// ships an <c>asInvoker</c> manifest) — uses the per-user-writable
/// <c>LocalApplicationData</c> (<c>%LocalAppData%</c>) location instead, because a standard
/// token cannot write an admin-owned machine-wide store (which otherwise crashes startup
/// with a read-only database and/or fails to write logs).
///
/// Keeping the database and the logs under the same root avoids split or lost data.
/// </summary>
public static class AppDataPaths
{
    /// <summary>
    /// <c>true</c> when launched in unattended UI-test mode (environment variable
    /// <c>DAQIFI_TEST_MODE=1</c>).
    /// </summary>
    public static bool IsTestMode { get; } =
        string.Equals(Environment.GetEnvironmentVariable("DAQIFI_TEST_MODE"), "1", StringComparison.Ordinal);

    /// <summary>
    /// Optional UI-test export target directory. When the environment variable
    /// <c>DAQIFI_TEST_EXPORT_PATH</c> is set, the logging-session export commands write
    /// straight into this directory (one <c>{session}.csv</c> per session) and skip the
    /// interactive <c>SaveFileDialog</c>/<c>FolderBrowserDialog</c> entirely, so the FlaUI
    /// harness can export to a known location with zero modal interaction. <c>null</c> in
    /// normal and production runs, where the dialog behaviour is unchanged. Mirrors
    /// <see cref="IsTestMode"/> — a one-liner the harness sets on the child process,
    /// impossible to trigger accidentally in production (the variable is never set there).
    /// </summary>
    public static string? TestExportPath { get; } = ResolveTestExportPath();

    /// <summary><c>true</c> when the current process is running with administrator rights.</summary>
    public static bool IsElevated { get; } = ComputeIsElevated();

    /// <summary>
    /// Root DAQiFi data directory for the current elevation/test context. Per-user when the
    /// process is un-elevated or in test mode; machine-wide otherwise.
    /// </summary>
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(IsTestMode || !IsElevated
            ? Environment.SpecialFolder.LocalApplicationData
            : Environment.SpecialFolder.CommonApplicationData),
        "DAQiFi");

    /// <summary>Directory where application logs are written.</summary>
    public static string LogDirectory { get; } = Path.Combine(DataDirectory, "Logs");

    private static string? ResolveTestExportPath()
    {
        var raw = Environment.GetEnvironmentVariable("DAQIFI_TEST_EXPORT_PATH");
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }

    private static bool ComputeIsElevated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            // If the elevation check fails for any reason, treat the process as un-elevated
            // so it falls back to the always-writable per-user location.
            return false;
        }
    }
}
