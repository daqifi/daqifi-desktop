using Daqifi.Desktop.Common.Loggers;
using Newtonsoft.Json.Linq;
using System.IO.Compression;

namespace Daqifi.Desktop.Bootloader;

public class WifiFirmwareDownloader
{
    private const string WifiFirmwareUrl = "https://api.github.com/repos/daqifi/winc1500-Manual-UART-Firmware-Update/releases/latest";
    private const string UserAgent = "Mozilla/5.0 (compatible; DAQiFiApp/1.0)";
    private readonly AppLogger _appLogger = AppLogger.Instance;

    public async Task<(string extractFolderPath, string latestVersion)> DownloadAndExtractWiFiAsync(
        IProgress<int> progress)
    {
        var daqifiFolderPath = Path.Combine(Path.GetTempPath(), "DAQiFi");
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            var response = await client.GetAsync(WifiFirmwareUrl);

            if (!response.IsSuccessStatusCode)
            {
                _appLogger.Error($"Failed to fetch GitHub API data. Status Code: {response.StatusCode}");
                return (string.Empty, string.Empty);
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var releaseData = JObject.Parse(jsonResponse);

            var latestVersion = releaseData["tag_name"]?.ToString()?.Trim();
            var zipballUrl = releaseData["zipball_url"]?.ToString();

            if (string.IsNullOrEmpty(zipballUrl))
            {
                _appLogger.Error("No zipball URL found for the latest release.");
                return (string.Empty, string.Empty);
            }

            progress.Report(0);

            var zipFileName = $"daqifi-winc1500-Manual-UART-Firmware-Update-{latestVersion}.zip";
            var zipFilePath = Path.Combine(daqifiFolderPath, zipFileName);

            try
            {
                using var zipResponse = await client.GetAsync(zipballUrl);
                await using var contentStream = await zipResponse.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream(zipFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                if (zipResponse.IsSuccessStatusCode)
                {
                    var totalBytes = zipResponse.Content.Headers.ContentLength ?? 1;
                    long bytesRead = 0;
                    var buffer = new byte[8192];

                    int read;
                    while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        fileStream.Write(buffer, 0, read);
                        bytesRead += read;
                        var progressValue = (int)((double)bytesRead / totalBytes * 50);
                        progress.Report(progressValue);
                    }
                }
                else
                {
                    throw new Exception("Failed to download the zipball file.");
                }
            }
            catch (Exception ex)
            {
                _appLogger.Error($"Error during download: {ex.Message}");
                return (string.Empty, string.Empty);
            }

            progress.Report(5);

            var extractFolderPath =
                Path.Combine(daqifiFolderPath, $"daqifi-winc1500-Manual-UART-Firmware-Update-{latestVersion}");

            if (Directory.Exists(extractFolderPath))
            {
                await Task.Delay(500);

                for (var i = 0; i < 3; i++)
                {
                    try
                    {
                        Directory.Delete(extractFolderPath, true);
                        break;
                    }
                    catch (IOException)
                    {
                        if (i == 2) throw;
                        await Task.Delay(500);
                    }
                }
            }

            Directory.CreateDirectory(extractFolderPath);
            ZipFile.ExtractToDirectory(zipFilePath, extractFolderPath);

            progress.Report(10);
            return (extractFolderPath, latestVersion);
        }
        catch (Exception ex)
        {
            _appLogger.Error($"Error: {ex.Message}");
            return (string.Empty, string.Empty);
        }
    }
}