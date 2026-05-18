using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daqifi.Desktop.Test.UITests;

/// <summary>
/// Phase 1 of the FlaUI UI-automation scaffold (issue #531).
///
/// Launches the DAQiFi Desktop executable, asserts the main window appears and
/// has a sensible title, then closes the app cleanly. Requires the WPF app to
/// already be built; the test is marked Inconclusive (skipped) if the exe is
/// not on disk so a clean CI run without a built desktop is informative rather
/// than red.
///
/// This is intentionally minimal: it proves FlaUI can drive the app under
/// MSTest. Phase 2 builds the connect/stream/disconnect happy path on top of
/// the helpers established here.
/// </summary>
[TestClass]
public class MainWindowSmokeTest
{
    // Assembly name is "DAQiFi" per Daqifi.Desktop.csproj <AssemblyName>; the
    // produced exe is therefore DAQiFi.exe under the desktop project's bin dir.
    private const string DesktopExeName = "DAQiFi.exe";
    private const string DesktopProjectName = "Daqifi.Desktop";
    private const string ExpectedTitleFragment = "DAQiFi";

    // Allow up to 60s for the main window to appear; WPF cold-start can be slow
    // on first launch (JIT, MEF composition, MahApps theme load).
    private static readonly TimeSpan MainWindowTimeout = TimeSpan.FromSeconds(60);

    [TestMethod]
    [TestCategory("UI")]
    public void MainWindow_Launches_And_HasExpectedTitle()
    {
        var exePath = TryLocateDesktopExe();
        if (exePath is null)
        {
            Assert.Inconclusive(
                $"Skipped: {DesktopExeName} was not found. Build the {DesktopProjectName} " +
                "project (Debug or Release, net10.0-windows) before running this UI test. " +
                "See issue #531 for the full FlaUI scaffold rollout plan.");
        }

        Application? app = null;
        try
        {
            try
            {
                app = Application.Launch(exePath!);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 740 /* ERROR_ELEVATION_REQUIRED */)
            {
                // Daqifi.Desktop/app.manifest declares requestedExecutionLevel="requireAdministrator",
                // so Process.Start fails with error 740 unless the test runner is elevated.
                // FlaUI cannot drive UAC consent, so the working approaches are:
                //   1. Run `dotnet test` from an elevated terminal / elevated CI agent, OR
                //   2. Build a non-elevated test target of the app (manifest = asInvoker) - see #531.
                // Mark Inconclusive so an un-elevated dev box reports SKIPPED rather than FAILED.
                Assert.Inconclusive(
                    "Skipped: DAQiFi.exe requires administrator elevation (app.manifest sets " +
                    "requestedExecutionLevel=requireAdministrator). Run the test from an elevated " +
                    "terminal, or switch the manifest to asInvoker for the UI-test build. See #531.");
                return; // unreachable; Assert.Inconclusive throws.
            }

            using var automation = new UIA3Automation();
            var mainWindow = app.GetMainWindow(automation, MainWindowTimeout);

            Assert.IsNotNull(mainWindow, "Main window was not found within the timeout.");

            // The MainWindow code-behind sets Title = $"DAQiFi v{Major}.{Minor}.{Build}".
            // Case-insensitive substring match keeps the assertion resilient to
            // version-string changes.
            var title = mainWindow.Title ?? string.Empty;
            Assert.IsTrue(
                title.Contains(ExpectedTitleFragment, StringComparison.OrdinalIgnoreCase),
                $"Expected window title to contain '{ExpectedTitleFragment}', but was '{title}'.");
        }
        finally
        {
            if (app is not null)
            {
                try
                {
                    app.Close();
                    // Close() requests graceful shutdown; give the app up to 5s
                    // to exit on its own. If it hasn't, force it so the next
                    // test run starts from a clean slate.
                    app.WaitWhileMainHandleIsMissing(TimeSpan.FromSeconds(5));
                    if (!app.HasExited)
                    {
                        app.Kill();
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
        }
    }

    /// <summary>
    /// Tries common build-output locations for DAQiFi.exe. Returns null if none
    /// of them exist; the caller turns that into Assert.Inconclusive.
    /// </summary>
    private static string? TryLocateDesktopExe()
    {
        // The test binary lands under
        //   <repo>/Daqifi.Desktop.Test/bin/<config>/<tfm>/Daqifi.Desktop.Test.dll
        // so the repo root is four levels up.
        var testAssemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                              ?? Directory.GetCurrentDirectory();
        var repoRoot = Path.GetFullPath(Path.Combine(testAssemblyDir, "..", "..", "..", ".."));

        // Match the same configuration we were built with, then fall back to
        // any sibling config so a Release test run can still drive a Debug app
        // build (or vice versa).
#if DEBUG
        var preferredConfigs = new[] { "Debug", "Release" };
#else
        var preferredConfigs = new[] { "Release", "Debug" };
#endif

        var tfms = new[] { "net10.0-windows", "net9.0-windows" };

        foreach (var config in preferredConfigs)
        {
            foreach (var tfm in tfms)
            {
                var candidate = Path.Combine(
                    repoRoot, DesktopProjectName, "bin", config, tfm, DesktopExeName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
