using Daqifi.Desktop.Common.Loggers;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Daqifi.Desktop.Loggers
{
    public class FirmwareUpdatationManager : ObservableObject
    {
        private static readonly string firmwareXmlUrl = "https://dev.alcyone.in/FirmwareVersion.xml";
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


        public async Task<string> CheckFirmwareVersion()
        {
            try
            {
                using HttpClient client = new HttpClient();
                string xmlContent = await client.GetStringAsync(firmwareXmlUrl);

                XDocument doc = XDocument.Parse(xmlContent);
                LatestFirmwareVersion = doc.Element("VersionInfo")?.Element("Version")?.Value;

                if (string.IsNullOrWhiteSpace(LatestFirmwareVersion))
                {
                    AppLogger.Error("Failed to retrieve the latest firmware version.");
                    return null;
                }
                // Build the URL for the .hex file using the latest version
                var url = $"http://dev.alcyone.in/FirmwareVersions/DAQiFi_Nyquist_{LatestFirmwareVersion}.hex";

                // Check if the file exists on the server
                bool fileExists = await CheckFileExistsAsync(url);

                if (!fileExists)
                {
                    AppLogger.Error($"Firmware file for version {LatestFirmwareVersion} not found on the server.");
                    return null;
                }
                if (fileExists)
                {
                    SetFirmwareDownloadUrl(LatestFirmwareVersion);
                }

                return LatestFirmwareVersion;
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Error while checking firmware version.");
                return null;
            }
        }


        private async Task<bool> CheckFileExistsAsync(string fileUrl)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, fileUrl));
                    return response.IsSuccessStatusCode;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Error while checking if file exists: {ex.Message}");
                return false;
            }
        }


        private static string firmwareDownloadUrl;

        public static void SetFirmwareDownloadUrl(string latestFirmwareVersion)
        {
            firmwareDownloadUrl = $"http://dev.alcyone.in/FirmwareVersions/DAQiFi_Nyquist_{latestFirmwareVersion}.hex";
        }

        public string DownloadFirmwareAsync()
        {
            try
            {
                string fileName = Path.GetFileName(firmwareDownloadUrl);
                string tempFilePath = Path.Combine(Path.GetTempPath(), fileName);

                using HttpClient client = new HttpClient();

                using HttpResponseMessage response = client.GetAsync(firmwareDownloadUrl).Result;
                response.EnsureSuccessStatusCode();

                using Stream contentStream = response.Content.ReadAsStream();
                using FileStream fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                contentStream.CopyTo(fileStream);
                return tempFilePath;
            }
            catch (TaskCanceledException ex)
            {
               AppLogger.Error($"Download timed out: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Error while downloading firmware: {ex.Message}");
                return null;
            }
        }


    }
}
