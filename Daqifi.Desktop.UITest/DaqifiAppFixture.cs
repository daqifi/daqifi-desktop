using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daqifi.Desktop.UITest;

/// <summary>
/// Base fixture for out-of-process FlaUI UI-automation tests against the real
/// DAQiFi Desktop GUI. Launches the produced Debug exe in unattended test mode,
/// resolves the main window, and tails the NLog log file for assertions.
/// Captures a screenshot and copies the log on failure during teardown.
/// </summary>
public abstract class DaqifiAppFixture
{
    #region Constants
    private const string TEST_MODE_ENV_VAR = "DAQIFI_TEST_MODE";
    private const string APP_EXE_NAME = "DAQiFi.exe";
    private const string LOG_FILE_NAME = "DAQifiAppLog.log";
    #endregion

    #region Protected Fields
    protected Application App = null!;
    protected UIA3Automation Automation = null!;
    protected Window MainWindow = null!;
    #endregion

    #region Private Fields
    private long _logStartOffset;
    private string _logFilePath = null!;
    #endregion

    #region Test Context
    /// <summary>MSTest-injected context, used to derive output paths for artifacts.</summary>
    public TestContext TestContext { get; set; } = null!;
    #endregion

    #region Setup / Teardown
    [TestInitialize]
    public void Setup()
    {
        var exePath = ResolveAppExePath();

        _logFilePath = ResolveLogFilePath();
        _logStartOffset = File.Exists(_logFilePath) ? new FileInfo(_logFilePath).Length : 0;

        var psi = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exePath)!
        };
        psi.Environment[TEST_MODE_ENV_VAR] = "1";

        App = Application.Launch(psi);
        Automation = new UIA3Automation();

        MainWindow = Retry.WhileNull(
            () => App.GetMainWindow(Automation, TimeSpan.FromSeconds(2)),
            timeout: TimeSpan.FromSeconds(60),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            timeoutMessage: "Main window did not appear within 60 seconds.").Result!;
    }

    [TestCleanup]
    public void Teardown()
    {
        try
        {
            if (TestContext?.CurrentTestOutcome == UnitTestOutcome.Failed)
            {
                CaptureFailureArtifacts();
            }
        }
        catch
        {
            // Never let artifact capture mask the real test outcome.
        }

        try
        {
            DisconnectAnyDevice();
        }
        catch
        {
            // Best-effort cleanup; teardown must not throw.
        }

        CloseApp();

        Automation?.Dispose();
    }
    #endregion

    #region App Lifecycle
    private void CloseApp()
    {
        if (App == null)
        {
            return;
        }

        try
        {
            App.Close();
            Retry.WhileFalse(
                () => App.HasExited,
                timeout: TimeSpan.FromSeconds(15),
                interval: TimeSpan.FromMilliseconds(250),
                throwOnTimeout: false);
        }
        catch
        {
            // Fall through to Kill.
        }

        try
        {
            if (!App.HasExited)
            {
                App.Kill();
            }
        }
        catch
        {
            // Process may already be gone.
        }

        App.Dispose();
    }

    /// <summary>
    /// Best-effort attempt to leave the app in a clean state by stopping any
    /// active logging. Overridden behavior is intentionally minimal here; concrete
    /// scenario teardown happens in the test bodies themselves.
    /// </summary>
    private void DisconnectAnyDevice()
    {
        // The app is closed immediately after; device disconnect is handled by the
        // app's own shutdown path. This hook exists for future explicit cleanup.
    }
    #endregion

    #region Element Helpers
    /// <summary>
    /// Finds the first descendant of the main window with the given AutomationId,
    /// retrying until it appears or the timeout elapses.
    /// </summary>
    protected AutomationElement FindByAutomationId(string automationId, int timeoutSeconds = 30)
    {
        return Retry.WhileNull(
            () => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId)),
            timeout: TimeSpan.FromSeconds(timeoutSeconds),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            timeoutMessage: $"Element with AutomationId '{automationId}' not found within {timeoutSeconds}s.").Result!;
    }
    #endregion

    #region Log File Helpers
    /// <summary>
    /// Waits until the NLog log file contains <paramref name="substring"/> in text
    /// appended after the fixture started, or the timeout elapses.
    /// </summary>
    /// <returns>True if the substring appeared; otherwise false.</returns>
    protected bool WaitForLogContains(string substring, TimeSpan timeout)
    {
        var result = Retry.WhileFalse(
            () => ReadNewLogText().Contains(substring, StringComparison.Ordinal),
            timeout: timeout,
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: false);
        return result.Result;
    }

    /// <summary>Reads log text appended since the fixture started.</summary>
    protected string ReadNewLogText()
    {
        if (!File.Exists(_logFilePath))
        {
            return string.Empty;
        }

        // KeepFileOpen = false in AppLogger, so the file is safe to open for shared read.
        using var stream = new FileStream(
            _logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (_logStartOffset > stream.Length)
        {
            // File was rotated/archived; read from start.
            _logStartOffset = 0;
        }

        stream.Seek(_logStartOffset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    #endregion

    #region Failure Artifacts
    private void CaptureFailureArtifacts()
    {
        var outDir = TestContext?.TestResultsDirectory ?? AppContext.BaseDirectory;
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var testName = TestContext?.TestName ?? "UnknownTest";

        try
        {
            if (MainWindow != null && !App.HasExited)
            {
                var shotPath = Path.Combine(outDir, $"{testName}_{stamp}.png");
                FlaUI.Core.Capturing.Capture.Element(MainWindow).ToFile(shotPath);
                TestContext?.AddResultFile(shotPath);
            }
        }
        catch
        {
            // Window may be gone; skip screenshot.
        }

        try
        {
            if (File.Exists(_logFilePath))
            {
                var logCopyPath = Path.Combine(outDir, $"{testName}_{stamp}_{LOG_FILE_NAME}");
                using (var src = new FileStream(
                           _logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var dst = new FileStream(logCopyPath, FileMode.Create, FileAccess.Write))
                {
                    src.CopyTo(dst);
                }

                TestContext?.AddResultFile(logCopyPath);
            }
        }
        catch
        {
            // Skip log capture on failure.
        }
    }
    #endregion

    #region Path Resolution
    /// <summary>
    /// Resolves the Debug build of the app exe relative to this assembly's location,
    /// walking up to the repo root then into the app's Debug output.
    /// </summary>
    private static string ResolveAppExePath()
    {
        var dir = Path.GetDirectoryName(typeof(DaqifiAppFixture).Assembly.Location)!;
        // ...\Daqifi.Desktop.UITest\bin\Debug\net10.0-windows -> repo root (4 up)
        var repoRoot = Path.GetFullPath(Path.Combine(dir, "..", "..", "..", ".."));
        var exe = Path.Combine(
            repoRoot, "Daqifi.Desktop", "bin", "Debug", "net10.0-windows", APP_EXE_NAME);
        if (!File.Exists(exe))
        {
            Assert.Inconclusive(
                $"App exe not found at {exe}. Build Daqifi.Desktop (Debug) first.");
        }

        return exe;
    }

    /// <summary>
    /// Resolves the NLog log file path. Matches AppLogger.cs exactly:
    /// %CommonApplicationData%\DAQifi\Logs\DAQifiAppLog.log
    /// </summary>
    private static string ResolveLogFilePath()
    {
        var commonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return Path.Combine(commonAppData, "DAQifi", "Logs", LOG_FILE_NAME);
    }
    #endregion
}
