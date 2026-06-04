using System;
using System.Collections.Generic;
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
    // When ExportHookDirectory is set, the child app is launched with this env var pointing at
    // a harness-owned directory, so the export commands write straight there with no dialog
    // (see Daqifi.Desktop.Common.AppDataPaths.TestExportPath).
    private const string TEST_EXPORT_PATH_ENV_VAR = "DAQIFI_TEST_EXPORT_PATH";
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

    // The DISCONNECT button in the per-device settings drawer's ACTIONS section (opened by the
    // tile gear, DeviceSettingsButton). It is a plain Button bound to DisconnectSelectedCommand,
    // so InvokePattern raises a real click and the bound command runs (cf. gotcha #12, which only
    // bites bound Commands on check controls). Disconnecting tears down the device: it unsubscribes
    // active channels, disposes the transport, and removes the device from ConnectedDevices —
    // exercised by the disconnect→reconnect lifecycle scenario (issue #559).
    private const string DISCONNECT_SELECTED_BUTTON_ID = "DisconnectSelectedButton";

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

    // The (invisible) live-plot stats indicator on the Live Graph pane. OxyPlot draws every
    // point to one canvas and exposes no per-point UI Automation elements, so the harness
    // cannot walk the tree for points. Instead it reads this element's Name — a machine-readable
    // "series=..;points=..;nonfinite=..;last=..;min=..;max=.." summary of what the live plot is
    // rendering — to assert the plot shows believable data while streaming (issue #560).
    private const string PLOT_STATS_TEXT_ID = "PlotStatsText";

    // Per-row DELETE button (one per session row, like ExportSessionButton — it is in the item
    // template, so a row-scoped search targets that one row) and the affirmative button of the
    // app's in-pane confirm overlay. That overlay is the dark, in-window card the app shows for
    // destructive confirmations (delete) instead of a MahApps modal; both its accent/danger style
    // variants carry the same id and only the visible one is in the UIA tree, so an id lookup
    // returns whichever is shown. Used by the log-then-delete path.
    private const string DELETE_SESSION_BUTTON_ID = "DeleteSessionButton";
    private const string CONFIRM_AFFIRMATIVE_BUTTON_ID = "ConfirmAffirmativeButton";

    // CSV-export controls on the Logged Data pane's APP LOGS sub-tab. The per-row export button
    // and the EXPORT ALL button drive the two dialog-free export paths through the
    // DAQIFI_TEST_EXPORT_PATH hook. Every session row carries the same per-row id (it is in the
    // item template), so a row-scoped search targets that one row's button. The row template's
    // name/date TextBlocks are not surfaced to UI Automation (only the action buttons are), so the
    // newest session is targeted by position — it is the last row (the list renders in insertion
    // order, no sort) — and the exported file name confirms which session it was.
    private const string EXPORT_SESSION_BUTTON_ID = "ExportSessionButton";
    private const string EXPORT_ALL_SESSIONS_BUTTON_ID = "ExportAllSessionsButton";

    // The Logged Data pane hosts two mutually-exclusive sub-tabs: APP LOGS (default,
    // showing the logged-session list) and DEVICE LOGS (showing the SD card browser).
    // Each sub-tab's content binds Visibility to its radio's IsChecked, so only the
    // selected sub-tab's content is in the UIA tree. Switching back to APP LOGS is
    // required before reading LoggedSessionList after working on the DEVICE LOGS sub-tab.
    private const string APP_LOGS_TAB_ID = "AppLogsTab";

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

    // The "Logging to Device" status panel shown on the Live Graph pane while a connected device
    // is logging to its SD card (issue #507). The panel's Border Visibility binds to
    // DaqifiViewModel.IsSdCardLoggingActive, but a bare Border has no UI Automation peer — it never
    // surfaces in the UIA tree, visible or not — so the harness keys off the elapsed-clock TextBlock
    // inside it instead: a TextBlock HAS a peer and is rendered only while the overlay is, so its
    // presence by id is a clean displayed/hidden signal (every detection id in this harness sits on
    // a control/TextBlock for this reason). Its AutomationProperties.Name is pinned to the bound
    // HH:mm:ss value so the once-a-second update is readable out-of-process (mirrors the
    // LoggingStatusText / PlotStatsText hooks).
    private const string SDCARD_LOGGING_ELAPSED_TEXT_ID = "SdLoggingElapsedText";

    // The per-row IMPORT button inside each SD card file row. Every realized row carries
    // the same id (it is in a GridView cell template), so a row-scoped search targets a
    // specific file; CommandParameter binds each button to its own row's SdCardFile.
    private const string IMPORT_SDCARD_FILE_BUTTON_ID = "ImportSdCardFileButton";

    // The file-name TextBlock in each SD card file row's NAME cell. A dedicated id lets the
    // harness read the file name deterministically rather than guessing from text order (the
    // row also contains the CREATED/FORMAT cells and the IMPORT button's "IMPORT" label).
    private const string SDCARD_FILE_NAME_TEXT_ID = "SdCardFileNameText";

    // The view-model shows a MahApps metro dialog after an import completes. It is hosted
    // inside the MetroWindow (not a separate top-level window) with a single affirmative
    // button whose default text is "OK"; the title reports success or failure.
    private const string IMPORT_DIALOG_OK_BUTTON_TEXT = "OK";
    private const string IMPORT_FAILED_TITLE = "Import Failed";

    // AutomationIds for the Profiles pane (save / activate / delete round-trip). Profiles are
    // saved experiment presets that re-apply a captured channel + frequency configuration to a
    // matching connected device. The tile name/date TextBlocks are not reliably surfaced to UI
    // Automation (like the logged-session rows), so a profile is targeted by POSITION — the
    // newest is the LAST tile (SubscribedProfiles appends; the list renders in insertion order)
    // — via its per-tile settings (gear) button, which opens the edit drawer holding ACTIVATE /
    // DELETE. The "+ ADD PROFILE" button opens the new-profile drawer (status-bar id when
    // profiles exist, empty-state id otherwise).
    private const string PROFILE_LIST_ID = "ProfileList";
    private const string PROFILE_SETTINGS_BUTTON_ID = "ProfileSettingsButton";
    private const string ADD_PROFILE_BUTTON_ID = "AddProfileButton";
    private const string ADD_PROFILE_BUTTON_EMPTY_ID = "AddProfileButtonEmpty";
    private const string NEW_PROFILE_NAME_INPUT_ID = "NewProfileNameInput";
    private const string SAVE_CURRENT_SETTINGS_BUTTON_ID = "SaveCurrentSettingsButton";
    private const string SAVE_NEW_PROFILE_BUTTON_ID = "SaveNewProfileButton";
    private const string NEW_PROFILE_DEVICE_CHECKBOX_ID = "NewProfileDeviceCheckbox";
    private const string ACTIVATE_PROFILE_BUTTON_ID = "ActivateProfileButton";
    private const string DELETE_PROFILE_BUTTON_ID = "DeleteProfileButton";

    // The Profiles status-bar "· {name} ACTIVE" indicator carries this AutomationId. Its
    // Visibility binds to the active-profile name (collapsed, and so absent from the UIA tree,
    // when none is active), so looking it up by id is a deterministic profile-active signal used
    // to confirm deactivation — independent of any profile name text (a profile named "...ACTIVE"
    // would defeat a substring scan).
    private const string ACTIVE_PROFILE_INDICATOR_ID = "ActiveProfileIndicator";

    // Channels pane CLEAR ALL — used to move the device to a known-different state between
    // saving a profile and activating it, so the re-application is observable.
    private const string CLEAR_ALL_CHANNELS_ID = "ClearAllChannels";

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

    /// <summary>
    /// When non-null, the child app is launched with <c>DAQIFI_TEST_EXPORT_PATH</c> set to this
    /// directory, so the logging-session export commands write straight into it with zero
    /// SaveFileDialog interaction (see <c>AppDataPaths.TestExportPath</c>). Scenarios that
    /// exercise CSV export override this with a temp directory; the default <c>null</c> leaves
    /// the production dialog behaviour untouched for every other scenario.
    /// </summary>
    protected virtual string? ExportHookDirectory => null;
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

        // Opt-in per scenario: route exports to a harness-owned directory with no dialog.
        if (!string.IsNullOrEmpty(ExportHookDirectory))
        {
            psi.Environment[TEST_EXPORT_PATH_ENV_VAR] = ExportHookDirectory;
        }

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

    #region Disconnect / Reconnect Lifecycle Helpers
    /// <summary>
    /// Disconnects the currently connected device through the real UI: opens its settings drawer
    /// (via the tile gear, <c>DeviceSettingsButton</c>, which selects the tile and so satisfies
    /// the command's <c>SelectedDevice != null</c> guard), then invokes the drawer's DISCONNECT
    /// button (<c>DisconnectSelectedButton</c>). The button is a plain <c>Button</c>, so
    /// InvokePattern raises a real click and the bound <c>DisconnectSelectedCommand</c> runs (cf.
    /// gotcha #12, which only bites bound <c>Command</c>s on check controls) — which unsubscribes
    /// the device's active channels, disposes its transport, and removes it from
    /// <c>ConnectedDevices</c>. Assumes a device is connected. Does not wait for teardown to
    /// settle; callers assert that via <see cref="WaitForNoConnectedDevices"/>,
    /// <see cref="WaitForChannelsCleared"/>, and <see cref="WaitForLoggingToggleDisabled"/>.
    /// </summary>
    protected void DisconnectSelectedDevice()
    {
        NavigateToTab(DEVICES_TAB_TEXT);

        // The DISCONNECT button lives inside the settings drawer; if it is absent the drawer is
        // closed (a Collapsed subtree is pruned from the UIA tree), so open it via the gear. Presence
        // by id — not on-screen position — is the drawer-open signal: opening the drawer also sets
        // SelectedTile/SelectedDevice, which the DisconnectSelectedCommand's CanExecute requires. Do
        // not key the open decision off IsOffscreen — the button sits in the ACTIONS section at the
        // bottom of the drawer's ScrollViewer, so it can be present yet scrolled out of view, and
        // re-clicking the gear then would not help.
        if (MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(DISCONNECT_SELECTED_BUTTON_ID)) == null)
        {
            var gear = FindByAutomationId(DEVICE_SETTINGS_BUTTON_ID);
            gear.WaitUntilEnabled(TimeSpan.FromSeconds(10));
            gear.AsButton().Invoke();
        }

        // Wait only for the button to be PRESENT (drawer realized) — not for !IsOffscreen. Because
        // the button is at the bottom of the drawer's ScrollViewer, on a short window it can be
        // present but scrolled off-screen; requiring !IsOffscreen would then time out and make this
        // flaky. InvokePattern raises the click on the automation peer regardless of scroll position
        // (the element need not be on-screen to Invoke), so presence is the only precondition that
        // matters — consistent with this harness's pattern-over-physical-click approach (gotcha #9).
        var disconnect = Retry.WhileNull(
            () => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(DISCONNECT_SELECTED_BUTTON_ID)),
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage:
                "The DISCONNECT button did not appear in the device settings drawer. The gear " +
                "(DeviceSettingsButton) may not have opened the drawer, or no device is connected.").Result!;

        var button = disconnect.AsButton();
        button.WaitUntilEnabled(TimeSpan.FromSeconds(10));
        button.Invoke();
    }

    /// <summary>
    /// Waits (polling) until no devices remain in the connected-devices container — the
    /// out-of-process signal of a clean disconnect. When <c>HasConnectedDevice</c> goes false the
    /// whole device-list content collapses out of the UIA tree (the Devices pane swaps to its
    /// empty state), so <see cref="GetConnectedDeviceCount"/> reads 0 whether the list is empty or
    /// absent. Navigates to the Devices tab first so the read is independent of the prior tab.
    /// </summary>
    protected void WaitForNoConnectedDevices(TimeSpan timeout)
    {
        NavigateToTab(DEVICES_TAB_TEXT);
        Retry.WhileFalse(
            () => GetConnectedDeviceCount() == 0,
            timeout: timeout,
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage:
                $"A device tile was still present in the connected-devices list within {timeout.TotalSeconds:N0}s " +
                "of disconnecting — the device was not removed from ConnectedDevices.");
    }

    /// <summary>
    /// Waits (polling) until the Channels pane shows no channel list — proving the disconnected
    /// device's channel subscriptions were torn down. The Channels content (including
    /// <c>ChannelList</c>) binds its Visibility to <c>HasConnectedDevice</c>; with no device
    /// connected the pane collapses that content (swapping to its empty state), so
    /// <c>ChannelList</c> leaves the UIA tree entirely. Its absence is therefore a clean "channels
    /// gone" signal — stronger than an empty list, since the pane rebuilt with zero connected
    /// devices. Navigates to the Channels tab first.
    /// </summary>
    protected void WaitForChannelsCleared(TimeSpan timeout)
    {
        NavigateToTab(CHANNELS_TAB_TEXT);
        Retry.WhileFalse(
            () => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(CHANNEL_LIST_ID)) == null,
            timeout: timeout,
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage:
                $"The Channels pane still showed its channel list within {timeout.TotalSeconds:N0}s of " +
                "disconnecting — the device's channels were not torn down (HasConnectedDevice stayed true, " +
                "so the pane did not return to its empty state).");
    }

    /// <summary>
    /// Reads whether the Live Graph logging toggle (<c>StartLoggingToggle</c>) is currently
    /// enabled. The toggle's <c>IsEnabled</c> binds to <c>CanToggleLogging</c>
    /// (= <c>ActiveChannels.Count &gt; 0</c>), so it is enabled only while at least one channel is
    /// subscribed. Navigates to the Live Graph tab first.
    /// </summary>
    protected bool IsLoggingToggleEnabled()
    {
        NavigateToTab(LIVE_GRAPH_TAB_TEXT);
        return FindByAutomationId(START_LOGGING_TOGGLE_ID).IsEnabled;
    }

    /// <summary>
    /// Waits (polling) until the Live Graph logging toggle (<c>StartLoggingToggle</c>) is
    /// disabled — the "logging controls return to the disconnected state" signal. After a
    /// disconnect the device's channels are unsubscribed, so <c>ActiveChannels</c> empties and
    /// <c>CanToggleLogging</c> goes false, which disables the toggle. Navigates to the Live Graph
    /// tab first.
    /// </summary>
    protected void WaitForLoggingToggleDisabled(TimeSpan timeout)
    {
        NavigateToTab(LIVE_GRAPH_TAB_TEXT);
        Retry.WhileFalse(
            () =>
            {
                var toggle = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(START_LOGGING_TOGGLE_ID));
                return toggle != null && !toggle.IsEnabled;
            },
            timeout: timeout,
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage:
                $"The logging toggle was still enabled within {timeout.TotalSeconds:N0}s of disconnecting — " +
                "CanToggleLogging stayed true, so the device's channels were not unsubscribed " +
                "(ActiveChannels did not empty).");
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

    /// <summary>
    /// Clears every active channel via the Channels pane's CLEAR ALL button, then waits until
    /// the analog "n / N ACTIVE" indicator reads zero. Navigates to the Channels tab first.
    /// Used to move the device to a known-different state before activating a profile, so the
    /// profile's re-application of channels is observable (0 active -> N active).
    /// </summary>
    protected void ClearAllChannels()
    {
        NavigateToTab(CHANNELS_TAB_TEXT);
        var clear = FindByAutomationId(CLEAR_ALL_CHANNELS_ID).AsButton();
        clear.WaitUntilEnabled(TimeSpan.FromSeconds(10));
        clear.Invoke();

        Retry.WhileFalse(
            () => ReadActiveAnalogCount() == 0,
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage: "Channels did not clear: the analog active count did not reach 0 after CLEAR ALL.");
    }

    /// <summary>
    /// Navigates to the Channels pane (rebuilding it from current device state) and waits until
    /// the analog "n / N ACTIVE" indicator reads <paramref name="expected"/>. The pane is
    /// recreated on tab entry, so it reflects channels a profile activation added to the device
    /// directly (bypassing the pane), making this a faithful ground-truth re-application check.
    /// </summary>
    protected void WaitForActiveAnalogChannelCount(int expected, TimeSpan timeout)
    {
        NavigateToTab(CHANNELS_TAB_TEXT);
        Retry.WhileFalse(
            () => ReadActiveAnalogCount() == expected,
            timeout: timeout,
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage:
                $"Analog active channel count did not reach {expected} (last read " +
                $"{ReadActiveAnalogCount()}). Expected the activated profile to re-apply its channels.");
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
        // The session list lives on the APP LOGS sub-tab; ensure it is selected so the
        // list is realized in the UIA tree even if a prior step left DEVICE LOGS active.
        SelectAppLogsSubTab();
        var list = Retry.WhileNull(
            () => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(LOGGED_SESSION_LIST_ID)),
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            timeoutMessage: "Logged-session list not found on the Logged Data pane.").Result!;

        return list.AsListBox().Items.Length;
    }

    /// <summary>
    /// Selects the APP LOGS sub-tab on the Logged Data pane (the default) and waits for
    /// the logged-session list to realize. The sub-tab is a RadioButton whose checked
    /// state drives the content's visibility directly (ElementName binding, no command),
    /// so the UIA SelectionItem pattern switches it reliably. Selecting it also unchecks
    /// the sibling DEVICE LOGS tab, collapsing the SD browser out of the UIA tree.
    /// </summary>
    private void SelectAppLogsSubTab()
    {
        var tab = FindByAutomationId(APP_LOGS_TAB_ID).AsRadioButton();
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
            timeoutMessage: "APP LOGS sub-tab could not be selected on the Logged Data pane.");

        // The session list only exists once the APP LOGS content is realized/visible.
        FindByAutomationId(LOGGED_SESSION_LIST_ID);
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

    /// <summary>
    /// Waits (polling) until the Logged Data pane holds exactly <paramref name="expected"/>
    /// session rows. Unlike <see cref="WaitForLoggedSessionCount"/> (a &gt;= minimum used to prove
    /// a session was created), this waits for an exact value — used after a delete to confirm the
    /// count returns to its pre-run baseline.
    /// </summary>
    protected void WaitForExactLoggedSessionCount(int expected, TimeSpan timeout)
    {
        Retry.WhileFalse(
            () => GetLoggedSessionCount() == expected,
            timeout: timeout,
            interval: TimeSpan.FromMilliseconds(400),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage:
                // Note: do NOT re-read the count here — the message is built eagerly on every call,
                // so an interpolated GetLoggedSessionCount() would add a UI read (that could itself
                // throw, outside ignoreException) and report the entry count, not the timeout count.
                $"Logged-session count did not return to {expected} within {timeout.TotalSeconds}s.");
    }

    /// <summary>
    /// Deletes the most-recently-created logged session via its per-row DELETE button on the
    /// Logged Data pane's APP LOGS sub-tab, then confirms the resulting in-pane dialog. The list
    /// renders in insertion order with no sort, so a just-finalized session is the LAST row; this
    /// invokes that row's <c>DeleteSessionButton</c>. (Row name/date TextBlocks are not surfaced
    /// to UI Automation — only the action buttons are — so the target row is identified by
    /// position, mirroring <see cref="ExportNewestLoggedSession"/>; cf. gotcha #15.) The button is
    /// a plain <c>Button</c>, so InvokePattern raises a real click and its bound
    /// <c>DeleteLoggingSessionCommand</c> runs (cf. gotcha #12). That command opens the app's
    /// dark in-pane confirm overlay (NOT a MahApps modal), which <see cref="ConfirmInPaneDialog"/>
    /// then accepts via its affirmative ("DELETE") button.
    /// </summary>
    protected void DeleteNewestLoggedSession()
    {
        NavigateToTab(LOGGED_DATA_TAB_TEXT);
        SelectAppLogsSubTab();

        var deleteButton = Retry.WhileNull(
            () =>
            {
                var items = MainWindow
                    .FindFirstDescendant(cf => cf.ByAutomationId(LOGGED_SESSION_LIST_ID))?.AsListBox()?.Items;
                if (items == null || items.Length == 0)
                {
                    return null;
                }

                // Last row = newest session. Scope the button search to that row.
                return items[^1].FindFirstDescendant(cf => cf.ByAutomationId(DELETE_SESSION_BUTTON_ID));
            },
            timeout: TimeSpan.FromSeconds(30),
            interval: TimeSpan.FromMilliseconds(500),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage:
                "The per-row DELETE button on the newest logged-session row was not found. The " +
                "session row may not have rendered yet.").Result!;

        var button = deleteButton.AsButton();
        button.WaitUntilEnabled(TimeSpan.FromSeconds(10));
        button.Invoke();

        ConfirmInPaneDialog();
    }

    /// <summary>
    /// Accepts the app's in-pane confirm overlay (<c>IsConfirmOpen</c>) by invoking its affirmative
    /// button (<c>ConfirmAffirmativeButton</c> — its label is e.g. "DELETE"), then waits for the
    /// overlay to close. This overlay is the dark, in-window card the app uses instead of a MahApps
    /// modal for destructive confirmations; its accent/danger style variants share the id and only
    /// the visible one is in the UIA tree. A plain Button's InvokePattern raises a real click, so
    /// the bound <c>ConfirmAffirmativeCommand</c> runs (cf. gotcha #12).
    /// </summary>
    protected void ConfirmInPaneDialog(TimeSpan? timeout = null)
    {
        var affirmative = Retry.WhileNull(
            () => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(CONFIRM_AFFIRMATIVE_BUTTON_ID)),
            timeout: timeout ?? TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage:
                "The in-pane confirm overlay's affirmative button never appeared, so the action's " +
                "confirmation dialog did not open.").Result!;

        var button = affirmative.AsButton();
        button.WaitUntilEnabled(TimeSpan.FromSeconds(10));
        button.Invoke();

        // Require the overlay to actually close (its affirmative button leaves the UIA tree).
        // Fail if it does not: a lingering overlay means its blocking scrim is still up, so any
        // later navigation/read would be unreliable — better to fail here, precisely, than to
        // proceed against a blocked UI and surface a confusing downstream failure.
        Retry.WhileFalse(
            () => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(CONFIRM_AFFIRMATIVE_BUTTON_ID)) == null,
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage:
                "The in-pane confirm overlay did not close after invoking its affirmative button; " +
                "its blocking scrim is still present, so the confirm command may not have run.");
    }
    #endregion

    #region Live-Plot (Plot-Stats) Helpers
    /// <summary>
    /// A parsed snapshot of what the live plot is rendering, read out-of-process from the
    /// Live Graph pane's invisible <c>PlotStatsText</c> indicator (issue #560). OxyPlot draws
    /// points to a single canvas with no per-point UI Automation elements, so this summary is
    /// the harness's black-box window onto the plot's ground truth.
    /// </summary>
    /// <param name="SeriesCount">Number of rendered series (one per streaming channel).</param>
    /// <param name="PointCount">Real sample points across all series (gap markers excluded).</param>
    /// <param name="NonFiniteCount">Real samples whose VALUE is NaN/Inf (expected 0).</param>
    /// <param name="Last">Latest-in-time finite sample value (<see cref="double.NaN"/> if none).</param>
    /// <param name="Min">Minimum finite sample value (<see cref="double.NaN"/> if none).</param>
    /// <param name="Max">Maximum finite sample value (<see cref="double.NaN"/> if none).</param>
    protected readonly record struct PlotStats(
        int SeriesCount, long PointCount, long NonFiniteCount, double Last, double Min, double Max);

    /// <summary>
    /// Reads and parses the live-plot stats indicator (<c>PlotStatsText</c>) from the Live Graph
    /// pane. Navigates there first (the indicator is only realized in the UIA tree while that tab
    /// is selected). Re-resolves and re-reads under a short retry because, while a device streams,
    /// a single UIA Name read can transiently fail (gotcha #10) and the summary itself only
    /// refreshes about once a second.
    /// </summary>
    protected PlotStats ReadPlotStats()
    {
        NavigateToTab(LIVE_GRAPH_TAB_TEXT);

        var stats = default(PlotStats);
        var lastRaw = string.Empty;

        // Read AND parse inside the retry: while a device streams, a single UIA Name read can
        // transiently fail or (rarely) return a partial value, so retrying the parse — not just
        // the read — keeps every caller's first read robust (e.g. the baseline in
        // WaitForPlotPointGrowth, which is otherwise unguarded). TryParse keeps it non-throwing.
        var read = Retry.WhileFalse(
            () =>
            {
                var name = MainWindow
                    .FindFirstDescendant(cf => cf.ByAutomationId(PLOT_STATS_TEXT_ID))?.Name;
                if (string.IsNullOrEmpty(name))
                {
                    return false;
                }

                lastRaw = name;
                return TryParsePlotStats(name, out stats);
            },
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: false,
            ignoreException: true).Result;

        // Assert (not throwOnTimeout) so the failure message can include the last raw value read,
        // which the eagerly-built timeoutMessage could not capture.
        Assert.IsTrue(
            read,
            "The live-plot stats indicator (PlotStatsText) was not readable/parseable on the Live " +
            $"Graph pane (last raw value: '{lastRaw}'). Ensure the plot-stats UIA hook in " +
            "LiveGraphPane.xaml is present and emits the expected 'series=..;points=..;..' format.");

        return stats;
    }

    /// <summary>
    /// Tries to parse the <c>"series=N;points=M;nonfinite=K;last=V;min=A;max=B"</c> summary string
    /// (invariant culture; <c>last/min/max</c> may be <c>"NaN"</c>) into a <see cref="PlotStats"/>.
    /// Returns false (so the caller retries) on any malformed or partially-read value, and requires
    /// every field to be present so a truncated read is not silently accepted.
    /// </summary>
    private static bool TryParsePlotStats(string summary, out PlotStats stats)
    {
        stats = default;

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in summary.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = field.Split('=', 2);
            if (pair.Length != 2)
            {
                return false;
            }

            fields[pair[0].Trim()] = pair[1].Trim();
        }

        if (!TryParseLong(fields, "points", out var points)
            || !TryParseLong(fields, "nonfinite", out var nonFinite)
            || !TryParseStat(fields, "last", out var last)
            || !TryParseStat(fields, "min", out var min)
            || !TryParseStat(fields, "max", out var max))
        {
            return false;
        }

        if (!fields.TryGetValue("series", out var seriesText)
            || !int.TryParse(
                seriesText,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var series))
        {
            return false;
        }

        stats = new PlotStats(series, points, nonFinite, last, min, max);
        return true;
    }

    /// <summary>Parses a required invariant-culture integer field; false if missing or malformed.</summary>
    private static bool TryParseLong(IReadOnlyDictionary<string, string> fields, string key, out long value)
    {
        value = 0;
        return fields.TryGetValue(key, out var text)
            && long.TryParse(
                text,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
    }

    /// <summary>
    /// Parses a required invariant-culture double field, tolerating the "NaN" sentinel; false if the
    /// field is missing (a truncated read should retry, not be read as NaN) or malformed.
    /// </summary>
    private static bool TryParseStat(IReadOnlyDictionary<string, string> fields, string key, out double value)
    {
        value = double.NaN;
        return fields.TryGetValue(key, out var text)
            && double.TryParse(
                text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
    }

    /// <summary>
    /// Waits (polling) until the live plot reports exactly <paramref name="expected"/> series.
    /// Series materialize one per channel as each channel produces its first sample, so the
    /// count ramps up over the first moments of streaming; this rides out that ramp. Reads the
    /// plot-stats indicator, so it navigates to the Live Graph pane.
    /// </summary>
    protected void WaitForPlotSeriesCount(int expected, TimeSpan timeout)
    {
        Retry.WhileFalse(
            () => ReadPlotStats().SeriesCount == expected,
            timeout: timeout,
            interval: TimeSpan.FromMilliseconds(500),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage:
                $"The live plot did not render exactly {expected} series (one per active channel) " +
                $"within {timeout.TotalSeconds:N0}s. A mismatch means the plot is not rendering the " +
                "active channels' data.");
    }

    /// <summary>
    /// Proves the live plot's rendered point count strictly increases over a streaming window
    /// (data is flowing, not frozen). Takes a baseline reading, then polls until the point count
    /// exceeds it or <paramref name="timeout"/> elapses, and fails if it never grew. Returns the
    /// baseline and the first reading that showed growth.
    /// </summary>
    protected (PlotStats Before, PlotStats After) WaitForPlotPointGrowth(TimeSpan timeout)
    {
        var before = ReadPlotStats();
        var after = before;

        var grew = Retry.WhileFalse(
            () =>
            {
                after = ReadPlotStats();
                return after.PointCount > before.PointCount;
            },
            timeout: timeout,
            interval: TimeSpan.FromMilliseconds(400),
            throwOnTimeout: false,
            ignoreException: true).Result;

        Assert.IsTrue(
            grew,
            $"The live plot's rendered point count did not increase within {timeout.TotalSeconds:N0}s " +
            $"(stayed at {before.PointCount}). The plot is frozen — data is not reaching it while streaming.");

        return (before, after);
    }

    /// <summary>
    /// Asserts the live plot stops accruing points after logging has stopped — by proving its
    /// rendered point count <b>converges to a stable value</b>. Once the session goes inactive no
    /// samples reach the plot (<c>HandleChannelUpdate</c> early-returns on <c>!Active</c>), so the
    /// count stops rising; but the stats indicator recomputes only about once a second, so it takes
    /// a tick or two to catch up to the final pre-stop points (and a brief post-stop pipeline drain
    /// may add a few). This polls for two consecutive equal readings spaced safely longer than the
    /// indicator's ~1 s refresh — so a genuinely frozen plot settles within a tick, while a plot
    /// that kept streaming at any real rate would never produce two equal readings and so would
    /// (correctly) fail. Convergence is the proof it stopped accruing. Call only after
    /// <see cref="StopLogging"/> has confirmed "LOGGING OFF".
    /// </summary>
    protected void AssertPlotStoppedAccruing(TimeSpan settleTimeout)
    {
        // Read interval comfortably exceeds the indicator's ~1 s recompute, guaranteeing at least
        // one refresh between consecutive reads: equal across that gap ⇒ the buffer is frozen;
        // unequal ⇒ still accruing. (Two reads inside one refresh period could alias to the same
        // stale value, so the gap must clear a full period.)
        var previous = -1L;
        var settled = Retry.WhileFalse(
            () =>
            {
                var current = ReadPlotStats().PointCount;
                var isStable = current >= 0 && current == previous;
                previous = current;
                return isStable;
            },
            timeout: settleTimeout,
            interval: TimeSpan.FromMilliseconds(1500),
            throwOnTimeout: false,
            ignoreException: true).Result;

        Assert.IsTrue(
            settled,
            $"The live plot's rendered point count never settled within {settleTimeout.TotalSeconds:N0}s " +
            "of stopping logging — it kept accruing. Stopping logging should freeze the plot (no samples " +
            "reach it once the session is inactive).");
    }
    #endregion

    #region CSV-Export Helpers
    /// <summary>
    /// Polls the app log for the session-finalize line the app emits when a logging session ends
    /// (<c>"Persisted sample count N for session S"</c>) and returns the most recent
    /// <c>(sessionId, sampleCount)</c> pair, or <c>(-1, -1)</c> if none appeared in time. The
    /// count is authoritative — the app logs it only after every buffered sample is flushed and
    /// COUNT-ed — so it is the oracle the export test cross-checks an exported CSV against.
    /// </summary>
    protected (int sessionId, long sampleCount) WaitForPersistedSampleCount(TimeSpan timeout)
    {
        var sessionId = -1;
        var sampleCount = -1L;
        Retry.WhileFalse(
            () =>
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(
                    ReadNewLogText(), @"Persisted sample count (\d+) for session (\d+)");
                if (matches.Count == 0)
                {
                    return false;
                }

                var last = matches[^1];
                sampleCount = long.Parse(last.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                sessionId = int.Parse(last.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            },
            timeout: timeout,
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: false);

        return (sessionId, sampleCount);
    }

    /// <summary>
    /// Exports the most-recently-created logged session via its per-row EXPORT button on the
    /// Logged Data pane's APP LOGS sub-tab. The list renders in insertion order with no sort, so a
    /// just-finalized session is the LAST row; this invokes that row's <c>ExportSessionButton</c>.
    /// (The row template's name/date TextBlocks are not surfaced to UI Automation — only the
    /// action buttons are — so the target row is identified by position, and the exported file's
    /// name confirms which session it was.) The button is a plain <c>Button</c>, so InvokePattern
    /// raises a real click and its bound <c>ExportLoggingSessionCommand</c> runs (cf. gotcha #12,
    /// which only bites bound <c>Command</c>s on check controls); its <c>CommandParameter</c>
    /// targets its own row. With the <c>DAQIFI_TEST_EXPORT_PATH</c> hook active the export writes
    /// <c>{session}.csv</c> into <see cref="ExportHookDirectory"/> with no dialog.
    /// </summary>
    protected void ExportNewestLoggedSession()
    {
        NavigateToTab(LOGGED_DATA_TAB_TEXT);
        SelectAppLogsSubTab();

        var exportButton = Retry.WhileNull(
            () =>
            {
                var items = MainWindow
                    .FindFirstDescendant(cf => cf.ByAutomationId(LOGGED_SESSION_LIST_ID))?.AsListBox()?.Items;
                if (items == null || items.Length == 0)
                {
                    return null;
                }

                // Last row = newest session. Scope the button search to that row.
                return items[^1].FindFirstDescendant(cf => cf.ByAutomationId(EXPORT_SESSION_BUTTON_ID));
            },
            timeout: TimeSpan.FromSeconds(30),
            interval: TimeSpan.FromMilliseconds(500),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage:
                "The per-row EXPORT button on the newest logged-session row was not found. The " +
                "finalized session row may not have rendered yet.").Result!;

        var button = exportButton.AsButton();
        button.WaitUntilEnabled(TimeSpan.FromSeconds(10));
        button.Invoke();
    }

    /// <summary>
    /// Exports every logged session at once via the EXPORT ALL button on the Logged Data pane's
    /// APP LOGS sub-tab. With the <c>DAQIFI_TEST_EXPORT_PATH</c> hook active the app writes one
    /// <c>{session}.csv</c> per session into <see cref="ExportHookDirectory"/> with no dialog.
    /// </summary>
    protected void ExportAllLoggedSessions()
    {
        NavigateToTab(LOGGED_DATA_TAB_TEXT);
        SelectAppLogsSubTab();
        var button = FindByAutomationId(EXPORT_ALL_SESSIONS_BUTTON_ID).AsButton();
        button.WaitUntilEnabled(TimeSpan.FromSeconds(10));
        button.Invoke();
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
    /// Navigates to the Logged Data pane's DEVICE LOGS sub-tab and returns the current SD
    /// card file count (refresh + parse of the "· SD card OK · N files" status line). Marks
    /// the test inconclusive when no SD card is installed and fails on an SD card error.
    /// Counting from the status line works at zero files (the file list itself is hidden
    /// when empty), so it gives a stable baseline before a logging run.
    /// </summary>
    protected int GetSdCardFileCount(int timeoutSeconds = 45)
    {
        NavigateToTab(LOGGED_DATA_TAB_TEXT);
        SelectDeviceLogsSubTab();
        return ReadSdCardFileCountAfterRefresh(timeoutSeconds);
    }

    /// <summary>
    /// Polls (re-refreshing the SD file list) until the SD card file count exceeds
    /// <paramref name="baseline"/> or <paramref name="timeout"/> elapses, returning the last
    /// count read. Uses a SINGLE overall timeout: it selects the DEVICE LOGS sub-tab once,
    /// then each attempt refreshes with a short, bounded status-settle wait — so, unlike
    /// nesting <see cref="GetSdCardFileCount"/> in an outer retry, one slow attempt cannot
    /// blow past the overall budget. Use after stopping an SD logging run to ride out any
    /// brief device-side finalize lag before the new file appears.
    /// </summary>
    protected int WaitForSdCardFileCountAbove(int baseline, TimeSpan timeout)
    {
        NavigateToTab(LOGGED_DATA_TAB_TEXT);
        SelectDeviceLogsSubTab();

        var latest = baseline;
        Retry.WhileFalse(
            () =>
            {
                // Short per-attempt settle keeps a single attempt well under the overall timeout.
                latest = ReadSdCardFileCountAfterRefresh(settleTimeoutSeconds: 10);
                return latest > baseline;
            },
            timeout: timeout,
            interval: TimeSpan.FromSeconds(1),
            throwOnTimeout: false);

        return latest;
    }

    /// <summary>
    /// Refreshes the SD card file list (assumes the DEVICE LOGS sub-tab is already selected),
    /// waits up to <paramref name="settleTimeoutSeconds"/> for the SD status line to reach a
    /// definitive state, and returns the file count parsed from it. Marks the test
    /// inconclusive when no SD card is installed and fails on an SD card error.
    /// </summary>
    private int ReadSdCardFileCountAfterRefresh(int settleTimeoutSeconds)
    {
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
            timeout: TimeSpan.FromSeconds(settleTimeoutSeconds),
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

    /// <summary>
    /// Waits (polling) until the Live Graph pane's "Logging to Device" status overlay reaches the
    /// expected display state. The overlay Border's Visibility binds to
    /// <c>DaqifiViewModel.IsSdCardLoggingActive</c>; the Border itself has no automation peer, so
    /// this keys off the <c>SdLoggingElapsedText</c> TextBlock inside it — a peer-bearing control
    /// rendered (and thus in the UIA tree) only while the overlay is — making its presence/absence
    /// a faithful displayed/hidden signal. The overlay appears only when a connected device actually
    /// reports SD-card logging — the device sets <c>IsLoggingToSdCard</c> and raises PropertyChanged,
    /// which the view model forwards to the binding — so this is a stronger end-to-end signal than
    /// the "Enabled SD card logging" log line alone, which the device emits even if its reported
    /// <c>IsLoggingToSdCard</c> never flips true. Navigates to the Live Graph pane first (the overlay
    /// is only realized in the tree while that tab is selected).
    /// </summary>
    /// <param name="shouldBeDisplayed">True to wait until the overlay is shown; false until it is hidden.</param>
    protected void WaitForSdLoggingOverlay(bool shouldBeDisplayed, TimeSpan timeout)
    {
        NavigateToTab(LIVE_GRAPH_TAB_TEXT);
        Retry.WhileFalse(
            () =>
            {
                var present = MainWindow.FindFirstDescendant(
                    cf => cf.ByAutomationId(SDCARD_LOGGING_ELAPSED_TEXT_ID)) != null;
                return present == shouldBeDisplayed;
            },
            timeout: timeout,
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage: shouldBeDisplayed
                ? "The Live Graph 'Logging to Device' overlay never appeared while SD-card logging. " +
                  "IsSdCardLoggingActive stayed false — either the device did not report " +
                  "IsLoggingToSdCard=true, or the view model did not forward its PropertyChanged to " +
                  "the overlay's Visibility binding."
                : "The Live Graph 'Logging to Device' overlay did not disappear after SD-card logging " +
                  "stopped — IsSdCardLoggingActive stayed true, so the live plot never returned.");
    }

    /// <summary>
    /// Reads the elapsed-time clock shown inside the "Logging to Device" overlay (the
    /// <c>SdLoggingElapsedText</c> hook, whose UIA Name is pinned to the bound HH:mm:ss value).
    /// Assumes the overlay is displayed — call after <see cref="WaitForSdLoggingOverlay"/> with
    /// <c>shouldBeDisplayed: true</c>. Polls briefly so a transient UIA read miss does not return a
    /// spurious empty string. Returns the raw clock text, or an empty string if it never read.
    /// </summary>
    protected string ReadSdLoggingElapsed(TimeSpan? timeout = null)
    {
        var value = string.Empty;
        Retry.WhileFalse(
            () =>
            {
                value = MainWindow.FindFirstDescendant(
                    cf => cf.ByAutomationId(SDCARD_LOGGING_ELAPSED_TEXT_ID))?.Name?.Trim() ?? string.Empty;
                return value.Length > 0;
            },
            timeout: timeout ?? TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(250),
            throwOnTimeout: false,
            ignoreException: true);
        return value;
    }
    #endregion

    #region SD-Card Import Helpers
    /// <summary>
    /// Imports a single SD card log file through the DEVICE LOGS sub-tab and returns the
    /// imported file's name (as read from its row). Selects the DEVICE LOGS sub-tab, waits
    /// for the file rows to realize, picks a target row — the file named
    /// <paramref name="targetFileName"/> when given (e.g. the file a logging run just
    /// wrote), otherwise the first file — selects it, invokes its per-row IMPORT button,
    /// then waits for and dismisses the completion dialog. Fails the test if the app reports
    /// an import failure.
    /// </summary>
    /// <param name="targetFileName">
    /// Exact name of the file to import; pass null/empty to import the first file. When the
    /// named file is not present in the list, falls back to the first file.
    /// </param>
    /// <param name="importTimeout">How long to allow the download/parse to complete.</param>
    /// <returns>The name of the file that was imported (empty if it could not be read).</returns>
    protected string ImportSdCardFile(string? targetFileName = null, TimeSpan? importTimeout = null)
    {
        NavigateToTab(LOGGED_DATA_TAB_TEXT);
        SelectDeviceLogsSubTab();

        var list = FindByAutomationId(SDCARD_FILE_LIST_ID, timeoutSeconds: 20);
        var listBox = list.AsListBox();

        // The file list is collapsed (absent from the UIA tree) until HasFiles is true, so
        // a realized list with rows means the device reported at least one SD log file.
        Retry.WhileEmpty(
            () => listBox.Items,
            timeout: TimeSpan.FromSeconds(20),
            interval: TimeSpan.FromMilliseconds(400),
            throwOnTimeout: true,
            timeoutMessage:
                "The SD card file list is realized but no file rows appeared. The device " +
                "may have no log files on its SD card; stage at least one and re-run.");

        var rows = listBox.Items;

        // Default to the first file; when a target name is given, import that exact file.
        var target = rows[0];
        var targetName = ReadSdCardFileRowName(rows[0]);
        if (!string.IsNullOrEmpty(targetFileName))
        {
            foreach (var row in rows)
            {
                var name = ReadSdCardFileRowName(row);
                if (string.Equals(name, targetFileName, StringComparison.OrdinalIgnoreCase))
                {
                    target = row;
                    targetName = name;
                    break;
                }
            }
        }

        // Select the row (faithful to "select a file and import it"); the per-row button's
        // CommandParameter binds to its own row regardless, so this is belt-and-suspenders.
        try
        {
            target.Patterns.SelectionItem.Pattern.Select();
        }
        catch (Exception ex)
        {
            // Selection is optional (the IMPORT button targets its own row via
            // CommandParameter); surface it for diagnostics rather than failing the import.
            TestContext?.WriteLine($"SD file row selection skipped: {ex.Message}");
        }

        // Invoke the row's IMPORT button. A plain Button's InvokePattern raises a real
        // click (OnClick), so its bound ImportFileCommand runs — unlike the check controls
        // in gotcha #12. Scope to the row first so a specific file is targeted; fall back
        // to the first matching button anywhere in the list.
        var importButton = Retry.WhileNull(
            () => target.FindFirstDescendant(cf => cf.ByAutomationId(IMPORT_SDCARD_FILE_BUTTON_ID))
                  ?? MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(IMPORT_SDCARD_FILE_BUTTON_ID)),
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage: "IMPORT button not found for the selected SD card file row.").Result!;

        var button = importButton.AsButton();
        button.WaitUntilEnabled(TimeSpan.FromSeconds(10));
        button.Invoke();

        WaitAndDismissImportDialog(importTimeout ?? TimeSpan.FromSeconds(120));

        return targetName;
    }

    /// <summary>
    /// Reads the file name shown in an SD card file row by returning the first non-empty
    /// Text descendant. The NAME column is the first cell in the row's GridView template,
    /// so the first realized text element is the file name.
    /// </summary>
    private static string ReadSdCardFileRowName(AutomationElement row)
    {
        // Prefer the dedicated NAME-cell id so other text in the row — the CREATED/FORMAT
        // cells and the IMPORT button's "IMPORT" label — can never be mistaken for the file
        // name (UIA descendant enumeration order is not a guaranteed contract).
        var nameCell = row.FindFirstDescendant(cf => cf.ByAutomationId(SDCARD_FILE_NAME_TEXT_ID));
        if (nameCell != null && !string.IsNullOrWhiteSpace(nameCell.Name))
        {
            return nameCell.Name.Trim();
        }

        // Fallback: the first non-empty Text descendant (NAME is the first cell in the row).
        foreach (var text in row.FindAllDescendants(cf => cf.ByControlType(ControlType.Text)))
        {
            var name = text.Name;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Reads the file names currently shown in the SD card file list, or an empty list when
    /// no files are present (the list is collapsed and absent from the UIA tree then).
    /// Assumes the DEVICE LOGS sub-tab is selected and a refresh has already settled — call
    /// it right after <see cref="GetSdCardFileCount"/> / <see cref="WaitForSdCardFileCountAbove"/>.
    /// Used to identify the file a logging run just wrote (the name in "after" but not "before").
    /// </summary>
    protected IReadOnlyList<string> ReadSdCardFileNames()
    {
        var list = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(SDCARD_FILE_LIST_ID));
        if (list == null)
        {
            return Array.Empty<string>();
        }

        var names = new List<string>();
        foreach (var row in list.AsListBox().Items)
        {
            var name = ReadSdCardFileRowName(row);
            if (!string.IsNullOrEmpty(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>
    /// Waits for the post-import MahApps dialog (hosted inside the MetroWindow) and
    /// dismisses it via its "OK" affirmative button. Detects the "Import Failed" title
    /// before dismissing and fails the test if present, so a failed import surfaces with a
    /// clear message rather than only as a missing session later.
    /// </summary>
    private void WaitAndDismissImportDialog(TimeSpan timeout)
    {
        var ok = Retry.WhileNull(
            () => FindDialogButtonByName(IMPORT_DIALOG_OK_BUTTON_TEXT),
            timeout: timeout,
            interval: TimeSpan.FromMilliseconds(400),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage:
                "The SD card import did not complete: no completion dialog appeared within " +
                $"{timeout.TotalSeconds:N0}s. The download/parse may have hung or the device " +
                "may have disconnected.").Result!;

        var failed = MainWindow.FindFirstDescendant(
            cf => cf.ByControlType(ControlType.Text).And(cf.ByName(IMPORT_FAILED_TITLE))) != null;

        ok.AsButton().Invoke();

        // Wait for the dialog to close so the overlay no longer blocks later navigation.
        Retry.WhileFalse(
            () => FindDialogButtonByName(IMPORT_DIALOG_OK_BUTTON_TEXT) == null,
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: false,
            ignoreException: true);

        if (failed)
        {
            Assert.Fail(
                "SD card import reported failure: the app showed its 'Import Failed' dialog. " +
                "See the captured app log for the underlying exception.");
        }
    }

    /// <summary>Finds a Button descendant of the main window whose Name matches (ignoring case).</summary>
    private AutomationElement? FindDialogButtonByName(string name)
    {
        foreach (var button in MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Button)))
        {
            if (string.Equals(button.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                return button;
            }
        }

        return null;
    }

    /// <summary>
    /// Polls the app log for the importer's "Imported N samples for session" line and
    /// returns the largest N seen since the fixture started, or -1 if none. A positive
    /// value is out-of-process proof the imported session holds real sample data.
    /// </summary>
    protected int WaitForImportedSampleCount(TimeSpan timeout)
    {
        var count = -1;
        Retry.WhileFalse(
            () =>
            {
                count = ReadMaxImportedSampleCount();
                return count >= 0;
            },
            timeout: timeout,
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: false);
        return count;
    }

    /// <summary>Parses the maximum "Imported N samples for session" count from new log text (-1 if none).</summary>
    private int ReadMaxImportedSampleCount()
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(
            ReadNewLogText(), @"Imported (\d+) samples for session");
        var max = -1;
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var value = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (value > max)
            {
                max = value;
            }
        }

        return max;
    }
    #endregion

    #region Profiles Helpers
    /// <summary>
    /// Reads the number of saved profiles on the Profiles pane by counting the per-tile settings
    /// (gear) buttons under the profile list — each tile renders exactly one. This is robust to
    /// the tile's inner layout (its name/date TextBlocks are not reliably surfaced to UI
    /// Automation, like the logged-session rows). Returns 0 when no profiles exist (the
    /// empty-state view renders instead of the list, so the list element is absent from the
    /// tree). Navigates to the Profiles tab first.
    /// </summary>
    protected int GetProfileCount()
    {
        NavigateToTab(PROFILES_TAB_TEXT);
        var list = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(PROFILE_LIST_ID));
        if (list == null)
        {
            return 0;
        }

        return list.FindAllDescendants(cf => cf.ByAutomationId(PROFILE_SETTINGS_BUTTON_ID)).Length;
    }

    /// <summary>Waits (polling) until the saved-profile count equals <paramref name="expected"/>.</summary>
    protected void WaitForProfileCount(int expected, TimeSpan timeout)
    {
        Retry.WhileFalse(
            () => GetProfileCount() == expected,
            timeout: timeout,
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage:
                $"Saved-profile count did not reach {expected} within {timeout.TotalSeconds}s " +
                $"(last read {GetProfileCount()}).");
    }

    /// <summary>
    /// Opens the new-profile drawer by invoking the "+ ADD PROFILE" button — the status-bar
    /// button when profiles already exist, or the empty-state button otherwise. Waits until the
    /// new-profile NAME field is realized, confirming the drawer opened in new-profile mode.
    /// </summary>
    protected void OpenNewProfileDrawer()
    {
        NavigateToTab(PROFILES_TAB_TEXT);

        var addButton = Retry.WhileNull(
            () => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(ADD_PROFILE_BUTTON_ID))
                  ?? MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(ADD_PROFILE_BUTTON_EMPTY_ID)),
            timeout: TimeSpan.FromSeconds(30),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage: "Add Profile button not found on the Profiles pane.").Result!;

        var button = addButton.AsButton();
        button.WaitUntilEnabled(TimeSpan.FromSeconds(10));
        button.Invoke();

        // The NAME field exists only while the drawer is open in new-profile mode.
        FindByAutomationId(NEW_PROFILE_NAME_INPUT_ID);
    }

    /// <summary>
    /// Types <paramref name="name"/> into the new-profile NAME field via the ValuePattern and
    /// waits until the field reads it back. Assumes the new-profile drawer is open.
    /// </summary>
    protected void SetNewProfileName(string name)
    {
        FindByAutomationId(NEW_PROFILE_NAME_INPUT_ID).Patterns.Value.Pattern.SetValue(name);

        Retry.WhileFalse(
            () => string.Equals(
                MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(NEW_PROFILE_NAME_INPUT_ID))
                    ?.Patterns.Value.Pattern.Value.Value,
                name,
                StringComparison.Ordinal),
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(200),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage: $"The new-profile NAME field did not accept the value '{name}'.");
    }

    /// <summary>
    /// Invokes "CAPTURE FROM CURRENT SETTINGS" in the new-profile drawer
    /// (<c>SaveCurrentSettingsCommand</c>), which snapshots every connected device's currently
    /// active channels + sampling frequency into a new profile and closes the drawer. Waits
    /// until the drawer closes. A plain Button's InvokePattern raises a real click, so its bound
    /// command runs.
    /// </summary>
    protected void SaveCurrentSettingsAsProfile()
    {
        var capture = FindByAutomationId(SAVE_CURRENT_SETTINGS_BUTTON_ID).AsButton();
        capture.WaitUntilEnabled(TimeSpan.FromSeconds(10));
        capture.Invoke();
        WaitForNewProfileDrawerClosed();
    }

    /// <summary>
    /// Ticks the first connected-device checkbox in the new-profile form via the TogglePattern.
    /// The checkbox's <c>IsChecked</c> binding drives selection directly (no bound command), so
    /// the pattern works from a background host. Selecting a device satisfies
    /// <c>CanSaveNewProfile</c>, enabling the SAVE PROFILE button.
    /// </summary>
    protected void SelectFirstNewProfileDevice()
    {
        Retry.WhileFalse(
            () =>
            {
                var checkbox = MainWindow
                    .FindFirstDescendant(cf => cf.ByAutomationId(NEW_PROFILE_DEVICE_CHECKBOX_ID))?.AsCheckBox();
                if (checkbox == null)
                {
                    return false;
                }

                if (checkbox.IsChecked != true)
                {
                    checkbox.IsChecked = true;
                }

                return checkbox.IsChecked == true;
            },
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage: "Could not select a device in the new-profile form (checkbox never became checked).");
    }

    /// <summary>
    /// Invokes "SAVE PROFILE" in the new-profile drawer (<c>SaveNewProfileCommand</c>), which
    /// persists the form's selections as a new profile and closes the drawer. Requires a name
    /// (auto-populated) and at least one selected device. Waits until the drawer closes.
    /// </summary>
    protected void SaveNewProfileFromForm()
    {
        var save = FindByAutomationId(SAVE_NEW_PROFILE_BUTTON_ID).AsButton();
        save.WaitUntilEnabled(TimeSpan.FromSeconds(10));
        save.Invoke();
        WaitForNewProfileDrawerClosed();
    }

    /// <summary>Waits until the new-profile drawer closes (its NAME field leaves the UIA tree).</summary>
    private void WaitForNewProfileDrawerClosed()
    {
        Retry.WhileFalse(
            () => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(NEW_PROFILE_NAME_INPUT_ID)) == null,
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage: "The new-profile drawer did not close after saving.");
    }

    /// <summary>
    /// Opens the edit drawer for the most-recently-created profile by invoking the settings
    /// (gear) button on the LAST profile tile. Profiles render in insertion order with no sort,
    /// so the newest — the one a test just created — is last. Waits until the drawer's
    /// ACTIVATE/DEACTIVATE button is realized. Navigates to the Profiles tab first; entering the
    /// tab rebuilds the pane with the drawer closed, so the gear is reachable underneath.
    /// </summary>
    protected void OpenLastProfileEditDrawer()
    {
        NavigateToTab(PROFILES_TAB_TEXT);

        var gear = Retry.WhileNull(
            () =>
            {
                var gears = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(PROFILE_LIST_ID))
                    ?.FindAllDescendants(cf => cf.ByAutomationId(PROFILE_SETTINGS_BUTTON_ID));
                return gears is { Length: > 0 } ? gears[^1] : null;
            },
            timeout: TimeSpan.FromSeconds(30),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage: "No profile tiles were found on the Profiles pane.").Result!;

        var button = gear.AsButton();
        button.WaitUntilEnabled(TimeSpan.FromSeconds(10));
        button.Invoke();

        // The ACTIVATE/DELETE buttons exist only while the edit drawer is open.
        FindByAutomationId(ACTIVATE_PROFILE_BUTTON_ID);
    }

    /// <summary>
    /// Invokes the ACTIVATE PROFILE button in the open edit drawer (the profile must be
    /// inactive). The command re-applies the profile's captured sampling frequency + channels to
    /// the matching connected device; the caller verifies that via the Devices flyout and the
    /// Channels pane "n / N ACTIVE" indicator.
    /// </summary>
    protected void ActivateSelectedProfile()
    {
        var button = FindByAutomationId(ACTIVATE_PROFILE_BUTTON_ID).AsButton();
        button.WaitUntilEnabled(TimeSpan.FromSeconds(10));
        button.Invoke();
    }

    /// <summary>
    /// Deactivates the active profile by invoking the (same) activate/deactivate button in the
    /// open edit drawer, then waits until no profile reports active. Deactivation is confirmed by
    /// the Profiles status-bar "ACTIVE" indicator leaving the tree — an INDEPENDENT signal driven
    /// by a Visibility binding on the active-profile name, not the button's own swapped label
    /// (a DataTrigger-driven content swap may not surface as a UIA name change). A profile must
    /// be deactivated before it can be deleted.
    /// </summary>
    protected void DeactivateSelectedProfile()
    {
        var button = FindByAutomationId(ACTIVATE_PROFILE_BUTTON_ID).AsButton();
        button.WaitUntilEnabled(TimeSpan.FromSeconds(10));
        button.Invoke();

        Retry.WhileTrue(
            () => IsAnyProfileActiveIndicatorPresent(),
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage:
                "The profile did not deactivate: the Profiles status-bar 'ACTIVE' indicator " +
                "stayed visible after invoking the deactivate button.");
    }

    /// <summary>
    /// Invokes the DELETE PROFILE button in the open edit drawer (<c>DeleteProfileCommand</c>).
    /// The profile must already be deactivated — the command refuses (and surfaces an inline
    /// error) while any profile is active. The command removes the profile and closes the drawer.
    /// </summary>
    protected void DeleteSelectedProfile()
    {
        var button = FindByAutomationId(DELETE_PROFILE_BUTTON_ID).AsButton();
        button.WaitUntilEnabled(TimeSpan.FromSeconds(10));
        button.Invoke();
    }

    /// <summary>
    /// True when the Profiles pane shows its "· {name} ACTIVE" status-bar indicator, i.e. some
    /// profile is active. The indicator carries a dedicated AutomationId and its Visibility binds
    /// to the active-profile name (collapsed, and so absent from the UIA tree, when none is
    /// active), so an id lookup is a deterministic signal — unaffected by profile names (a profile
    /// named "...ACTIVE" would defeat a substring scan of the pane's text).
    /// </summary>
    private bool IsAnyProfileActiveIndicatorPresent() =>
        MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(ACTIVE_PROFILE_INDICATOR_ID)) != null;

    /// <summary>
    /// Waits (polling) until the per-device sampling frequency flyout reads
    /// <paramref name="expectedHz"/> (within 0.5 Hz) and returns the value read back. Used to
    /// confirm a profile activation re-applied its captured frequency to the device.
    /// </summary>
    protected double WaitForSamplingFrequency(double expectedHz, TimeSpan timeout)
    {
        var last = double.NaN;
        Retry.WhileFalse(
            () =>
            {
                last = GetSamplingFrequency();
                return Math.Abs(last - expectedHz) < 0.5;
            },
            timeout: timeout,
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage:
                $"Sampling frequency did not reach {expectedHz} Hz (last read {last}). Expected " +
                "the activated profile to re-apply its captured frequency.");

        return last;
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
