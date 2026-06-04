# Daqifi.Desktop.UITest — FlaUI UI-Automation Harness

This project drives the **real DAQiFi Desktop GUI out-of-process** (black box, via the
Windows UI Automation tree) against a **physically attached device** (USB serial or
WiFi). It is the **integration gate** of the development loop — separate from the fast
unit gate (`dotnet test` on the MSTest+Moq suites), which needs no hardware.

It covers six end-to-end workflows plus a launch smoke test:

| Test | What it verifies |
|---|---|
| `LaunchSmokeTests.Launch_MainWindowAppears_AndIsResponsive` | App launches with no prompts; main window appears and is responsive |
| `AddDeviceTests.AddDevice_ConnectsToAttachedDevice` | Open connection dialog → discover → connect; device shows in connected list |
| `ConfigureLoggingTests.ConfigureLogging_SetsFrequencyAndChannels` | Set sample frequency (device flyout) + enable analog channels; read both back |
| `LoggingSessionTests.StartLoggingSession_RendersLivePlot_RunsStopsAndDeletesSession` | Start logging → **assert the live plot renders believable data while streaming** (issue #560) → data accrues (new session row) → stop → **assert the plot stops accruing** → **delete that session** (issue #557) via its per-row trash action, accepting the dark **in-pane confirm overlay**. The believability check reads the Live Graph pane's **plot-stats hook** (`PlotStatsText`, see gotcha #18) — accessible UI state, not app internals — and asserts **series count == active channels**, **rendered point count strictly increases** over a window (flowing, not frozen), and **values are finite, in a plausible range, and not a dead flatline at zero**; after stop it asserts the point count freezes (no samples reach the plot once the session is inactive). Then asserts the row is gone and the session count returns to its pre-run baseline (the view), with **DB-level deletion proven separately from the app's log lines** — `DeleteLoggingSession completed` present and `Failed in DeleteLoggingSession` absent (the SQL `DELETE`s commit in a transaction; the view removal alone isn't DB-proof, since that delete swallows its exceptions). Leaves the test-mode DB self-cleaned (no per-run session leak). |
| `SdCardLoggingTests.SdCardLogging_LogsToSdCard_ThenImportsToSession` | Full SD lifecycle in one pass: switch to **Log to Device** (SD card) mode → run a session → a new file appears on the SD card (file count increased), confirming SD-card logging not a stream session → assert the Live Graph swaps its empty plot for the **Logging to Device** status overlay (issue #507) with a live HH:mm:ss elapsed clock while logging, and that the overlay disappears once logging stops → then **import that just-written file** (identified by diffing the file list — or a staged `error`-prefixed file when present, opportunistically guarding daqifi-core #195) and assert a new, non-empty `LoggingSession` appears in `LoggedSessionList`. The import is triangulated from the "Import Complete" dialog, the importer's `Imported N samples` log line (N&gt;0), and a +1 session delta. **USB only; needs an SD card.** |
| `CsvExportTests.ExportLoggedSession_ProducesValidCsv` / `…ExportAllLoggedSessions_ProducesValidCsv` | Run a short logging session, then export it to CSV — once via the per-session **EXPORT** button and once via **EXPORT ALL** — through the `DAQIFI_TEST_EXPORT_PATH` hook (**no `SaveFileDialog`**, see below). Read the produced CSV back from disk (black box) and validate it: header is `Time` + one `Device:Serial:Channel` column per channel with no formula-injection prefix; every row has the header's field count (RFC 4180 consistency via a strict quote-aware parser); the time column parses as a round-trip timestamp and is non-decreasing; every value cell is a finite invariant-culture number; and the **value-cell count equals the session's persisted sample count** (each sample → one cell), proving no rows were dropped, duplicated, or corrupted. The sample count is read out-of-process from the app's `Persisted sample count N for session S` finalize log line. |
| `ProfilesTests.SaveActivateDelete_ProfileRoundTrips` / `…CreateProfileViaForm_AppearsAndDeletes` | Full **Profiles** lifecycle. Configure a known state (set a frequency + enable analog channels), **save** it as a profile — once by capturing the live device settings (`SaveCurrentSettingsCommand`) and once via the new-profile form (`SaveNewProfileCommand`) — and assert it appears in the list. Then change the device config to something different (clear channels, set a different frequency), **activate** the saved profile (`ActivateProfileCommand`), and assert the captured **channel + frequency intent is re-applied to the device** — verified through the Channels pane `n / N ACTIVE` ground-truth indicator and the per-device frequency flyout (0 → N active; changed-Hz → captured-Hz). Finally **delete** the profile (`DeleteProfileCommand`) and assert the list returns to its original membership. Each test records the profile count up-front and removes any profile it creates (asserts membership before/after via deltas). A fresh launch never has an active profile (`IsProfileActive` is not persisted to XML), so single-profile activation takes the no-confirm path. **USB or WiFi.** |

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

## Dialog-free CSV export (`DAQIFI_TEST_EXPORT_PATH`)

Logged-session export normally goes through a Win32 `SaveFileDialog`/`FolderBrowserDialog`,
which is fragile to drive out-of-process from a background test host (gotcha #9). Rather than
script the dialog, the app exposes a **test-mode export hook** at the same seam as
`DAQIFI_TEST_MODE`:

- **`AppDataPaths.TestExportPath`** reads the environment variable **`DAQIFI_TEST_EXPORT_PATH`**
  once at startup. When it is set, `ExportLoggingSessionCommand` **and**
  `ExportAllLoggingSessionCommand` (`DaqifiViewModel`) export **straight into that directory**
  — one `{session}.csv` per session via `ExportDialogViewModel.ExportToDirectoryAsync` — and
  **skip the file dialog entirely**. Both commands use the same seam, so single-session export
  and "Export All" are equally dialog-free.
- The hook directory is used for **both** export variants, so one env var covers both: a
  single-session export still lands as `{session}.csv` inside it (the layout otherwise reserved
  for multi-session export). The exporter and its default options (all samples, absolute time)
  are unchanged — only the destination selection is bypassed.
- When the variable is **unset** (every production run, and every scenario that doesn't opt in)
  the interactive dialog behaviour is **completely unchanged**. It is impossible to trigger
  accidentally in production because nothing sets the variable there.

The fixture wires it per scenario: a test overrides `DaqifiAppFixture.ExportHookDirectory` to a
fresh temp directory (`CsvExportTests` does this), which `Setup` passes to the child process as
`DAQIFI_TEST_EXPORT_PATH`. The test then triggers export through the **real UI buttons**
(`ExportSessionButton` per row / `ExportAllSessionsButton`) and reads the resulting CSV from
that directory — zero modal interaction, fully deterministic.

The companion oracle is the **`Persisted sample count N for session S`** log line the app
writes when a session finalizes (`LoggingManager.PersistSessionSampleCount`, emitted after all
buffered samples are flushed). The harness reads `N` from it and asserts the exported CSV holds
exactly `N` value cells.

---

## Architecture

- **`DaqifiAppFixture`** — base fixture (`[TestInitialize]` / `[TestCleanup]`). Launches the
  exe in test mode, waits for the main window, exposes reusable helpers, and on failure
  captures a screenshot + the NLog log into the test output. All scenario classes inherit it.
- **Scenario classes** — `AddDeviceTests`, `ConfigureLoggingTests`, `LoggingSessionTests`,
  `SdCardLoggingTests`, `LaunchSmokeTests`. Each is independent; setup connects/configures
  fresh, teardown closes the app.
- **Assertions are black-box**: visible UI state (via UI Automation) plus the NLog log file
  (`...\DAQiFi\Logs\DAQifiAppLog.log` — under `%LOCALAPPDATA%` in test mode, `%ProgramData%`
  for elevated production runs; the fixture probes both). **Do not** reference app internals
  for assertions.
- **Readiness waits** use FlaUI `Retry`/`WaitUntil*` — never fixed `Thread.Sleep` for
  readiness. (A couple of *deliberate, documented* sleeps exist for known binding delays,
  e.g. the frequency slider’s `Delay=500`.)

### AutomationIds — where they live
IDs are added only on the controls the scenarios touch. Most panes are the
`View/Prototype/*.xaml` files (they are the **live** views despite the “Prototype” suffix —
`MainWindow.xaml` hosts them; confirm against the runtime tree, not a prototype twin). The
**Profiles** pane is the exception: its live view is `View/ProfilesPane.xaml` (no `Prototype`
suffix), also hosted by `MainWindow.xaml`.

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
| Channels “CLEAR ALL” (status bar; clears every section) | `ClearAllChannels` | `View/Prototype/ChannelsPanePrototype.xaml` |
| Logging toggle + status label | `StartLoggingToggle` / `LoggingStatusText` | `View/Prototype/LiveGraphPane.xaml` |
| Live-plot stats hook (invisible; surfaces the rendered plot's ground truth — see gotcha #18) | `PlotStatsText` | `View/Prototype/LiveGraphPane.xaml` |
| SD-card "Logging to Device" overlay — its live elapsed clock, which doubles as the overlay's displayed/hidden hook (the overlay Border has no automation peer; this TextBlock is in the UIA tree only while the overlay is shown — issue #507) | `SdLoggingElapsedText` | `View/Prototype/LiveGraphPane.xaml` |
| Logged-session list | `LoggedSessionList` | `View/Prototype/LoggedDataPanePrototype.xaml` |
| Per-row session **EXPORT** button (one per row) | `ExportSessionButton` | `View/Prototype/LoggedDataPanePrototype.xaml` |
| Per-row session **DELETE** button (one per row) | `DeleteSessionButton` | `View/Prototype/LoggedDataPanePrototype.xaml` |
| **EXPORT ALL** sessions button | `ExportAllSessionsButton` | `View/Prototype/LoggedDataPanePrototype.xaml` |
| In-pane confirm overlay **affirmative** button (e.g. "DELETE"; both accent/danger variants share the id — only the visible one is in the tree) | `ConfirmAffirmativeButton` | `View/Prototype/LoggedDataPanePrototype.xaml` |
| Logging-mode selector (device drawer) | `LoggingModeStreamToApp` / `LoggingModeLogToDevice` | `View/Prototype/DevicesPanePrototype.xaml` |
| SD-card data-format selector (visible only in Log-to-Device mode) | `SdCardFormatSelector` | `View/Prototype/DevicesPanePrototype.xaml` |
| Logged Data → APP LOGS / DEVICE LOGS sub-tabs | `AppLogsTab` / `DeviceLogsTab` | `View/Prototype/LoggedDataPanePrototype.xaml` |
| SD refresh button / status line / file list | `RefreshSdCardFilesButton` / `SdCardStatusText` / `SdCardFileList` | `View/DeviceLogsView.xaml` |
| Per-row SD file IMPORT button | `ImportSdCardFileButton` | `View/DeviceLogsView.xaml` |
| Per-row SD file NAME cell (for deterministic file-name reads) | `SdCardFileNameText` | `View/DeviceLogsView.xaml` |
| Profiles: saved-profile list | `ProfileList` | `View/ProfilesPane.xaml` |
| Profiles: per-tile settings (gear) → opens the edit drawer | `ProfileSettingsButton` | `View/ProfilesPane.xaml` |
| Profiles: “+ ADD PROFILE” (status bar / empty-state CTA) | `AddProfileButton` / `AddProfileButtonEmpty` | `View/ProfilesPane.xaml` |
| New-profile drawer: NAME field | `NewProfileNameInput` | `View/ProfilesPane.xaml` |
| New-profile drawer: CAPTURE FROM CURRENT SETTINGS / SAVE PROFILE | `SaveCurrentSettingsButton` / `SaveNewProfileButton` | `View/ProfilesPane.xaml` |
| New-profile drawer: per-device select checkbox | `NewProfileDeviceCheckbox` | `View/ProfilesPane.xaml` |
| Edit drawer: ACTIVATE/DEACTIVATE / DELETE PROFILE | `ActivateProfileButton` / `DeleteProfileButton` | `View/ProfilesPane.xaml` |
| Profiles status-bar active indicator (present only while a profile is active) | `ActiveProfileIndicator` | `View/ProfilesPane.xaml` |

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

13. **The Logged Data pane's two sub-tabs are mutually exclusive in the UIA tree.** APP LOGS
    (`AppLogsTab`, the default — hosts `LoggedSessionList`) and DEVICE LOGS (`DeviceLogsTab` —
    hosts the SD card browser: `SdCardFileList`, `RefreshSdCardFilesButton`, the per-row
    `ImportSdCardFileButton`) each bind their content's `Visibility` to their radio's
    `IsChecked`. A `Collapsed` subtree is absent from the UIA tree, so **only the selected
    sub-tab's content is reachable**. After working on DEVICE LOGS (e.g. importing a file) you
    must switch back to APP LOGS before reading `LoggedSessionList` — `GetLoggedSessionCount`
    now does this via `SelectAppLogsSubTab` so it is robust regardless of the prior sub-tab.
    Both sub-tab radios are driven by `IsChecked` (no bound `Command`), so the SelectionItem
    pattern switches them reliably (cf. gotcha #12).

14. **MahApps `ShowMessageAsync` dialogs are NOT suppressed in test mode.** The test-mode
    no-op message box (`NoOpMessageBoxService`) only covers the firewall-warning path; the
    in-window metro dialog the import view-model shows on completion ("Import Complete" /
    "Import Failed") still appears and **blocks the import `Task` until dismissed**. It is
    hosted inside the `MetroWindow` (not a top-level window), so its single affirmative button
    (default text **"OK"**) is reachable as a `Button` descendant of `MainWindow` — invoke it
    to dismiss (`WaitAndDismissImportDialog`). The imported `LoggingSession` is added to
    `LoggingManager.LoggingSessions` *before* the dialog, so the new `LoggedSessionList` row is
    already present even while the dialog is up; still dismiss it so later navigation isn't
    blocked by the overlay.

15. **Logged-session rows expose only their buttons to UIA — target a session by position, not
    text.** Each `LoggedSessionList` row's template renders the session name/date/chips as
    `TextBlock`s nested in non-control `Border`/`Grid`/`StackPanel` layout, and those `TextBlock`s
    **do not surface as UIA descendants of the row** — a row's only reachable descendants are its
    three action `Button`s (settings / `ExportSessionButton` / delete). So you cannot find a row by
    reading its name. The list renders in **insertion order with no sort**, so the just-finalized
    session is the **last** row; `CsvExportTests` invokes the last row's `ExportSessionButton` and
    confirms which session it hit via the exported file name (`Session_{id}.csv`, from the finalize
    log line). Relatedly, the list **disables UI virtualization in test mode only** — code-behind
    calls `VirtualizingStackPanel.SetIsVirtualizing(SessionList, false)` when
    `AppDataPaths.IsTestMode`. With virtualization on, an off-screen row (including the newest, at
    the bottom of a long list) is absent from the UIA tree, so its `ExportSessionButton` is
    unreachable by the harness. Production keeps virtualization **on** (the list grows unbounded
    over a device's lifetime); screen readers realize rows on navigation, so the per-row
    `AutomationProperties.Name` (bound to `LoggingSession.AccessibilitySummary`) still works there.

16. **Profiles are targeted by position, driven through the drawer, and confirmed by independent
    signals.** Three things make the Profiles round-trip automatable: **(a)** like the
    logged-session rows (#15), a profile tile exposes only its **gear button**
    (`ProfileSettingsButton`) to UIA — its name/date TextBlocks do not surface — so a profile is
    targeted by **position**: the newest is the **last** tile (`SubscribedProfiles` appends; the
    list renders in insertion order, and the plain `ItemsControl` does not virtualize, so every
    tile is realized). **(b)** A profile tile **activates via a `MouseBinding` LeftClick**, which
    does not land from a background host (#6/#12), so activation/deletion is driven through the
    **edit drawer's** `ActivateProfileButton` / `DeleteProfileButton` — plain `Button`s whose
    `InvokePattern` raises a real click and runs the bound command. Open the drawer via the tile
    gear first. **(c)** Deactivation is confirmed by the **status-bar active indicator**
    (`ActiveProfileIndicator`) leaving the tree — its Visibility binds to the active-profile name,
    so it is collapsed (and absent from UIA) when none is active. Look it up **by AutomationId**,
    not by scanning the pane's text for "ACTIVE" (a profile *named* "…ACTIVE" would defeat a
    substring scan), and **not** via the activate button's swapped label — that label is a
    DataTrigger content swap and may not surface as a UIA name change (#8). Two more load-bearing facts: `IsProfileActive` is **not persisted**
    to the profiles XML, so a fresh launch has **no active profile** and single-profile activation
    takes the no-confirm path (`ActivateProfile` only prompts when switching *between* two active
    profiles); and **delete is blocked while any profile is active**, so the cleanup path must
    deactivate before deleting. Frequency lives in the per-device flyout, not the profile drawer
    (#5) — the profile only *captures and re-applies* it.

17. **Destructive confirmations use a dark *in-pane* overlay, not a MahApps modal.** Delete actions
    (the per-row session delete, "Delete All") route through `DaqifiViewModel.ShowConfirm`, which
    drives an in-window confirm card (`IsConfirmOpen` + `ConfirmAffirmativeCommand`) — **not**
    `ShowMessageAsync` (cf. #14, which still applies to the SD-import completion dialog). Its
    affirmative button carries `AutomationProperties.AutomationId="ConfirmAffirmativeButton"` on
    **both** style variants (accent for non-destructive, danger/red for destructive); their
    `Visibility` binds to `ConfirmAffirmativeIsDestructive`, and a `Collapsed` subtree is absent from
    the UIA tree, so an id lookup returns whichever is currently shown. The card is a plain `Button`,
    so `InvokePattern` raises a real click and runs the bound command (cf. #12). `ConfirmInPaneDialog`
    invokes it and then waits for the overlay to leave the tree so its scrim no longer blocks later
    navigation/reads. A session count returning to baseline proves the row left the bound
    `LoggingSessions` collection (the view); it is **not** by itself DB-level proof, because
    `DbLogger.DeleteLoggingSession` swallows its own exceptions, so the view-model removes the row
    even on a failed delete. DB-level deletion is asserted separately from the app's NLog lines
    (`DeleteLoggingSession completed` present, `Failed in DeleteLoggingSession` absent).

18. **The live plot is an OxyPlot canvas with no per-point UIA elements — assert on a stats hook,
    not the plot.** The Live Graph plot (`oxy:PlotView x:Name="DataLog"`, `Model="{Binding
    Plotter.PlotModel}"`) draws every series and point to a **single drawing surface**; individual
    points/series are **not** exposed as UI Automation elements, so the harness cannot walk the tree
    to verify what is rendered. To assert the plot shows believable data while streaming (issue
    #560) without touching app internals, `LiveGraphPane.xaml` carries an **invisible UIA-visible
    indicator** `PlotStatsText` whose `AutomationProperties.Name` is bound to
    `PlotLogger.PlotStatsSummary` — a machine-readable, invariant-culture string
    `"series=N;points=M;nonfinite=K;last=V;min=A;max=B"` recomputed on the plot's own once-a-second
    render tick. The harness reads and parses it (`ReadPlotStats` → `PlotStats`) to assert series
    count == active channels, strictly-increasing point count (data flowing), and finite/plausible/
    non-flatline values; mirrors the `LoggingStatusText` hook (#8). Two load-bearing details:
    **(a)** the element is kept out of the `IsLogging`-collapsed status-chip `StackPanel` and made
    invisible with **`Opacity="0"` + `IsHitTestVisible="False"`, NOT `Visibility="Collapsed"`** — a
    `Collapsed` element is pruned from the UIA tree (so unreadable), whereas an `Opacity=0` element
    stays in the tree and readable, and being outside the chips means it is readable **after** stop
    too (for the freezes-after-stop assertion). **(b)** `PlotStatsSummary` distinguishes a **gap
    marker** (`DataPoint.Undefined`, NaN *X*) from a real sample (finite *X*), so intentional gap
    NaNs are not counted as data nor mistaken for a non-finite sample *value* — `nonfinite` counts
    only real samples whose *value* is NaN/Inf. Samples reach the plot **only** while a stream-mode
    session is active (`LoggingManager.HandleChannelUpdate` early-returns unless
    `Active && CurrentMode == Stream && hasActiveApplicationSession`), which is exactly why the plot
    accrues between start and stop and freezes after — and why an SD-card (`Log to Device`) run does
    not populate the live plot.

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
