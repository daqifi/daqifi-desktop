using System.IO;
using System.IO.Ports;
using Daqifi.Core.Firmware;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device.SerialDevice;
using Daqifi.Desktop.Models;
using Microsoft.Extensions.Logging;

namespace Daqifi.Desktop.Device.Firmware;

/// <summary>
/// Owns the full PIC32 + WiFi firmware update flow (download, bootloader/HID session, progress,
/// cancellation, WiFi-module update) and the firmware version-check / outdated-device notifications.
/// <para>
/// Extracted from <c>DaqifiViewModel</c> (issue #592). Every collaborator is constructor-injected
/// (firmware services, loggers, the firmware data directory), all bound progress/status state is
/// reached through the <see cref="IFirmwareUpdateHost"/> seam, and dialog presentation is delegated
/// to the host — so the coordinator has no dependency on WPF or on desktop singletons
/// (<c>AppLogger.Instance</c>, <c>App.ServiceProvider</c>, <c>App.DaqifiDataDirectory</c>) and is
/// unit-testable in isolation.
/// </para>
/// </summary>
public class FirmwareUpdateCoordinator
{
    #region Private Fields
    private readonly IFirmwareUpdateHost _host;
    private readonly IFirmwareUpdateService _firmwareUpdateService;
    private readonly IFirmwareDownloadService _firmwareDownloadService;
    private readonly ILogger<FirmwareUpdateService> _firmwareLogger;
    private readonly IAppLogger _appLogger;
    private readonly string _firmwareDataDirectory;
    private readonly Func<string, string, IFirmwareUpdateService> _wifiFirmwareUpdateServiceFactory;

    /// <summary>
    /// App-global bootloader watcher. During an auto-update the connected device reboots into the HID
    /// bootloader; the watcher would otherwise discover and exclusively grab it, starving the
    /// coordinator's own flasher. The coordinator suspends the watcher's discovery for the PIC32 flash so
    /// its flasher keeps exclusive access. Null is tolerated (tests / no watcher).
    /// </summary>
    private readonly IBootloaderWatcher? _watcher;
    private CancellationTokenSource? _firmwareUploadCts;
    private string _latestFirmwareVersion = string.Empty;

    private const int WifiChipInfoMaxAttempts = 3;
    private static readonly TimeSpan WifiChipInfoRetryDelay = TimeSpan.FromSeconds(2);

    /// <summary>Production default for <see cref="_wifiUpdateModeSettleDelay"/>.</summary>
    public static readonly TimeSpan DefaultWifiUpdateModeSettleDelay = TimeSpan.FromSeconds(5);

    // After entering LAN FW-update mode (SYSTem:COMMunicate:LAN:FWUpdate) the device needs a few
    // seconds to re-init the WINC into a state where the serial bridge is reachable. Launching the
    // WINC flash tool immediately makes the FIRST attempt exit without programming (it reports a
    // false success); a second attempt works only because the device has since settled. Wait here
    // so the first attempt is reliable. Injectable (production default DefaultWifiUpdateModeSettleDelay)
    // so unit tests can collapse the wait without weakening the production behavior.
    private readonly TimeSpan _wifiUpdateModeSettleDelay;

    /// <summary>Minimum supported WiFi module firmware version. A device below this — or whose WiFi
    /// chip info cannot be read — is flagged as needing a WiFi-only flash.</summary>
    public const string MinimumWifiFirmwareVersion = "19.7.7";
    #endregion

