# Daqifi.Desktop.UITest — FlaUI UI-Automation Harness

This project drives the **real DAQiFi Desktop GUI out-of-process** (black box, via the
Windows UI Automation tree) against a **physically attached device** (USB serial or
WiFi). It is the **integration gate** of the development loop — separate from the fast
unit gate (`dotnet test` on the MSTest+Moq suites), which needs no hardware.

It covers four end-to-end workflows plus a launch smoke test:

| Test | What it verifies |
|---|---|
| `LaunchSmokeTests.Launch_MainWindowAppears_AndIsResponsive` | App launches with no prompts; main window appears and is responsive |
| `AddDeviceTests.AddDevice_ConnectsToAttachedDevice` | Open connection dialog → discover → connect; device shows in connected list |
| `ConfigureLoggingTests.ConfigureLogging_SetsFrequencyAndChannels` | Set sample frequency (device flyout) + enable analog channels; read both back |
| `LoggingSessionTests.StartLoggingSession_RunsAndStops` | Start logging → data accrues (new session row) → stop |
| `SdCardLoggingTests.SdCardLogging_LogsToSdCard_NotStream` | Switch to **Log to Device** (SD card) mode → run a session → a new file appears on the SD card (file count increased), confirming SD-card logging, not a stream session. **USB only; needs an SD card.** |

Every UI test is tagged `[TestCategory("Ui")]` and `[TestCategory("RequiresDevice")]`
so it never runs as part of the unit gate.

---

## Running it

### Prerequisites
- **A DAQiFi device physically attached and powered on** (USB by default).
- **An interactive, unlocked Windows desktop session.** These tests launch and read a
  real GUI; they cannot run headless / over plain SSH / on a locked workstation.
- The app's Debug exe (the harness builds it via a `ProjectReference`, then launches the
  produced `Daqifi.Desktop\bin\Debug\net10.0-windows\DAQiFi.exe`).

### Commands (run from the repo root)

```powershell
# All UI scenarios (builds the app + the test project first)
dotnet test Daqifi.Desktop.UITest

# A single scenario
dotnet test Daqifi.Desktop.UITest --filter "FullyQualifiedName~AddDevice_ConnectsToAttachedDevice"

# Only the device-gated tests
dotnet test Daqifi.Desktop.UITest --filter "TestCategory=RequiresDevice"
```

> **Output tip for AI sessions:** the app build emits a large wall of pre-existing
> analyzer warnings that can bury the test result. Redirect to a file and grep the
> outcome:
> ```powershell
> dotnet test Daqifi.Desktop.UITest -c Debug --nologo *> run.txt
> Get-Content run.txt | Select-String "Passed!|Failed!|Total tests|Error Message"
> ```

### Transport selection
The connect scenario is parameterised. Default is **Serial (USB)**.
```powershell
$env:DAQIFI_TEST_TRANSPORT = "Serial"   # or "Wifi"  (omit/clear for the Serial default)
```
WiFi additionally requires the firewall rule for UDP 30303 to already exist (seed it once,
elevated, outside this harness — see CLAUDE.md “Device Communication”).

### The two gates of the loop
```powershell
# Fast inner gate — every edit, NO hardware:
dotnet test --filter "TestCategory!=Ui&FullyQualifiedName!~WindowsFirewallWrapperTests"

# Integration gate — device attached, on demand:
dotnet test Daqifi.Desktop.UITest
```

### Running it for a specific PR
```powershell
gh pr checkout <PR_NUMBER>          # or: git checkout <branch>
dotnet build "DAQiFi Desktop.sln" -c Debug
# (attach the device, then:)
dotnet test Daqifi.Desktop.UITest -c Debug
```
Each test launches a **fresh app instance** and disconnects on teardown, so the suite is
self-contained. The test-mode database lives in `%LOCALAPPDATA%\DAQiFi` and persists
logged sessions across runs; tests assert on *deltas* (e.g. session count increased), so a
non-empty DB is fine. Delete that folder for a truly clean slate.

---

