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
    private const string MAIN_WINDOW_CLASS = "MetroWindow";

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

    // AutomationIds for the logging-configuration controls. Sampling frequency is set
    // in the per-device settings flyout (opened by the gear icon on the device tile),
    // not on the Profiles pane; channels are toggled on the Channels pane.
    private const string DEVICE_SETTINGS_BUTTON_ID = "DeviceSettingsButton";
    private const string SAMPLING_FREQUENCY_INPUT_ID = "SamplingFrequencyInput";
    private const string CHANNEL_LIST_ID = "ChannelList";
    private const string SELECT_ALL_ANALOG_ID = "SelectAllAnalogChannels";

    // AutomationIds for the logging-session controls (StartLoggingToggle/LoggingStatusText
    // from Step 2; LoggedSessionList added in Step 5 on the Logged Data pane).
    private const string START_LOGGING_TOGGLE_ID = "StartLoggingToggle";
    private const string LOGGING_STATUS_TEXT_ID = "LoggingStatusText";
    private const string LOGGED_SESSION_LIST_ID = "LoggedSessionList";

    // AutomationIds for SD-card logging mode. The logging-mode selector lives in the
    // per-device settings drawer (gear icon); the SD-card DATA FORMAT selector is
    // rendered there only while in "Log to Device" mode, so its presence is an
    // independent confirmation that the mode switch took effect. The SD file list and
    // status line live on the Logged Data pane's DEVICE LOGS sub-tab.
    private const string LOGGING_MODE_STREAM_ID = "LoggingModeStreamToApp";
    private const string LOGGING_MODE_SDCARD_ID = "LoggingModeLogToDevice";
    private const string SDCARD_FORMAT_SELECTOR_ID = "SdCardFormatSelector";
    private const string DEVICE_LOGS_TAB_ID = "DeviceLogsTab";
    private const string REFRESH_SDCARD_FILES_BUTTON_ID = "RefreshSdCardFilesButton";
    private const string SDCARD_STATUS_TEXT_ID = "SdCardStatusText";
    private const string SDCARD_FILE_LIST_ID = "SdCardFileList";

    private const string LOGGING_ON_TEXT = "LOGGING ON";
    private const string LOGGING_OFF_TEXT = "LOGGING OFF";

    private const string CONNECTION_DIALOG_TITLE = "CONNECT DEVICE";
    private const string DEVICES_TAB_TEXT = "Devices";
    private const string CHANNELS_TAB_TEXT = "Channels";
    private const string PROFILES_TAB_TEXT = "Profiles";
    private const string LIVE_GRAPH_TAB_TEXT = "Live Graph";
    private const string LOGGED_DATA_TAB_TEXT = "Logged Data";
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

        // The app shows a WPF SplashScreen window first, then the real MetroWindow.
        // App.GetMainWindow returns whatever owns the process MainWindowHandle at the
        // moment it is called, which during startup is the splash screen — searching
        // that for tabs/controls finds nothing. Wait specifically for the MetroWindow.
        // ignoreException swallows transient UIA COM timeouts during first-run EF
        // migration (the synchronous migrate briefly stops the UI message pump).
        MainWindow = Retry.WhileNull(
            () =>
            {
                foreach (var window in App.GetAllTopLevelWindows(Automation))
                {
                    if (window.ClassName == MAIN_WINDOW_CLASS)
                    {
                        return window;
                    }
                }

                return null;
            },
            timeout: TimeSpan.FromSeconds(60),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage: "Main MetroWindow did not appear within 60 seconds.").Result!;
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
        // Re-resolve and re-select until the tab reports selected. While a logging
        // session is streaming, the UI is busy and a single Select() COM call can
        // transiently fail (ElementNotAvailable / 0x80040201); retrying rides it out.
        Retry.WhileFalse(
            () =>
            {
                var tabItem = FindNavTabItem(headerText);
                if (tabItem == null)
                {
                    return false;
                }

                if (tabItem.Patterns.SelectionItem.Pattern.IsSelected.Value)
                {
                    return true;
                }

                tabItem.Select();
                return tabItem.Patterns.SelectionItem.Pattern.IsSelected.Value;
            },
            timeout: TimeSpan.FromSeconds(timeoutSeconds),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage: $"Navigation tab '{headerText}' could not be selected within {timeoutSeconds}s.");
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

        // Success is signalled by a device appearing in the connected-devices container.
        // (The modal dialog does not reliably auto-close under UI automation even though
        // it does during interactive use, so we assert on the connection itself.)
        WaitForConnectedDeviceCount(1, TimeSpan.FromSeconds(60));

        // Close the dialog if it is still open so it does not block subsequent steps.
        if (dialog.IsAvailable)
        {
            try { dialog.Close(); } catch { /* best effort */ }
            Retry.WhileTrue(
                () => dialog.IsAvailable,
                timeout: TimeSpan.FromSeconds(15),
                interval: TimeSpan.FromMilliseconds(300),
                throwOnTimeout: false,
                ignoreException: true);
        }
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
        // The dialog is shown modally via Window.ShowDialog(), so it is an owned/modal
        // window of the main window — it does NOT appear in GetAllTopLevelWindows.
        // Look in MainWindow.ModalWindows first, falling back to top-level enumeration.
        return Retry.WhileNull(
            () => MainWindow.ModalWindows
                     .FirstOrDefault(w => string.Equals(
                         w.Title, CONNECTION_DIALOG_TITLE, StringComparison.OrdinalIgnoreCase))
                  ?? App.GetAllTopLevelWindows(Automation)
                     .FirstOrDefault(w => string.Equals(
                         w.Title, CONNECTION_DIALOG_TITLE, StringComparison.OrdinalIgnoreCase)),
            timeout: TimeSpan.FromSeconds(timeoutSeconds),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            ignoreException: true,
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

    #region Configure-Logging Helper
    /// <summary>
    /// Sets the sampling frequency on the Profiles edit drawer to
    /// <paramref name="targetHz"/> via the RangeValuePattern, then waits until the
    /// slider reads the value back. Navigates to the Profiles tab first. Assumes the
    /// edit drawer is already visible (the default Profiles layout shows it).
    /// </summary>
    /// <param name="targetHz">Desired sampling frequency in Hz (1..1000).</param>
    /// <returns>The frequency value read back from the slider after setting it.</returns>
    protected double SetSamplingFrequency(double targetHz)
    {
        var slider = OpenDeviceSettingsFrequencySlider();
        slider.WaitUntilEnabled(TimeSpan.FromSeconds(10));

        var rangeValue = slider.Patterns.RangeValue.Pattern;
        rangeValue.SetValue(targetHz);

        // The slider settles on the value immediately, but its binding to the
        // device frequency uses Delay=500ms. Wait for the slider to read the target,
        // then allow the delayed binding to commit to the device before the caller
        // navigates away (navigating closes the flyout and cancels a pending commit).
        Retry.WhileFalse(
            () => Math.Abs(slider.Patterns.RangeValue.Pattern.Value.Value - targetHz) < 0.5,
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(200),
            throwOnTimeout: true,
            timeoutMessage:
                $"Sampling frequency did not reach {targetHz} Hz (last read " +
                $"{slider.Patterns.RangeValue.Pattern.Value.Value}).");

        // Allow the Delay=500ms two-way binding to push the value to the device.
        System.Threading.Thread.Sleep(900);

        return slider.Patterns.RangeValue.Pattern.Value.Value;
    }

    /// <summary>
    /// Reads the sampling frequency from the per-device settings flyout.
    /// </summary>
    protected double GetSamplingFrequency()
    {
        var slider = OpenDeviceSettingsFrequencySlider();
        return slider.Patterns.RangeValue.Pattern.Value.Value;
    }

    /// <summary>
    /// Navigates to the Devices pane and ensures the per-device settings flyout is
    /// open (clicking the gear icon on the connected device tile if needed), then
    /// returns the frequency slider inside it.
    /// </summary>
    private AutomationElement OpenDeviceSettingsFrequencySlider()
    {
        NavigateToTab(DEVICES_TAB_TEXT);

        // If the flyout is not already open, the slider is absent/offscreen; open it.
        var slider = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(SAMPLING_FREQUENCY_INPUT_ID));
        if (slider == null || slider.IsOffscreen)
        {
            var gear = FindByAutomationId(DEVICE_SETTINGS_BUTTON_ID);
            gear.WaitUntilEnabled(TimeSpan.FromSeconds(10));
            gear.AsButton().Invoke();
        }

        return Retry.WhileNull(
            () =>
            {
                var s = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(SAMPLING_FREQUENCY_INPUT_ID));
                return s != null && !s.IsOffscreen ? s : null;
            },
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage: "Sampling frequency slider did not appear in the device settings flyout.").Result!;
    }

    /// <summary>
    /// Enables the analog channels on the Channels pane via the "SELECT ALL" button.
    /// Individual channel tiles toggle only through a LeftClick MouseBinding (a real
    /// foreground mouse click), which does not land reliably from a background test
    /// host; the SELECT ALL button exposes the InvokePattern and works regardless of
    /// foreground. Navigates to the Channels tab first and waits for the device to
    /// report its channel set, then confirms via the pane's "n / N ACTIVE" indicator.
    /// </summary>
    /// <returns>The number of analog channels reported active after selecting all.</returns>
    protected int EnableAllAnalogChannels()
    {
        NavigateToTab(CHANNELS_TAB_TEXT);

        // Wait for the channel tiles to materialize (device pushes its channel set).
        var channelList = FindByAutomationId(CHANNEL_LIST_ID);
        Retry.WhileEmpty(
            () => channelList.FindAllChildren(),
            timeout: TimeSpan.FromSeconds(30),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            timeoutMessage:
                "No analog channel tiles appeared on the Channels pane. " +
                "Ensure a DAQiFi device is connected and reporting channels.");

        var selectAll = FindByAutomationId(SELECT_ALL_ANALOG_ID);
        selectAll.WaitUntilEnabled(TimeSpan.FromSeconds(10));
        selectAll.AsButton().Invoke();

        // Confirm activation via the ground-truth "n / N ACTIVE" indicator.
        var count = Retry.WhileFalse(
            () => ReadActiveAnalogCount() > 0,
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            timeoutMessage: "No analog channels became active after invoking SELECT ALL.");

        return ReadActiveAnalogCount();
    }

    /// <summary>
    /// Reads the number of active analog channels from the pane's "n / N ACTIVE"
    /// indicator. Navigates to the Channels tab first.
    /// </summary>
    protected int GetActiveAnalogChannelCount()
    {
        NavigateToTab(CHANNELS_TAB_TEXT);
        return ReadActiveAnalogCount();
    }

    /// <summary>Reads the "n / N ACTIVE" count on the current (Channels) view, or -1.</summary>
    private int ReadActiveAnalogCount()
    {
        foreach (var text in MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Text)))
        {
            var name = text.Name;
            if (string.IsNullOrEmpty(name) || !name.Contains("ACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // e.g. "·  2  /  16  ACTIVE" -> capture the first number (active count).
            var match = System.Text.RegularExpressions.Regex.Match(name, @"(\d+)\s*/\s*\d+");
            if (match.Success)
            {
                return int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return -1;
    }
    #endregion

    #region Logging-Session Helper
    /// <summary>
    /// Navigates to the Live Graph pane and returns the logging toggle button.
    /// Waits until the toggle is enabled (gated by <c>CanToggleLogging</c>).
    /// </summary>
    protected Button GetLoggingToggle(int enabledTimeoutSeconds = 30)
    {
        NavigateToTab(LIVE_GRAPH_TAB_TEXT);
        var toggle = FindByAutomationId(START_LOGGING_TOGGLE_ID);
        toggle.WaitUntilEnabled(TimeSpan.FromSeconds(enabledTimeoutSeconds));
        return toggle.AsButton();
    }

    /// <summary>
    /// Starts a logging session by toggling <c>StartLoggingToggle</c> on via the
    /// TogglePattern, then waits (polling) until the toggle reports On and the
    /// status text reads "LOGGING ON".
    /// </summary>
    protected void StartLogging()
    {
        var toggle = GetLoggingToggle();
        if (toggle.Patterns.Toggle.Pattern.ToggleState.Value != ToggleState.On)
        {
            toggle.Patterns.Toggle.Pattern.Toggle();
        }

        Retry.WhileFalse(
            () => toggle.Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.On,
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(200),
            throwOnTimeout: true,
            timeoutMessage: "Logging toggle did not switch On after Toggle().");

        WaitForLoggingStatus(LOGGING_ON_TEXT, TimeSpan.FromSeconds(15));
    }

    /// <summary>
    /// Stops the active logging session by toggling <c>StartLoggingToggle</c> off via
    /// the TogglePattern, then waits (polling) until the toggle reports Off and the
    /// status text reads "LOGGING OFF".
    /// </summary>
    protected void StopLogging()
    {
        // Navigate back to the Live Graph pane: the accrual check may have left another
        // tab selected, and the toggle only exists in the realized (selected) pane.
        NavigateToTab(LIVE_GRAPH_TAB_TEXT);
        var toggle = FindByAutomationId(START_LOGGING_TOGGLE_ID).AsButton();
        if (toggle.Patterns.Toggle.Pattern.ToggleState.Value != ToggleState.Off)
        {
            toggle.Patterns.Toggle.Pattern.Toggle();
        }

        Retry.WhileFalse(
            () => toggle.Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.Off,
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(200),
            throwOnTimeout: true,
            timeoutMessage: "Logging toggle did not switch Off after Toggle().");

        WaitForLoggingStatus(LOGGING_OFF_TEXT, TimeSpan.FromSeconds(15));
    }

    /// <summary>
    /// Waits (polling) until the visible logging state matches the expected status.
    /// Reads the <c>LoggingStatusText</c> label (its UIA Name mirrors its Text via the
    /// AutomationProperties.Name binding in the view) as the primary signal, falling
    /// back to the logging toggle's TogglePattern state — the control the label
    /// mirrors — so the wait stays reliable even if the label is momentarily stale.
    /// </summary>
    protected void WaitForLoggingStatus(string expectedText, TimeSpan timeout)
    {
        var expectedState = string.Equals(expectedText, LOGGING_ON_TEXT, StringComparison.Ordinal)
            ? ToggleState.On
            : ToggleState.Off;

        Retry.WhileFalse(
            () =>
            {
                var status = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(LOGGING_STATUS_TEXT_ID));
                if (status != null && string.Equals(status.Name, expectedText, StringComparison.Ordinal))
                {
                    return true;
                }

                var toggle = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(START_LOGGING_TOGGLE_ID));
                return toggle != null
                    && toggle.Patterns.Toggle.Pattern.ToggleState.Value == expectedState;
            },
            timeout: timeout,
            interval: TimeSpan.FromMilliseconds(200),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage: $"Logging state did not become '{expectedText}' within {timeout.TotalSeconds}s.");
    }

    /// <summary>
    /// Strictly waits (polling) until the user-visible <c>LoggingStatusText</c> label
    /// reads <paramref name="expectedText"/> — reading ONLY the label's UIA Name, with
    /// <b>no</b> fallback to the toggle's state. Use this for assertions where the label
    /// itself is the thing under test: the toggle is the binding source so it always
    /// flips, but the label only updates when <c>IsLogging</c> raises a change
    /// notification. This catches the "toggle is On but the label still says LOGGING
    /// OFF" regression that <see cref="WaitForLoggingStatus"/>'s toggle fallback hides.
    /// </summary>
    protected void WaitForLoggingStatusLabel(string expectedText, TimeSpan timeout)
    {
        Retry.WhileFalse(
            () =>
            {
                var status = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(LOGGING_STATUS_TEXT_ID));
                return status != null && string.Equals(status.Name, expectedText, StringComparison.Ordinal);
            },
            timeout: timeout,
            interval: TimeSpan.FromMilliseconds(200),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage:
                $"The logging status label never read '{expectedText}'. If the toggle " +
                "flipped but the label stayed stale, the IsLogging setter is not raising " +
                "PropertyChanged on the start/stop path, so bindings to IsLogging never refresh.");
    }

    /// <summary>
    /// Reads the number of logged-session rows currently shown on the Logged Data pane.
    /// Empty sessions (no samples) are deleted by the app on stop, so a row appearing
    /// here is an out-of-process signal that data actually accrued during the run.
    /// Navigates to the Logged Data tab first.
    /// </summary>
    protected int GetLoggedSessionCount()
    {
        NavigateToTab(LOGGED_DATA_TAB_TEXT);
        var list = Retry.WhileNull(
            () => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(LOGGED_SESSION_LIST_ID)),
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            timeoutMessage: "Logged-session list not found on the Logged Data pane.").Result!;

        return list.AsListBox().Items.Length;
    }

    /// <summary>
    /// Waits (polling) until the Logged Data pane holds at least
    /// <paramref name="minimum"/> session rows.
    /// </summary>
    protected void WaitForLoggedSessionCount(int minimum, TimeSpan timeout)
    {
        Retry.WhileFalse(
            () => GetLoggedSessionCount() >= minimum,
            timeout: timeout,
            interval: TimeSpan.FromMilliseconds(400),
            throwOnTimeout: true,
            timeoutMessage:
                $"Expected at least {minimum} logged-session row(s) but found fewer within {timeout.TotalSeconds}s.");
    }
    #endregion

    #region SD-Card Logging Helpers
    /// <summary>
    /// Switches the connected device's logging mode via the segmented selector in the
    /// per-device settings drawer. <paramref name="logToDevice"/> true selects
    /// "Log to Device" (SD card); false selects "Stream to App". Confirms the switch by
    /// an INDEPENDENT signal — the SD-card DATA FORMAT selector is rendered only while
    /// <c>Shell.IsLogToDeviceMode</c> is true — rather than the radio's own checked
    /// state, which the UIA SelectionItem pattern sets directly regardless of whether
    /// the bound switch command actually ran.
    /// </summary>
    /// <param name="logToDevice">True for SD card logging; false for stream-to-app.</param>
    protected void SetLoggingMode(bool logToDevice)
    {
        EnsureDeviceSettingsDrawerOpen();

        var radioId = logToDevice ? LOGGING_MODE_SDCARD_ID : LOGGING_MODE_STREAM_ID;
        var radio = FindByAutomationId(radioId).AsRadioButton();

        // "Log to Device" is gated on a USB connection (SD card logging requires USB);
        // WaitUntilEnabled surfaces a clear failure if the device is not USB-connected.
        radio.WaitUntilEnabled(TimeSpan.FromSeconds(10));
        if (!radio.IsChecked)
        {
            // Select() raises Checked, which the view's EventTrigger forwards to the
            // SetLoggingMode command (a RadioButton's bound Command would not fire from
            // a background automation host — see the harness gotchas).
            radio.IsChecked = true;
        }

        // The DATA FORMAT section's Visibility binds through BooleanToVisibilityConverter
        // (false -> Collapsed), and a Collapsed subtree is absent from the UIA tree. So
        // *presence* of the selector is the signal that Shell.IsLogToDeviceMode is true —
        // independent of scroll position (an on-screen check would falsely time out if the
        // drawer ever needs scrolling to reveal it).
        if (logToDevice)
        {
            Retry.WhileNull(
                () => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(SDCARD_FORMAT_SELECTOR_ID)),
                timeout: TimeSpan.FromSeconds(15),
                interval: TimeSpan.FromMilliseconds(300),
                throwOnTimeout: true,
                ignoreException: true,
                timeoutMessage:
                    "Switching to 'Log to Device' did not take effect: the SD card DATA FORMAT " +
                    "selector never appeared, so Shell.IsLogToDeviceMode stayed false (the mode " +
                    "switch command did not run).");
        }
        else
        {
            Retry.WhileFalse(
                () => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(SDCARD_FORMAT_SELECTOR_ID)) == null,
                timeout: TimeSpan.FromSeconds(15),
                interval: TimeSpan.FromMilliseconds(300),
                throwOnTimeout: true,
                ignoreException: true,
                timeoutMessage:
                    "Switching to 'Stream to App' did not take effect: the SD card DATA FORMAT " +
                    "selector remained visible (Shell.IsLogToDeviceMode stayed true).");
        }
    }

    /// <summary>
    /// Navigates to the Logged Data pane's DEVICE LOGS sub-tab, refreshes the SD card
    /// file list from the connected USB device, and returns the file count parsed from
    /// the SD status line ("· SD card OK · N files"). Marks the test inconclusive when
    /// no SD card is installed and fails on an SD card error. Counting from the status
    /// line works at zero files (the file list itself is hidden when empty), so it gives
    /// a stable before/after baseline.
    /// </summary>
    protected int GetSdCardFileCount(int timeoutSeconds = 45)
    {
        NavigateToTab(LOGGED_DATA_TAB_TEXT);
        SelectDeviceLogsSubTab();
        InvokeRefreshSdCardFiles();

        // Wait until the refresh settles into a definitive SD state, capturing the line.
        var statusText = string.Empty;
        Retry.WhileFalse(
            () =>
            {
                var status = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(SDCARD_STATUS_TEXT_ID));
                if (status == null || status.IsOffscreen)
                {
                    return false;
                }

                var name = status.Name ?? string.Empty;
                if (name.Contains("SD card OK", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("No SD card", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("SD card error", StringComparison.OrdinalIgnoreCase))
                {
                    statusText = name;
                    return true;
                }

                return false;
            },
            timeout: TimeSpan.FromSeconds(timeoutSeconds),
            interval: TimeSpan.FromMilliseconds(400),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage:
                "SD card status did not reach a definitive state after refresh. Ensure the " +
                "device is USB-connected and reporting its SD card.");

        if (statusText.Contains("No SD card", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Inconclusive(
                "The attached device reports no SD card installed; SD card logging cannot be " +
                "exercised. Insert a FAT32-formatted SD card and re-run.");
        }

        if (statusText.Contains("SD card error", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail($"The device reported an SD card error during refresh: '{statusText}'.");
        }

        // e.g. "· SD card OK · 3 files" / "· SD card OK · 1 file" -> capture the count.
        var match = System.Text.RegularExpressions.Regex.Match(
            statusText, @"(\d+)\s*files?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        Assert.IsTrue(
            match.Success,
            $"Could not parse the SD card file count from the status line '{statusText}'.");

        return int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Ensures the per-device settings drawer is open (clicking the gear on the device
    /// tile if needed) and the logging-mode selector inside it is realized.
    /// </summary>
    private void EnsureDeviceSettingsDrawerOpen()
    {
        NavigateToTab(DEVICES_TAB_TEXT);

        var probe = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(LOGGING_MODE_SDCARD_ID));
        if (probe == null || probe.IsOffscreen)
        {
            var gear = FindByAutomationId(DEVICE_SETTINGS_BUTTON_ID);
            gear.WaitUntilEnabled(TimeSpan.FromSeconds(10));
            gear.AsButton().Invoke();
        }

        Retry.WhileNull(
            () =>
            {
                var el = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(LOGGING_MODE_SDCARD_ID));
                return el != null && !el.IsOffscreen ? el : null;
            },
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage: "Device settings drawer did not open (logging-mode selector not visible).");
    }

    /// <summary>
    /// Selects the DEVICE LOGS sub-tab on the Logged Data pane and waits for its content
    /// (the SD card file view) to realize. The sub-tab is a RadioButton whose checked
    /// state drives the view's visibility directly (ElementName binding, no command), so
    /// the UIA SelectionItem pattern switches it reliably.
    /// </summary>
    private void SelectDeviceLogsSubTab()
    {
        var tab = FindByAutomationId(DEVICE_LOGS_TAB_ID).AsRadioButton();
        Retry.WhileFalse(
            () =>
            {
                if (!tab.IsChecked)
                {
                    tab.IsChecked = true;
                }

                return tab.IsChecked;
            },
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage: "DEVICE LOGS sub-tab could not be selected on the Logged Data pane.");

        // The refresh button only exists once the DeviceLogsView is realized/visible.
        FindByAutomationId(REFRESH_SDCARD_FILES_BUTTON_ID);
    }

    /// <summary>Invokes the REFRESH button on the DEVICE LOGS sub-tab to re-read the SD file list.</summary>
    private void InvokeRefreshSdCardFiles()
    {
        var refresh = FindByAutomationId(REFRESH_SDCARD_FILES_BUTTON_ID).AsButton();
        refresh.WaitUntilEnabled(TimeSpan.FromSeconds(10));
        refresh.Invoke();
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
    /// Resolves the NLog log file path. The app under test runs in test mode, which writes
    /// its data and logs to the per-user LocalApplicationData location (see AppDataPaths);
    /// prefer that, but fall back to the machine-wide location for robustness.
    /// </summary>
    private static string ResolveLogFilePath()
    {
        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DAQiFi", "Logs", LOG_FILE_NAME);
        var common = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "DAQiFi", "Logs", LOG_FILE_NAME);

        // If a machine-wide log already exists and the per-user one does not, use it;
        // otherwise default to the per-user (test-mode) location the app writes to.
        if (!File.Exists(local) && File.Exists(common))
        {
            return common;
        }

        return local;
    }
    #endregion
}
