using Daqifi.Core.Device.SdCard;
using Daqifi.Desktop.Loggers;

namespace Daqifi.Desktop.Test.Loggers;

/// <summary>
/// Covers the timestamp-quality classification used to warn when an SD card file's time axis could
/// not be reconstructed (issue #572 follow-up).
/// </summary>
/// <remarks>
/// Since daqifi-core#303 this reads Core's per-entry <see cref="SdCardLogEntry.HasDeviceTimestamp"/>
/// instead of inferring substitution from timestamps that collapsed onto one tick. The tests
/// changed shape accordingly: entries now state whether they carried a device timestamp rather than
/// encoding it in the tick values, and the old 20%-threshold cases are gone — with an exact signal
/// there is no inference error left for a margin to absorb.
/// </remarks>
[TestClass]
public class ImportTimestampQualityTests
{
    private static readonly DateTime BaseTime = new(2026, 6, 23, 14, 32, 17, DateTimeKind.Utc);
    private static readonly TimeSpan SampleStep = TimeSpan.FromMilliseconds(100);

    /// <summary>An entry Core reconstructed a real device timestamp for.</summary>
    private static SdCardLogEntry EntryWithDeviceTimestamp(int index) =>
        new(BaseTime + index * SampleStep, [1.25], 0, null);

    /// <summary>
    /// An entry Core had no usable device timestamp for, so it substituted the session base time.
    /// </summary>
    private static SdCardLogEntry EntryWithoutDeviceTimestamp() =>
        new(BaseTime, [1.25], 0, null) { HasDeviceTimestamp = false };

    [TestMethod]
    public void Observe_AllEntriesCarryDeviceTimestamps_IsHealthy()
    {
        // Arrange
        var quality = new ImportTimestampQuality();

        // Act
        for (var i = 0; i < 100; i++)
        {
            quality.Observe(EntryWithDeviceTimestamp(i));
        }

        // Assert
        Assert.AreEqual(100, quality.TotalEntries);
        Assert.AreEqual(0, quality.EntriesWithoutDeviceTimestamp);
        Assert.IsFalse(quality.HasFlatTimeAxis);
        Assert.IsFalse(quality.HasDegenerateTimeAxis);
        Assert.IsNull(quality.BuildUserWarning());
    }

    /// <summary>
    /// The case the old inference could not tell apart from a substituted run: real samples whose
    /// device timestamps genuinely repeat. Firmware does emit duplicate ticks, so counting entries
    /// that shared the first tick used to flag a perfectly good file once enough of them repeated.
    /// Core's flag makes these unambiguously healthy.
    /// </summary>
    [TestMethod]
    public void Observe_RepeatedDeviceTimestamps_IsStillHealthy()
    {
        // Arrange
        var quality = new ImportTimestampQuality();

        // Act — every entry lands on the same tick, but each is a real device timestamp
        for (var i = 0; i < 100; i++)
        {
            quality.Observe(EntryWithDeviceTimestamp(0));
        }

        // Assert
        Assert.AreEqual(100, quality.TotalEntries);
        Assert.IsFalse(quality.HasDegenerateTimeAxis,
            "Duplicate device timestamps are real samples, not substituted ones.");
        Assert.IsNull(quality.BuildUserWarning());
    }

    [TestMethod]
    public void Observe_NoEntryCarriesADeviceTimestamp_IsFlatAndDegenerate()
    {
        // Arrange - the issue #572 collapse shape: every entry at the session base time
        var quality = new ImportTimestampQuality();

        // Act
        for (var i = 0; i < 50; i++)
        {
            quality.Observe(EntryWithoutDeviceTimestamp());
        }

        // Assert
        Assert.IsTrue(quality.HasFlatTimeAxis);
        Assert.IsTrue(quality.HasDegenerateTimeAxis);
        Assert.AreEqual(1.0, quality.SubstitutedFraction);

        var warning = quality.BuildUserWarning();
        Assert.IsNotNull(warning);
        StringAssert.Contains(warning, "flat");
    }

