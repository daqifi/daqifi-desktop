using System.Globalization;

namespace Daqifi.Desktop.Loggers;

/// <summary>
/// Accumulates timestamp statistics for the entries of an SD card import and
/// classifies whether the file's time axis could be reconstructed.
/// </summary>
/// <remarks>
/// Core's SD card parsers assign the session base time to any entry whose
/// message carries no usable timestamp (e.g. <c>msg_time_stamp == 0</c>, or a
/// CSV row without a timestamp column), so such entries collapse onto one
/// identical tick. A healthy parse produces strictly advancing timestamps with
/// only the anchor entry at the base time, which is what makes the collapse
/// detectable here with O(1) memory: count entries that share the first
/// entry's tick. See issue #572 for the full background.
/// </remarks>
public sealed class ImportTimestampQuality
{
    #region Constants
    /// <summary>
    /// Fraction of entries (beyond the first) collapsed onto the first
    /// timestamp above which the time axis is reported as degenerate. A
    /// healthy file sits at ~0; a file whose messages all lack timestamps
    /// sits at 1.0. The margin tolerates occasional firmware stragglers
    /// without missing mostly-collapsed files.
    /// </summary>
    private const double DEGENERATE_COLLAPSED_FRACTION = 0.2;
    #endregion

    #region Private Fields
    private long _firstTicks;
    private long _minTicks = long.MaxValue;
    private long _maxTicks = long.MinValue;
    #endregion

    #region Public Properties
    /// <summary>
    /// Total number of entries observed.
    /// </summary>
    public long TotalEntries { get; private set; }

    /// <summary>
    /// Number of entries (including the first) whose timestamp equals the
    /// first entry's timestamp.
    /// </summary>
    public long EntriesAtFirstTimestamp { get; private set; }

    /// <summary>
    /// Fraction of entries beyond the first that collapsed onto the first
    /// entry's timestamp. Zero for an empty or single-entry import.
    /// </summary>
    public double CollapsedFraction => TotalEntries > 1
        ? (EntriesAtFirstTimestamp - 1) / (double)(TotalEntries - 1)
        : 0.0;

    /// <summary>
    /// True when every observed entry shares one identical timestamp.
    /// </summary>
    public bool HasFlatTimeAxis => TotalEntries > 1 && _minTicks == _maxTicks;

    /// <summary>
    /// True when the time axis is unusable for analysis: either fully flat,
    /// or a meaningful fraction of entries collapsed onto the first timestamp.
    /// </summary>
    public bool HasDegenerateTimeAxis =>
        HasFlatTimeAxis || (TotalEntries > 1 && CollapsedFraction >= DEGENERATE_COLLAPSED_FRACTION);
    #endregion

    #region Public Methods
    /// <summary>
    /// Records one parsed entry's timestamp.
    /// </summary>
    /// <param name="timestampTicks">The entry's reconstructed timestamp, in ticks.</param>
    public void Observe(long timestampTicks)
    {
        if (TotalEntries == 0)
        {
            _firstTicks = timestampTicks;
        }

        if (timestampTicks == _firstTicks)
        {
            EntriesAtFirstTimestamp++;
        }

        if (timestampTicks < _minTicks) { _minTicks = timestampTicks; }
        if (timestampTicks > _maxTicks) { _maxTicks = timestampTicks; }
        TotalEntries++;
    }

    /// <summary>
    /// Builds a user-facing warning describing the timestamp problem, or
    /// returns null when the time axis is usable.
    /// </summary>
    /// <returns>A warning suitable for the import-complete dialog, or null.</returns>
    public string? BuildUserWarning()
    {
        if (!HasDegenerateTimeAxis)
        {
            return null;
        }

        if (HasFlatTimeAxis)
        {
            return "This file does not contain usable per-sample timestamps, so every imported " +
                   "sample shares one timestamp and the session's time axis will be flat. " +
                   "Older device firmware may not record timestamps in SD card logs.";
        }

        var percent = (CollapsedFraction * 100).ToString("0", CultureInfo.InvariantCulture);
        return $"About {percent}% of the samples in this file have no usable timestamp and were " +
               "collapsed onto the session start time, so time spacing for those samples is not " +
               "meaningful. Older device firmware may not record timestamps in SD card logs.";
    }
    #endregion
}