    #region Constructor
    /// <summary>
    /// Creates the coordinator. All firmware collaborators are required and injected; the
    /// composition root (the view model / DI container) is responsible for building the production
    /// defaults so this class never news up service clients or reaches into singletons.
    /// </summary>
    /// <param name="host">The host view-model surface the coordinator reads input from and pushes state to.</param>
    /// <param name="firmwareUpdateService">PIC32 firmware update service.</param>
    /// <param name="firmwareDownloadService">Firmware package download service.</param>
    /// <param name="firmwareLogger">Logger passed to the WiFi update service built by the default factory.</param>
    /// <param name="appLogger">Application logger used for diagnostics and Sentry breadcrumbs.</param>
    /// <param name="firmwareDataDirectory">Base directory under which firmware packages are downloaded.</param>
    /// <param name="wifiFirmwareUpdateServiceFactory">
    /// Factory used to build the WiFi firmware update service for a specific firmware version and COM
    /// port. When null, the built-in desktop factory (<see cref="CreateWifiFirmwareUpdateService"/>)
    /// is used; tests inject a fake to assert WiFi sequencing without hardware.
    /// </param>
    /// <param name="wifiUpdateModeSettleDelay">
    /// Overrides the WiFi-update-mode settle delay. Null uses
    /// <see cref="DefaultWifiUpdateModeSettleDelay"/>; tests pass <see cref="TimeSpan.Zero"/> to skip the wait.
    /// </param>
    /// <param name="watcher">
    /// App-global bootloader watcher whose discovery is suspended around the PIC32 flash so it doesn't
    /// grab the rebooting device. Null is tolerated (tests / no watcher).
    /// </param>
    public FirmwareUpdateCoordinator(
        IFirmwareUpdateHost host,
        IFirmwareUpdateService firmwareUpdateService,
        IFirmwareDownloadService firmwareDownloadService,
        ILogger<FirmwareUpdateService> firmwareLogger,
        IAppLogger appLogger,
        string firmwareDataDirectory,
        Func<string, string, IFirmwareUpdateService>? wifiFirmwareUpdateServiceFactory = null,
        TimeSpan? wifiUpdateModeSettleDelay = null,
        IBootloaderWatcher? watcher = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _firmwareUpdateService = firmwareUpdateService ?? throw new ArgumentNullException(nameof(firmwareUpdateService));
        _firmwareDownloadService = firmwareDownloadService ?? throw new ArgumentNullException(nameof(firmwareDownloadService));
        _firmwareLogger = firmwareLogger ?? throw new ArgumentNullException(nameof(firmwareLogger));
        _appLogger = appLogger ?? throw new ArgumentNullException(nameof(appLogger));
        _firmwareDataDirectory = firmwareDataDirectory ?? throw new ArgumentNullException(nameof(firmwareDataDirectory));
        _wifiFirmwareUpdateServiceFactory = wifiFirmwareUpdateServiceFactory ?? CreateWifiFirmwareUpdateService;
        _wifiUpdateModeSettleDelay = wifiUpdateModeSettleDelay ?? DefaultWifiUpdateModeSettleDelay;
        _watcher = watcher;
    }
    #endregion

    #region Public Properties
    /// <summary>
    /// The latest firmware version discovered by <see cref="RefreshFirmwareUpdatesAsync"/>, or an
    /// empty string if no check has succeeded yet. Read by the host to flag outdated devices.
    /// </summary>
    public string LatestFirmwareVersion => _latestFirmwareVersion;
    #endregion

