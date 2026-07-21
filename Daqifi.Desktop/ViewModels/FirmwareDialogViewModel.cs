using Daqifi.Core.Firmware;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device.Firmware;
using Daqifi.Desktop.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.IO;
using File = System.IO.File;

namespace Daqifi.Desktop.ViewModels;

public partial class FirmwareDialogViewModel : ObservableObject, IDisposable
{
    private readonly IFirmwareUpdateService _firmwareUpdateService;
    private readonly IFirmwareDownloadService _firmwareDownloadService;
    private readonly Daqifi.Core.Device.IStreamingDevice _coreDevice;

    /// <summary>
    /// The app-global watcher holding the sitting bootloaders. When the flash starts, the dialog asks it
    /// to release THIS device's hold (so the flasher can open it by path) while every other held
    /// bootloader stays wedge-proof; disposing the flash lease re-grabs/drops it and resumes discovery.
    /// Null only in unit tests that construct the view model without the DI container.
    /// </summary>
    private readonly IBootloaderWatcher? _watcher;

    /// <summary>
    /// OS HID device path of the bootloader to flash. Passed to the path-targeted
    /// <see cref="IFirmwareUpdateService.UpdateFirmwareAsync(Daqifi.Core.Device.IStreamingDevice,string,IProgress{FirmwareUpdateProgress}?,string?,CancellationToken)"/>
    /// overload so the right device is flashed when several identical bootloaders are present. Null in
    /// tests / standalone use (falls back to first-match flashing).
    /// </summary>
    private readonly string? _targetDevicePath;
    private CancellationTokenSource? _updateCts;

