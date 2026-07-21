using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Helpers;
using Microsoft.EntityFrameworkCore;
using OxyPlot;
using System.Diagnostics;
using ChannelType = Daqifi.Core.Channel.ChannelType;

namespace Daqifi.Desktop.Logger;

/// <summary>
/// Channel identity and presentation info discovered from a session's sample rows.
/// </summary>
/// <param name="ChannelName">Channel identifier (e.g., "AI0").</param>
/// <param name="DeviceSerialNo">Serial number of the device that owns the channel.</param>
/// <param name="Type">Channel type used to pick the Y axis.</param>
/// <param name="Color">Series color stored with the samples.</param>
internal sealed record SessionChannelInfo(string ChannelName, string DeviceSerialNo, ChannelType Type, string Color);

/// <summary>
/// Result of the fast Phase-1 session load: the channels discovered at the session's first
/// timestamp, an initial point batch for each of them, the session's first timestamp, and the
/// total sample count. <see cref="IsEmpty"/> is true for a session with no samples.
/// </summary>
/// <param name="Channels">Channels discovered at the first timestamp, naturally ordered and de-duplicated.</param>
/// <param name="Points">One point list per discovered channel, filled with up to the initial-load cap of samples.</param>
/// <param name="FirstTime">The session's earliest timestamp (the delta-time origin), or null for an empty session.</param>
/// <param name="TotalSampleCount">Count of every sample row in the session.</param>
internal sealed record InitialSessionLoad(
    IReadOnlyList<SessionChannelInfo> Channels,
    Dictionary<(string deviceSerial, string channelName), List<DataPoint>> Points,
    DateTime? FirstTime,
    int TotalSampleCount)
{
    /// <summary>True when the session had no samples to discover channels from.</summary>
    public bool IsEmpty => Channels.Count == 0;

    /// <summary>
    /// A fresh empty-session result: no channels, no points, no time origin. Returns a new instance on
    /// each access so the empty sentinel never shares its mutable <see cref="Points"/> dictionary across
    /// loads (a cached singleton would leak any accidental mutation into every later empty-session load).
    /// </summary>
    public static InitialSessionLoad Empty => new([], new(), null, 0);
}

/// <summary>
/// Owns the session read/delete path extracted from <see cref="DatabaseLogger"/> (issue #592): the
/// EF/ADO.NET queries that turn a session id into point lists (channel discovery, the fast initial
/// batch, the full-range sampled load, the single-timestamp value spread, per-device frequency) and
/// the transactional delete of a session's storage.
/// <para>
/// The database context factory and application logger are constructor-injected, so the repository
/// has no dependency on WPF or on desktop singletons (<c>App.ServiceProvider</c>,
/// <c>AppLogger.Instance</c>) and is unit-testable in isolation against a real temp-SQLite factory.
/// <see cref="DatabaseLogger"/> is the composition root: it builds the repository with the factory it
/// already receives and orchestrates plot construction around the point data the repository returns.
/// </para>
/// </summary>
public sealed class SessionDataRepository
{
    #region Constants
    /// <summary>
    /// Upper bound on rows loaded by the fast Phase-1 batch (<see cref="LoadInitialSession"/>) for
    /// immediate display. <see cref="DatabaseLogger"/> compares a session's total sample count against
    /// this to decide whether a Phase-2 full-range load is needed.
    /// </summary>
    internal const int INITIAL_LOAD_POINTS = 100_000;

    /// <summary>
    /// Number of time-range segments <see cref="LoadSampledData"/> seeks across, i.e. the approximate
    /// number of points produced per channel for the full-range view. <see cref="DatabaseLogger"/>'s
    /// viewport-density heuristic reads the same constant so the "is this channel sampled vs. fully
    /// loaded" decision stays consistent with how this repository samples.
    /// </summary>
    internal const int SAMPLED_POINTS_PER_CHANNEL = 3000;