    #region Upload firmware and update processes
    /// <summary>
    /// Runs a firmware update for the host's selected device. A user-selected <c>.hex</c> file
    /// triggers a manual (PIC32-only) upload; otherwise the latest package is downloaded and both the
    /// PIC32 and WiFi modules are flashed.
    /// </summary>
    public async Task UploadFirmwareAsync()
    {
        if (_host.IsFirmwareUploading)
        {
            return;
        }

        if (_host.SelectedDevice?.ConnectionType != ConnectionType.Usb)
        {
            return;
        }

        if (_host.SelectedDevice is not SerialStreamingDevice serialStreamingDevice)
        {
            return;
        }

        _host.SelectedDeviceSupportsFirmwareUpdate = true;
        _host.HasErrorOccured = false;
        _host.IsUploadComplete = false;
        _host.UploadFirmwareProgress = 0;
        _host.UploadWiFiProgress = 0;
        _host.FirmwareUpdateStatusText = "Preparing firmware update...";

        _host.DeviceBeingUpdated = _host.SelectedDevice;

        var isManualUpload = !string.IsNullOrWhiteSpace(_host.FirmwareFilePath);

        _firmwareUploadCts?.Dispose();
        _firmwareUploadCts = new CancellationTokenSource();
        _host.IsFirmwareUploading = true;
        _appLogger.AddBreadcrumb("firmware", $"Firmware update started for {serialStreamingDevice.Name}");

        try
        {
            // Quiesce inside the try so a fault here still runs the finally (which clears
            // IsFirmwareUploading and DeviceBeingUpdated); otherwise the UI could stay stuck "uploading".
            // Pass the upload token so a CancelUpload() interrupts the wait rather than blocking on it.
            await _host.QuiesceWifiFirmwareProbeAsync(_firmwareUploadCts.Token);

            var coreDevice = serialStreamingDevice.ConnectedCoreStreamingDevice;

            if (!coreDevice.IsConnected)
            {
                _appLogger.Error($"Device {serialStreamingDevice.Name} is not connected. Cannot update firmware on a disconnected device.");
                _host.Notifications.Add(new Notifications
                {
                    Message = $"Please connect device {serialStreamingDevice.Name} before attempting firmware update.",
                    DeviceSerialNo = serialStreamingDevice.DeviceSerialNo
                });

                return;
            }

            // For an auto-update, download into a LOCAL variable rather than the bound
            // FirmwareFilePath property. Writing the downloaded path back into the property
            // would make the NEXT in-session update look like a manual upload (isManualUpload
            // is derived from FirmwareFilePath being non-empty), silently skipping the
            // WiFi-module step until the app is restarted. See issue #599.
            string effectiveFirmwarePath;
            if (!isManualUpload)
            {
                _host.FirmwareUpdateStatusText = "Downloading latest firmware package...";
                effectiveFirmwarePath = await _firmwareDownloadService.DownloadLatestFirmwareAsync(
                    GetFirmwareDownloadDirectory(),
                    includePreRelease: true,
                    cancellationToken: _firmwareUploadCts.Token);
            }
            else
            {
                effectiveFirmwarePath = _host.FirmwareFilePath;
            }

            if (string.IsNullOrWhiteSpace(effectiveFirmwarePath) || !File.Exists(effectiveFirmwarePath))
            {
                throw new FileNotFoundException("Firmware file path is invalid or does not exist.", effectiveFirmwarePath);
            }

            var pic32Progress = new Progress<FirmwareUpdateProgress>(report =>
            {
                _host.UploadFirmwareProgress = Math.Clamp((int)Math.Round(report.PercentComplete), 0, 100);
                if (!string.IsNullOrWhiteSpace(report.CurrentOperation))
                {
                    _host.FirmwareUpdateStatusText = report.CurrentOperation;
                }
            });

            // Suspend the watcher's HID discovery for just the PIC32 flash: the connected device reboots
            // into the bootloader here, and the watcher must not grab it out from under this flasher.
            // Existing holds on OTHER sitting bootloaders stay alive. (Known limitation: if another
            // bootloader is held while this runs, Core's first-match enumeration could land on the held
            // one — auto-update is fundamentally a single-device operation, so this is acceptable.)
            var watcherLease = _watcher != null ? await _watcher.SuspendDiscoveryAsync() : null;
            await using (watcherLease)
            {
                await _firmwareUpdateService.UpdateFirmwareAsync(
                    coreDevice,
                    effectiveFirmwarePath,
                    pic32Progress,
                    _firmwareUploadCts.Token);
            }

            if (!isManualUpload)
            {
                await UpdateWifiModuleAsync(coreDevice, serialStreamingDevice, _firmwareUploadCts.Token);
            }

            _host.IsUploadComplete = true;
            _appLogger.AddBreadcrumb("firmware", "Firmware update completed");
            _host.ShowFirmwareUpdateSucceeded();
        }
        catch (OperationCanceledException)
        {
            _host.FirmwareUpdateStatusText = "Firmware update canceled.";
            _appLogger.Warning("Firmware update canceled by user.");
            _appLogger.AddBreadcrumb("firmware", "Firmware update cancelled", Common.Loggers.BreadcrumbLevel.Warning);
        }
        catch (FirmwareUpdateException ex)
        {
            _appLogger.AddBreadcrumb("firmware", $"Firmware update failed: {ex.FailedState}", Common.Loggers.BreadcrumbLevel.Error);
            HandleFirmwareUpdateException(ex);
        }
        catch (Exception ex)
        {
            _host.HasErrorOccured = true;
            _appLogger.Error(ex, "Problem Uploading Firmware");
            _appLogger.AddBreadcrumb("firmware", "Firmware update failed", Common.Loggers.BreadcrumbLevel.Error);
            _host.ShowFirmwareError("Firmware update failed. Please try again.");
        }
        finally
        {
            _host.IsFirmwareUploading = false;
            _firmwareUploadCts?.Dispose();
            _firmwareUploadCts = null;
            _host.DeviceBeingUpdated = null;

            // Consume any manual .hex selection so the auto/manual decision is a per-run input,
            // not sticky session state. A manual upload is intentionally PIC32-only (no WiFi),
            // and isManualUpload is derived from FirmwareFilePath being non-empty. Without this
            // reset a prior manual selection would trap every later run in manual mode and
            // silently skip the WiFi-module flash until the app is restarted — the symmetric
            // case of issue #599. The next run defaults to a full auto-update unless the user
            // explicitly re-selects a file. (The auto path never writes this property.)
            _host.FirmwareFilePath = string.Empty;
        }
    }

    /// <summary>
    /// Requests cancellation of an in-flight firmware upload. No-op when nothing is uploading.
    /// </summary>
    public void CancelUpload()
    {
        if (!_host.IsFirmwareUploading)
        {
            return;
        }

        _host.FirmwareUpdateStatusText = "Canceling firmware update...";
        _firmwareUploadCts?.Cancel();
    }

