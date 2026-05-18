using System;
using System.IO;
using System.Reflection;
using FlaUI.Core;
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
public class MainWindowSmokeTests
{
    // Assembly name is "DAQiFi" per Daqifi.Desktop.csproj <AssemblyName>; the
    // produced exe is therefore DAQiFi.exe under the desktop project's bin dir.
    private const string DESKTOP_EXE_NAME = "DAQiFi.exe";
    private const string DESKTOP_PROJECT_NAME = "Daqifi.Desktop";
    private const string EXPECTED_TITLE_FRAGMENT = "DAQiFi";

    // Allow up to 60s for the main window to appear; WPF cold-start can be slow
    // on first launch (JIT, MEF composition, MahApps theme load).
    private static readonly TimeSpan MAIN_WINDOW_TIMEOUT = TimeSpan.FromSeconds(60);

    // Grace period for graceful shutdown after Close(); matches Phase 2 teardown.
    private static readonly TimeSpan SHUTDOWN_GRACE = TimeSpan.FromSeconds(5);

    [TestMethod]
    [TestCategory("UI")]
    public void MainWindow_Launches_And_HasExpectedTitle()
    {
        var exePath = TryLocateDesktopExe();
        if (exePath is null)
        {
            Assert.Inconclusive(
                $"Skipped: {DESKTOP_EXE_NAME} was not found. Build the {DESKTOP_PROJECT_NAME} " +
                "project (Debug or Release, net10.0-windows) before running this UI test. " +
                "See issue #531 for the full FlaUI scaffold rollout plan.");
            return; // unreachable; Assert.Inconclusive throws.
        }

        Application? app = null;
        try
        {
            app = UIAppLifecycle.LaunchOrInconclusive(exePath);

            using var automation = new UIA3Automation();
            var mainWindow = app.GetMainWindow(automation, MAIN_WINDOW_TIMEOUT);

            Assert.IsNotNull(mainWindow, "Main window was not found within the timeout.");

            // The MainWindow code-behind sets Title = $"DAQiFi v{Major}.{Minor}.{Build}".
            // Case-insensitive substring match keeps the assertion resilient to
            // version-string changes.
            var title = mainWindow.Title ?? string.Empty;
            Assert.IsTrue(
                title.Contains(EXPECTED_TITLE_FRAGMENT, StringComparison.OrdinalIgnoreCase),
                $"Expected window title to contain '{EXPECTED_TITLE_FRAGMENT}', but was '{title}'.");
        }
        finally
        {
            UIAppLifecycle.CloseAppGracefully(app, SHUTDOWN_GRACE);
        }
    }

    /// <summary>
    /// Tries common build-output locations for DAQiFi.exe. Returns null if none
    /// of them exist; the caller turns that into Assert.Inconclusive.
    /// </summary>
    internal static string? TryLocateDesktopExe()
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
                    repoRoot, DESKTOP_PROJECT_NAME, "bin", config, tfm, DESKTOP_EXE_NAME);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
