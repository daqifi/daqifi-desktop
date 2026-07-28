using System.Globalization;
using Daqifi.Core.Device.SdCard;

namespace Daqifi.Desktop.Loggers;

/// <summary>
/// Accumulates timestamp statistics for the entries of an SD card import and
/// classifies whether the file's time axis could be reconstructed.
/// </summary>
/// <remarks>
/// Core's SD card parsers assign the session base time to any entry whose message carries no
/// usable timestamp (e.g. <c>msg_time_stamp == 0</c>, an unknown tick rate, or a CSV row without a
/// timestamp column) and report that per entry via
/// <see cref="SdCardLogEntry.HasDeviceTimestamp"/> (daqifi-core#303). Counting those flags is an
/// exact answer to "how much of this file has a real time axis".
///
/// <para>Before Core exposed the flag this had to be inferred: substituted entries all collapse
/// onto one identical tick, so the old implementation counted entries sharing the first entry's
/// tick and called the axis degenerate past a 20% margin. That inference had false positives the
/// margin existed to absorb — the hardware genuinely emits duplicate ticks on occasion, and those
/// are real samples, not substituted ones. See issue #572 for the original background.</para>
/// </remarks>
public sealed class ImportTimestampQuality
{
    #region Constants
    /// <summary>
    /// Format for the substituted-sample percentage. Always renders one decimal, so a small
    /// non-zero share can never collapse to a bare "0".
    /// </summary>
    private const string PERCENT_FORMAT = "0.0";

    /// <summary>
    /// Stands in for a percentage too small to survive <see cref="PERCENT_FORMAT"/>'s precision.
    /// Substitutions did happen, so rendering "0.0%" would state the opposite of the sentence it
    /// sits in.
    /// </summary>
    private const string BELOW_PERCENT_RESOLUTION = "<0.1";
    #endregion

    #region Public Properties
    /// <summary>
    /// Total number of entries observed.
    /// </summary>
    public long TotalEntries { get; private set; }

    /// <summary>
    /// Number of entries Core could not reconstruct a device timestamp for, and substituted the
    /// session base time into.
    /// </summary>
    public long EntriesWithoutDeviceTimestamp { get; private set; }

    /// <summary>
    /// Fraction of entries carrying no device timestamp. Zero for an empty import.
    /// </summary>
    public double SubstitutedFraction => TotalEntries > 0
        ? EntriesWithoutDeviceTimestamp / (double)TotalEntries
        : 0.0;

    /// <summary>
    /// True when no entry in the file carried a device timestamp, so every sample sits at the
    /// session base time and the time axis is entirely flat.
    /// </summary>
    public bool HasFlatTimeAxis => TotalEntries > 0 && EntriesWithoutDeviceTimestamp == TotalEntries;

    /// <summary>
    /// True when any part of the time axis is not real: at least one entry's timestamp was
    /// substituted rather than reconstructed from the device.
    /// </summary>
    public bool HasDegenerateTimeAxis => EntriesWithoutDeviceTimestamp > 0;
    #endregion

    #region Public Methods
    /// <summary>
    /// Records one parsed entry.
    /// </summary>
    /// <param name="entry">The entry Core produced for this sample.</param>
    public void Observe(SdCardLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        TotalEntries++;

        if (!entry.HasDeviceTimestamp)
        {
            EntriesWithoutDeviceTimestamp++;
        }
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

        var percent = FormatSubstitutedPercent();
        var count = EntriesWithoutDeviceTimestamp.ToString("N0", CultureInfo.CurrentCulture);
        return $"{count} of the samples in this file ({percent}%) have no usable timestamp and " +
               "were placed at the session start time, so time spacing for those samples is not " +
               "meaningful. Older device firmware may not record timestamps in SD card logs.";
    }
    #endregion

    #region Private Methods
    /// <summary>
    /// Renders <see cref="SubstitutedFraction"/> as a percentage that can never read as zero while
    /// entries were in fact substituted: a rate below the format's resolution (1 in 5,000, say)
    /// reports as <c>&lt;0.1</c> rather than rounding down to nothing.
    /// </summary>
    private string FormatSubstitutedPercent()
    {
        // Round first and format the rounded value, so the "did this collapse to zero" test and
        // the text the user actually reads can never disagree.
        var percent = Math.Round(SubstitutedFraction * 100, 1, MidpointRounding.AwayFromZero);

        return percent > 0.0
            ? percent.ToString(PERCENT_FORMAT, CultureInfo.InvariantCulture)
            : BELOW_PERCENT_RESOLUTION;
    }
    #endregion
}
