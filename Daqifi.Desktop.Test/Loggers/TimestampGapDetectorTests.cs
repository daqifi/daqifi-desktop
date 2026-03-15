using Daqifi.Desktop.Logger;

namespace Daqifi.Desktop.Test.Loggers;

[TestClass]
public class TimestampGapDetectorTests
{
    #region Private Fields
    private TimestampGapDetector _detector;
    private readonly (string deviceSerial, string channelName) _key = ("SN001", "AI1");
    private readonly (string deviceSerial, string channelName) _key2 = ("SN001", "AI2");
    #endregion

    #region Setup
    [TestInitialize]
    public void Initialize()
    {
        _detector = new TimestampGapDetector();
    }
    #endregion

    #region First Sample Tests
    [TestMethod]
    public void IsGap_FirstSample_ReturnsFalse()
    {
        // Arrange / Act
        var result = _detector.IsGap(_key, 0.0);

        // Assert
        Assert.IsFalse(result, "First sample should never be flagged as a gap.");
    }

    [TestMethod]
    public void IsGap_SecondSample_ReturnsFalse()
    {
        // Arrange
        _detector.IsGap(_key, 0.0);   // first sample – initialises lastTimestamp

        // Act
        var result = _detector.IsGap(_key, 10.0);  // second sample – initialises EMA

        // Assert
        Assert.IsFalse(result, "Second sample initialises the EMA and should not be flagged.");
    }
    #endregion

    #region Normal Cadence Tests
    [TestMethod]
    public void IsGap_ConsistentSampleRate_NeverDetectsGap()
    {
        // Arrange – simulate 10 ms sample period (100 Hz)
        const double period = 10.0;
        _detector.IsGap(_key, 0.0);

        // Act / Assert – 50 consecutive samples at the same rate
        for (var i = 1; i <= 50; i++)
        {
            var isGap = _detector.IsGap(_key, i * period);
            Assert.IsFalse(isGap, $"Sample {i} at consistent cadence should not be a gap.");
        }
    }

    [TestMethod]
    public void IsGap_DeltaExactlyAtThreshold_ReturnsFalse()
    {
        // Arrange – warm up EMA with a 10 ms period
        const double period = 10.0;
        WarmUp(_key, period, warmUpCount: 10);

        // Act – delta is exactly 2× average (equal, not strictly greater)
        var result = _detector.IsGap(_key, 11 * period);  // 10 ms × 2.0 exactly

        // Assert
        Assert.IsFalse(result, "A delta equal to the threshold should not be detected as a gap.");
    }
    #endregion

    #region Gap Detection Tests
    [TestMethod]
    public void IsGap_DeltaExceedsThreshold_ReturnsTrue()
    {
        // Arrange – warm up EMA with a 10 ms period
        const double period = 10.0;
        WarmUp(_key, period, warmUpCount: 10);

        // Act – introduce a gap significantly larger than 2× average (e.g. 5× period)
        var result = _detector.IsGap(_key, 11 * period + 5 * period);

        // Assert
        Assert.IsTrue(result, "A delta well above 2× average should be detected as a gap.");
    }

    [TestMethod]
    public void IsGap_SlightlyAboveThreshold_ReturnsTrue()
    {
        // Arrange – warm up EMA with a 10 ms period
        const double period = 10.0;
        WarmUp(_key, period, warmUpCount: 20);

        // Act – delta is just over 2× the stable average of ~10 ms
        // After warm-up the EMA converges closely to 10 ms; 20.01 ms is just above 20 ms threshold
        var lastTimestamp = 20 * period;
        var result = _detector.IsGap(_key, lastTimestamp + period * 2.01);

        // Assert
        Assert.IsTrue(result, "A delta just above 2× the EMA average should be detected as a gap.");
    }

    [TestMethod]
    public void IsGap_AfterGap_ResumesWithoutFalsePositives()
    {
        // Arrange – warm up and trigger one gap
        const double period = 10.0;
        WarmUp(_key, period, warmUpCount: 10);
        var lastTimestamp = 10 * period;
        Assert.IsTrue(_detector.IsGap(_key, lastTimestamp + 500.0), "Large gap should be detected.");

        // Act / Assert – once the gap is marked, normal cadence should resume cleanly.
        var baseAfterGap = lastTimestamp + 500.0;
        for (var i = 1; i <= 5; i++)
        {
            var isGap = _detector.IsGap(_key, baseAfterGap + i * period);
            Assert.IsFalse(isGap, $"Normal-cadence sample {i} after a detected gap should not be a gap.");
        }

        var result = _detector.IsGap(_key, baseAfterGap + 6 * period);
        Assert.IsFalse(result, "Normal-cadence sample after EMA re-stabilises should not be a gap.");
    }

