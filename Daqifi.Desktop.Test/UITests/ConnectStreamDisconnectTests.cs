using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.UIA3;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daqifi.Desktop.Test.UITests;

/// <summary>
/// Phase 2 of the FlaUI UI-automation scaffold (issue #531).
///
/// Drives the full connect -> enable-channel -> stream -> disconnect happy path
/// against a real DAQiFi device.
///
/// Skip behavior is opt-in rather than opt-out. The test self-skips unless the
/// environment variable <c>DAQIFI_BENCH_DEVICE_AVAILABLE</c> is set to a truthy
/// value ("1", "true", or "yes", case-insensitive). On a normal CI run with no
/// bench device wired up, the test reports <c>Inconclusive</c> with a pointer
/// to issue #531; on the bench machine, set the env var and the test runs.
///
/// Before enabling on the bench:
///   1. A DAQiFi Nyquist must be attached via USB (or reachable via Wi-Fi).
///   2. The required XAML controls must be annotated with the AutomationIds
///      referenced below. Each Id has a comment in the XAML pointing back to
///      this test + issue #531 for traceability.
///   3. The desktop app must be built (Phase 1 verifies the launch path).
///
/// Naming convention used for AutomationIds: "Daqifi.&lt;Pane&gt;.&lt;Control&gt;",
/// e.g. "Daqifi.Connection.AddDeviceButton". Keeping a stable, dotted namespace
/// makes future selectors greppable in the XAML.
/// </summary>
[TestClass]
public class ConnectStreamDisconnectTests
{
    private const string DESKTOP_EXE_NAME = "DAQiFi.exe";
    private const string DESKTOP_PROJECT_NAME = "Daqifi.Desktop";

    // Env-var gate: the test only runs when this is set to a truthy value on the
    // host. This replaces the previous unconditional [Ignore] attribute so the
    // bench machine can opt in without a code change (PR #531 / Qodo finding #3).
    private const string BENCH_AVAILABLE_ENV_VAR = "DAQIFI_BENCH_DEVICE_AVAILABLE";

    // AutomationIds that Phase 2 expects to exist in MainWindow / dialogs.
    // None of these are wired up yet; add them in the corresponding XAML with
    // a comment referencing this test + #531 when enabling the test.
    private const string ADD_DEVICE_BUTTON_ID = "Daqifi.Connection.AddDeviceButton";
    private const string CONNECT_BUTTON_ID = "Daqifi.Connection.ConnectButton";
    private const string DEVICE_LIST_ID = "Daqifi.Devices.ConnectedList";
    private const string FIRST_CHANNEL_TOGGLE_ID = "Daqifi.Channels.FirstChannelEnable";
    private const string START_STREAMING_ID = "Daqifi.Streaming.StartButton";
    private const string STOP_STREAMING_ID = "Daqifi.Streaming.StopButton";
    private const string DISCONNECT_BUTTON_ID = "Daqifi.Connection.DisconnectButton";
    private const string LIVE_GRAPH_ID = "Daqifi.Graph.Live";

    // ConnectionDialog (a separate top-level MetroWindow) is identified by its
    // window title; see Daqifi.Desktop/View/ConnectionDialog.xaml.
    private const string CONNECTION_DIALOG_TITLE = "CONNECT DEVICE";

    private static readonly TimeSpan MAIN_WINDOW_TIMEOUT = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DEVICE_APPEAR_TIMEOUT = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CONNECTION_DIALOG_TIMEOUT = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan STREAMING_DWELL_TIME = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SHUTDOWN_GRACE = TimeSpan.FromSeconds(5);

