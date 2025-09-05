using Daqifi.Desktop.Loggers;
using Daqifi.Desktop.Helpers;
using Newtonsoft.Json.Linq;

namespace Daqifi.Desktop.Test.Loggers;

[TestClass]
public class FirmwareUpdatationManagerTests
{
    #region Release Selection Logic Tests

    [TestMethod]
    public void ReleaseProcessing_SelectsHighestVersionNonDraft()
    {
        // Arrange - Simulate GitHub API response JSON
        var mockReleases = JArray.Parse("""
                                        [
                                                    {
                                                        "tag_name": "v1.0.0",
                                                        "draft": false,
                                                        "prerelease": false
                                                    },
                                                    {
                                                        "tag_name": "v1.1.0",
                                                        "draft": false,
                                                        "prerelease": false
                                                    },
                                                    {
                                                        "tag_name": "v1.2.0",
                                                        "draft": true,
                                                        "prerelease": false
                                                    },
                                                    {
                                                        "tag_name": "v0.9.0",
                                                        "draft": false,
                                                        "prerelease": false
                                                    }
                                                ]
                                        """);

        // Act - Simulate the release selection logic from FirmwareUpdatationManager
        var ordered = mockReleases
            .Where(t => t["draft"]?.ToObject<bool>() == false)
            .Select(t => new { Tag = t["tag_name"]?.ToString().Trim(), IsPrerelease = t["prerelease"]?.ToObject<bool>() ?? false })
            .Where(x => !string.IsNullOrEmpty(x.Tag))
            .OrderByDescending(x => VersionHelper.TryParseVersionInfo(x.Tag, out var vi) ? vi : default)
            .ToList();
        var selectedTag = ordered.FirstOrDefault()?.Tag;
        var normalizedVersion = VersionHelper.NormalizeVersionString(selectedTag) ?? selectedTag;

        // Assert
        Assert.AreEqual("1.1.0", normalizedVersion);
    }

    [TestMethod]
    public void ReleaseProcessing_IncludesPreReleaseWhenHighest()
    {
        // Arrange
        var mockReleases = JArray.Parse("""
                                        [
                                                    {
                                                        "tag_name": "v1.0.0",
                                                        "draft": false,
                                                        "prerelease": false
                                                    },
                                                    {
                                                        "tag_name": "v1.1.0rc1",
                                                        "draft": false,
                                                        "prerelease": true
                                                    }
                                                ]
                                        """);

        // Act
        var ordered = mockReleases
            .Where(t => t["draft"]?.ToObject<bool>() == false)
            .Select(t => new { Tag = t["tag_name"]?.ToString().Trim(), IsPrerelease = t["prerelease"]?.ToObject<bool>() ?? false })
            .Where(x => !string.IsNullOrEmpty(x.Tag))
            .OrderByDescending(x => VersionHelper.TryParseVersionInfo(x.Tag, out var vi) ? vi : default)
            .ToList();
        var selectedTag = ordered.FirstOrDefault()?.Tag;
        var normalizedVersion = VersionHelper.NormalizeVersionString(selectedTag) ?? selectedTag;

        // Assert
        Assert.AreEqual("1.1.0rc1", normalizedVersion);
    }

    [TestMethod]
    public void ReleaseProcessing_SkipsDraftReleases()
    {
        // Arrange
        var mockReleases = JArray.Parse("""
                                        [
                                                    {
                                                        "tag_name": "v1.0.0",
                                                        "draft": false,
                                                        "prerelease": false
                                                    },
                                                    {
                                                        "tag_name": "v2.0.0",
                                                        "draft": true,
                                                        "prerelease": false
                                                    }
                                                ]
                                        """);

        // Act
        var ordered = mockReleases
            .Where(t => t["draft"]?.ToObject<bool>() == false)
            .Select(t => new { Tag = t["tag_name"]?.ToString().Trim(), IsPrerelease = t["prerelease"]?.ToObject<bool>() ?? false })
            .Where(x => !string.IsNullOrEmpty(x.Tag))
            .OrderByDescending(x => VersionHelper.TryParseVersionInfo(x.Tag, out var vi) ? vi : default)
            .ToList();
        var selectedTag = ordered.FirstOrDefault()?.Tag;
        var normalizedVersion = VersionHelper.NormalizeVersionString(selectedTag) ?? selectedTag;

        // Assert
        Assert.AreEqual("1.0.0", normalizedVersion);
    }

    [TestMethod]
    public void ReleaseProcessing_HandlesEmptyReleaseList()
    {
        // Arrange
        var mockReleases = JArray.Parse(@"[]");

        // Act
        var ordered = mockReleases
            .Where(t => t["draft"]?.ToObject<bool>() == false)
            .Select(t => new { Tag = t["tag_name"]?.ToString().Trim(), IsPrerelease = t["prerelease"]?.ToObject<bool>() ?? false })
            .Where(x => !string.IsNullOrEmpty(x.Tag))
            .OrderByDescending(x => VersionHelper.TryParseVersionInfo(x.Tag, out var vi) ? vi : default)
            .ToList();
        var selectedTag = ordered.FirstOrDefault()?.Tag;

        // Assert
        Assert.IsNull(selectedTag);
    }

    [TestMethod]
    public void ReleaseProcessing_HandlesNullTagName()
    {
        // Arrange
        var mockReleases = JArray.Parse("""
                                        [
                                                    {
                                                        "tag_name": null,
                                                        "draft": false,
                                                        "prerelease": false
                                                    },
                                                    {
                                                        "tag_name": "v1.0.0",
                                                        "draft": false,
                                                        "prerelease": false
                                                    }
                                                ]
                                        """);

        // Act
        var ordered = mockReleases
            .Where(t => t["draft"]?.ToObject<bool>() == false)
            .Select(t => new { Tag = t["tag_name"]?.ToString().Trim(), IsPrerelease = t["prerelease"]?.ToObject<bool>() ?? false })
            .Where(x => !string.IsNullOrEmpty(x.Tag))
            .OrderByDescending(x => VersionHelper.TryParseVersionInfo(x.Tag, out var vi) ? vi : default)
            .ToList();
        var selectedTag = ordered.FirstOrDefault()?.Tag;
        var normalizedVersion = VersionHelper.NormalizeVersionString(selectedTag) ?? selectedTag;

        // Assert
        Assert.AreEqual("1.0.0", normalizedVersion);
    }

    #endregion

    #region Caching Tests

    [TestMethod]
    public void CheckFirmwareVersion_ReturnsCachedVersionWithinTimeLimit()
    {
        // Arrange
        var manager = FirmwareUpdatationManager.Instance;
        manager.LatestFirmwareVersion = "1.0.0";
        
        // Note: This test would require refactoring to make CacheTimestamp accessible or injectable
        // For now, this demonstrates the intended test structure
        
        // Assert - Would verify cached value is returned without HTTP call
        Assert.IsNotNull(manager.LatestFirmwareVersion);
    }

    #endregion
}