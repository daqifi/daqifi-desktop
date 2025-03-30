using System.Diagnostics;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Bootloader.Exceptions;

namespace Daqifi.Desktop.Bootloader
{
    public class WifiModuleUpdater
    {
        private readonly AppLogger _logger;
        
        public WifiModuleUpdater(AppLogger logger)
        {
            _logger = logger;
        }

        public async Task UpdateWifiModuleAsync(
            IFirmwareUpdateDevice device, 
            IProgress<int> progress, 
            CancellationToken cancellationToken = default)
        {
            try
            {
                progress.Report(0);
                var wifiDownloader = new WiFiDownloader();
                var (extractFolderPath, latestVersion) = await wifiDownloader.DownloadAndExtractWiFiAsync(progress);

                if (string.IsNullOrEmpty(extractFolderPath))
                {
                    return;
                }

                var matchingFiles = Directory.GetFiles(extractFolderPath, "winc_flash_tool.cmd", SearchOption.AllDirectories);
                
                if (matchingFiles.Length == 0)
                {
                    _logger.Error("winc_flash_tool.cmd not found in the extracted folder.");
                    return;
                }

                var cmdFilePath = matchingFiles[0];

                try
                {
                    await PrepareDeviceForUpdate(device);
                    await ExecuteWifiUpdate(cmdFilePath, device.Name, latestVersion, progress);
                    await FinalizeDeviceUpdate(device);
                }
                catch (Exception ex)
                {
                    throw new FirmwareUpdateException($"Error during WiFi update: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error updating WiFi firmware");
                throw;
            }
        }

        private async Task PrepareDeviceForUpdate(IFirmwareUpdateDevice device)
        {
            try
            {
                device.Connect();
                device.EnableLanUpdateMode();
                await Task.Delay(1000);
                device.Disconnect();
            }
            catch (Exception ex)
            {
                throw new FirmwareUpdateException($"Error during UART communication: {ex.Message}");
            }
        }

        private async Task ExecuteWifiUpdate(string cmdFilePath, string portName, string version, IProgress<int> progress)
        {
            var processCommand = $"\"{cmdFilePath}\" /p {portName} /d WINC1500 /v {version} /k /e /i aio /w";
            
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

            using (var process = new Process { StartInfo = processStartInfo })
            {
                process.Start();

                // Handle output stream
                var outputTask = MonitorProcessOutput(process, progress);
                var errorTask = MonitorProcessError(process);

                // Wait for the process to exit and tasks to complete
                process.WaitForExit();
                await Task.WhenAll(outputTask, errorTask);

                if (process.ExitCode != 0)
                {
                    throw new FirmwareUpdateException("WiFi update process failed");
                }
            }
        }

        private async Task MonitorProcessOutput(Process process, IProgress<int> progress)
        {
            while (!process.StandardOutput.EndOfStream)
            {
                var line = await process.StandardOutput.ReadLineAsync();
                _logger.Information(line);

                UpdateProgressBasedOnOutput(line, progress);

                if (line.Contains("Power cycle WINC and set to bootloader mode"))
                {
                    await Task.Delay(1000);
                    process.StandardInput.WriteLine();
                }

                if (line.Contains("Programming device failed"))
                {
                    throw new FirmwareUpdateException("WiFi module programming failed during firmware update process");
                }
            }
        }

        private async Task MonitorProcessError(Process process)
        {
            while (!process.StandardError.EndOfStream)
            {
                var errorLine = await process.StandardError.ReadLineAsync();
                _logger.Error(errorLine);
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

        private async Task FinalizeDeviceUpdate(IFirmwareUpdateDevice device)
        {
            device.Connect();
            device.ResetLanAfterUpdate();
            device.Reboot();
            await Task.Delay(1000); // Give device time to reboot
        }
    }
} 