    /// <summary>
    /// Series color used when a persisted sample row has no color (legacy or imported data). Must be a
    /// string <see cref="OxyPlot.OxyColor.Parse"/> accepts.
    /// </summary>
    internal const string FALLBACK_CHANNEL_COLOR = "#FF808080";
    #endregion

    #region Private Fields
    private readonly IDbContextFactory<LoggingContext> _loggingContext;
    private readonly IAppLogger _appLogger;
    #endregion

    #region Constructor
    /// <summary>
    /// Creates the repository.
    /// </summary>
    /// <param name="loggingContext">Factory for the logging database context used by every query.</param>
    /// <param name="appLogger">Application logger used to report read failures and duplicate-channel warnings.</param>
    public SessionDataRepository(IDbContextFactory<LoggingContext> loggingContext, IAppLogger appLogger)
    {
        _loggingContext = loggingContext ?? throw new ArgumentNullException(nameof(loggingContext));
        _appLogger = appLogger ?? throw new ArgumentNullException(nameof(appLogger));
    }
    #endregion

    #region Channel discovery + initial load
    /// <summary>
    /// Phase 1 of a session display (&lt;1s): discovers the channels present at the session's first
    /// timestamp, loads a small initial batch of points for each of them for immediate display, and
    /// counts the session's total samples. Designed to be called from a background thread; performs no
    /// UI or plot work. Returns <see cref="InitialSessionLoad.Empty"/> for a session with no samples.
    /// <para>
    /// A channel can appear at the first timestamp more than once (firmware that emits multiple
    /// messages per sample period, or SD imports whose timestamps could not be reconstructed). Series
    /// and legend items are keyed by (serial, channel), so duplicates are collapsed in SQL and again
    /// via <see cref="DeduplicateChannelInfo"/> — a degenerate session must not materialize thousands
    /// of rows just to discover its channels, nor abort the whole load on a duplicate key (#572).
    /// </para>
    /// </summary>
    /// <param name="sessionId">The session to load.</param>
    /// <returns>The discovered channels, their seeded-and-filled point lists, the first timestamp, and the total sample count.</returns>
    internal InitialSessionLoad LoadInitialSession(int sessionId)
    {
        using var context = _loggingContext.CreateDbContext();
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        var baseQuery = context.Samples.AsNoTracking()
            .Where(s => s.LoggingSessionID == sessionId);

        // Get the first timestamp to extract channel info (instant via composite index)
        var firstSample = baseQuery
            .OrderBy(s => s.TimestampTicks)
            .Select(s => new { s.TimestampTicks })
            .FirstOrDefault();

        if (firstSample == null)
        {
            return InitialSessionLoad.Empty;
        }

        // Collapse duplicate (serial, channel) rows at the first timestamp in SQL.
        var channelGroups = baseQuery
            .Where(s => s.TimestampTicks == firstSample.TimestampTicks)
            .GroupBy(s => new { s.DeviceSerialNo, s.ChannelName })
            .Select(g => new
            {
                g.Key.DeviceSerialNo,
                g.Key.ChannelName,
                Type = g.Min(s => (int)s.Type),
                Color = g.Min(s => s.Color),
                RowCount = g.Count()
            })
            .ToList();

        var duplicateRows = channelGroups.Sum(g => g.RowCount) - channelGroups.Count;
        if (duplicateRows > 0)
        {
            _appLogger.Warning(
                $"Session {sessionId}: first timestamp has {duplicateRows} duplicate " +
                "channel sample(s); ignoring duplicates for channel discovery.");
        }

        // Color is nullable in the Samples table (legacy/imported rows can omit it) and the plot
        // factory feeds it straight to OxyColor.Parse, which throws on null. Fall back to a neutral
        // grey so one colorless row cannot abort the whole session load.
        var channels = DeduplicateChannelInfo(channelGroups.Select(g =>
            new SessionChannelInfo(g.ChannelName, g.DeviceSerialNo, (ChannelType)g.Type, g.Color ?? FALLBACK_CHANNEL_COLOR)));

        // Seed one point list per discovered channel, then fill from the initial batch. Channels not
        // present at the first timestamp are intentionally never seeded, so their later samples are
        // dropped — series exist only for channels discovered here.
        var points = new Dictionary<(string deviceSerial, string channelName), List<DataPoint>>();
        foreach (var channel in channels)
        {
            points[(channel.DeviceSerialNo, channel.ChannelName)] = [];
        }

        // Load initial batch for fast display (100K rows, ~16ms via index)
        DateTime? firstTime = null;
        foreach (var sample in baseQuery
            .OrderBy(s => s.TimestampTicks)
            .Select(s => new { s.ChannelName, s.DeviceSerialNo, s.TimestampTicks, s.Value })
            .Take(INITIAL_LOAD_POINTS)
            .AsEnumerable())
        {
            var key = (sample.DeviceSerialNo, sample.ChannelName);
            firstTime ??= new DateTime(sample.TimestampTicks);
            var deltaTime = (sample.TimestampTicks - firstTime.Value.Ticks) / 10000.0;

            if (points.TryGetValue(key, out var channelPoints))
            {
                channelPoints.Add(new DataPoint(deltaTime, sample.Value));
            }
        }

        var totalSampleCount = baseQuery.Count();

        return new InitialSessionLoad(channels, points, firstTime, totalSampleCount);
    }

