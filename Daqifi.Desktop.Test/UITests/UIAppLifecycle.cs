using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using FlaUI.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daqifi.Desktop.Test.UITests;

/// <summary>
/// Shared helpers for the FlaUI UI-test scaffold (issue #531).
///
/// Centralises two cross-cutting concerns so Phase 1
/// (<see cref="MainWindowSmokeTests"/>) and Phase 2
/// (<see cref="ConnectStreamDisconnectTests"/>) stay in sync:
///
///  - <see cref="LaunchOrInconclusive"/>: wraps
///    <c>Application.Launch</c> and surfaces the UAC-elevation case
///    (Win32 error 740) as <c>Assert.Inconclusive</c> instead of a
///    hard failure, since FlaUI can't drive the UAC consent dialog.
///  - <see cref="CloseAppGracefully"/>: best-effort teardown that
///    waits on <c>app.HasExited</c> (not the main-window handle)
///    so the app's shutdown path — device disconnect, settings
///    flush — has a chance to finish before <c>Kill()</c>.
/// </summary>
internal static class UIAppLifecycle
{
    /// <summary>
    /// Launches the given executable and returns the <see cref="Application"/>
    /// handle, or marks the test inconclusive (rather than failing) when the
    /// launch would require UAC elevation that the test runner can't drive.
    /// </summary>
    /// <remarks>
    /// Daqifi.Desktop/app.manifest declares
    /// <c>requestedExecutionLevel="requireAdministrator"</c>, so
    /// <c>Process.Start</c> fails with <c>ERROR_ELEVATION_REQUIRED (740)</c>
    /// unless the test runner is already elevated. The two working approaches
    /// are:
    ///   1. Run <c>dotnet test</c> from an elevated terminal / CI agent, OR
    ///   2. Build a non-elevated test target of the app (manifest =
    ///      <c>asInvoker</c>) - see #531.
    /// </remarks>
    public static Application LaunchOrInconclusive(string exePath)
    {
        try
        {
            // Set WorkingDirectory to the exe's folder so the app's relative
            // path lookups (config files, resource probing, settings stores)
            // resolve from a known location instead of inheriting the test
            // runner's cwd (which can be anything from MSTest's bin/ down to
            // wherever the IDE was launched from).
            var startInfo = new ProcessStartInfo(exePath)
            {
                WorkingDirectory = Path.GetDirectoryName(exePath)
                                   ?? Environment.CurrentDirectory,
            };
            return Application.Launch(startInfo);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 740 /* ERROR_ELEVATION_REQUIRED */)
        {
            Assert.Inconclusive(
                "Skipped: DAQiFi.exe requires administrator elevation (app.manifest sets " +
                "requestedExecutionLevel=requireAdministrator). Run the test from an elevated " +
                "terminal, or switch the manifest to asInvoker for the UI-test build. See #531.");
            // Assert.Inconclusive throws; this return keeps the compiler happy.
            throw;
        }
    }

    /// <summary>
    /// Requests graceful shutdown via <c>Close()</c>, polls
    /// <c>app.HasExited</c> for up to <paramref name="grace"/>, then forces
    /// <c>Kill()</c> if necessary, and always <c>Dispose()</c>s.
    ///
    /// Polling <c>HasExited</c> rather than calling
    /// <c>WaitWhileMainHandleIsMissing</c> ensures we wait for the
    /// <em>process</em> to exit, not just for the main-window handle to
    /// vanish; the latter can return while the app's shutdown handlers
    /// (device disconnect, settings flush) are still running.
    /// </summary>
    public static void CloseAppGracefully(Application? app, TimeSpan grace)
    {
        if (app is null)
        {
            return;
        }

        try
        {
            app.Close();

            var deadline = DateTime.UtcNow + grace;
            while (DateTime.UtcNow < deadline && !app.HasExited)
            {
                Thread.Sleep(100);
            }

            if (!app.HasExited)
            {
                app.Kill();
                // Kill() is asynchronous - the OS posts the termination but the
                // process can still hold file/socket handles for a moment after.
                // Block here so callers see a fully-exited process when
                // Dispose() runs, preventing stray processes between consecutive
                // test runs. FlaUI's Application doesn't expose the underlying
                // Process directly, but we can fetch it by ProcessId.
                WaitForExitByProcessId(app.ProcessId, grace);
            }
        }
        catch
        {
            // Best-effort teardown - never let cleanup mask the real failure.
        }
        finally
        {
            app.Dispose();
        }
    }

    /// <summary>
    /// Blocks until the OS process with the given PID has exited or the grace
    /// window expires. Swallows the (expected) <see cref="ArgumentException"/>
    /// from <see cref="Process.GetProcessById(int)"/> when the process is
    /// already gone by the time we look it up.
    /// </summary>
    private static void WaitForExitByProcessId(int processId, TimeSpan grace)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.WaitForExit((int)grace.TotalMilliseconds);
        }
        catch (ArgumentException)
        {
            // Process already exited - no further wait needed.
        }
    }
}
