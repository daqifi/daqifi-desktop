using Daqifi.Desktop.Common.Loggers;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Reflection;

namespace Daqifi.Desktop.UpdateVersion;

public class VersionNotification : ObservableObject
{
    private readonly AppLogger AppLogger = AppLogger.Instance;
    #region Properties

    private int _notificationcount;
    public int NotificationCount
    {
        get => _notificationcount;
        set
        {
            _notificationcount = value;
            NotifyPropertyChanged(nameof(NotificationCount));
        }
    }
    private string _versionNumber;
    public string VersionNumber
    {
        get => _versionNumber;
        set
        {
            _versionNumber = value;
            NotifyPropertyChanged(nameof(VersionNumber));
        }
    }
    #endregion


    #region Version Checking Function 
    public async Task CheckForUpdatesAsync()
    {
        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString(); 
        const string githubApiUrl = "https://api.github.com/repos/daqifi/daqifi-desktop/releases/latest";
        try
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"); 
                var response = await client.GetAsync(githubApiUrl);
                if (!response.IsSuccessStatusCode)
                {
                    var ex= new HttpRequestException("Unable to fetch release information from GitHub.");
                    AppLogger.Error(ex, $"Error checking for updates: {ex.Message}");
                }
                var jsonResponse = await response.Content.ReadAsStringAsync();
                var releaseData = JObject.Parse(jsonResponse);
                var latestVersion = releaseData["tag_name"].ToString().Trim();
                var current = new Version(currentVersion);
                var latest = new Version(latestVersion.TrimStart('v'));
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