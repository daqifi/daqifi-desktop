using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
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
    private static readonly TimeSpan TOGGLE_PROPAGATION_TIMEOUT = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan STREAMING_DWELL_TIME = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SHUTDOWN_GRACE = TimeSpan.FromSeconds(5);

    [TestMethod]
    [TestCategory("UI-Bench")]
    public void ConnectStreamDisconnectHappyPath()
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
            app = UIAppLifecycle.LaunchOrInconclusive(exePath);

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
            // Re-find the list inside the predicate each poll: UI Automation
            // elements can become stale across major tree transitions (e.g.
            // when the ConnectionDialog closes), and a cached element that's
            // gone stale will keep throwing inside WaitFor until timeout.
            //
            // If the device never appears within the timeout, treat it as
            // "bench device not actually discoverable" and skip
            // (Assert.Inconclusive) rather than fail - the env-var gate told
            // us a device was *expected*, but the connect path can still no-op
            // when the hardware is powered off or the cable is unplugged.
            // Phase 2 must skip-on-unavailable per the #531 compliance bar
            // (Qodo review #1).
            WaitForOrInconclusive(
                () => FindListItems(mainWindow, cf, DEVICE_LIST_ID).ItemCount > 0,
                DEVICE_APPEAR_TIMEOUT,
                "Device did not appear in the connected list within "
                + $"{DEVICE_APPEAR_TIMEOUT.TotalSeconds:F0}s. The "
                + $"{BENCH_AVAILABLE_ENV_VAR} env var is set but no device was "
                + "discovered - check the USB connection / power state.");

            // ----- Enable first channel -----
            // Set the toggle deterministically to the 'On' state rather than
            // unconditionally inverting it. If the channel was already enabled
            // (e.g., from persisted UI state or a prior run on the bench),
            // a blind Toggle() would disable it and the rest of the flow
            // (Start streaming, graph proof-of-life, Disconnect) would fail
            // or observe no data.
            var firstChannel = FindByAutomationId(mainWindow, cf, FIRST_CHANNEL_TOGGLE_ID,
                "Enable-toggle on the first analog channel.");
            var firstChannelToggle = firstChannel.AsToggleButton();
            if (firstChannelToggle.ToggleState != ToggleState.On)
            {
                firstChannelToggle.Toggle();

                // Wait for the toggle state change to propagate through the
                // WPF data-binding before asserting downstream stream behavior.
                // Re-find the element each poll so we don't rely on a possibly
                // stale AutomationElement reference after the UI updates.
                WaitFor(
                    () => FindByAutomationId(mainWindow, cf, FIRST_CHANNEL_TOGGLE_ID,
                            "First-channel toggle (post-toggle).")
                        .AsToggleButton()
                        .ToggleState == ToggleState.On,
                    TOGGLE_PROPAGATION_TIMEOUT,
                    "First channel did not reach the 'On' state after toggling.");
            }

            // ----- Capture graph baseline, start streaming, check for update -----
            // Capture graph geometry BEFORE invoking Start so we have a
            // baseline to diff against. UI Automation can't see OxyPlot /
            // LiveCharts plot data directly, so the best UIA-only proxy for
            // "data is arriving" is "the live graph's bounding rectangle
            // changed after streaming started". Once the XAML grows an
            // automation-visible point-count probe (tracked under #531) this
            // can become a hard assertion.
            //
            // The baseline MUST be captured before invoking Start: otherwise
            // the WPF data-binding could have already pushed the first sample
            // by the time we read the rectangle, and the post-stream diff
            // would compare against an already-updated baseline (false-fail).
            var liveGraph = FindByAutomationId(mainWindow, cf, LIVE_GRAPH_ID,
                "Live graph control. Should visibly update after streaming starts.");
            Assert.IsTrue(liveGraph.BoundingRectangle.Width > 0,
                "Pre-stream graph rectangle was empty; the control wasn't laid out before Start.");
            var preStreamRect = liveGraph.BoundingRectangle;

            var start = FindByAutomationId(mainWindow, cf, START_STREAMING_ID,
                "Start-streaming command button.");
            start.AsButton().Invoke();

            // Poll-and-detect rather than fixed-sleep: WaitFor returns as soon
            // as we observe a visible change to the graph (and skips the
            // remaining dwell), but if nothing has changed by the deadline we
            // still spent the same wall-clock budget as the old Thread.Sleep
            // and get a precise failure message ("did not visibly update").
            WaitFor(
                () =>
                {
                    var graph = FindByAutomationId(mainWindow, cf, LIVE_GRAPH_ID,
                        "Live graph control (streaming-update poll).");
                    var rect = graph.BoundingRectangle;
                    return !graph.IsOffscreen
                           && graph.IsEnabled
                           && rect.Width > 0
                           && rect.Height > 0
                           && !rect.Equals(preStreamRect);
                },
                STREAMING_DWELL_TIME,
                "Live graph did not visibly update during the streaming dwell; "
                + "streaming may not have started or data is not reaching the plot.");

            // ----- Stop streaming -----
            var stop = FindByAutomationId(mainWindow, cf, STOP_STREAMING_ID,
                "Stop-streaming command button.");
            stop.AsButton().Invoke();

            // ----- Disconnect -----
            var disconnect = FindByAutomationId(mainWindow, cf, DISCONNECT_BUTTON_ID,
                "Disconnect command button.");
            disconnect.AsButton().Invoke();

            // Require BOTH that the list element still exists AND that its
            // data-item count is zero. Without the ListFound guard, an
            // AutomationId regression (or a transient UIA-tree drop) would
            // make `ItemCount == 0` pass for the wrong reason: "the list is
            // gone" looks identical to "the list is empty" (Qodo review #8).
            WaitFor(
                () =>
                {
                    var snap = FindListItems(mainWindow, cf, DEVICE_LIST_ID);
                    return snap.ListFound && snap.ItemCount == 0;
                },
                DEVICE_APPEAR_TIMEOUT,
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
    /// Result of looking up a list container by AutomationId and counting its
    /// data rows. <see cref="ListFound"/> distinguishes "list element missing"
    /// from "list found and empty" - a disconnect predicate that just checked
    /// for "0 children" would otherwise produce a false-positive pass when
    /// the AutomationId regressed (Qodo review #8).
    /// </summary>
    private readonly record struct ListItemSnapshot(bool ListFound, int ItemCount);

    /// <summary>
    /// Re-finds the list element by AutomationId and counts its data rows.
    /// Always re-locates the parent on each call so a stale AutomationElement
    /// from a prior UI-tree refresh can't poison a polling loop.
    ///
    /// Counts use UI Automation patterns first (Grid.RowCount or
    /// ListBox.Items.Length) because WPF UI virtualization can leave rows
    /// unrealized (offscreen list items are not present as descendants in the
    /// UIA tree until they scroll into view) - so counting realized
    /// descendants alone would report 0 even when the list has items. The
    /// realized-descendant fallback (ListItem / DataItem) covers the case
    /// where the control isn't a ListBox / Grid but still exposes its items
    /// directly.
    /// </summary>
    private static ListItemSnapshot FindListItems(
        AutomationElement scope, ConditionFactory cf, string automationId)
    {
        var element = scope.FindFirstDescendant(cf.ByAutomationId(automationId));
        if (element is null)
        {
            return new ListItemSnapshot(ListFound: false, ItemCount: 0);
        }

        // Prefer GridPattern.RowCount when available - it reports the logical
        // row count regardless of virtualization. WPF DataGrids commonly count
        // their header band as a row in GridPattern; if a Header descendant
        // exists, subtract one so an empty grid can reach ItemCount=0
        // (otherwise the disconnect predicate would never see 0 and would
        // time out).
        var gridPattern = element.Patterns.Grid.PatternOrDefault;
        if (gridPattern is not null)
        {
            // GridPattern.RowCount is AutomationProperty<int>; .Value reads
            // the actual count via UIA.
            var rowCount = gridPattern.RowCount.Value;
            var header = element.FindFirstDescendant(cf.ByControlType(ControlType.Header));
            if (header is not null && rowCount > 0)
            {
                rowCount--;
            }
            return new ListItemSnapshot(ListFound: true, ItemCount: Math.Max(0, rowCount));
        }

        // ListBox.Items.Length is similarly virtualization-safe; AsListBox
        // throws if the element isn't a ListBox, so guard with try/catch
        // rather than a fragile control-type sniff.
        try
        {
            var listBox = element.AsListBox();
            return new ListItemSnapshot(ListFound: true, ItemCount: listBox.Items.Length);
        }
        catch
        {
            // Fall through to the realized-descendant fallback.
        }

        // Last-resort fallback: count realized ListItem / DataItem descendants.
        // This loses accuracy under virtualization but works for non-virtualized
        // ItemsControls and DataGrids.
        var listItems = element.FindAllDescendants(cf.ByControlType(ControlType.ListItem));
        if (listItems.Length > 0)
        {
            return new ListItemSnapshot(ListFound: true, ItemCount: listItems.Length);
        }

        var dataItems = element.FindAllDescendants(cf.ByControlType(ControlType.DataItem));
        return new ListItemSnapshot(ListFound: true, ItemCount: dataItems.Length);
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

        Assert.Fail(BuildTimeoutMessage(failureMessage, timeout,
            "enumerating top-level windows", lastException));
        throw new InvalidOperationException("unreachable; Assert.Fail throws.");
    }

    private static void WaitFor(Func<bool> condition, TimeSpan timeout, string failureMessage)
    {
        if (TryPoll(condition, timeout, out var lastException))
        {
            return;
        }

        Assert.Fail(BuildTimeoutMessage(failureMessage, timeout,
            "polling", lastException));
    }

    /// <summary>
    /// Same poll-then-give-up shape as <see cref="WaitFor"/>, but reports
    /// timeout via <see cref="Assert.Inconclusive(string)"/> rather than
    /// <see cref="Assert.Fail(string)"/>. Used when "condition didn't become
    /// true" means "the bench device isn't actually available" (the env-var
    /// gate says we should run, but the hardware turned out to be absent /
    /// powered off / unplugged) - which the #531 compliance bar requires to
    /// be a skip, not a failure.
    /// </summary>
    private static void WaitForOrInconclusive(
        Func<bool> condition, TimeSpan timeout, string failureMessage)
    {
        if (TryPoll(condition, timeout, out var lastException))
        {
            return;
        }

        Assert.Inconclusive(BuildTimeoutMessage(failureMessage, timeout,
            "polling", lastException));
    }

    /// <summary>
    /// Shared poll loop for <see cref="WaitFor"/> and
    /// <see cref="WaitForOrInconclusive"/>. Returns <c>true</c> when
    /// <paramref name="condition"/> evaluated to true before the deadline;
    /// returns <c>false</c> on timeout, with the last observed exception (if
    /// any) emitted via <paramref name="lastException"/> so the caller can
    /// surface it.
    /// </summary>
    private static bool TryPoll(
        Func<bool> condition, TimeSpan timeout, out Exception? lastException)
    {
        lastException = null;
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (condition())
                {
                    return true;
                }
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

        return false;
    }

    /// <summary>
    /// Builds a uniform "timed out" failure message that appends the last
    /// observed exception (if any). Kept under the 120-column limit by
    /// constructing the exception suffix on its own line.
    /// </summary>
    private static string BuildTimeoutMessage(
        string failureMessage, TimeSpan timeout, string context, Exception? lastException)
    {
        var suffix = lastException is null
            ? string.Empty
            : $" Last exception during {context}: "
              + $"{lastException.GetType().Name}: {lastException.Message}";
        return $"{failureMessage} (waited {timeout.TotalSeconds:F0}s).{suffix}";
    }
}
