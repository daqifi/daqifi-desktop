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

public partial class FirmwareDialogViewModel : ObservableObject
{
    private readonly IFirmwareUpdateService _firmwareUpdateService;
    private readonly IFirmwareDownloadService _firmwareDownloadService;
    private readonly Daqifi.Core.Device.IStreamingDevice _coreDevice;

    /// <summary>
    /// The bootloader hold (keep-alive) that kept the device out of USB selective-suspend while the
    /// user was deciding. Paused the instant the flash begins so the flasher owns the HID I/O. Null
    /// only in unit tests that construct the view model without the DI container.
    /// </summary>
    private readonly IBootloaderHoldService? _bootloaderHoldService;
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
    /// <param name="firmwareUpdateService">Optional override for tests; otherwise resolved from DI.</param>
    /// <param name="firmwareDownloadService">Optional override for tests; otherwise resolved from DI.</param>
    public FirmwareDialogViewModel(
        string? hidDeviceName,
        IFirmwareUpdateService? firmwareUpdateService = null,
        IFirmwareDownloadService? firmwareDownloadService = null,
        IBootloaderHoldService? bootloaderHoldService = null)
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

        // Optional: the connection dialog grabs the bootloader hold and this dialog pauses it at flash
        // start. Resolved from DI in production; null is fine (no hold to pause) for tests/standalone use.
        _bootloaderHoldService = bootloaderHoldService ?? App.ServiceProvider?.GetService<IBootloaderHoldService>();

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
                    cancellationToken: _updateCts.Token);
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

            // Stop the keep-alive hold right as the flash begins so the flasher owns the device's HID
            // I/O. The hold stayed active across the user's time in this dialog (so the device couldn't
            // selective-suspend/wedge while they decided); pausing here hands the flasher a warm,
            // still-open handle it reopens with no idle gap.
            if (_bootloaderHoldService != null)
            {
                await _bootloaderHoldService.PauseForFlashAsync();
            }

            try
            {
                await _firmwareUpdateService.UpdateFirmwareAsync(
                    _coreDevice,
                    FirmwareFilePath,
                    progress,
                    _updateCts.Token);
            }
            catch
            {
                // Flash failed or was cancelled — re-establish the hold so the device stays wedge-proof
                // if the user retries from this still-open dialog (the keep-alive was paused above). On
                // success we skip this: the device has rebooted into the application and is gone.
                if (_bootloaderHoldService != null)
                {
                    await _bootloaderHoldService.BeginHoldAsync();
                }

                throw;
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
}