    /// <summary>
    /// Collapses duplicate (device serial, channel name) rows to a single entry,
    /// keeping the first occurrence, and orders the result naturally by channel name.
    /// A session can contain several samples for one channel at a single timestamp
    /// (duplicate device messages, SD imports without reconstructable timestamps);
    /// series and legend construction require exactly one entry per channel.
    /// </summary>
    /// <param name="rows">Channel rows discovered from a session's samples.</param>
    /// <returns>One entry per (device serial, channel name), naturally ordered.</returns>
    internal static List<SessionChannelInfo> DeduplicateChannelInfo(IEnumerable<SessionChannelInfo> rows)
    {
        return rows
            .DistinctBy(r => (r.DeviceSerialNo, r.ChannelName))
            .NaturalOrderBy(r => r.ChannelName)
            .ToList();
    }
    #endregion

    #region Full-range sampled load
    /// <summary>
    /// Loads a uniformly sampled subset of data covering the full time range
    /// using targeted index seeks. Instead of reading all N million rows,
    /// divides the time range into <see cref="SAMPLED_POINTS_PER_CHANNEL"/> segments and
    /// seeks to each segment boundary via the composite index. Each seek
    /// reads one batch of interleaved channel data (~channelCount rows).
    /// Result: ~3000 points per channel in ~1-3 seconds regardless of total dataset size.
    /// </summary>
    /// <param name="sessionId">The session to load.</param>
    /// <param name="channelCount">Number of channels, used to size each seek batch.</param>
    /// <param name="localPoints">Pre-seeded (one entry per channel key) point lists to fill.</param>
    /// <returns>The session's first timestamp, or null when the session has no usable time range.</returns>
    public DateTime? LoadSampledData(
        int sessionId,
        int channelCount,
        Dictionary<(string deviceSerial, string channelName), List<DataPoint>> localPoints)
    {
        using var context = _loggingContext.CreateDbContext();
        var connection = context.Database.GetDbConnection();
        connection.Open();

        // Get time bounds via index (instant)
        long minTicks, maxTicks;
        using (var boundsCmd = connection.CreateCommand())
        {
            boundsCmd.CommandText = @"
                SELECT MIN(TimestampTicks), MAX(TimestampTicks)
                FROM Samples
                WHERE LoggingSessionID = @id";
            var idParam = boundsCmd.CreateParameter();
            idParam.ParameterName = "@id";
            idParam.Value = sessionId;
            boundsCmd.Parameters.Add(idParam);

            using var reader = boundsCmd.ExecuteReader();
            if (!reader.Read() || reader.IsDBNull(0))
            {
                return null;
            }

            minTicks = reader.GetInt64(0);
            maxTicks = reader.GetInt64(1);
        }

        if (minTicks >= maxTicks)
        {
            return null;
        }

        var localFirstTime = new DateTime(minTicks);
        var tickStep = Math.Max(1, (maxTicks - minTicks) / SAMPLED_POINTS_PER_CHANNEL);
        // Read at least channelCount rows per seek to get one sample per channel
        var batchSize = Math.Max(channelCount * 2, 100);

        // Prepared statement for repeated seeks
        using var seekCmd = connection.CreateCommand();
        seekCmd.CommandText = @"
            SELECT ChannelName, DeviceSerialNo, TimestampTicks, Value
            FROM Samples
            WHERE LoggingSessionID = @id AND TimestampTicks >= @t
            ORDER BY TimestampTicks
            LIMIT @limit";

        var seekIdParam = seekCmd.CreateParameter();
        seekIdParam.ParameterName = "@id";
        seekIdParam.Value = sessionId;
        seekCmd.Parameters.Add(seekIdParam);

        var seekTParam = seekCmd.CreateParameter();
        seekTParam.ParameterName = "@t";
        seekTParam.Value = minTicks;
        seekCmd.Parameters.Add(seekTParam);

        var seekLimitParam = seekCmd.CreateParameter();
        seekLimitParam.ParameterName = "@limit";
        seekLimitParam.Value = batchSize;
        seekCmd.Parameters.Add(seekLimitParam);

        seekCmd.Prepare();

        // Track which timestamps we've already added to avoid duplicates
        // from overlapping batches
        var lastAddedTimestamp = new Dictionary<(string, string), long>();

        // Use <= so the final iteration (i == SAMPLED_POINTS_PER_CHANNEL)
        // seeks at maxTicks, ensuring the session tail is always included
        for (var i = 0; i <= SAMPLED_POINTS_PER_CHANNEL; i++)
        {
            var seekTimestamp = i < SAMPLED_POINTS_PER_CHANNEL
                ? minTicks + i * tickStep
                : maxTicks;
            seekTParam.Value = seekTimestamp;

            using var reader = seekCmd.ExecuteReader();
            while (reader.Read())
            {
                var channelName = reader.GetString(0);
                var deviceSerialNo = reader.GetString(1);
                var timestampTicks = reader.GetInt64(2);
                var value = reader.GetDouble(3);

                var key = (deviceSerialNo, channelName);

                // Skip duplicate timestamps from overlapping batches
                if (lastAddedTimestamp.TryGetValue(key, out var lastT) && timestampTicks <= lastT)
                {
                    continue;
                }

                lastAddedTimestamp[key] = timestampTicks;

                var deltaTime = (timestampTicks - localFirstTime.Ticks) / 10000.0;
                if (localPoints.TryGetValue(key, out var points))
                {
                    points.Add(new DataPoint(deltaTime, value));
                }
            }
        }

        return localFirstTime;
    }

