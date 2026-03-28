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
    public void IsGap_NullDelta_ReturnsFalse()
    {
        // First message has no prior reference point — firmware delta is null.
        var result = _detector.IsGap(_key, null);
        Assert.IsFalse(result, "Null firmware delta (first message) should never be flagged as a gap.");
    }

    [TestMethod]
    public void IsGap_ZeroDelta_ReturnsFalse()
    {
        var result = _detector.IsGap(_key, 0.0);
        Assert.IsFalse(result, "Zero firmware delta should never be flagged as a gap.");
    }

    [TestMethod]
    public void IsGap_FirstRealDelta_SeedsEmaAndReturnsFalse()
    {
        // First message (null delta), then second message with a real firmware delta.
        _detector.IsGap(_key, null);
        var result = _detector.IsGap(_key, 10.0);
        Assert.IsFalse(result, "First real firmware delta should seed the EMA and not be flagged.");
    }
    #endregion

    #region Normal Cadence Tests
    [TestMethod]
    public void IsGap_ConsistentSampleRate_NeverDetectsGap()
    {
        const double period = 10.0; // 100 Hz
        WarmUp(_key, period, warmUpCount: 10);

        // 50 more samples at the same rate
        for (var i = 1; i <= 50; i++)
        {
            var isGap = _detector.IsGap(_key, period);
            Assert.IsFalse(isGap, $"Sample {i} at consistent cadence should not be a gap.");
        }
    }

    [TestMethod]
    public void IsGap_DeltaExactlyAtThreshold_ReturnsFalse()
    {
        const double period = 10.0;
        WarmUp(_key, period, warmUpCount: 10);

        // Delta exactly 2× average (equal, not strictly greater)
        var result = _detector.IsGap(_key, period * 2.0);
        Assert.IsFalse(result, "A delta equal to the threshold should not be detected as a gap.");
    }
    #endregion

    #region Gap Detection Tests
    [TestMethod]
    public void IsGap_DeltaExceedsThreshold_ReturnsTrue()
    {
        const double period = 10.0;
        WarmUp(_key, period, warmUpCount: 10);

        // Firmware reports a 50 ms gap (5× period, well above 2× threshold)
        var result = _detector.IsGap(_key, 5 * period);
        Assert.IsTrue(result, "A delta well above 2× average should be detected as a gap.");
    }

    [TestMethod]
    public void IsGap_SlightlyAboveThreshold_ReturnsTrue()
    {
        const double period = 10.0;
        WarmUp(_key, period, warmUpCount: 20);

        // After warm-up the EMA converges closely to 10 ms; 20.1 ms is just above 20 ms threshold
        var result = _detector.IsGap(_key, period * 2.01);
        Assert.IsTrue(result, "A delta just above 2× the EMA average should be detected as a gap.");
    }

    [TestMethod]
    public void IsGap_AfterGap_ResumesWithoutFalsePositives()
    {
        const double period = 10.0;
        WarmUp(_key, period, warmUpCount: 10);

        // Trigger a gap
        Assert.IsTrue(_detector.IsGap(_key, 500.0), "Large gap should be detected.");

        // Normal cadence resumes — gap detection reseeds
        for (var i = 1; i <= 5; i++)
        {
            var isGap = _detector.IsGap(_key, period);
            Assert.IsFalse(isGap, $"Normal-cadence sample {i} after a detected gap should not be a gap.");
        }
    }

    [TestMethod]
    public void IsGap_AfterLargeGap_FutureGapStillDetects()
    {
        const double period = 10.0;
        WarmUp(_key, period, warmUpCount: 10);

        Assert.IsTrue(_detector.IsGap(_key, 500.0), "Large gap should be detected.");

        // Re-seed with one normal delta, then hit another gap
        Assert.IsFalse(_detector.IsGap(_key, period), "First normal sample after a gap should re-seed the EMA.");
        var result = _detector.IsGap(_key, period * 2.5);

        Assert.IsTrue(result, "A later gap should still be detected after a previous large outage.");
    }
    #endregion

    #region TCP Jitter Immunity Tests
    [TestMethod]
    public void IsGap_TcpJitterBurst_DoesNotFalsePositive()
    {
        // Simulate 1000 Hz streaming where TCP batching causes bursty arrival
        // but firmware timestamps are perfectly consistent.
        const double period = 1.0; // 1 ms at 1000 Hz
        WarmUp(_key, period, warmUpCount: 20);

        // Firmware says exactly 1 ms between each sample — no gap regardless of arrival pattern
        for (var i = 0; i < 100; i++)
        {
            var isGap = _detector.IsGap(_key, period);
            Assert.IsFalse(isGap, $"Consistent firmware delta should never be flagged, even if TCP batches arrivals.");
        }
    }

    [TestMethod]
    public void IsGap_MildFirmwareJitter_DoesNotFalsePositive()
    {
        // Firmware timer has slight jitter (±10%) — should not trigger false positives
        const double period = 1.0;
        WarmUp(_key, period, warmUpCount: 20);

        var rng = new Random(42);
        for (var i = 0; i < 100; i++)
        {
            var jitteredDelta = period * (0.9 + rng.NextDouble() * 0.2); // 0.9–1.1 ms
            var isGap = _detector.IsGap(_key, jitteredDelta);
            Assert.IsFalse(isGap, $"Mild firmware jitter at step {i} should not be detected as a gap.");
        }
    }
    #endregion

    #region Clear Tests
    [TestMethod]
    public void Clear_ResetsStateForAllChannels()
    {
        const double period = 10.0;
        WarmUp(_key, period, warmUpCount: 10);
        WarmUp(_key2, period, warmUpCount: 10);

        _detector.Clear();

        // After clear, both channels behave as if freshly created
        Assert.IsFalse(_detector.IsGap(_key, null), "After Clear(), first sample on channel 1 should not be a gap.");
        Assert.IsFalse(_detector.IsGap(_key2, null), "After Clear(), first sample on channel 2 should not be a gap.");
    }

    [TestMethod]
    public void Clear_AfterGapWouldHaveFired_NoLongerFires()
    {
        const double period = 10.0;
        WarmUp(_key, period, warmUpCount: 10);

        _detector.Clear();

        // Re-seed: null (first message), then one real delta
        _detector.IsGap(_key, null);
        _detector.IsGap(_key, period);

        var result = _detector.IsGap(_key, period);
        Assert.IsFalse(result, "Normal sample after Clear() and re-seed should not be a gap.");
    }
    #endregion

    #region Channel Isolation Tests
    [TestMethod]
    public void IsGap_TwoChannels_StateIsIsolated()
    {
        const double period = 10.0;
        WarmUp(_key, period, warmUpCount: 10);

        // Channel 2 sees its first sample — no gap
        var result = _detector.IsGap(_key2, null);
        Assert.IsFalse(result, "An unrelated channel should not be affected by another channel's EMA.");
    }

    [TestMethod]
    public void IsGap_Gap_OnOneChannel_DoesNotAffectOther()
    {
        const double period = 10.0;
        WarmUp(_key, period, warmUpCount: 10);
        WarmUp(_key2, period, warmUpCount: 10);

        // Trigger a gap on channel 1 only
        _detector.IsGap(_key, 500.0);

        // Channel 2 next normal sample is still clean
        var result = _detector.IsGap(_key2, period);
        Assert.IsFalse(result, "A gap on one channel should not affect another channel.");
    }
    #endregion

    #region EMA Adaptation Tests
    [TestMethod]
    public void IsGap_EmaAdaptsToSlowerSampleRate_DoesNotFalsePositive()
    {
        // Start at 10 ms, gradually slow to 50 ms
        _detector.IsGap(_key, null); // first message
        _detector.IsGap(_key, 10.0); // seed EMA

        for (var i = 1; i <= 30; i++)
        {
            // Linearly increase the period from 10 ms to 50 ms
            var currentPeriod = 10.0 + (40.0 / 30) * i;
            var isGap = _detector.IsGap(_key, currentPeriod);
            Assert.IsFalse(isGap, $"Gradually slowing sample rate at step {i} should not be detected as a gap.");
        }
    }
    #endregion

    #region Helpers
    /// <summary>
    /// Seeds the detector with <paramref name="warmUpCount"/> firmware deltas to stabilise the EMA.
    /// Sends a null delta first (first message), then <paramref name="warmUpCount"/> constant deltas.
    /// </summary>
    private void WarmUp((string, string) key, double period, int warmUpCount)
    {
        _detector.IsGap(key, null); // first message — no prior reference
        for (var i = 0; i < warmUpCount; i++)
        {
            _detector.IsGap(key, period);
        }
    }
    #endregion
}
