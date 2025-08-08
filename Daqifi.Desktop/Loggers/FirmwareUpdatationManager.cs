using Daqifi.Desktop.Common.Loggers;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using Daqifi.Desktop.Helpers;

namespace Daqifi.Desktop.Loggers;

public partial class FirmwareUpdatationManager : ObservableObject
{
    private readonly AppLogger AppLogger = AppLogger.Instance;
        
    [ObservableProperty]
    private string _latestFirmwareVersion;

    public static FirmwareUpdatationManager Instance { get; } = new FirmwareUpdatationManager();

    private static DateTime CacheTimestamp;

    public async Task<string?> CheckFirmwareVersion()
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
            // Prefer latest non-draft, including pre-releases; if both exist, pick the highest semver
            var ordered = releaseData
                .Where(t => t != null && t["draft"]?.ToObject<bool>() == false)
                .Select(t => new { Tag = t["tag_name"]?.ToString()?.Trim(), IsPrerelease = t["prerelease"]?.ToObject<bool>() ?? false })
                .Where(x => !string.IsNullOrEmpty(x.Tag))
                .OrderByDescending(x => VersionHelper.TryParseVersionInfo(x.Tag, out var vi) ? vi : default)
                .ToList();
            var tag = ordered.FirstOrDefault()?.Tag;
            LatestFirmwareVersion = VersionHelper.NormalizeVersionString(tag) ?? tag;
            CacheTimestamp = DateTime.Now;
            return LatestFirmwareVersion;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Error while checking firmware version: " + ex.Message);
            return null;
        }
    }
}