    internal async Task UpdateWifiModuleAsync(
        Daqifi.Core.Device.IStreamingDevice coreDevice,
        SerialStreamingDevice serialStreamingDevice,
        CancellationToken cancellationToken,
        bool force = false)
    {
        // Only the Nyquist family carries a separately-flashable WINC1500 module. ESP32 / unknown
        // devices integrate WiFi into the SoC and answer GETChipInfo? with non-version data, which must
        // never be read as "needs flash" — don't attempt an unsupported WiFi flash on them.
        if (!serialStreamingDevice.HasWincWifiModule)
        {
            _appLogger.Information(
                $"{serialStreamingDevice.Name} has no separately-flashable WiFi module; skipping WiFi update.");
            return;
        }

        // Core's WiFi updater also performs its own version probe when the passed device
        // implements ILanChipInfoProvider. Keep the explicit desktop-side check here so
        // desktop can skip unnecessary downloads and surface the current/update version in UI.
        // When force is true the caller is an explicit user-initiated WiFi flash, so skip the
        // up-to-date short-circuit and always flash; the auto path after a PIC32 update leaves
        // force false so an already-current module is still skipped.
        if (!force && serialStreamingDevice is ILanChipInfoProvider lanChipProvider)
        {
            _host.FirmwareUpdateStatusText = "Checking WiFi firmware version...";
            _appLogger.Information("Checking WiFi firmware version before deciding whether to flash the WiFi module.");

            // Power the WINC on before querying it. After a PIC32 reflash the device reboots and the
            // WiFi module comes back powered OFF, so GETChipInfo? hits a dead module and every retry
            // fails — which made the code fall through to "flash anyway" and needlessly re-flash an
            // already-current module (then NACK). The connection-time probe (CheckWifiFirmwareAsync)
            // already powers on before its query; mirror that here so the version check can succeed.
            serialStreamingDevice.PowerOnWifiModule();

            var chipInfo = await TryGetLanChipInfoAsync(lanChipProvider, cancellationToken);

            if (chipInfo == null)
            {
                _appLogger.Warning("WiFi chip info unavailable after startup retries; continuing with WiFi update.");
                _host.FirmwareUpdateStatusText = "WiFi firmware version unavailable; continuing with update.";
            }
            else
            {
                _appLogger.Information(
                    $"WiFi chip info query succeeded. Device WiFi firmware version: {chipInfo.FwVersion}.");

                // Use the same target version + decision helper as the connect-time probe
                // (WifiFirmwareNeedsFlash against MinimumWifiFirmwareVersion) so the two surfaces
                // never disagree about whether a module is out of date.
                if (!WifiFirmwareNeedsFlash(chipInfo, out var reportedWifiVersion))
                {
                    _host.FirmwareUpdateStatusText = $"WiFi firmware already up to date ({reportedWifiVersion}).";
                    _appLogger.Information(
                        $"WiFi firmware is already up to date (device: {reportedWifiVersion}, target: {MinimumWifiFirmwareVersion}); skipping WiFi flash.");
                    _host.UploadWiFiProgress = 100;
                    return;
                }

                _host.FirmwareUpdateStatusText = $"WiFi update available ({reportedWifiVersion} → {MinimumWifiFirmwareVersion}). Downloading...";
                _appLogger.Information(
                    $"WiFi firmware update required (device: {reportedWifiVersion}, target: {MinimumWifiFirmwareVersion}); proceeding with WiFi flash.");
            }
        }

        _host.FirmwareUpdateStatusText = "Downloading WiFi firmware package...";
        var wifiDownloadProgress = new Progress<int>(percent =>
        {
            // Map download progress into the initial segment of the WiFi bar.
            _host.UploadWiFiProgress = Math.Clamp((int)Math.Round(percent * 0.2), 0, 20);
        });

        var wifiPackage = await _firmwareDownloadService.DownloadWifiFirmwareAsync(
            GetWifiDownloadDirectory(),
            wifiDownloadProgress,
            cancellationToken);

        if (wifiPackage == null)
        {
            throw new InvalidOperationException("No WiFi firmware package was found for update.");
        }

        var wifiVersion = NormalizeWifiFirmwareVersion(wifiPackage.Value.Version);
        _host.FirmwareUpdateStatusText = $"Updating WiFi module ({wifiVersion})...";
        var wifiUpdateProgress = new Progress<FirmwareUpdateProgress>(report =>
        {
            _host.UploadWiFiProgress = Math.Clamp((int)Math.Round(report.PercentComplete), 0, 100);
            if (!string.IsNullOrWhiteSpace(report.CurrentOperation))
            {
                _host.FirmwareUpdateStatusText = report.CurrentOperation;
            }
        });

        await FlashWifiPackageAsync(
            coreDevice, serialStreamingDevice, wifiVersion, wifiPackage.Value.ExtractedPath, wifiUpdateProgress, cancellationToken);
    }

