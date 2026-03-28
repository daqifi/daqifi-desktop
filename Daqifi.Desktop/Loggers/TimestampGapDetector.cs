namespace Daqifi.Desktop.Logger;

/// <summary>
/// Detects timestamp gaps in a stream of data samples using firmware-derived inter-message
/// deltas (from the device's hardware timer) rather than PC arrival times.
/// This eliminates false positives caused by TCP jitter / packet batching.
/// A gap is signalled when the firmware delta exceeds <see cref="GapThresholdMultiplier"/> times
/// the running average, which indicates actual data loss.
/// </summary>
internal sealed class TimestampGapDetector
{
    #region Constants
    /// <summary>
    /// A gap is detected when the firmware delta exceeds this multiple of the running average delta.
    /// </summary>
    internal const double GapThresholdMultiplier = 2.0;

    /// <summary>
    /// Smoothing factor for the exponential moving average of firmware timestamp deltas.
    /// Lower values adapt more slowly, making gap detection more stable.
    /// </summary>
    internal const double EmaAlpha = 0.1;
    #endregion

    #region Private Fields
    private readonly Dictionary<(string deviceSerial, string channelName), double> _avgDeltaMs = [];
    private readonly HashSet<(string deviceSerial, string channelName)> _seeded = [];
    #endregion

    #region Public Methods
    /// <summary>
    /// Evaluates whether the firmware-measured inter-message delta indicates missing samples
    /// and updates the running EMA for the channel.
    /// </summary>
    /// <param name="key">The per-channel key.</param>
    /// <param name="firmwareDeltaMs">
    /// The firmware-derived time since the previous message, in milliseconds.
    /// Computed from consecutive <c>msg_time_stamp</c> values via <c>TimestampProcessor</c>.
    /// Pass <see langword="null"/> for the first message in a session (no prior reference point).
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the firmware delta significantly exceeds the running average,
    /// indicating missing samples that should break the chart line; otherwise <see langword="false"/>.
    /// </returns>
    public bool IsGap((string deviceSerial, string channelName) key, double? firmwareDeltaMs)
    {
        // First message or no firmware data — no gap possible.
        if (firmwareDeltaMs is not > 0)
        {
            return false;
        }

        var delta = firmwareDeltaMs.Value;

        if (!_seeded.Contains(key))
        {
            // Second message — seed the EMA with the first real delta.
            _seeded.Add(key);
            _avgDeltaMs[key] = delta;
            return false;
        }

        if (_avgDeltaMs.TryGetValue(key, out var avgDelta) && avgDelta > 0)
        {
            if (delta > GapThresholdMultiplier * avgDelta)
            {
                // Reset the EMA after a detected gap so a single large outage does not
                // desensitise future gap detection on the same channel.
                _avgDeltaMs.Remove(key);
                _seeded.Remove(key);
                return true;
            }

            _avgDeltaMs[key] = (1.0 - EmaAlpha) * avgDelta + EmaAlpha * delta;
        }
        else
        {
            _avgDeltaMs[key] = delta;
        }

        return false;
    }

    /// <summary>
    /// Resets all per-channel tracking state.
    /// </summary>
    public void Clear()
    {
        _avgDeltaMs.Clear();
        _seeded.Clear();
    }
    #endregion
}