    [ObservableProperty]
    private string _version = "Bootloader mode";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UploadFirmwareCommand))]
    private string _firmwareFilePath = string.Empty;

    /// <summary>
    /// The latest published firmware the user can flash without browsing for a <c>.hex</c>. In
    /// bootloader/recovery mode the device model isn't known, so this is typically a single
    /// "DAQiFi — &lt;version&gt;" entry, loaded asynchronously after construction.
    /// </summary>
    public ObservableCollection<FirmwareOption> AvailableFirmwares { get; } = [];

    /// <summary>The firmware option selected in the dropdown to flash when no .hex is browsed.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UploadFirmwareCommand))]
    private FirmwareOption? _selectedFirmware;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelUploadFirmwareCommand))]
    [NotifyCanExecuteChangedFor(nameof(UploadFirmwareCommand))]
    private bool _isFirmwareUploading;

    [ObservableProperty]
    private bool _isUploadComplete;

    [ObservableProperty]
    private bool _hasErrorOccured;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UploadFirmwareProgressText))]
    private int _uploadFirmwareProgress;

    public string UploadFirmwareProgressText => $"Upload Progress: {UploadFirmwareProgress}%";

    /// <summary>
    /// Creates the bootloader firmware dialog view-model for a HID bootloader session and kicks off
    /// loading the latest published firmware so the user can flash it without locating a .hex file.
    /// </summary>
    /// <param name="hidDeviceName">HID device name for the bootloader session (used by the flasher adapter).</param>
    /// <param name="targetDevicePath">OS HID device path of the bootloader to flash; null falls back to first-match.</param>
    /// <param name="firmwareUpdateService">Optional override for tests; otherwise resolved from DI.</param>
    /// <param name="firmwareDownloadService">Optional override for tests; otherwise resolved from DI.</param>
    /// <param name="watcher">Optional override for tests; otherwise resolved from DI.</param>
    public FirmwareDialogViewModel(
        string? hidDeviceName,
        string? targetDevicePath = null,
        IFirmwareUpdateService? firmwareUpdateService = null,
        IFirmwareDownloadService? firmwareDownloadService = null,
        IBootloaderWatcher? watcher = null)
    {
        // Resolve from DI rather than newing up services/HttpClient here. Both are registered in
        // App.ConfigureServices and the provider is always present at runtime; the throw is a
        // fail-fast for a misconfigured container (tests pass mocks via the constructor args).
        _firmwareUpdateService = firmwareUpdateService
            ?? App.ServiceProvider?.GetService<IFirmwareUpdateService>()
            ?? throw new InvalidOperationException("IFirmwareUpdateService is not registered.");

        _firmwareDownloadService = firmwareDownloadService
            ?? App.ServiceProvider?.GetService<IFirmwareDownloadService>()
            ?? throw new InvalidOperationException("IFirmwareDownloadService is not registered.");

        // Optional: the app-global watcher holds the bootloaders; this dialog asks it to release the
        // target's hold at flash start. Resolved from DI in production; null is fine for tests/standalone.
        _watcher = watcher ?? App.ServiceProvider?.GetService<IBootloaderWatcher>();
        _targetDevicePath = targetDevicePath;

        _coreDevice = new BootloaderSessionStreamingDeviceAdapter(hidDeviceName ?? "DAQiFi Bootloader");

        _ = LoadFirmwareOptionsAsync();
    }

    /// <summary>
    /// Fetches the latest published firmware release and populates the dropdown so the user can flash
    /// the latest DAQiFi firmware without browsing for a .hex file. Best-effort: a failure leaves the
    /// dropdown empty and the user can still browse for a file.
    /// </summary>
    private async Task LoadFirmwareOptionsAsync()
    {
        try
        {
            var latestRelease = await _firmwareDownloadService.GetLatestReleaseAsync(includePreRelease: true);
            if (latestRelease == null)
            {
                return;
            }

            var option = new FirmwareOption
            {
                DeviceModel = "DAQiFi",
                Version = latestRelease.Version.ToString()
            };

            // GetLatestReleaseAsync may resume this continuation off the UI thread; AvailableFirmwares
            // is bound to the dropdown, so marshal the mutation onto the dispatcher.
            void AddOption()
            {
                AvailableFirmwares.Add(option);
                SelectedFirmware ??= option;
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                await dispatcher.InvokeAsync(AddOption);
            }
            else
            {
                AddOption();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Warning($"Failed to load latest firmware for bootloader dialog: {ex.Message}");
        }
    }

    [RelayCommand]
    private void BrowseFirmwarePath()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            DefaultExt = ".hex",
            Filter = "Firmware|*.hex"
        };

        var result = dialog.ShowDialog();

        if (result == false)
        {
            return;
        }

        FirmwareFilePath = dialog.FileName;
    }

    /// <summary>
    /// Upload is allowed once there's something to flash: a browsed .hex path, or a dropdown
    /// selection (populated asynchronously). Gating the command prevents a click before
    /// <see cref="LoadFirmwareOptionsAsync"/> finishes from showing a false "something went wrong".
    /// </summary>
    private bool CanUploadFirmware() =>
        !IsFirmwareUploading
        && (!string.IsNullOrWhiteSpace(FirmwareFilePath) || SelectedFirmware != null);

    [RelayCommand(CanExecute = nameof(CanUploadFirmware))]
    private async Task UploadFirmware()
    {
        if (IsFirmwareUploading)
        {
            return;
        }

        // A manually-browsed .hex takes precedence; otherwise flash the latest firmware selected
        // from the dropdown (downloaded on demand).
        var isManualUpload = !string.IsNullOrWhiteSpace(FirmwareFilePath);
        if (!isManualUpload && SelectedFirmware == null)
        {
            HasErrorOccured = true;
            return;
        }

        _updateCts?.Dispose();
        _updateCts = new CancellationTokenSource();

        // Lease the device from the watcher for the duration of the flash. Acquired just before the flash
        // (after any download) and released in the finally so discovery/holds always recover.
        IAsyncDisposable? flashLease = null;

        try
        {
            HasErrorOccured = false;
            IsUploadComplete = false;
            UploadFirmwareProgress = 0;
            IsFirmwareUploading = true;
            AppLogger.Instance.AddBreadcrumb("firmware", "Firmware update started");

            if (!isManualUpload)
            {
                FirmwareFilePath = await _firmwareDownloadService.DownloadLatestFirmwareAsync(
                    GetFirmwareDownloadDirectory(),
                    includePreRelease: true,
                    cancellationToken: _updateCts.Token) ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(FirmwareFilePath) || !File.Exists(FirmwareFilePath))
            {
                HasErrorOccured = true;
                return;
            }

            var progress = new Progress<FirmwareUpdateProgress>(report =>
            {
                UploadFirmwareProgress = Math.Clamp((int)Math.Round(report.PercentComplete), 0, 100);
            });

            // Hand this device off to the flasher right as the flash begins: the watcher pauses HID
            // discovery and releases THIS bootloader's hold (so the flasher can open it by path) while
            // every other held bootloader stays wedge-proof. The hold stayed active across the user's
            // time in this dialog (so the device couldn't selective-suspend/wedge while they decided).
            // Disposing the lease (in the finally) re-grabs the device on a failed/cancelled flash, or
            // drops it once a successful flash leaves it in application mode — and resumes discovery.
            if (_targetDevicePath != null)
            {
                if (_watcher != null)
                {
                    flashLease = await _watcher.PrepareFlashAsync(_targetDevicePath);
                }

                // Several identical bootloaders may be present — target this exact one by path.
                await _firmwareUpdateService.UpdateFirmwareAsync(
                    _coreDevice,
                    FirmwareFilePath,
                    progress,
                    _targetDevicePath,
                    _updateCts.Token);
            }
            else
            {
                // No specific target (tests / standalone) — first-match flash.
                await _firmwareUpdateService.UpdateFirmwareAsync(
                    _coreDevice,
                    FirmwareFilePath,
                    progress,
                    _updateCts.Token);
            }

            IsUploadComplete = true;
            AppLogger.Instance.AddBreadcrumb("firmware", "Firmware update completed");
        }
        catch (OperationCanceledException)
        {
            AppLogger.Instance.Warning("Manual firmware upload canceled by user.");
            AppLogger.Instance.AddBreadcrumb("firmware", "Firmware update cancelled", Common.Loggers.BreadcrumbLevel.Warning);
        }
        catch (FirmwareUpdateException ex)
        {
            HasErrorOccured = true;
            AppLogger.Instance.Error(ex, $"Firmware upload failed in state {ex.FailedState}: {ex.Operation}");
            AppLogger.Instance.AddBreadcrumb("firmware", $"Firmware update failed: {ex.FailedState}", Common.Loggers.BreadcrumbLevel.Error);
        }
        catch (Exception ex)
        {
            HasErrorOccured = true;
            AppLogger.Instance.Error(ex, "Problem Uploading Firmware");
            AppLogger.Instance.AddBreadcrumb("firmware", "Firmware update failed", Common.Loggers.BreadcrumbLevel.Error);
        }
        finally
        {
            // Release the device back to the watcher (re-grab on failure / drop on success) and resume
            // discovery, regardless of how the flash ended. Best-effort: a failure resuming the watcher
            // must not crash the command or mask the flash's own outcome (already recorded above).
            if (flashLease != null)
            {
                try
                {
                    await flashLease.DisposeAsync();
                }
                catch (Exception ex)
                {
                    AppLogger.Instance.Warning(ex, "Error releasing the bootloader flash lease back to the watcher.");
                }
            }

            IsFirmwareUploading = false;
            _updateCts?.Dispose();
            _updateCts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelUploadFirmware))]
    private void CancelUploadFirmware()
    {
        _updateCts?.Cancel();
    }

    private bool CanCancelUploadFirmware()
    {
        return IsFirmwareUploading;
    }

    private static string GetFirmwareDownloadDirectory()
    {
        var firmwareDirectory = Path.Combine(App.DaqifiDataDirectory, "Firmware", "PIC32");
        Directory.CreateDirectory(firmwareDirectory);
        return firmwareDirectory;
    }

    /// <summary>
    /// Cancels and releases the firmware-update cancellation source. Called by <c>FirmwareDialog</c>
    /// when the dialog window closes, so dismissing the dialog mid-flash does not leak the token
    /// source for the lifetime of the process.
    /// </summary>
    public void Dispose()
    {
        var cts = _updateCts;
        _updateCts = null;
        if (cts != null)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed by the update's finally block — nothing left to cancel.
            }

            cts.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