## How the unattended launch works (`DAQIFI_TEST_MODE`)

The production app ships a `requireAdministrator` manifest and, when not elevated, can pop
modal dialogs (firewall warning) — both of which would hang an unattended launch. The
harness launches the Debug exe with the environment variable **`DAQIFI_TEST_MODE=1`**
(set by `DaqifiAppFixture` on the child process), which triggers, in the app:

- **`App.IsTestMode`** (`App.xaml.cs`) reads the env var at startup.
- A Debug-only **`asInvoker` manifest** (`app.Debug.manifest`, selected by an MSBuild
  `Condition`) so there is **no UAC prompt**. Release keeps `requireAdministrator`.
- **No-op message box** (`NoOpMessageBoxService` via `FirewallConfiguration.SetMessageBoxService`)
  so nothing modal can appear in test mode.
- The **data directory and logs resolve to `%LOCALAPPDATA%\DAQiFi`** (database + `Logs\`)
  instead of the shared `%ProgramData%` location (which an un-elevated process cannot write —
  it would crash at startup with “attempt to write a readonly database”).
- **Firewall init is skipped** (it requires admin).

This is governed by `AppDataPaths` (in `Daqifi.Desktop.Common`), the single source of truth:
**elevated** runs use machine-wide `%ProgramData%`; **any un-elevated run** — the harness
*or* a normal non-admin Debug launch — uses per-user `%LOCALAPPDATA%` and skips firewall
init. Production (Release) is always elevated, so its `%ProgramData%` paths are unchanged.

---

## Architecture

- **`DaqifiAppFixture`** — base fixture (`[TestInitialize]` / `[TestCleanup]`). Launches the
  exe in test mode, waits for the main window, exposes reusable helpers, and on failure
  captures a screenshot + the NLog log into the test output. All scenario classes inherit it.
- **Scenario classes** — `AddDeviceTests`, `ConfigureLoggingTests`, `LoggingSessionTests`,
  `LaunchSmokeTests`. Each is independent; setup connects/configures fresh, teardown closes
  the app.
- **Assertions are black-box**: visible UI state (via UI Automation) plus the NLog log file
  (`...\DAQiFi\Logs\DAQifiAppLog.log` — under `%LOCALAPPDATA%` in test mode, `%ProgramData%`
  for elevated production runs; the fixture probes both). **Do not** reference app internals
  for assertions.
- **Readiness waits** use FlaUI `Retry`/`WaitUntil*` — never fixed `Thread.Sleep` for
  readiness. (A couple of *deliberate, documented* sleeps exist for known binding delays,
  e.g. the frequency slider’s `Delay=500`.)

### AutomationIds — where they live
IDs are added only on the controls the scenarios touch. The panes are the
`View/Prototype/*.xaml` files (they are the **live** views despite the “Prototype” suffix —
`MainWindow.xaml` hosts them; confirm against the runtime tree, not a prototype twin).

| Logical control | AutomationId | File |
|---|---|---|
| Add Device button (status bar / empty state) | `AddDeviceButton` / `AddDeviceButtonEmpty` | `View/Prototype/DevicesPanePrototype.xaml` |
| Connected-device list | `ConnectedDeviceList` | `View/Prototype/DevicesPanePrototype.xaml` |
| Device settings gear (opens frequency flyout) | `DeviceSettingsButton` | `View/Prototype/DevicesPanePrototype.xaml` |
| Sample-frequency slider (in the device flyout) | `SamplingFrequencyInput` | `View/Prototype/DevicesPanePrototype.xaml` |
| Connection dialog tabs | `ConnTab_Wifi` / `ConnTab_Manual` / `ConnTab_Serial` | `View/ConnectionDialog.xaml` |
| Discovered / serial device lists | `DiscoveredDeviceList` / `SerialPortList` | `View/ConnectionDialog.xaml` |
| Connect buttons | `ConnectButton_Wifi` / `ConnectButton_Manual` / `ConnectButton_Serial` | `View/ConnectionDialog.xaml` |
| Channel list + “SELECT ALL” (analog) | `ChannelList` / `SelectAllAnalogChannels` | `View/Prototype/ChannelsPanePrototype.xaml` |
| Logging toggle + status label | `StartLoggingToggle` / `LoggingStatusText` | `View/Prototype/LiveGraphPane.xaml` |
| Logged-session list | `LoggedSessionList` | `View/Prototype/LoggedDataPanePrototype.xaml` |
| Logging-mode selector (device drawer) | `LoggingModeStreamToApp` / `LoggingModeLogToDevice` | `View/Prototype/DevicesPanePrototype.xaml` |
| SD-card data-format selector (visible only in Log-to-Device mode) | `SdCardFormatSelector` | `View/Prototype/DevicesPanePrototype.xaml` |
| Logged Data → DEVICE LOGS sub-tab | `DeviceLogsTab` | `View/Prototype/LoggedDataPanePrototype.xaml` |
| SD refresh button / status line / file list | `RefreshSdCardFilesButton` / `SdCardStatusText` / `SdCardFileList` | `View/DeviceLogsView.xaml` |

---

## ⚠️ Non-obvious gotchas (hard-won — read before changing the harness or the views)

These are the things that make out-of-process automation of *this* app work. Several are
backed by small, deliberate app-side changes — **do not revert them** without understanding
why.

1. **`PART_SelectedContentHost` is load-bearing.** The nav `TabControl` (`MainWindow.xaml`)
   and the connection dialog `TabControl` (`ConnectionDialog.xaml`) use custom templates.
   WPF only exposes a `TabControl`’s **selected content** to UI Automation through a content
   host named exactly **`PART_SelectedContentHost`** (via `TabItemAutomationPeer`). Without
   that name the entire pane/dialog content is *rendered but invisible to automation* — no
   AutomationId inside any tab is reachable. Keep `ContentSource="SelectedContent"` on it.

2. **The main window is a `MetroWindow`, behind a splash.** `Application.GetMainWindow`
   returns the WPF `SplashScreen` during startup. Wait for the window whose `ClassName` is
   `MetroWindow` (see `MAIN_WINDOW_CLASS`).

3. **The connection dialog is modal/owned.** It is shown via `Window.ShowDialog()`, so it is
   **not** in `GetAllTopLevelWindows`. Find it in `MainWindow.ModalWindows`.

4. **Connect does not auto-close the dialog under automation.** It does interactively, but
   under automation the modal does not reliably close. Assert success by a device appearing
   in `ConnectedDeviceList`, then close the dialog explicitly.

5. **Frequency lives in the per-device flyout, not Profiles.** Connect → on the **Devices**
   pane click the gear (`DeviceSettingsButton`) → a flyout exposes `SamplingFrequencyInput`.
   The slider binds with `Delay=500`, so after setting it, wait out the delay before
   navigating away (navigation closes the flyout and cancels a pending commit). Profiles are
   saved experiment presets — *not* the frequency-setting path.

6. **Enable channels via “SELECT ALL”, not per-tile clicks.** Individual channel tiles toggle
   only through a `MouseBinding` (a real foreground mouse click), which does **not land
   reliably from a background test host**. The `SelectAllAnalogChannels` button exposes the
   InvokePattern and works regardless of foreground. Verify via the pane’s ground-truth
   `n / N ACTIVE` indicator (the old “is the value TextBlock visible” heuristic was a false
   positive — it read the always-visible type label).

7. **The logging toggle is gated by active channels.** `StartLoggingToggle` is disabled until
   `CanToggleLogging` (= `ActiveChannels.Count > 0`). Enable channels *before* expecting the
   toggle to be enabled.

8. **`LoggingStatusText` reads its state from the toggle.** The label swaps Text via a Style
   `DataTrigger`, which WPF historically did not surface as a UIA Name change. The view now
   pins `AutomationProperties.Name="{Binding Text, RelativeSource=Self}"` so the label is
   accessible; the harness still falls back to the toggle’s `TogglePattern` state for
   robustness.

9. **Background test host ⇒ window often isn’t foreground ⇒ physical clicks miss.** Prefer
   `InvokePattern` / `TogglePattern` / `SelectionItemPattern` / `RangeValuePattern` over
   `.Click()` wherever a control exposes a pattern. `SetForegroundWindow` from a background
   process is restricted by Windows and is not a reliable fix.

10. **`NavigateToTab` retries selection.** While a device streams, UIA calls can transiently
    fail (`0x80040201`); the helper re-resolves and re-selects until the tab reports selected.
    Only the *selected* tab’s content is realised in the tree, so navigate before searching
    for a control on another pane (e.g. `StopLogging` navigates back to Live Graph).

11. **Invalid `PackIconMaterial` Kinds crash panes at load.** Icon `Kind` is parsed at runtime,
    so a bad value (e.g. the former `SdStorage`) throws `XamlParseException` and destabilises
    the app when that pane loads. Use valid Material icon names.

12. **A `RadioButton`/`ToggleButton` bound `Command` does NOT fire from automation — drive it
    through `IsChecked` instead.** A `ButtonBase.Command` executes only on a real `Click`
    (mouse/keyboard/access-key). The UIA patterns a background host can raise on a check control —
    `SelectionItem.Select()` (RadioButton) and `Toggle.Toggle()` (ToggleButton) — set `IsChecked`
    but never call `OnClick`, so a bound `Command` silently never runs and the action doesn’t
    happen. Controls whose *effect* is driven by `IsChecked` itself work fine via these patterns:
    the `StartLoggingToggle` (`IsChecked` TwoWay → `IsLogging` setter) and the Logged-Data
    `DeviceLogsTab` (its `IsChecked` drives the view’s visibility via an `ElementName` binding)
    are both automatable as-is. The **logging-mode segmented selector** was the exception — it
    used `Command="{Binding SetLoggingModeCommand}"`, so `Select()` checked the button visually
    but the device never switched mode. Fix: the two RadioButtons now forward their `Checked`
    event to the command via a `Microsoft.Xaml.Behaviors` `EventTrigger`/`InvokeCommandAction`
    (the `SetLoggingMode` setter is idempotent, so the redundant `Checked` raised when the OneWay
    `IsChecked` binding refreshes is a harmless no-op). Confirm a mode switch by an **independent**
    signal — `SetLoggingMode` waits for the `SdCardFormatSelector` (rendered only while
    `Shell.IsLogToDeviceMode` is true), not the radio’s own checked state, which `Select()` sets
    directly regardless of whether the command ran.

---

## Adding a new scenario

1. **Find the control** in the running app. Launch the Debug exe with `DAQIFI_TEST_MODE=1`
   and inspect with FlaUInspect / Accessibility Insights, or write a throwaway
   `[TestCategory("Diag")]` test in this project that dumps the UIA subtree (run with
   `--filter "TestCategory=Diag"`). Confirm you’re on the **live** view, not a prototype twin,
   and remember gotcha #1 (content must be under `PART_SelectedContentHost` to be visible).
2. **Add an AutomationId** (PascalCase) to that control *only*. Prefer a literal id; a
   `{Binding Name}` AutomationId can evaluate empty at element creation (seen on channel
   tiles) — don’t rely on it.
3. **Add a reusable helper** to `DaqifiAppFixture` (e.g. a new `protected` method) and a
   constant for the id, following the existing regions.
4. **Write the test** in a new `*Tests.cs` class inheriting `DaqifiAppFixture`, tagged
   `[TestCategory("Ui")]` and `[TestCategory("RequiresDevice")]`. Assert only through visible
   UI + the NLog log.
5. **Use patterns, not clicks** (gotcha #9) and `Retry`/`WaitUntil*`, never fixed sleeps.
6. **Run with a device attached** and verify green before committing. Conventional commits
   (`test(ui): …`, and `fix(...)` for any app fix the harness surfaces).

If the harness uncovers a real app bug (it’s designed to — see gotcha #11 for one it already
caught), fix it in a separate, clearly-described commit.
