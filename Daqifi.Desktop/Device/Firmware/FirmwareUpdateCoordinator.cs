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
    private CancellationTokenSource? _firmwareUploadCts;
    private string _latestFirmwareVersion = string.Empty;
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
    public FirmwareUpdateCoordinator(
        IFirmwareUpdateHost host,
        IFirmwareUpdateService firmwareUpdateService,
        IFirmwareDownloadService firmwareDownloadService,
        ILogger<FirmwareUpdateService> firmwareLogger,
        IAppLogger appLogger,
        string firmwareDataDirectory,
        Func<string, string, IFirmwareUpdateService>? wifiFirmwareUpdateServiceFactory = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _firmwareUpdateService = firmwareUpdateService ?? throw new ArgumentNullException(nameof(firmwareUpdateService));
        _firmwareDownloadService = firmwareDownloadService ?? throw new ArgumentNullException(nameof(firmwareDownloadService));
        _firmwareLogger = firmwareLogger ?? throw new ArgumentNullException(nameof(firmwareLogger));
        _appLogger = appLogger ?? throw new ArgumentNullException(nameof(appLogger));
        _firmwareDataDirectory = firmwareDataDirectory ?? throw new ArgumentNullException(nameof(firmwareDataDirectory));
        _wifiFirmwareUpdateServiceFactory = wifiFirmwareUpdateServiceFactory ?? CreateWifiFirmwareUpdateService;
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

            await _firmwareUpdateService.UpdateFirmwareAsync(
                coreDevice,
                effectiveFirmwarePath,
                pic32Progress,
                _firmwareUploadCts.Token);

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

    private async Task UpdateWifiModuleAsync(
        Daqifi.Core.Device.IStreamingDevice coreDevice,
        SerialStreamingDevice serialStreamingDevice,
        CancellationToken cancellationToken)
    {
        // Let Core probe the WiFi version and look up the latest release in one call so the desktop
        // can skip unnecessary downloads and surface the current/update versions in the UI. Core owns
        // the startup retry policy for the chip-info probe (the chip may still be booting right after
        // a PIC32 update; see FirmwareUpdateServiceOptions.LanChipInfoMaxAttempts/LanChipInfoRetryDelay).
        // Because the desktop owns this check, the Core flash call below passes skipVersionCheck: true
        // so the device isn't queried a second time.
        const string versionUnavailableStatus = "WiFi firmware version unavailable; continuing with update.";

        _host.FirmwareUpdateStatusText = "Checking WiFi firmware version...";
        _appLogger.Information("Checking WiFi firmware version before deciding whether to flash the WiFi module.");

        WifiFirmwareStatus? wifiStatus = null;
        try
        {
            wifiStatus = await _firmwareUpdateService.CheckWifiFirmwareStatusAsync(coreDevice, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The status check is a UX optimization and expected failures already surface as Reason
            // values, so this guards only unexpected faults (e.g. a broken service instance). Never
            // let those abort the flash below, which runs on its own freshly created service.
            _appLogger.Warning($"WiFi firmware status check failed ({ex.Message}); continuing with WiFi update.");
            _host.FirmwareUpdateStatusText = versionUnavailableStatus;
        }

        if (wifiStatus != null)
        {
            if (wifiStatus.CurrentChipInfo != null)
            {
                _appLogger.Information(
                    "WiFi chip info query succeeded. " +
                    $"Device WiFi firmware version: {wifiStatus.CurrentChipInfo.FwVersion}.");
            }

            switch (wifiStatus.Reason)
            {
                case WifiFirmwareStatusReason.UpToDate:
                {
                    var deviceVersion = wifiStatus.CurrentChipInfo!.FwVersion;
                    var latestVersion = NormalizeWifiFirmwareVersion(wifiStatus.LatestRelease!.TagName);
                    _host.FirmwareUpdateStatusText = $"WiFi firmware already up to date ({deviceVersion}).";
                    _appLogger.Information(
                        $"WiFi firmware is already up to date (device: {deviceVersion}, latest: {latestVersion}); " +
                        "skipping WiFi flash.");
                    _host.UploadWiFiProgress = 100;
                    return;
                }

                case WifiFirmwareStatusReason.UpdateAvailable:
                {
                    var deviceVersion = wifiStatus.CurrentChipInfo!.FwVersion;
                    var latestVersion = NormalizeWifiFirmwareVersion(wifiStatus.LatestRelease!.TagName);
                    _host.FirmwareUpdateStatusText =
                        $"WiFi update available ({deviceVersion} → {latestVersion}). Downloading...";
                    _appLogger.Information(
                        $"WiFi firmware update required (device: {deviceVersion}, latest: {latestVersion}); " +
                        "proceeding with WiFi flash.");
                    break;
                }

                case WifiFirmwareStatusReason.ChipInfoUnavailable:
                    _appLogger.Warning(
                        "WiFi chip info unavailable after startup retries; continuing with WiFi update.");
                    _host.FirmwareUpdateStatusText = versionUnavailableStatus;
                    break;

                case WifiFirmwareStatusReason.DeviceDoesNotSupportLanQuery:
                    _appLogger.Warning(
                        "Device does not support WiFi chip info queries; continuing with WiFi update.");
                    _host.FirmwareUpdateStatusText = versionUnavailableStatus;
                    break;

                case WifiFirmwareStatusReason.LatestReleaseUnavailable:
                    _appLogger.Warning(
                        "Latest WiFi firmware release metadata was unavailable; continuing with WiFi update.");
                    break;

                default:
                    // VersionUnparseable or future reasons — the comparison was inconclusive,
                    // so conservatively continue with the flash.
                    _appLogger.Warning(
                        $"WiFi firmware version check was inconclusive ({wifiStatus.Reason}); " +
                        "continuing with WiFi update.");
                    _host.FirmwareUpdateStatusText = versionUnavailableStatus;
                    break;
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

        // Preserve the legacy serial prep/reset sequence now that the firmware flow uses the
        // underlying Core device directly instead of routing through a desktop-shaped adapter.
        var lanUpdateModeEnabled = false;
        try
        {
            lanUpdateModeEnabled = serialStreamingDevice.EnableLanUpdateMode();

            var wifiUpdateService = _wifiFirmwareUpdateServiceFactory(wifiVersion, serialStreamingDevice.PortName);
            try
            {
                await wifiUpdateService.UpdateWifiModuleAsync(
                    coreDevice,
                    wifiPackage.Value.ExtractedPath,
                    wifiUpdateProgress,
                    cancellationToken,
                    skipVersionCheck: true);
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
                // Bring the device back out of WiFi-update / USB-transparent (bridge) mode. The managed
                // ResetLanAfterUpdate routes through the Core device; if a failed flash has already torn
                // that connection down, its SetTransparentMode 0 silently no-ops and the device is left
                // stranded in transparent mode — it stops answering SCPI and vanishes from the app until
                // it is power-cycled.
                try
                {
                    serialStreamingDevice.ResetLanAfterUpdate();
                }
                catch (Exception ex)
                {
                    _appLogger.Warning($"ResetLanAfterUpdate failed for {serialStreamingDevice.PortName}: {ex.Message}");
                }

                // Safety net: only when the managed reset could NOT have worked (the Core connection is
                // gone), also send the transparent-mode exit over a raw serial write so a failed flash
                // can never strand the device. Run it off the calling thread — it does blocking serial
                // I/O with short sleeps.
                if (!coreDevice.IsConnected)
                {
                    await Task.Run(() => ExitWifiTransparentModeRaw(serialStreamingDevice.PortName));
                }
            }
        }
    }

    /// <summary>
    /// Sends the USB-transparent-mode exit (<c>SYSTem:USB:SetTransparentMode 0</c>) directly over a raw
    /// serial write, mirroring the bridge-activation path. This is the safety net for a failed WiFi
    /// flash whose managed Core connection has already been torn down: the PIC32 still parses this
    /// command even while bridging to the WINC, so it reliably pulls the device back out of transparent
    /// mode where the managed <see cref="SerialStreamingDevice.ResetLanAfterUpdate"/> could not. No-op
    /// if the port can't be opened (e.g. another handle still holds it).
    /// </summary>
    private void ExitWifiTransparentModeRaw(string portName)
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
            port.Write("SYSTem:COMMUnicate:LAN:APPLY\n");
            Thread.Sleep(300);
            _appLogger.Information($"Sent transparent-mode exit to {portName} (raw) after WiFi flash.");
        }
        catch (Exception ex)
        {
            _appLogger.Warning($"Raw transparent-mode exit could not open {portName}: {ex.Message}");
        }
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

        return new FirmwareUpdateService(
            FirmwareUpdateServiceConfig.CreateBootloaderHidTransport(),
            _firmwareDownloadService,
            new WifiPromptDelayProcessRunner(
                new ProcessExternalProcessRunner(),
                promptResponseDelay: TimeSpan.FromSeconds(2),
                bridgeActivationAction: bridgeActivationAction),
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