    /// <summary>
    /// Flashes an already-available WINC firmware package (downloaded or local) to the WiFi
    /// module, wrapping the flash in the required LAN-update-mode prep/reset serial sequence.
    /// <paramref name="extractedBasePath"/> is the base directory the flash tool runs against;
    /// the tool selects the version sub-folder via the <c>/v {wifiVersion}</c> argument.
    /// </summary>
    private async Task FlashWifiPackageAsync(
        Daqifi.Core.Device.IStreamingDevice coreDevice,
        SerialStreamingDevice serialStreamingDevice,
        string wifiVersion,
        string extractedBasePath,
        IProgress<FirmwareUpdateProgress> progress,
        CancellationToken cancellationToken)
    {
        // Final gate before the device enters WiFi update mode: ensure no connect-time WiFi probe is
        // still mid SCPI exchange. Cancelling its token (done when the flash started) does not abort a
        // POWer:STATe 1 / GETChipInfo? already on the wire — await it fully draining here, because any
        // byte that lands once the WINC is bridging corrupts the program and bricks the module.
        await _host.QuiesceWifiFirmwareProbeAsync(cancellationToken);

        // Preserve the legacy serial prep/reset sequence now that the firmware flow uses the
        // underlying Core device directly instead of routing through a desktop-shaped adapter.
        var lanUpdateModeEnabled = false;
        try
        {
            lanUpdateModeEnabled = serialStreamingDevice.EnableLanUpdateMode();

            // If the device wouldn't enter LAN update mode (e.g. it disconnected), the flash tool
            // can't reach the WINC — abort instead of running it against a device that isn't ready.
            if (!lanUpdateModeEnabled)
            {
                _host.FirmwareUpdateStatusText = "Failed to enter WiFi update mode. Reconnect the device and try again.";
                _appLogger.Warning($"Failed to enable LAN update mode for {serialStreamingDevice.PortName}; aborting WiFi flash.");
                throw new InvalidOperationException(
                    $"Could not put {serialStreamingDevice.PortName} into WiFi update mode (device not connected?).");
            }

            // Give the WINC time to come up in bridge mode before the flash tool opens the port,
            // otherwise the first attempt runs too early and exits without programming.
            _host.FirmwareUpdateStatusText = "Waiting for WiFi module to enter update mode...";
            await Task.Delay(_wifiUpdateModeSettleDelay, cancellationToken);

            // Release the desktop's managed serial connection so the external WINC flash tool can open
            // the COM port. Verified from logs: on the WiFi-only path the established connection keeps
            // holding the port, so the tool can't open it and returns in ~1s (false-success guard fires);
            // the app's own raw open even gets "Access denied" until this Disconnect runs. The tool talks
            // to the WINC over raw serial (the bridge-activation writes), and Core's device commands were
            // already issued by EnableLanUpdateMode above — so hand Core a no-op device for the tool step
            // (mirrors the bootloader flash) and let the freed port go to the tool. WifiPromptDelay-
            // ProcessRunner's pre-launch wait gives Windows time to release the USB-CDC handle.
            _appLogger.Information($"Releasing managed connection on {serialStreamingDevice.PortName} before the WINC flash tool.");
            serialStreamingDevice.Disconnect();
            var flashDevice = new BootloaderSessionStreamingDeviceAdapter(serialStreamingDevice.Name);

            var wifiUpdateService = _wifiFirmwareUpdateServiceFactory(wifiVersion, serialStreamingDevice.PortName);
            try
            {
                // Core verifies the flash from the WINC tool's own output (it throws if the success
                // marker never appears — e.g. the port wasn't released and the tool couldn't run),
                // so the desktop's timed false-success guard is no longer needed.
                await wifiUpdateService.UpdateWifiModuleAsync(
                    flashDevice,
                    extractedBasePath,
                    progress,
                    cancellationToken);
            }
            finally
            {
                (wifiUpdateService as IDisposable)?.Dispose();
            }
        }
        finally
        {
            if (lanUpdateModeEnabled)
            {
                // Bring the device back out of WiFi-update / USB-transparent (bridge) mode. First try
                // the managed ResetLanAfterUpdate (restores LAN config while the device still answers
                // protobuf). Then the raw exit, which guarantees the device leaves transparent mode even
                // when the managed connection both holds the port AND is being ignored by the device.
                try { serialStreamingDevice.ResetLanAfterUpdate(); }
                catch (Exception ex)
                {
                    _appLogger.Warning($"ResetLanAfterUpdate failed for {serialStreamingDevice.PortName}: {ex.Message}");
                }

                ExitWifiTransparentModeRaw(serialStreamingDevice);
            }
        }
    }

