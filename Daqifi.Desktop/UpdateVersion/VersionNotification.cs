using Daqifi.Desktop.Common.Loggers;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Daqifi.Desktop.UpdateVersion;

public partial class VersionNotification : ObservableObject, IDisposable
{
    private readonly AppLogger _appLogger = AppLogger.Instance;
    private readonly HttpMessageHandler _httpMessageHandler;
    private readonly bool _ownsHandler;
    private readonly string _currentVersion;

    #region Properties

    [ObservableProperty]
    private int _notificationCount;

    [ObservableProperty]
    private string _versionNumber;
    #endregion

    #region Constructor
    public VersionNotification(HttpMessageHandler? httpMessageHandler = null, string? currentVersion = null)
    {
        _ownsHandler = httpMessageHandler == null;
        _httpMessageHandler = httpMessageHandler ?? new HttpClientHandler();
        _currentVersion = currentVersion ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
    }
    #endregion

    #region IDisposable
    public void Dispose()
    {
        if (_ownsHandler && _httpMessageHandler is IDisposable disposable)
            disposable.Dispose();
    }
    #endregion

    #region Version Checking Function
    public async Task CheckForUpdatesAsync()
    {
        var githubApiUrl = "https://api.github.com/repos/daqifi/daqifi-desktop/releases/latest";
        try
        {
            using var client = new HttpClient(_httpMessageHandler, disposeHandler: false);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            var response = await client.GetAsync(githubApiUrl);
            if (!response.IsSuccessStatusCode)
            {
                _appLogger.Error(new HttpRequestException("Unable to fetch release information from GitHub."),
                    $"Error checking for updates: non-success status {(int)response.StatusCode}");
                return;
            }
            var jsonResponse = await response.Content.ReadAsStringAsync();
            var releaseData = JObject.Parse(jsonResponse);
            var tagToken = releaseData["tag_name"];
            if (tagToken == null || tagToken.Type == JTokenType.Null)
            {
                _appLogger.Error(new InvalidOperationException("GitHub response missing tag_name."),
                    "Error checking for updates: tag_name was null or missing");
                return;
            }
            var latestVersionTag = tagToken.ToString().Trim();
            if (!Version.TryParse(latestVersionTag.TrimStart('v'), out var latest))
            {
                _appLogger.Error(new FormatException($"Could not parse version from tag '{latestVersionTag}'."),
                    $"Error checking for updates: unparseable tag '{latestVersionTag}'");
                return;
            }
            if (!Version.TryParse(_currentVersion, out var current))
            {
                return;
            }
            if (latest > current)
            {
                NotificationCount = 1;
                VersionNumber = latestVersionTag;
            }
            else
            {
                NotificationCount = 0;
            }
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, $"Error checking for updates: {ex.Message}");
        }
    }
    #endregion
}