    /// <summary>
    /// Builds per-channel value spreads for a session whose samples all share a
    /// single timestamp. Phase 1 caps how many rows it loads, so this aggregates
    /// MIN/MAX over every row in SQL — exact without materializing the rows —
    /// and represents each channel as a two-point vertical segment at delta-time
    /// zero (one point when the value never changes).
    /// </summary>
    /// <param name="contextFactory">Factory for the logging database context.</param>
    /// <param name="sessionId">The session whose samples are aggregated.</param>
    /// <param name="channelKeys">The channels discovered for the session.</param>
    /// <returns>One point list per requested channel key.</returns>
    internal static Dictionary<(string deviceSerial, string channelName), List<DataPoint>> LoadSingleTickValueSpread(
        IDbContextFactory<LoggingContext> contextFactory,
        int sessionId,
        List<(string deviceSerial, string channelName)> channelKeys)
    {
        var result = new Dictionary<(string deviceSerial, string channelName), List<DataPoint>>();
        foreach (var key in channelKeys)
        {
            result[key] = [];
        }

        using var context = contextFactory.CreateDbContext();
        var spreads = context.Samples.AsNoTracking()
            .Where(s => s.LoggingSessionID == sessionId)
            .GroupBy(s => new { s.DeviceSerialNo, s.ChannelName })
            .Select(g => new
            {
                g.Key.DeviceSerialNo,
                g.Key.ChannelName,
                MinValue = g.Min(s => s.Value),
                MaxValue = g.Max(s => s.Value)
            })
            .ToList();

        foreach (var spread in spreads)
        {
            if (!result.TryGetValue((spread.DeviceSerialNo, spread.ChannelName), out var points))
            {
                continue;
            }

            points.Add(new DataPoint(0, spread.MinValue));
            if (spread.MaxValue > spread.MinValue)
            {
                points.Add(new DataPoint(0, spread.MaxValue));
            }
        }

        return result;
    }
    #endregion