    [TestMethod]
    [TestCategory("UI-Bench")]
    public void ConnectStreamDisconnect_HappyPath()
    {
        if (!IsBenchDeviceAvailable())
        {
            Assert.Inconclusive(
                "Skipped: no bench device available. Set the environment variable " +
                $"{BENCH_AVAILABLE_ENV_VAR}=1 on the bench machine (with a DAQiFi Nyquist " +
                "attached and XAML AutomationIds wired up) to run this happy-path test. " +
                "See issue #531.");
            return; // unreachable
        }

        var exePath = MainWindowSmokeTests.TryLocateDesktopExe();
        if (exePath is null)
        {
            Assert.Inconclusive(
                $"Skipped: {DESKTOP_EXE_NAME} was not found. Build the {DESKTOP_PROJECT_NAME} " +
                "project first.");
            return; // unreachable
        }

        Application? app = null;
        try
        {
            app = UIAppLifecycle.LaunchOrInconclusive(exePath!);

            using var automation = new UIA3Automation();
            var mainWindow = app.GetMainWindow(automation, MAIN_WINDOW_TIMEOUT);
            Assert.IsNotNull(mainWindow, "Main window did not appear.");
            var cf = automation.ConditionFactory;

            // ----- Connect -----
            // The Add Device button lives on the main window; clicking it opens the
            // separate ConnectionDialog (a MetroWindow) where the Connect button
            // actually lives.
            var addDevice = FindByAutomationId(mainWindow, cf, ADD_DEVICE_BUTTON_ID,
                "Add-device entry point (USB/Serial picker).");
            addDevice.AsButton().Invoke();

            var connectionDialog = WaitForTopLevelWindow(app, automation,
                CONNECTION_DIALOG_TITLE, CONNECTION_DIALOG_TIMEOUT,
                "Connection dialog did not appear after invoking Add Device.");

            var connect = FindByAutomationId(connectionDialog, cf, CONNECT_BUTTON_ID,
                "Confirm button on the connection dialog.");
            connect.AsButton().Invoke();

            // Wait for the connected-devices list to show at least one row.
            var deviceList = FindByAutomationId(mainWindow, cf, DEVICE_LIST_ID,
                "Connected-devices list container.");
            WaitFor(() => deviceList.FindAllChildren().Length > 0, DEVICE_APPEAR_TIMEOUT,
                "Device did not appear in the connected list.");

            // ----- Enable first channel -----
            var firstChannel = FindByAutomationId(mainWindow, cf, FIRST_CHANNEL_TOGGLE_ID,
                "Enable-toggle on the first analog channel.");
            // Toggle controls in MahApps are typically ToggleButtons; click via Invoke.
            firstChannel.AsToggleButton().Toggle();

            // ----- Start streaming, dwell, check graph has data -----
            var start = FindByAutomationId(mainWindow, cf, START_STREAMING_ID,
                "Start-streaming command button.");
            start.AsButton().Invoke();

            // Capture graph geometry BEFORE the dwell so we can detect that data
            // actually arrived during streaming (the rectangle grows / changes as
            // points are plotted). UI-Automation can't see OxyPlot/LiveCharts data
            // directly, so this is the best UIA-only proxy for "non-zero data" we
            // can do until the XAML grows an automation-visible point-count probe
            // (tracked under #531).
            var liveGraph = FindByAutomationId(mainWindow, cf, LIVE_GRAPH_ID,
                "Live graph control. Should contain non-zero point count after dwell.");
            var preStreamRect = liveGraph.BoundingRectangle;

            Thread.Sleep(STREAMING_DWELL_TIME);

            // Re-fetch in case the surface was lazy-bound on Start.
            liveGraph = FindByAutomationId(mainWindow, cf, LIVE_GRAPH_ID,
                "Live graph control (post-dwell).");

            Assert.IsFalse(liveGraph.IsOffscreen, "Live graph was offscreen after Start.");
            Assert.IsTrue(liveGraph.BoundingRectangle.Width > 0
                          && liveGraph.BoundingRectangle.Height > 0,
                "Live graph had zero-sized bounding box; streaming likely did not start.");

            // Proxy data-arrival check: enabled-state should remain visible and the
            // graph's rectangle stayed non-empty across the dwell. A stronger
            // assertion needs an AutomationProperties.HelpText (or similar) bound
            // to the live point count - tracked under #531 follow-up.
            Assert.IsTrue(liveGraph.IsEnabled,
                "Live graph was disabled after streaming dwell; streaming likely did not start.");
            Assert.IsTrue(preStreamRect.Width > 0,
                "Pre-stream graph rectangle was empty; the control wasn't laid out before Start.");

            // ----- Stop streaming -----
            var stop = FindByAutomationId(mainWindow, cf, STOP_STREAMING_ID,
                "Stop-streaming command button.");
            stop.AsButton().Invoke();

            // ----- Disconnect -----
            var disconnect = FindByAutomationId(mainWindow, cf, DISCONNECT_BUTTON_ID,
                "Disconnect command button.");
            disconnect.AsButton().Invoke();

            WaitFor(() => deviceList.FindAllChildren().Length == 0, DEVICE_APPEAR_TIMEOUT,
                "Device was not removed from the connected list after disconnect.");
        }
        finally
        {
            UIAppLifecycle.CloseAppGracefully(app, SHUTDOWN_GRACE);
        }
    }

    private static bool IsBenchDeviceAvailable()
    {
        var raw = Environment.GetEnvironmentVariable(BENCH_AVAILABLE_ENV_VAR);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        return trimmed.Equals("1", StringComparison.OrdinalIgnoreCase)
               || trimmed.Equals("true", StringComparison.OrdinalIgnoreCase)
               || trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static AutomationElement FindByAutomationId(
        AutomationElement scope, ConditionFactory cf, string automationId, string description)
    {
        var element = scope.FindFirstDescendant(cf.ByAutomationId(automationId));
        Assert.IsNotNull(element,
            $"Could not find AutomationId '{automationId}' ({description}). " +
            "Add AutomationProperties.AutomationId to the XAML and reference issue #531.");
        return element!;
    }

    /// <summary>
    /// Polls the app's top-level windows for one whose title contains
    /// <paramref name="titleFragment"/> (case-insensitive). The ConnectionDialog
    /// is a separate <c>MetroWindow</c>, so descendants of the main window can't
    /// see it.
    /// </summary>
    private static Window WaitForTopLevelWindow(
        Application app, UIA3Automation automation, string titleFragment,
        TimeSpan timeout, string failureMessage)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? lastException = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var match = app.GetAllTopLevelWindows(automation)
                    .FirstOrDefault(w => (w.Title ?? string.Empty)
                        .Contains(titleFragment, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    return match;
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            Thread.Sleep(200);
        }

        var detail = lastException is null
            ? string.Empty
            : $" Last exception while enumerating top-level windows: {lastException.GetType().Name}: {lastException.Message}";
        Assert.Fail($"{failureMessage} (waited {timeout.TotalSeconds:F0}s).{detail}");
        throw new InvalidOperationException("unreachable; Assert.Fail throws.");
    }

    private static void WaitFor(Func<bool> condition, TimeSpan timeout, string failureMessage)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? lastException = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (condition()) return;
            }
            catch (Exception ex)
            {
                // Swallow but remember transient UIA errors while controls are still
                // spinning up; if we eventually time out, surface the last error so
                // the failure message points at the real root cause.
                lastException = ex;
            }
            Thread.Sleep(200);
        }

        var detail = lastException is null
            ? string.Empty
            : $" Last exception during polling: {lastException.GetType().Name}: {lastException.Message}";
        Assert.Fail($"{failureMessage} (waited {timeout.TotalSeconds:F0}s).{detail}");
    }
}
