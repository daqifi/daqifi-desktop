using Daqifi.Desktop.Common.Loggers;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Daqifi.Desktop.Loggers
{
    public class FirmwareUpdatationManager : ObservableObject
    {
        
        private readonly AppLogger AppLogger = AppLogger.Instance;


        private string _latestFirmwareVersion;
        public string LatestFirmwareVersion
        {
            get => _latestFirmwareVersion;
            set
            {
                _latestFirmwareVersion = value;
                NotifyPropertyChanged(nameof(LatestFirmwareVersion));
            }
        }

        public static FirmwareUpdatationManager Instance { get; } = new FirmwareUpdatationManager();


        


        private static string firmwareDownloadUrl;

    

        private const string firmwareApiUrl = "https://api.github.com/repos/daqifi/daqifi-nyquist-firmware/releases";


        private static DateTime CacheTimestamp;

        public async Task<string> CheckFirmwareVersion()
        {

            if (!string.IsNullOrEmpty(LatestFirmwareVersion) && (DateTime.Now - CacheTimestamp).TotalMinutes < 60)
            {
                return LatestFirmwareVersion;
            }
            try
            {
                string githubApiUrl = "https://api.github.com/repos/daqifi/daqifi-nyquist-firmware/releases";
                string userAgent = "Mozilla/5.0 (compatible; AcmeApp/1.0)";
                HttpClientHandler handler = new HttpClientHandler { AllowAutoRedirect = true };
                using HttpClient client = new HttpClient(handler);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

                HttpResponseMessage response = client.GetAsync(githubApiUrl).Result;
                if (!response.IsSuccessStatusCode)
                {
                    if (response.Headers.Contains("X-RateLimit-Reset"))
                    {
                        string resetTimeString = response.Headers.GetValues("X-RateLimit-Reset").FirstOrDefault();
                        if (long.TryParse(resetTimeString, out long resetTimeUnix))
                        {
                            DateTime resetTime = DateTimeOffset.FromUnixTimeSeconds(resetTimeUnix).UtcDateTime;
                            AppLogger.Error($"Rate limit reached. Next attempt allowed after: {resetTime} UTC.");
                        }
                    }
                    AppLogger.Error($"Failed to fetch firmware version. Status Code: {response.StatusCode}");
                    return null;
                }

                string jsonResponse = await response.Content.ReadAsStringAsync();
                var releaseData = JArray.Parse(jsonResponse);
                var latestRelease = releaseData.FirstOrDefault();
                LatestFirmwareVersion = latestRelease["tag_name"]?.ToString()?.Trim();
                CacheTimestamp = DateTime.Now;
                if (!string.IsNullOrEmpty(LatestFirmwareVersion) && LatestFirmwareVersion.StartsWith("v"))
                {
                    LatestFirmwareVersion = LatestFirmwareVersion.Substring(1, LatestFirmwareVersion.Length - 3).TrimEnd('.');
                }
                return LatestFirmwareVersion;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Error while checking firmware version: " + ex.Message);
                return null;
            }
        }

        public string DownloadFirmware()
        {
            try
            {
                HttpClientHandler handler = new HttpClientHandler { AllowAutoRedirect = true };
                using HttpClient client = new HttpClient(handler);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("DaqifiFirmwareUpdater/1.0");

                HttpResponseMessage response = client.GetAsync(firmwareApiUrl).Result;
                if (!response.IsSuccessStatusCode)
                {
                    AppLogger.Error($"Failed to fetch firmware release info. Status Code: {response.StatusCode}");
                    return null;
                }

                string jsonResponse = response.Content.ReadAsStringAsync().Result;
                var releaseData = JArray.Parse(jsonResponse);
                var latestRelease = releaseData.FirstOrDefault();
                var assets = latestRelease["assets"] as JArray;
                var hexFileAsset = assets?.FirstOrDefault(a => a["name"]?.ToString().EndsWith(".hex") == true);

                if (hexFileAsset == null)
                {
                    AppLogger.Error("No .hex firmware file found in the release assets.");
                    return null;
                }

                firmwareDownloadUrl = hexFileAsset["browser_download_url"]?.ToString();
                string fileName = hexFileAsset["name"]?.ToString();
                string daqifiFolderPath = Path.Combine(Path.GetTempPath(), "DAQiFi");
                Directory.CreateDirectory(daqifiFolderPath);
                string filePath = Path.Combine(daqifiFolderPath, fileName);

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                HttpResponseMessage hexFileResponse = client.GetAsync(firmwareDownloadUrl).Result;
                if (!hexFileResponse.IsSuccessStatusCode)
                {
                    AppLogger.Error($"Failed to download firmware file. Status Code: {hexFileResponse.StatusCode}");
                    return null;
                }

                using Stream contentStream = hexFileResponse.Content.ReadAsStream();
                using FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                contentStream.CopyTo(fileStream);

                return filePath;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Error while downloading firmware: " + ex.Message);
                return null;
            }
        }
    }
}