    #region Session metadata
    /// <summary>
    /// Loads per-device sampling frequency for a session from <c>SessionDeviceMetadata</c>.
    /// Returns an empty dictionary for legacy sessions logged before metadata was persisted —
    /// the legend will simply omit the frequency line for those.
    /// </summary>
    /// <param name="sessionId">The session whose device metadata to load.</param>
    /// <returns>Map of device serial number to configured sampling frequency in Hz.</returns>
    public Dictionary<string, int> LoadSessionDeviceFrequency(int sessionId)
    {
        var result = new Dictionary<string, int>();
        try
        {
            using var context = _loggingContext.CreateDbContext();
            var metadata = context.SessionDeviceMetadata.AsNoTracking()
                .Where(m => m.LoggingSessionID == sessionId)
                .Select(m => new { m.DeviceSerialNo, m.SamplingFrequencyHz })
                .ToList();

            foreach (var entry in metadata)
            {
                if (!string.IsNullOrEmpty(entry.DeviceSerialNo))
                {
                    result[entry.DeviceSerialNo] = entry.SamplingFrequencyHz;
                }
            }
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Failed to load SessionDeviceMetadata");
        }
        return result;
    }
    #endregion

    #region Delete
    /// <summary>
    /// Deletes a session's samples, device metadata, and the session row itself in a single
    /// transaction. On failure the transaction is rolled back, the error is logged, and the exception
    /// is rethrown so the caller can keep the session's bound UI row instead of silently dropping a
    /// row whose data still exists in the database (#592). The completion timing is always logged.
    /// </summary>
    /// <param name="session">The session to delete.</param>
    /// <exception cref="Exception">Rethrown when the delete transaction fails.</exception>
    public void DeleteSession(LoggingSession session)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var context = _loggingContext.CreateDbContext();
            var connection = context.Database.GetDbConnection();
            connection.Open();

            using var transaction = connection.BeginTransaction();

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM Samples WHERE LoggingSessionID = @id";
                var param = cmd.CreateParameter();
                param.ParameterName = "@id";
                param.Value = session.ID;
                cmd.Parameters.Add(param);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM SessionDeviceMetadata WHERE LoggingSessionID = @id";
                var param = cmd.CreateParameter();
                param.ParameterName = "@id";
                param.Value = session.ID;
                cmd.Parameters.Add(param);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM Sessions WHERE ID = @id";
                var param = cmd.CreateParameter();
                param.ParameterName = "@id";
                param.Value = session.ID;
                cmd.Parameters.Add(param);
                cmd.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            // Log here for the low-level DB context, then propagate. Swallowing this (the pre-#592
            // behavior) let the session-list view model remove the bound row even when the delete
            // failed, so a row whose data still existed reappeared on the next reload.
            _appLogger.Error(ex, "Failed in DeleteLoggingSession");
            throw;
        }
        finally
        {
            stopwatch.Stop();
            _appLogger.Information($"DeleteLoggingSession completed in {stopwatch.ElapsedMilliseconds}ms");
        }
    }
    #endregion
}
