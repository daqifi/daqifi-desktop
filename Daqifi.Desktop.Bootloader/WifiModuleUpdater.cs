using System.Diagnostics;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Bootloader.Exceptions;

namespace Daqifi.Desktop.Bootloader;

public class WifiModuleUpdater
{
    private readonly AppLogger _appLogger = AppLogger.Instance;

    public async Task UpdateWifiModuleAsync(
        IFirmwareUpdateDevice device,
        IProgress<int> progress)
    {
        try
        {
            progress.Report(0);
            var wifiDownloader = new WifiFirmwareDownloader();
            var (extractFolderPath, latestVersion) = await wifiDownloader.DownloadAndExtractWiFiAsync(progress);

            if (string.IsNullOrEmpty(extractFolderPath))
            {
                throw new FirmwareUpdateException("Failed to download WiFi firmware");
            }

            var matchingFiles = Directory.GetFiles(extractFolderPath, "winc_flash_tool.cmd", SearchOption.AllDirectories);

            if (matchingFiles.Length == 0)
            {
                _appLogger.Error("winc_flash_tool.cmd not found in the extracted folder.");
                return;
            }

            var cmdFilePath = matchingFiles[0];

            try
            {
                await PrepareDeviceForUpdate(device);
                await ExecuteWifiUpdate(cmdFilePath, device.Name, latestVersion, progress);
                await FinalizeDeviceUpdate(device, progress);
            }
            catch (Exception ex)
            {
                throw new FirmwareUpdateException($"Error during WiFi update: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Error updating WiFi firmware");
            throw;
        }
    }

    private async Task PrepareDeviceForUpdate(IFirmwareUpdateDevice device)
    {
        try
        {
            _appLogger.Information($"Connecting to device {device.Name} for WiFi update preparation...");
            var connected = device.Connect();

            if (!connected)
            {
                throw new FirmwareUpdateException($"Failed to connect to device {device.Name} for WiFi update");
            }

            // Give device time to fully initialize after connection
            await Task.Delay(2000);

            _appLogger.Information("Enabling LAN update mode...");
            device.EnableLanUpdateMode();

            // Wait for commands to be sent and processed
            await Task.Delay(2000);

            _appLogger.Information("Disconnecting device before WiFi programming...");
            device.Disconnect();

            // Give device time to process the mode change
            await Task.Delay(1000);
        }
        catch (Exception ex)
        {
            throw new FirmwareUpdateException($"Error preparing device for WiFi update: {ex.Message}");
        }
    }

    private async Task ExecuteWifiUpdate(string cmdFilePath, string portName, string version, IProgress<int> progress)
    {
        var processCommand = $"\"{cmdFilePath}\" /p {portName} /d WINC1500 /v {version} /k /e /i aio /w";
        var processOutput = new List<string>();

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c {processCommand}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(cmdFilePath)
        };

        using var process = new Process();
        process.StartInfo = processStartInfo;
        process.Start();

        // Handle output stream
        var outputTask = MonitorProcessOutput(process, progress, processOutput);
        var errorTask = MonitorProcessError(process, processOutput);

        // Wait for the process to exit and tasks to complete
        await process.WaitForExitAsync();
        await Task.WhenAll(outputTask, errorTask);

        if (process.ExitCode != 0)
        {
            var outputLog = string.Join(Environment.NewLine, processOutput);
            throw new FirmwareUpdateException(
                $"WiFi update process failed. Process output:{Environment.NewLine}{outputLog}");
        }
    }

    private async Task MonitorProcessOutput(Process process, IProgress<int> progress, List<string> processOutput)
    {
        while (!process.StandardOutput.EndOfStream)
        {
            var line = await process.StandardOutput.ReadLineAsync();
            processOutput.Add($"[OUT] {line}");
            Console.WriteLine(line);

            UpdateProgressBasedOnOutput(line, progress);

            if (line.Contains("Power cycle WINC and set to bootloader mode"))
            {
                Console.WriteLine("waiting for 2 second...");
                await Task.Delay(2000);
                await process.StandardInput.WriteLineAsync();
                Console.WriteLine("Simulated key press to continue.");
            }

            if (line.Contains("Programming device failed"))
            {
                var outputLog = string.Join(Environment.NewLine, processOutput);
                throw new FirmwareUpdateException($"WiFi module programming failed during firmware update process. Process output:{Environment.NewLine}{outputLog}");
            }
        }
    }

    private async Task MonitorProcessError(Process process, List<string> processOutput)
    {
        while (!process.StandardError.EndOfStream)
        {
            var errorLine = await process.StandardError.ReadLineAsync();
            processOutput.Add($"[ERR] {errorLine}");
            Console.WriteLine(errorLine);
            _appLogger.Error(errorLine);
        }
    }

    private void UpdateProgressBasedOnOutput(string line, IProgress<int> progress)
    {
        if (line.Contains("begin write operation"))
        {
            progress.Report(33);
        }
        else if (line.Contains("begin read operation"))
        {
            progress.Report(66);
        }
        else if (line.Contains("begin verify operation"))
        {
            progress.Report(90);
        }
    }

    private async Task FinalizeDeviceUpdate(IFirmwareUpdateDevice device, IProgress<int> progress)
    {
        try
        {
            _appLogger.Information($"Reconnecting to device {device.Name} to finalize WiFi update...");

            // Give device time to complete WiFi programming and be ready to reconnect
            await Task.Delay(3000);

            var connected = device.Connect();
            if (!connected)
            {
                throw new FirmwareUpdateException($"Failed to reconnect to device {device.Name} after WiFi update");
            }

            progress.Report(93);

            // Wait for device to stabilize after reconnection
            await Task.Delay(2000);

            _appLogger.Information("Resetting LAN configuration after WiFi update...");
            device.ResetLanAfterUpdate();
            progress.Report(96);

            // Wait for LAN reset commands to be processed
            await Task.Delay(2000);

            _appLogger.Information("Rebooting device to complete WiFi update...");
            device.Reboot();
            progress.Report(100);
        }
        catch (Exception ex)
        {
            throw new FirmwareUpdateException($"Error finalizing WiFi update: {ex.Message}");
        }
    }
}