    /// <summary>
    /// Brings the device out of USB-transparent / FW-update mode by sending
    /// <c>SYSTem:USB:SetTransparentMode 0</c> over a raw serial write (while transparent the device
    /// ignores the managed protobuf connection — only a raw write reaches the PIC32). If the port is
    /// still held by the managed connection — which can't itself exit the mode — that connection is
    /// disconnected to free the port and the exit is retried. A flash must never leave the device
    /// stranded in transparent mode, where it stops answering and vanishes from the app.
    /// </summary>
    private void ExitWifiTransparentModeRaw(SerialStreamingDevice device)
    {
        var portName = device.PortName;
        if (TrySendTransparentModeExit(portName))
        {
            return;
        }

        // The raw open failed — almost always because the managed connection still holds the port (yet
        // can't send the exit itself while the device is transparent). Release it, let the OS free the
        // handle, then retry the raw exit.
        _appLogger.Information($"Releasing managed connection on {portName} to send the transparent-mode exit.");
        try { device.Disconnect(); }
        catch (Exception ex)
        {
            _appLogger.Warning($"Disconnect before transparent-mode exit failed for {portName}: {ex.Message}");
        }
        Thread.Sleep(500);
        TrySendTransparentModeExit(portName);
    }

    /// <summary>
    /// Opens <paramref name="portName"/> raw and sends the transparent-mode exit + LAN apply. Returns
    /// false if the port can't be opened (e.g. still held by the managed connection).
    /// </summary>
    private bool TrySendTransparentModeExit(string portName)
    {
        try
        {
            // USB CDC virtual ports ignore the baud rate; match the bridge-activation defaults.
            using var port = new SerialPort(portName, 9600, Parity.None, 8, StopBits.One)
            {
                DtrEnable = true,
                RtsEnable = false,
                WriteTimeout = 2000
            };
            port.Open();
            Thread.Sleep(200);
            port.Write("SYSTem:USB:SetTransparentMode 0\n");
            Thread.Sleep(150);
            // Re-apply normal LAN config so the module returns to its operating mode.
            port.Write("SYSTem:COMMunicate:LAN:APPLY\n");
            Thread.Sleep(300);
            _appLogger.Information($"Sent transparent-mode exit to {portName} (raw) after WiFi flash.");
            return true;
        }
        catch (Exception ex)
        {
            _appLogger.Warning($"Raw transparent-mode exit could not open {portName}: {ex.Message}");
            return false;
        }
    }

    private async Task<LanChipInfo?> TryGetLanChipInfoAsync(
        ILanChipInfoProvider lanChipProvider,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= WifiChipInfoMaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var chipInfo = await lanChipProvider.GetLanChipInfoAsync(cancellationToken);
                if (chipInfo != null)
                {
                    return chipInfo;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _appLogger.Warning(ex,
                    $"WiFi chip info query attempt {attempt}/{WifiChipInfoMaxAttempts} failed.");
            }

            if (attempt >= WifiChipInfoMaxAttempts)
            {
                break;
            }

            _appLogger.Information(
                $"WiFi chip info unavailable on attempt {attempt}/{WifiChipInfoMaxAttempts}; retrying after startup delay.");
            _host.FirmwareUpdateStatusText = "Waiting for device to finish starting up before checking WiFi firmware version...";
            await Task.Delay(WifiChipInfoRetryDelay, cancellationToken);
        }

