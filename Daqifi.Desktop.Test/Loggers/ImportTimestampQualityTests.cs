using Daqifi.Desktop.Loggers;

namespace Daqifi.Desktop.Test.Loggers;

/// <summary>
/// Covers the timestamp-quality classification used to warn when an SD card
/// file's time axis could not be reconstructed (issue #572 follow-up).
/// </summary>
[TestClass]
public class ImportTimestampQualityTests
{
    private const long BASE_TICKS = 638_000_000_000_000_000;
    private const long TICK_STEP = 1_000_000; // 100 ms

    [TestMethod]
    public void Observe_AdvancingTimestamps_IsHealthy()
    {
        // Arrange
        var quality = new ImportTimestampQuality();

        // Act
        for (var i = 0; i < 100; i++)
        {
            quality.Observe(BASE_TICKS + i * TICK_STEP);
        }

        // Assert
        Assert.AreEqual(100, quality.TotalEntries);
        Assert.AreEqual(1, quality.EntriesAtFirstTimestamp);
        Assert.IsFalse(quality.HasFlatTimeAxis);
        Assert.IsFalse(quality.HasDegenerateTimeAxis);
        Assert.IsNull(quality.BuildUserWarning());
    }

    [TestMethod]
    public void Observe_AllIdenticalTimestamps_IsFlatAndDegenerate()
    {
        // Arrange - the issue #572 collapse shape: every entry at base time
        var quality = new ImportTimestampQuality();

        // Act
        for (var i = 0; i < 50; i++)
        {
            quality.Observe(BASE_TICKS);
        }

        // Assert
        Assert.IsTrue(quality.HasFlatTimeAxis);
        Assert.IsTrue(quality.HasDegenerateTimeAxis);
        Assert.AreEqual(1.0, quality.CollapsedFraction);

        var warning = quality.BuildUserWarning();
        Assert.IsNotNull(warning);
        StringAssert.Contains(warning, "flat");
    }

    [TestMethod]
    public void Observe_PartialCollapseAboveThreshold_IsDegenerateButNotFlat()
    {
        // Arrange - 100 entries: the first plus 30 more collapsed onto its
        // timestamp, interleaved with 69 advancing entries (mixed file where
        // only some messages carry msg_time_stamp)
        var quality = new ImportTimestampQuality();
        quality.Observe(BASE_TICKS);
        for (var i = 1; i < 70; i++)
        {
            quality.Observe(BASE_TICKS + i * TICK_STEP);
        }
        for (var i = 0; i < 30; i++)
        {
            quality.Observe(BASE_TICKS);
        }

        // Assert - 30 of 99 follow-on entries collapsed (~30%)
        Assert.AreEqual(100, quality.TotalEntries);
        Assert.AreEqual(31, quality.EntriesAtFirstTimestamp);
        Assert.IsFalse(quality.HasFlatTimeAxis);
        Assert.IsTrue(quality.HasDegenerateTimeAxis);

        var warning = quality.BuildUserWarning();
        Assert.IsNotNull(warning);
        StringAssert.Contains(warning, "%");
    }

    [TestMethod]
    public void Observe_PartialCollapseBelowThreshold_IsHealthy()
    {
        // Arrange - 10 of 99 follow-on entries collapsed (~10%, below the 20% threshold)
        var quality = new ImportTimestampQuality();
        quality.Observe(BASE_TICKS);
        for (var i = 1; i < 90; i++)
        {
            quality.Observe(BASE_TICKS + i * TICK_STEP);
        }
        for (var i = 0; i < 10; i++)
        {
            quality.Observe(BASE_TICKS);
        }

        // Assert
        Assert.AreEqual(100, quality.TotalEntries);
        Assert.IsFalse(quality.HasDegenerateTimeAxis);
        Assert.IsNull(quality.BuildUserWarning());
    }

    [TestMethod]
    public void Observe_CollapseExactlyAtThreshold_IsDegenerate()
    {
        // Arrange - 101 entries, 20 of 100 follow-on entries collapsed (exactly 20%)
        var quality = new ImportTimestampQuality();
        quality.Observe(BASE_TICKS);
        for (var i = 1; i <= 80; i++)
        {
            quality.Observe(BASE_TICKS + i * TICK_STEP);
        }
        for (var i = 0; i < 20; i++)
        {
            quality.Observe(BASE_TICKS);
        }

        // Assert
        Assert.AreEqual(101, quality.TotalEntries);
        Assert.AreEqual(0.2, quality.CollapsedFraction, 1e-12);
        Assert.IsTrue(quality.HasDegenerateTimeAxis);
    }

    [TestMethod]
    public void Observe_SingleEntry_IsHealthy()
    {
        // Arrange
        var quality = new ImportTimestampQuality();

        // Act
        quality.Observe(BASE_TICKS);

        // Assert - one sample has no meaningful time axis either way; no warning
        Assert.IsFalse(quality.HasFlatTimeAxis);
        Assert.IsFalse(quality.HasDegenerateTimeAxis);
        Assert.IsNull(quality.BuildUserWarning());
    }

    [TestMethod]
    public void NoEntries_IsHealthy()
    {
        // Arrange
        var quality = new ImportTimestampQuality();

        // Assert
        Assert.AreEqual(0, quality.TotalEntries);
        Assert.AreEqual(0.0, quality.CollapsedFraction);
        Assert.IsFalse(quality.HasDegenerateTimeAxis);
        Assert.IsNull(quality.BuildUserWarning());
    }
}