    [TestMethod]
    public void IsGap_AfterLargeGap_FutureGapStillDetects()
    {
        // Arrange – warm up and trigger one large gap
        const double period = 10.0;
        WarmUp(_key, period, warmUpCount: 10);
        var lastTimestamp = 10 * period;
        var baseAfterGap = lastTimestamp + 500.0;

        Assert.IsTrue(_detector.IsGap(_key, baseAfterGap), "Large gap should be detected.");

        // Act – resume normal cadence to re-seed the detector, then introduce another gap.
        Assert.IsFalse(_detector.IsGap(_key, baseAfterGap + period), "First normal sample after a gap should re-seed the EMA.");
        var result = _detector.IsGap(_key, baseAfterGap + period + period * 2.5);

        // Assert – later gaps should still be detected once the detector is re-seeded.
        Assert.IsTrue(result, "A later gap should still be detected after a previous large outage.");
    }
    #endregion

    #region Clear Tests
    [TestMethod]
    public void Clear_ResetsStateForAllChannels()
    {
        // Arrange – warm up two channels
        const double period = 10.0;
        WarmUp(_key, period, warmUpCount: 10);
        WarmUp(_key2, period, warmUpCount: 10);

        // Act
        _detector.Clear();

        // Assert – after clear, both channels behave as if freshly created
        Assert.IsFalse(_detector.IsGap(_key, 0.0), "After Clear(), first sample on channel 1 should not be a gap.");
        Assert.IsFalse(_detector.IsGap(_key2, 0.0), "After Clear(), first sample on channel 2 should not be a gap.");
    }

    [TestMethod]
    public void Clear_AfterGapWouldHaveFired_NoLongerFires()
    {
        // Arrange – warm up and establish an EMA
        const double period = 10.0;
        WarmUp(_key, period, warmUpCount: 10);

        // Act – clear, then feed two seed samples to reinitialise the channel
        _detector.Clear();
        _detector.IsGap(_key, 0.0);   // first sample post-clear
        _detector.IsGap(_key, period); // second sample post-clear (seeds EMA)

        // A gap that would have fired pre-clear should now be irrelevant; check a normal sample
        var result = _detector.IsGap(_key, 2 * period);
        Assert.IsFalse(result, "Normal sample after Clear() and re-seed should not be a gap.");
    }
    #endregion

    #region Channel Isolation Tests
    [TestMethod]
    public void IsGap_TwoChannels_StateIsIsolated()
    {
        // Arrange – warm up only channel 1
        const double period = 10.0;
        WarmUp(_key, period, warmUpCount: 10);

        // Act – channel 2 sees its first sample (no EMA yet), should not be a gap
        var result = _detector.IsGap(_key2, 0.0);

        // Assert
        Assert.IsFalse(result, "An unrelated channel should not be affected by another channel's EMA.");
    }

    [TestMethod]
    public void IsGap_Gap_OnOneChannel_DoesNotAffectOther()
    {
        // Arrange – warm up both channels
        const double period = 10.0;
        WarmUp(_key, period, warmUpCount: 10);
        WarmUp(_key2, period, warmUpCount: 10);

        // Act – trigger a gap on channel 1 only
        _detector.IsGap(_key, 10 * period + 500.0);

        // Assert – channel 2 next normal sample is still clean
        var result = _detector.IsGap(_key2, 11 * period);
        Assert.IsFalse(result, "A gap on one channel should not affect another channel.");
    }
    #endregion

    #region EMA Adaptation Tests
    [TestMethod]
    public void IsGap_EmaAdaptsToSlowerSampleRate_DoesNotFalsePositive()
    {
        // Arrange – start at 10 ms, then gradually slow to 50 ms
        _detector.IsGap(_key, 0.0);
        double t = 0;
        for (var i = 1; i <= 30; i++)
        {
            // Linearly increase the period from 10 ms to 50 ms
            var currentPeriod = 10.0 + (40.0 / 30) * i;
            t += currentPeriod;
            var isGap = _detector.IsGap(_key, t);
            Assert.IsFalse(isGap, $"Gradually slowing sample rate at step {i} should not be detected as a gap.");
        }
    }
    #endregion

    #region Helpers
    /// <summary>
    /// Feeds <paramref name="warmUpCount"/> evenly-spaced samples to stabilise the EMA.
    /// </summary>
    private void WarmUp((string, string) key, double period, int warmUpCount)
    {
        for (var i = 0; i <= warmUpCount; i++)
        {
            _detector.IsGap(key, i * period);
        }
    }
    #endregion
}
