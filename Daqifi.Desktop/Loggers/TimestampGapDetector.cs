namespace Daqifi.Desktop.Logger;

/// <summary>
/// Detects timestamp gaps in a stream of data samples using a per-channel exponential
/// moving average (EMA) of inter-sample deltas.
/// A gap is signalled when the current delta exceeds <see cref="GapThresholdMultiplier"/> times
/// the running average, which indicates packet loss or a similar discontinuity.
/// </summary>
internal sealed class TimestampGapDetector
{
    #region Constants
    /// <summary>
    /// A gap is detected when the current delta exceeds this multiple of the running average delta.
    /// </summary>
    internal const double GapThresholdMultiplier = 2.0;

    /// <summary>
    /// Smoothing factor for the exponential moving average of timestamp deltas.
    /// Lower values adapt more slowly, making gap detection more stable.
    /// </summary>
    internal const double EmaAlpha = 0.1;
    #endregion

    #region Private Fields
    private readonly Dictionary<(string deviceSerial, string channelName), double> _lastTimestampMs = [];
    private readonly Dictionary<(string deviceSerial, string channelName), double> _avgDeltaMs = [];
    #endregion

    #region Public Methods
    /// <summary>
    /// Evaluates whether a gap exists before the new sample at <paramref name="deltaTimeMs"/>
    /// and updates the running EMA for the channel.
    /// </summary>
    /// <param name="key">The per-channel key.</param>
    /// <param name="deltaTimeMs">The new sample's time offset from session start, in milliseconds.</param>
    /// <returns>
    /// <see langword="true"/> if the inter-sample delta significantly exceeds the running average,
    /// indicating a gap that should break the chart line; otherwise <see langword="false"/>.
    /// </returns>
    public bool IsGap((string deviceSerial, string channelName) key, double deltaTimeMs)
    {
        bool gapDetected = false;

        if (_lastTimestampMs.TryGetValue(key, out var lastDeltaTime))
        {
            var timeDelta = deltaTimeMs - lastDeltaTime;

            if (_avgDeltaMs.TryGetValue(key, out var avgDelta))
            {
                if (avgDelta > 0 && timeDelta > GapThresholdMultiplier * avgDelta)
                {
                    gapDetected = true;
                }

                _avgDeltaMs[key] = (1.0 - EmaAlpha) * avgDelta + EmaAlpha * timeDelta;
            }
            else
            {
                _avgDeltaMs[key] = timeDelta;
            }
        }

        _lastTimestampMs[key] = deltaTimeMs;
        return gapDetected;
    }

    /// <summary>
    /// Resets all per-channel tracking state.
    /// </summary>
    public void Clear()
    {
        _lastTimestampMs.Clear();
        _avgDeltaMs.Clear();
    }
    #endregion
}
