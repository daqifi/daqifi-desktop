using System;
using System.Globalization;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daqifi.Desktop.UITest;

/// <summary>
/// Scenario — device disconnect + reconnect lifecycle (issue #559). Drives the real GUI
/// out-of-process to prove a <b>clean disconnect</b> and a <b>deterministic reconnect</b> in one
/// self-contained pass against the physically attached device.
///
/// Two cycles, each: connect → enable channels → run a brief logging session (start, prove the
/// live plot streams real data, stop) → disconnect via the device drawer's DISCONNECT button. After
/// each disconnect it asserts a <b>clean teardown</b> through the visible UI alone — the device
/// leaves <c>ConnectedDeviceList</c>, its channels disappear from the Channels pane (subscriptions
/// torn down), and the logging controls fall back to the disabled state (<c>CanToggleLogging</c>
/// goes false once <c>ActiveChannels</c> empties) — plus a black-box negative log check that the
/// app did not log a disconnect failure.
///
/// The second cycle is the payload: it proves the same device, after a full disconnect, <b>reconnects
/// and is fully usable again</b> — channels re-enable and a second logging session starts, streams,
/// and stops exactly like the first, with no leaked state (un-disposed transports, dangling channel
/// subscriptions, or stale connection status) degrading it. A final disconnect leaves no lingering
/// state, and the test self-cleans the sessions it created so the persistent test-mode DB returns to
/// its pre-test baseline. All assertions read visible/accessible UI plus the NLog log — never app
/// internals. Requires a DAQiFi device.
/// </summary>
[TestClass]
public class ConnectionLifecycleTests : DaqifiAppFixture
{
    #region Constants
    // A gentle frequency keeps the UI responsive for out-of-process automation while a session
    // streams (mirrors LoggingSessionTests; the 1000 Hz case is covered by ConfigureLoggingTests).
    private const double TARGET_FREQUENCY_HZ = 100d;

    // Upper bounds for POLLING waits (Retry) — they return as soon as the signal is observed, so a
    // generous ceiling costs nothing on the happy path while riding out real device/UI latency.
    private static readonly TimeSpan PlotGrowthTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan SessionAppearTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StatusLabelTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan TeardownTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(20);

    // Safety bound on the cleanup delete-loop so a UI hiccup can never spin forever.
    private const int MAX_CLEANUP_DELETES = 12;
    #endregion

    /// <summary>
    /// Runs two connect → active → disconnect cycles against the attached device: it asserts a
    /// clean teardown after each disconnect (device leaves the connected list, channels clear,
    /// logging controls disable, no disconnect error logged) and proves the same device, after a
    /// full disconnect, reconnects and is fully usable again (a second logging session starts,
    /// streams, and stops just like the first). Self-cleans the sessions it created. See the class
    /// summary for the full scenario rationale. Requires a DAQiFi device.
    /// </summary>
    [TestMethod]
    [TestCategory("Ui")]
    [TestCategory("RequiresDevice")]
    public void DisconnectThenReconnect_TearsDownCleanly_AndDeviceIsUsableAgain()
    {
        var transport = ResolveTransport();

        // Clean slate: no device connected yet, and a recorded session baseline so we can both
        // prove each cycle persists a session and return the DB to baseline at the end.
        Assert.AreEqual(
            0, GetConnectedDeviceCount(), "Expected no connected devices at the start of the test.");
        var baselineSessions = GetLoggedSessionCount();

        // ── Cycle 1: connect, drive the device into an active streaming state, then disconnect ──
        RunBriefLoggingSessionOnFreshConnection(transport, "first");

        DisconnectSelectedDevice();
        AssertCleanDisconnect("first");

        // ── Cycle 2: reconnect the SAME device and prove it is fully usable again ──
        // A second session that starts/streams/stops just like the first is the proof that the
        // disconnect left no leaked state (un-disposed transport, dangling subscriptions, stale
        // ConnectionStatus) that would otherwise bite on re-establish.
        RunBriefLoggingSessionOnFreshConnection(transport, "second");

        // Final disconnect — must again leave no lingering state.
        DisconnectSelectedDevice();
        AssertCleanDisconnect("second");

        // Self-clean: remove the sessions this test created so the persistent test-mode DB returns
        // to its pre-test baseline (no per-run leak). Done after the lifecycle assertions so a
        // delete hiccup can never mask the real subject of this test.
        CleanUpLoggedSessionsTo(baselineSessions);

        CaptureScreenshot("DisconnectThenReconnect_final");

        // Per-test independence: the base fixture's [TestCleanup] closes the app. A fresh app
        // instance is launched per test.
    }

