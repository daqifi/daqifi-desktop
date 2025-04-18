using Daqifi.Desktop.Common.Loggers;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Daqifi.Desktop.UpdateVersion;

public partial class VersionNotification : ObservableObject
{
    private readonly AppLogger AppLogger = AppLogger.Instance;
    #region Properties

    [ObservableProperty]
    private int _notificationCount;

    [ObservableProperty]
    private string _versionNumber;
    #endregion
    
    #region Version Checking Function 
    public async Task CheckForUpdatesAsync()
    {
        string currentVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString(); 
        string githubApiUrl = "https://api.github.com/repos/daqifi/daqifi-desktop/releases/latest";
        try
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"); 
                var response = await client.GetAsync(githubApiUrl);
                if (!response.IsSuccessStatusCode)
                {
                    var ex= new HttpRequestException("Unable to fetch release information from GitHub.");
                    AppLogger.Error(ex, $"Error checking for updates: {ex.Message}");
                }
                string jsonResponse = await response.Content.ReadAsStringAsync();
                JObject releaseData = JObject.Parse(jsonResponse);
                string latestVersion = releaseData["tag_name"].ToString().Trim();
                Version current = new Version(currentVersion);
                Version latest = new Version(latestVersion.TrimStart('v'));
                if (latest > current)
                {
                    NotificationCount = 1;
                    VersionNumber = latestVersion;
                }
                else
                {
                    NotificationCount = 0;
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"Error checking for updates: {ex.Message}");
        }
    }
    #endregion
}