using Daqifi.Desktop.Logger;
using OxyPlot;
using ChannelType = Daqifi.Core.Channel.ChannelType;

namespace Daqifi.Desktop.Test.Loggers;

/// <summary>
/// Regression tests for issue #572: sessions whose samples contain duplicate
/// (device serial, channel name) rows at a single timestamp must not abort
/// channel discovery when displaying a logging session. The de-duplication now
/// lives in <see cref="SessionDataRepository"/> (extracted from <c>DatabaseLogger</c>, #592).
/// </summary>
[TestClass]
public class SessionDataRepositoryChannelDiscoveryTests
{
    private static SessionChannelInfo Analog(string channelName, string serial, string color = "#D32F2F")
    {
        return new SessionChannelInfo(channelName, serial, ChannelType.Analog, color);
    }

    [TestMethod]
    public void DeduplicateChannelInfo_DuplicateChannelAtSameTimestamp_ReturnsSingleEntry()
    {
        // Arrange - the data shape from issue #572: the same channel sampled
        // twice at the session's first timestamp
        var rows = new List<SessionChannelInfo>
        {
            Analog("AI0", "9090684023231015079"),
            Analog("AI0", "9090684023231015079"),
            Analog("AI1", "9090684023231015079")
        };

        // Act
        var result = SessionDataRepository.DeduplicateChannelInfo(rows);

        // Assert
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("AI0", result[0].ChannelName);
        Assert.AreEqual("AI1", result[1].ChannelName);
    }

    [TestMethod]
    public void DeduplicateChannelInfo_DuplicateWithDifferentColor_KeepsFirstOccurrence()
    {
        // Arrange
        var rows = new List<SessionChannelInfo>
        {
            Analog("AI0", "1234", "#111111"),
            Analog("AI0", "1234", "#222222")
        };

        // Act
        var result = SessionDataRepository.DeduplicateChannelInfo(rows);

        // Assert
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("#111111", result[0].Color);
    }

    [TestMethod]
    public void DeduplicateChannelInfo_SameChannelNameOnDifferentDevices_KeepsBoth()
    {
        // Arrange
        var rows = new List<SessionChannelInfo>
        {
            Analog("AI0", "device-a"),
            Analog("AI0", "device-b")
        };

        // Act
        var result = SessionDataRepository.DeduplicateChannelInfo(rows);

        // Assert
        Assert.AreEqual(2, result.Count);
    }

    [TestMethod]
    public void DeduplicateChannelInfo_UnorderedChannels_ReturnsNaturalOrder()
    {
        // Arrange
        var rows = new List<SessionChannelInfo>
        {
            Analog("AI10", "1234"),
            Analog("AI2", "1234"),
            Analog("AI0", "1234")
        };

        // Act
        var result = SessionDataRepository.DeduplicateChannelInfo(rows);

        // Assert
        string[] expectedOrder = ["AI0", "AI2", "AI10"];
        CollectionAssert.AreEqual(expectedOrder, result.Select(r => r.ChannelName).ToArray());
    }

    [TestMethod]
    public void DeduplicateChannelInfo_EmptyInput_ReturnsEmptyList()
    {
        // Act
        var result = SessionDataRepository.DeduplicateChannelInfo([]);

        // Assert
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void DeduplicateChannelInfo_DeduplicatedKeys_CanSeedPointDictionaryWithoutThrowing()
    {
        // Arrange - every channel duplicated, as in a session whose duplicate
        // samples all share the first timestamp
        var rows = new List<SessionChannelInfo>();
        for (var i = 0; i < 8; i++)
        {
            rows.Add(Analog($"AI{i}", "1234"));
            rows.Add(Analog($"AI{i}", "1234"));
        }

        var localPoints = new Dictionary<(string deviceSerial, string channelName), List<DataPoint>>();

        // Act - mirrors LoadInitialSession seeding one dictionary entry per channel
        foreach (var row in SessionDataRepository.DeduplicateChannelInfo(rows))
        {
            localPoints.Add((row.DeviceSerialNo, row.ChannelName), []);
        }

        // Assert
        Assert.AreEqual(8, localPoints.Count);
    }
}