    [TestMethod]
    public void Observe_SomeEntriesSubstituted_IsDegenerateButNotFlat()
    {
        // Arrange - a mixed file where only some messages carried msg_time_stamp
        var quality = new ImportTimestampQuality();

        // Act
        for (var i = 0; i < 70; i++)
        {
            quality.Observe(EntryWithDeviceTimestamp(i));
        }
        for (var i = 0; i < 30; i++)
        {
            quality.Observe(EntryWithoutDeviceTimestamp());
        }

        // Assert
        Assert.AreEqual(100, quality.TotalEntries);
        Assert.AreEqual(30, quality.EntriesWithoutDeviceTimestamp);
        Assert.AreEqual(0.3, quality.SubstitutedFraction, 1e-12);
        Assert.IsFalse(quality.HasFlatTimeAxis);
        Assert.IsTrue(quality.HasDegenerateTimeAxis);

        var warning = quality.BuildUserWarning();
        Assert.IsNotNull(warning);
        StringAssert.Contains(warning, "30");
        StringAssert.Contains(warning, "%");
    }

    /// <summary>
    /// A single substituted entry is now reported. The old 20% margin existed to absorb the
    /// inference's false positives; with an exact per-entry answer, every substituted sample is a
    /// confirmed hole in the time axis rather than a guess.
    /// </summary>
    [TestMethod]
    public void Observe_OneSubstitutedEntryAmongMany_IsReported()
    {
        // Arrange
        var quality = new ImportTimestampQuality();

        // Act
        for (var i = 0; i < 999; i++)
        {
            quality.Observe(EntryWithDeviceTimestamp(i));
        }
        quality.Observe(EntryWithoutDeviceTimestamp());

        // Assert
        Assert.AreEqual(1000, quality.TotalEntries);
        Assert.AreEqual(1, quality.EntriesWithoutDeviceTimestamp);
        Assert.IsTrue(quality.HasDegenerateTimeAxis);
        Assert.IsFalse(quality.HasFlatTimeAxis);

        var warning = quality.BuildUserWarning();
        Assert.IsNotNull(warning);
        StringAssert.Contains(warning, "0.1%",
            "A sub-1% share must not round to 0% and read as though nothing was substituted.");
    }

    [TestMethod]
    public void Observe_SingleEntryWithDeviceTimestamp_IsHealthy()
    {
        // Arrange
        var quality = new ImportTimestampQuality();

        // Act
        quality.Observe(EntryWithDeviceTimestamp(0));

        // Assert
        Assert.IsFalse(quality.HasFlatTimeAxis);
        Assert.IsFalse(quality.HasDegenerateTimeAxis);
        Assert.IsNull(quality.BuildUserWarning());
    }

    [TestMethod]
    public void Observe_SingleSubstitutedEntry_IsFlat()
    {
        // Arrange
        var quality = new ImportTimestampQuality();

        // Act
        quality.Observe(EntryWithoutDeviceTimestamp());

        // Assert — one sample has no meaningful time axis, and Core told us so outright
        Assert.IsTrue(quality.HasFlatTimeAxis);
        Assert.IsTrue(quality.HasDegenerateTimeAxis);
        Assert.IsNotNull(quality.BuildUserWarning());
    }

    [TestMethod]
    public void NoEntries_IsHealthy()
    {
        // Arrange
        var quality = new ImportTimestampQuality();

        // Assert
        Assert.AreEqual(0, quality.TotalEntries);
        Assert.AreEqual(0.0, quality.SubstitutedFraction);
        Assert.IsFalse(quality.HasFlatTimeAxis, "An empty import has no axis to call flat.");
        Assert.IsFalse(quality.HasDegenerateTimeAxis);
        Assert.IsNull(quality.BuildUserWarning());
    }

    [TestMethod]
    public void Observe_NullEntry_Throws()
    {
        var quality = new ImportTimestampQuality();

        Assert.ThrowsExactly<ArgumentNullException>(() => quality.Observe(null!));
    }
}
