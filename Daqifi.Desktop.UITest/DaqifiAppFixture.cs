using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
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
    private const string TRANSPORT_ENV_VAR = "DAQIFI_TEST_TRANSPORT";
    private const string APP_EXE_NAME = "DAQiFi.exe";
    private const string LOG_FILE_NAME = "DAQifiAppLog.log";

    // AutomationIds (set in Step 2) for the navigation + connection controls.
    private const string ADD_DEVICE_BUTTON_ID = "AddDeviceButton";
    private const string ADD_DEVICE_BUTTON_EMPTY_ID = "AddDeviceButtonEmpty";
    private const string CONNECTED_DEVICE_LIST_ID = "ConnectedDeviceList";
    private const string CONN_TAB_WIFI_ID = "ConnTab_Wifi";
    private const string CONN_TAB_SERIAL_ID = "ConnTab_Serial";
    private const string DISCOVERED_DEVICE_LIST_ID = "DiscoveredDeviceList";
    private const string SERIAL_PORT_LIST_ID = "SerialPortList";
    private const string CONNECT_BUTTON_WIFI_ID = "ConnectButton_Wifi";
    private const string CONNECT_BUTTON_SERIAL_ID = "ConnectButton_Serial";

    private const string CONNECTION_DIALOG_TITLE = "CONNECT DEVICE";
    private const string DEVICES_TAB_TEXT = "Devices";
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

    #region Navigation Helpers
    /// <summary>
    /// Selects a top-level navigation tab in the main window by its header text
    /// (e.g. "Devices", "Channels", "Live Graph", "Profiles"). The nav TabItems
    /// carry no AutomationId, so they are located by the header TextBlock text.
    /// </summary>
    protected void NavigateToTab(string headerText, int timeoutSeconds = 30)
    {
        var tabItem = Retry.WhileNull(
            () => FindNavTabItem(headerText),
            timeout: TimeSpan.FromSeconds(timeoutSeconds),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            timeoutMessage: $"Navigation tab '{headerText}' not found within {timeoutSeconds}s.").Result!;

        tabItem.Select();

        Retry.WhileFalse(
            () => tabItem.IsSelected,
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(200),
            throwOnTimeout: true,
            timeoutMessage: $"Navigation tab '{headerText}' did not become selected.");
    }

    private TabItem? FindNavTabItem(string headerText)
    {
        var tabItems = MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.TabItem));
        foreach (var element in tabItems)
        {
            var match = element.FindFirstDescendant(cf => cf.ByText(headerText));
            if (match != null)
            {
                return element.AsTabItem();
            }
        }

        return null;
    }
    #endregion

    #region Connect Helper
    /// <summary>
    /// Resolves the device transport for the run from the
    /// <c>DAQIFI_TEST_TRANSPORT</c> environment variable ("Wifi" or "Serial").
    /// Defaults to <see cref="DeviceTransport.Serial"/> when unset/unrecognized,
    /// since a USB-attached device is the most common bench configuration.
    /// </summary>
    protected static DeviceTransport ResolveTransport()
    {
        var raw = Environment.GetEnvironmentVariable(TRANSPORT_ENV_VAR);
        if (!string.IsNullOrWhiteSpace(raw)
            && Enum.TryParse<DeviceTransport>(raw.Trim(), ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return DeviceTransport.Serial;
    }

    /// <summary>
    /// Drives the full add-device workflow out-of-process: navigates to the Devices
    /// tab, opens the connection dialog, selects the transport tab, waits for real
    /// discovery to populate, selects the first discovered device, and connects.
    /// Returns once the dialog has closed and at least one device tile is present.
    /// Reusable by later scenarios that need a connected device as a precondition.
    /// </summary>
    /// <param name="transport">WiFi or Serial transport to use.</param>
    /// <param name="discoveryTimeoutSeconds">How long to wait for a device to appear.</param>
    protected void ConnectFirstDevice(DeviceTransport transport, int discoveryTimeoutSeconds = 60)
    {
        NavigateToTab(DEVICES_TAB_TEXT);

        OpenConnectionDialog();
        var dialog = WaitForConnectionDialog();

        var (tabId, listId, connectButtonId) = transport switch
        {
            DeviceTransport.Wifi =>
                (CONN_TAB_WIFI_ID, DISCOVERED_DEVICE_LIST_ID, CONNECT_BUTTON_WIFI_ID),
            _ =>
                (CONN_TAB_SERIAL_ID, SERIAL_PORT_LIST_ID, CONNECT_BUTTON_SERIAL_ID)
        };

        // Select the transport tab inside the dialog.
        var transportTab = Retry.WhileNull(
            () => dialog.FindFirstDescendant(cf => cf.ByAutomationId(tabId)),
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            timeoutMessage: $"Transport tab '{tabId}' not found in connection dialog.").Result!;
        transportTab.AsTabItem().Select();

        // Wait for real discovery latency: the list must gain at least one item.
        var list = Retry.WhileNull(
            () => dialog.FindFirstDescendant(cf => cf.ByAutomationId(listId)),
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            timeoutMessage: $"Device list '{listId}' not found in connection dialog.").Result!;

        var listBox = list.AsListBox();
        Retry.WhileEmpty(
            () => listBox.Items,
            timeout: TimeSpan.FromSeconds(discoveryTimeoutSeconds),
            interval: TimeSpan.FromMilliseconds(500),
            throwOnTimeout: true,
            timeoutMessage:
                $"No device discovered on {transport} within {discoveryTimeoutSeconds}s. " +
                "Ensure a DAQiFi device is physically attached and reachable.");

        // Select the first discovered device.
        var firstItem = listBox.Items[0];
        firstItem.Select();
        Retry.WhileFalse(
            () => firstItem.IsSelected,
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(200),
            throwOnTimeout: true,
            timeoutMessage: "First discovered device did not become selected.");

        // Connect.
        var connectButton = Retry.WhileNull(
            () => dialog.FindFirstDescendant(cf => cf.ByAutomationId(connectButtonId)),
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            timeoutMessage: $"Connect button '{connectButtonId}' not found in connection dialog.").Result!;
        var connectBtn = connectButton.AsButton();
        connectBtn.WaitUntilEnabled(TimeSpan.FromSeconds(10));
        connectBtn.Invoke();

        // Dialog closes once the connection completes.
        Retry.WhileTrue(
            () => dialog.IsAvailable,
            timeout: TimeSpan.FromSeconds(60),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            timeoutMessage: "Connection dialog did not close after Connect was invoked.");

        // A device tile must appear in the connected-devices container.
        WaitForConnectedDeviceCount(1, TimeSpan.FromSeconds(30));
    }

    /// <summary>Opens the connection dialog via the Add Device button (status bar or empty-state).</summary>
    private void OpenConnectionDialog()
    {
        var addButton = Retry.WhileNull(
            () => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(ADD_DEVICE_BUTTON_ID))
                  ?? MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(ADD_DEVICE_BUTTON_EMPTY_ID)),
            timeout: TimeSpan.FromSeconds(30),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            timeoutMessage: "Add Device button not found on the Devices pane.").Result!;

        var button = addButton.AsButton();
        button.WaitUntilEnabled(TimeSpan.FromSeconds(10));
        button.Invoke();
    }

    /// <summary>Waits for the modal connection dialog window to appear.</summary>
    protected Window WaitForConnectionDialog(int timeoutSeconds = 30)
    {
        return Retry.WhileNull(
            () => App.GetAllTopLevelWindows(Automation)
                     .FirstOrDefault(w => string.Equals(
                         w.Title, CONNECTION_DIALOG_TITLE, StringComparison.OrdinalIgnoreCase)),
            timeout: TimeSpan.FromSeconds(timeoutSeconds),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            timeoutMessage: $"Connection dialog ('{CONNECTION_DIALOG_TITLE}') did not open.").Result!;
    }

    /// <summary>Reads the number of device tiles currently in the connected-devices container.</summary>
    protected int GetConnectedDeviceCount()
    {
        var container = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(CONNECTED_DEVICE_LIST_ID));
        if (container == null)
        {
            return 0;
        }

        // ConnectedDeviceList is an ItemsControl; its generated item containers are
        // its direct children. Count children that look like data items.
        return container.FindAllChildren().Length;
    }

    /// <summary>Waits until the connected-devices container holds at least <paramref name="minimum"/> tiles.</summary>
    protected void WaitForConnectedDeviceCount(int minimum, TimeSpan timeout)
    {
        Retry.WhileFalse(
            () => GetConnectedDeviceCount() >= minimum,
            timeout: timeout,
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            timeoutMessage:
                $"Expected at least {minimum} connected device tile(s) but found fewer within {timeout.TotalSeconds}s.");
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
