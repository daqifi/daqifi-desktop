using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daqifi.Core.Communication.Transport;
using Daqifi.Core.Device;
using Daqifi.Core.Firmware;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device.Firmware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using File = System.IO.File;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace Daqifi.Desktop.ViewModels;

public partial class FirmwareDialogViewModel : ObservableObject
{
    private readonly IFirmwareUpdateService _firmwareUpdateService;
    private readonly IStreamingDevice _coreDevice;
    private CancellationTokenSource? _updateCts;

    [ObservableProperty]
    private string _version = "Bootloader mode";

    [ObservableProperty]
    private string _firmwareFilePath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelUploadFirmwareCommand))]
    private bool _isFirmwareUploading;

    [ObservableProperty]
    private bool _isUploadComplete;

    [ObservableProperty]
    private bool _hasErrorOccured;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UploadFirmwareProgressText))]
    private int _uploadFirmwareProgress;

    public string UploadFirmwareProgressText => $"Upload Progress: {UploadFirmwareProgress}%";

    public FirmwareDialogViewModel(
        string? hidDeviceName,
        IFirmwareUpdateService? firmwareUpdateService = null)
    {
        _firmwareUpdateService = firmwareUpdateService
            ?? App.ServiceProvider?.GetService<IFirmwareUpdateService>()
            ?? CreateFallbackFirmwareUpdateService();

        _coreDevice = new BootloaderSessionStreamingDeviceAdapter(hidDeviceName ?? "DAQiFi Bootloader");
    }

    [RelayCommand]
    private void BrowseFirmwarePath()
    {
        var dialog = new OpenFileDialog
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

    [RelayCommand]
    private async Task UploadFirmware()
    {
        if (IsFirmwareUploading)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(FirmwareFilePath) || !File.Exists(FirmwareFilePath))
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

            var progress = new Progress<FirmwareUpdateProgress>(report =>
            {
                UploadFirmwareProgress = Math.Clamp((int)Math.Round(report.PercentComplete), 0, 100);
            });

            await _firmwareUpdateService.UpdateFirmwareAsync(
                _coreDevice,
                FirmwareFilePath,
                progress,
                _updateCts.Token);

            IsUploadComplete = true;
        }
        catch (OperationCanceledException)
        {
            AppLogger.Instance.Warning("Manual firmware upload canceled by user.");
        }
        catch (FirmwareUpdateException ex)
        {
            HasErrorOccured = true;
            AppLogger.Instance.Error(ex, $"Firmware upload failed in state {ex.FailedState}: {ex.Operation}");
        }
        catch (Exception ex)
        {
            HasErrorOccured = true;
            AppLogger.Instance.Error(ex, "Problem Uploading Firmware");
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

    private static IFirmwareUpdateService CreateFallbackFirmwareUpdateService()
    {
        var downloadService = new GitHubFirmwareDownloadService(new HttpClient());
        return new FirmwareUpdateService(
            new HidLibraryTransport(),
            downloadService,
            new ProcessExternalProcessRunner(),
            NullLogger<FirmwareUpdateService>.Instance);
    }
}
