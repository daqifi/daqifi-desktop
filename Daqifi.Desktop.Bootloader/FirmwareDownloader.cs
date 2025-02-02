using Daqifi.Desktop.Common.Loggers;
using Newtonsoft.Json.Linq;

namespace Daqifi.Desktop.Bootloader;

public class FirmwareDownloader
{
    private const string FirmwareApiUrl = "https://api.github.com/repos/daqifi/daqifi-nyquist-firmware/releases";
    private readonly AppLogger _appLogger = AppLogger.Instance;
    
    public string Download()
        {
            try
            {
                var handler = new HttpClientHandler { AllowAutoRedirect = true };
                using var client = new HttpClient(handler);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("DaqifiFirmwareUpdater/1.0");

                var response = client.GetAsync(FirmwareApiUrl).Result;
                if (!response.IsSuccessStatusCode)
                {
                    _appLogger.Error($"Failed to fetch firmware release info. Status Code: {response.StatusCode}");
                    return string.Empty;
                }

                var jsonResponse = response.Content.ReadAsStringAsync().Result;
                var releaseData = JArray.Parse(jsonResponse);
                var latestRelease = releaseData.FirstOrDefault();
                var assets = latestRelease["assets"] as JArray;
                var hexFileAsset = assets?.FirstOrDefault(a => a["name"]?.ToString().EndsWith(".hex") == true);

                if (hexFileAsset == null)
                {
                    _appLogger.Error("No .hex firmware file found in the release assets.");
                    return string.Empty;
                }

                var firmwareDownloadUrl = hexFileAsset["browser_download_url"]?.ToString();
                var fileName = hexFileAsset["name"]?.ToString();
                var daqifiFolderPath = Path.Combine(Path.GetTempPath(), "DAQiFi");
                Directory.CreateDirectory(daqifiFolderPath);
                var filePath = Path.Combine(daqifiFolderPath, fileName);

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                var hexFileResponse = client.GetAsync(firmwareDownloadUrl).Result;
                if (!hexFileResponse.IsSuccessStatusCode)
                {
                    _appLogger.Error($"Failed to download firmware file. Status Code: {hexFileResponse.StatusCode}");
                    return null;
                }

                using var contentStream = hexFileResponse.Content.ReadAsStream();
                using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                contentStream.CopyTo(fileStream);

                return filePath;
            }
            catch (Exception ex)
            {
                _appLogger.Error("Error while downloading firmware: " + ex.Message);
                return string.Empty;
            }
        }
}