    /// <summary>
    /// Connects to the attached device on a fresh connection, enables the analog channels, and runs
    /// a brief logging session — asserting the device is genuinely active and usable: the logging
    /// controls enable, the session starts (label reads "LOGGING ON"), the live plot streams real
    /// data (rendered point count grows), the session stops cleanly (label reads "LOGGING OFF"),
    /// and a new logged-session row is persisted (empty sessions are discarded, so a new row is
    /// out-of-process proof that samples accrued). <paramref name="label"/> names the connection
    /// ("first"/"second") in assertion messages.
    /// </summary>
    private void RunBriefLoggingSessionOnFreshConnection(DeviceTransport transport, string label)
    {
        ConnectFirstDevice(transport);
        Assert.AreEqual(
            1, GetConnectedDeviceCount(),
            $"Expected exactly one connected device on the {label} connection.");

        // Record the session count for THIS connection so the +1 delta proves this run accrued data
        // (in cycle 2 the cycle-1 session is still present, so an absolute baseline would not work).
        var sessionsBefore = GetLoggedSessionCount();

        // A gentle, known frequency keeps the UI responsive while the plot streams during the
        // out-of-process polling below — independent of whatever rate the freshly re-discovered
        // device defaults to. Set it before enabling channels (the toggle gates on active channels).
        SetSamplingFrequency(TARGET_FREQUENCY_HZ);

        var activeChannels = EnableAllAnalogChannels();
        Assert.IsTrue(
            activeChannels > 0,
            $"No analog channels became active on the {label} connection.");

        // With channels subscribed, the logging controls must be usable (CanToggleLogging true).
        // This is the "before" half of the disconnect assertion that the controls later disable.
        Assert.IsTrue(
            IsLoggingToggleEnabled(),
            $"The logging toggle was not enabled despite {activeChannels} active channel(s) on the " +
            $"{label} connection — the controls were not usable.");

        // Start → assert the user-visible label (not just the toggle) flips, then prove the live
        // plot is actually streaming data (point count grows) — the device is genuinely active.
        StartLogging();
        WaitForLoggingStatusLabel("LOGGING ON", StatusLabelTimeout);
        WaitForPlotPointGrowth(PlotGrowthTimeout);

        // Stop → assert the label flips back, proving the session ended cleanly.
        StopLogging();
        WaitForLoggingStatusLabel("LOGGING OFF", StatusLabelTimeout);

        // A new logged-session row proves the session ran and accrued data on this connection.
        WaitForLoggedSessionCount(sessionsBefore + 1, SessionAppearTimeout);
        var sessionsAfter = GetLoggedSessionCount();
        Assert.IsTrue(
            sessionsAfter > sessionsBefore,
            $"Expected a new logged session after the {label} run (before={sessionsBefore}, " +
            $"after={sessionsAfter}). No new session row means the session did not accrue data — " +
            "the device was not fully usable on this connection.");
    }

    /// <summary>
    /// Asserts the just-performed disconnect tore the device down cleanly, reading only visible UI
    /// plus the NLog log: the device left <c>ConnectedDeviceList</c>, its channels disappeared from
    /// the Channels pane (subscriptions torn down), the logging controls fell back to disabled
    /// (<c>CanToggleLogging</c> false once <c>ActiveChannels</c> emptied), and the app logged no
    /// disconnect failure. <paramref name="label"/> names the disconnect ("first"/"second").
    /// </summary>
    private void AssertCleanDisconnect(string label)
    {
        WaitForNoConnectedDevices(TeardownTimeout);
        WaitForChannelsCleared(TeardownTimeout);
        WaitForLoggingToggleDisabled(TeardownTimeout);

        // Black-box negative check: ConnectionManager.Disconnect logs "Failed in Disconnect" only
        // when device.Disconnect()/transport teardown throws. Its absence confirms a clean teardown
        // (the UI signals above prove the device/channels/controls reset; this guards the seam where
        // an un-disposed transport would surface).
        Assert.IsFalse(
            ReadNewLogText().Contains("Failed in Disconnect", StringComparison.Ordinal),
            $"The app logged 'Failed in Disconnect' during/after the {label} disconnect — the device " +
            "teardown (transport disposal) threw, so the disconnect was not clean.");
    }

    /// <summary>
    /// Deletes the logged sessions this test created (newest-first via the per-row DELETE action,
    /// accepting the in-pane confirm overlay) until the session count returns to
    /// <paramref name="baseline"/>, then asserts it. Keeps the persistent test-mode DB from leaking
    /// one session per cycle. The delete loop is bounded so a UI hiccup cannot spin forever.
    /// </summary>
    private void CleanUpLoggedSessionsTo(int baseline)
    {
        var deletes = 0;
        while (GetLoggedSessionCount() > baseline && deletes++ < MAX_CLEANUP_DELETES)
        {
            DeleteNewestLoggedSession();
        }

        WaitForExactLoggedSessionCount(baseline, CleanupTimeout);
    }

    /// <summary>
    /// Saves a screenshot of the main window into the test results directory and registers it as a
    /// result file. Best-effort; never throws into the test.
    /// </summary>
    private void CaptureScreenshot(string name)
    {
        try
        {
            var outDir = TestContext?.TestResultsDirectory ?? AppContext.BaseDirectory;
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var path = Path.Combine(outDir, $"{name}_{stamp}.png");
            FlaUI.Core.Capturing.Capture.Element(MainWindow).ToFile(path);
            TestContext?.AddResultFile(path);
        }
        catch
        {
            // Screenshot capture must not affect the test outcome.
        }
    }
}
