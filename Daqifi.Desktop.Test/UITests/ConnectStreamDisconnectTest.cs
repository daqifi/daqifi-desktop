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
/// against a real DAQiFi device. This test is intentionally [Ignore]'d until a
/// device is wired to the bench so a normal CI run never touches it; remove the
/// Ignore attribute (or run the "UI-Bench" category explicitly) once:
///
///   1. A DAQiFi Nyquist is attached via USB (or reachable via Wi-Fi).
///   2. The required XAML controls have been annotated with the AutomationIds
///      referenced below. Each Id has a comment in the XAML pointing back to
///      this test + issue #531 for traceability.
///   3. The desktop app has been built (Phase 1 verifies the launch path).
///
/// Naming convention used for AutomationIds: "Daqifi.&lt;Pane&gt;.&lt;Control&gt;",
/// e.g. "Daqifi.Connection.AddDeviceButton". Keeping a stable, dotted namespace
/// makes future selectors greppable in the XAML.
/// </summary>
[TestClass]
[Ignore("Phase 2 - needs bench device. See issue #531; remove [Ignore] when a Nyquist is on the bench and XAML AutomationIds are wired up.")]
public class ConnectStreamDisconnectTest
{
    private const string DesktopExeName = "DAQiFi.exe";
    private const string DesktopProjectName = "Daqifi.Desktop";

    // AutomationIds that Phase 2 expects to exist in MainWindow / dialogs.
    // None of these are wired up yet; add them in the corresponding XAML with
    // a comment referencing this test + #531 when enabling the test.
    private const string AddDeviceButtonId    = "Daqifi.Connection.AddDeviceButton";
    private const string ConnectButtonId      = "Daqifi.Connection.ConnectButton";
    private const string DeviceListId         = "Daqifi.Devices.ConnectedList";
    private const string FirstChannelToggleId = "Daqifi.Channels.FirstChannelEnable";
    private const string StartStreamingId     = "Daqifi.Streaming.StartButton";
    private const string StopStreamingId      = "Daqifi.Streaming.StopButton";
    private const string DisconnectButtonId   = "Daqifi.Connection.DisconnectButton";
    private const string LiveGraphId          = "Daqifi.Graph.Live";

    private static readonly TimeSpan MainWindowTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DeviceAppearTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StreamingDwellTime = TimeSpan.FromSeconds(3);

    [TestMethod]
    [TestCategory("UI-Bench")]
    public void ConnectStreamDisconnect_HappyPath()
    {
        var exePath = TryLocateDesktopExe();
        if (exePath is null)
        {
            Assert.Inconclusive($"Skipped: {DesktopExeName} was not found. Build the {DesktopProjectName} project first.");
        }

        Application? app = null;
        try
        {
            app = Application.Launch(exePath!);
            using var automation = new UIA3Automation();
            var mainWindow = app.GetMainWindow(automation, MainWindowTimeout);
            Assert.IsNotNull(mainWindow, "Main window did not appear.");
            var cf = automation.ConditionFactory;

            // ----- Connect -----
            var addDevice = FindByAutomationId(mainWindow, cf, AddDeviceButtonId,
                "Add-device entry point (USB/Serial picker).");
            addDevice.AsButton().Invoke();

            var connect = FindByAutomationId(mainWindow, cf, ConnectButtonId,
                "Confirm button on the connection dialog.");
            connect.AsButton().Invoke();

            // Wait for the connected-devices list to show at least one row.
            var deviceList = FindByAutomationId(mainWindow, cf, DeviceListId,
                "Connected-devices list container.");
            WaitFor(() => deviceList.FindAllChildren().Length > 0, DeviceAppearTimeout,
                "Device did not appear in the connected list.");

            // ----- Enable first channel -----
            var firstChannel = FindByAutomationId(mainWindow, cf, FirstChannelToggleId,
                "Enable-toggle on the first analog channel.");
            // Toggle controls in MahApps are typically ToggleButtons; click via Invoke.
            firstChannel.AsToggleButton().Toggle();

            // ----- Start streaming, dwell, check graph has data -----
            var start = FindByAutomationId(mainWindow, cf, StartStreamingId,
                "Start-streaming command button.");
            start.AsButton().Invoke();

            Thread.Sleep(StreamingDwellTime);

            var liveGraph = FindByAutomationId(mainWindow, cf, LiveGraphId,
                "Live graph control. Should contain non-zero point count after dwell.");

            // OxyPlot / LiveCharts surfaces don't expose data points through UIA, so
            // we settle for proof-of-life: the control exists, is visible, and has
            // a non-trivial bounding rectangle. Strengthen this once we know which
            // graph library is in use (search MainWindow.xaml for oxy:/lvc:).
            Assert.IsFalse(liveGraph.IsOffscreen, "Live graph was offscreen after Start.");
            Assert.IsTrue(liveGraph.BoundingRectangle.Width > 0
                          && liveGraph.BoundingRectangle.Height > 0,
                "Live graph had zero-sized bounding box; streaming likely did not start.");

            // ----- Stop streaming -----
            var stop = FindByAutomationId(mainWindow, cf, StopStreamingId,
                "Stop-streaming command button.");
            stop.AsButton().Invoke();

            // ----- Disconnect -----
            var disconnect = FindByAutomationId(mainWindow, cf, DisconnectButtonId,
                "Disconnect command button.");
            disconnect.AsButton().Invoke();

            WaitFor(() => deviceList.FindAllChildren().Length == 0, DeviceAppearTimeout,
                "Device was not removed from the connected list after disconnect.");
        }
        finally
        {
            if (app is not null)
            {
                try
                {
                    app.Close();
                    // Give the app up to 5s to shut down gracefully before forcing it,
                    // matching the Phase 1 smoke-test teardown.
                    app.WaitWhileMainHandleIsMissing(TimeSpan.FromSeconds(5));
                    if (!app.HasExited) app.Kill();
                }
                catch { /* best-effort */ }
                finally { app.Dispose(); }
            }
        }
    }

    private static AutomationElement FindByAutomationId(
        Window window, ConditionFactory cf, string automationId, string description)
    {
        var element = window.FindFirstDescendant(cf.ByAutomationId(automationId));
        Assert.IsNotNull(element,
            $"Could not find AutomationId '{automationId}' ({description}). " +
            "Add AutomationProperties.AutomationId to the XAML and reference issue #531.");
        return element!;
    }

    private static void WaitFor(Func<bool> condition, TimeSpan timeout, string failureMessage)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (condition()) return;
            }
            catch
            {
                // Swallow transient UIA errors while controls are still spinning up.
            }
            Thread.Sleep(200);
        }
        Assert.Fail(failureMessage + $" (waited {timeout.TotalSeconds:F0}s)");
    }

    private static string? TryLocateDesktopExe()
    {
        var testAssemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                              ?? Directory.GetCurrentDirectory();
        var repoRoot = Path.GetFullPath(Path.Combine(testAssemblyDir, "..", "..", "..", ".."));
        var configs = new[] { "Debug", "Release" };
        var tfms = new[] { "net10.0-windows", "net9.0-windows" };
        return (from c in configs
                from t in tfms
                let p = Path.Combine(repoRoot, DesktopProjectName, "bin", c, t, DesktopExeName)
                where File.Exists(p)
                select p).FirstOrDefault();
    }
}