        return null;
    }

    /// <summary>
    /// Determines whether the WiFi module firmware needs to be flashed. A null chip-info
    /// result (GETChipInfo? failed) or a version below <see cref="MinimumWifiFirmwareVersion"/>
    /// both require a flash.
    /// </summary>
    public static bool WifiFirmwareNeedsFlash(LanChipInfo? chipInfo, out string reportedVersion)
    {
        if (chipInfo == null || string.IsNullOrWhiteSpace(chipInfo.FwVersion))
        {
            reportedVersion = "Unknown";
            return true;
        }

        reportedVersion = chipInfo.FwVersion;

        if (!FirmwareVersion.TryParse(chipInfo.FwVersion, out var current) ||
            !FirmwareVersion.TryParse(MinimumWifiFirmwareVersion, out var minimum))
        {
            // A version we can't trust — treat as needing a flash.
            return true;
        }

        return current < minimum;
    }

    private FirmwareUpdateService CreateWifiFirmwareUpdateService(string wifiVersion, string portName)
    {
        // Bridge activation action: opened at the "Power cycle WINC" prompt to trigger the
        // device's bridge-mode state machine right before the flash tool starts programming.
        // By deferring APPLY (SYSTem:COMMunicate:LAN:APPLY) until this point we give the
        // firmware a guaranteed promptResponseDelay window (2 s) to complete the WiFi
        // deinit/reinit cycle and call wifi_serial_bridge_Init() before the flash tool
        // issues its first serial bridge query.
        Action bridgeActivationAction = () =>
        {
            _appLogger.Information($"Opening {portName} to send bridge activation commands.");
            try
            {
                // USB CDC virtual ports ignore the baud rate; match the Core transport defaults.
                using var port = new SerialPort(portName, 9600, Parity.None, 8, StopBits.One)
                {
                    DtrEnable = true,
                    RtsEnable = false,
                    WriteTimeout = 2000
                };
                port.Open();
                // Brief pause to allow the DTR signal to be recognised by the firmware
                // before we send commands (firmware checks isCdcHostConnected via DTR).
                Thread.Sleep(200);
                // Re-assert the FW-update-requested flag (idempotent; belt-and-suspenders).
                port.Write("SYSTem:COMMUnicate:LAN:FWUpdate\n");
                Thread.Sleep(100);
                // Trigger the WiFi manager REINIT → bridge-mode state machine.
                port.Write("SYSTem:COMMUnicate:LAN:APPLY\n");
                // Give the firmware a moment to enqueue the APPLY before we close the port.
                Thread.Sleep(300);
                _appLogger.Information("Bridge activation commands sent successfully.");
            }
            catch (Exception ex)
            {
                _appLogger.Warning($"Bridge activation failed for {portName}: {ex.Message}");
            }
        };

        // Start from the shared firmware config so the bootloader HID timeouts stay aligned with
        // the transport (keeps read and write windows symmetric), then layer on WiFi-specific
        // settings. The WiFi flow flashes via the external WINC tool rather than the HID loop, so
        // the bootloader timeout is inert here, but starting from CreateOptions() keeps every
        // construction site uniform (issue #575).
        var wifiOptions = FirmwareUpdateServiceConfig.CreateOptions();
        // winc_flash_tool.cmd requires an explicit release version folder.
        // Keep legacy argument profile used by shipped WINC tool bundle.
        wifiOptions.WifiFlashToolArgumentsTemplate = $"/p {{port}} /d WINC1500 /v {wifiVersion} /k /e /i aio /w";
        wifiOptions.WifiPortOverride = portName;
        // After sending FWUpdate (flag-only, no APPLY), disconnect quickly so the
        // COM port is free for the bridge activation raw write at the "Power cycle
        // WINC" prompt.  The FWUpdate flag persists in firmware RAM until APPLY fires.
        wifiOptions.PostLanFirmwareModeDelay = TimeSpan.FromMilliseconds(100);
        // Give Windows a little more time to re-enumerate the UART before reconnect attempts.
        wifiOptions.PostWifiReconnectDelay = TimeSpan.FromSeconds(3);

        // Core now owns the WiFi-flash lifecycle (port-release wait, WINC prompt handling +
        // bridge activation, retry, and output-based success verification). Hand the bridge
        // activation in as a callback and let Core's prompt responder drive it; the desktop keeps
        // only the device-level prep (LAN update mode) and post-flash transparent-mode recovery.
        wifiOptions.WifiBridgeActivationCallback = bridgeActivationAction;
        wifiOptions.WincBootPromptResponseDelay = TimeSpan.FromSeconds(2);
        // Let the OS free the COM handle after disconnect before the WINC tool opens the port.
        wifiOptions.PostLanDisconnectPortReleaseDelay = TimeSpan.FromSeconds(1.5);

        return new FirmwareUpdateService(
            FirmwareUpdateServiceConfig.CreateBootloaderHidTransport(),
            _firmwareDownloadService,
            new ProcessExternalProcessRunner(),
            _firmwareLogger,
            options: wifiOptions);
    }

    private static string NormalizeWifiFirmwareVersion(string rawVersion)
    {
        var normalized = (rawVersion ?? string.Empty).Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[1..];
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("WiFi firmware version metadata is missing.");
        }

        // Keep command argument safe even if an unexpected tag format appears.
        var invalidChars = new[] { ' ', '\t', '\r', '\n', '"', '\'', ';' };
        if (normalized.IndexOfAny(invalidChars) >= 0)
        {
            throw new InvalidOperationException($"Invalid WiFi firmware version tag '{rawVersion}'.");
        }

        return normalized;
    }

    private void HandleFirmwareUpdateException(FirmwareUpdateException exception)
    {
        _host.HasErrorOccured = true;

        var summary = $"Firmware update failed during '{exception.Operation}' ({exception.FailedState}).";
        _appLogger.Error(exception, summary);

        if (!string.IsNullOrWhiteSpace(exception.RecoveryGuidance))
        {
            _appLogger.Warning($"Firmware recovery guidance: {exception.RecoveryGuidance}");
        }

        var dialogMessage = summary;
        if (!string.IsNullOrWhiteSpace(exception.RecoveryGuidance))
        {
            dialogMessage += $"{Environment.NewLine}{Environment.NewLine}Suggested recovery: {exception.RecoveryGuidance}";
        }

        _host.ShowFirmwareError(dialogMessage);
    }

    private string GetFirmwareDownloadDirectory()
    {
        var firmwareDirectory = Path.Combine(_firmwareDataDirectory, "Firmware", "PIC32");
        Directory.CreateDirectory(firmwareDirectory);
        return firmwareDirectory;
    }

    private string GetWifiDownloadDirectory()
    {
        var wifiDirectory = Path.Combine(_firmwareDataDirectory, "Firmware", "WiFi");
        Directory.CreateDirectory(wifiDirectory);
        return wifiDirectory;
    }
    #endregion

    #region Firmware version checking
    /// <summary>
    /// Checks the latest published firmware against every connected device, flags outdated devices,
    /// and adds/removes the corresponding "outdated firmware" notifications. Failures are logged and
    /// swallowed so a transient network error never disrupts the UI update path.
    /// </summary>
    public async Task RefreshFirmwareUpdatesAsync()
    {
        var connectedDevices = _host.ConnectedDevices;
        if (connectedDevices.Count == 0)
        {
            return;
        }

        try
        {
            var latestRelease = await _firmwareDownloadService.GetLatestReleaseAsync(includePreRelease: true);
            _latestFirmwareVersion = latestRelease?.Version.ToString() ?? string.Empty;

            if (latestRelease == null)
            {
                return;
            }

            foreach (var device in connectedDevices)
            {
                var updateCheck = await _firmwareDownloadService.CheckForUpdateAsync(
                    device.DeviceVersion ?? string.Empty,
                    includePreRelease: true);
                var isOutdated = updateCheck.UpdateAvailable;
                device.IsFirmwareOutdated = isOutdated;

                if (!string.IsNullOrWhiteSpace(device.DeviceSerialNo))
                {
                    if (isOutdated)
                    {
                        AddNotification(device, latestRelease.Version.ToString());
                    }
                    else
                    {
                        RemoveFirmwareNotification(device);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _appLogger.Warning($"Failed to check firmware updates: {ex.Message}");
        }
    }

    private void AddNotification(IStreamingDevice device, string latestFirmware)
    {
        var message = $"Device With Serial {device.DeviceSerialNo} has Outdated Firmware. Please Update to Version {latestFirmware}.";

        var existingNotification = _host.Notifications.FirstOrDefault(n => n.DeviceSerialNo != null
                                                                        && n.IsFirmwareUpdate
                                                                        && n.DeviceSerialNo == device.DeviceSerialNo);

        if (existingNotification == null)
        {
            _host.Notifications.Add(new Notifications
            {
                DeviceSerialNo = device.DeviceSerialNo,
                Message = message,
                IsFirmwareUpdate = true
            });
        }

        _host.RefreshNotificationCount();
    }

    /// <summary>
    /// Removes the outdated-firmware notification for a specific device, if present. Called when the
    /// device's firmware is up to date or when it disconnects.
    /// </summary>
    public void RemoveFirmwareNotification(IStreamingDevice deviceToRemove)
    {
        if (deviceToRemove?.DeviceSerialNo == null)
        {
            return;
        }

        var notificationsToRemove = _host.Notifications
            .FirstOrDefault(x => x.DeviceSerialNo != null && x.DeviceSerialNo == deviceToRemove.DeviceSerialNo && x.IsFirmwareUpdate);

        if (notificationsToRemove != null)
        {
            _host.Notifications.Remove(notificationsToRemove);
            _host.RefreshNotificationCount();
        }
    }
    #endregion
}
