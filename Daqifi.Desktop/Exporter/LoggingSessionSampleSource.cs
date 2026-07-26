using System.Runtime.CompilerServices;
using Daqifi.Core.Logging.Export;
using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Logger;
using Microsoft.EntityFrameworkCore;

namespace Daqifi.Desktop.Exporter;

/// <summary>
/// Adapts a desktop <see cref="LoggingSession"/> to the core
/// <see cref="ISampleSource"/> seam consumed by <see cref="CsvExporter"/>.
/// Supports two modes: an EF Core path backed by <see cref="LoggingContext"/>
/// (production) and an in-memory path over a pre-populated
/// <see cref="LoggingSession.DataSamples"/> collection (used by tests).
/// </summary>
public sealed class LoggingSessionSampleSource : ISampleSource
{
    #region Private Fields
    private readonly LoggingSession _session;
    /// <summary>EF path only: null when this source was built over an in-memory collection.</summary>
    private readonly IDbContextFactory<LoggingContext>? _contextFactory;
    /// <summary>In-memory path only: null when this source was built over the EF store.</summary>
    private readonly ICollection<DataSample>? _inMemorySamples;
    private IReadOnlyList<ChannelDescriptor>? _channelsCache;
    private int? _countCache;
    #endregion

    #region Constructors
    /// <summary>
    /// Creates a sample source that reads from the persisted EF Core store.
    /// </summary>
    /// <param name="session">The session whose samples should be exported.</param>
    /// <param name="contextFactory">Factory that produces short-lived <see cref="LoggingContext"/>s.</param>
    public LoggingSessionSampleSource(LoggingSession session, IDbContextFactory<LoggingContext> contextFactory)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    }

    /// <summary>
    /// Creates a sample source backed by a pre-populated in-memory sample collection.
    /// Used by tests that don't have a real database available.
    /// </summary>
    /// <param name="session">The session the samples belong to (only <see cref="LoggingSession.ID"/> is read).</param>
    /// <param name="inMemorySamples">Sample rows to enumerate.</param>
    public LoggingSessionSampleSource(LoggingSession session, ICollection<DataSample> inMemorySamples)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _inMemorySamples = inMemorySamples ?? throw new ArgumentNullException(nameof(inMemorySamples));
    }
    #endregion

    #region Private Helpers
    /// <summary>
    /// Creates a short-lived <see cref="LoggingContext"/> for the EF-backed path. The two
    /// constructors are mutually exclusive, so every caller reaches here only after finding
    /// <c>_inMemorySamples</c> null — which means the EF constructor ran and set the factory.
    /// </summary>
    private LoggingContext CreateContext()
    {
        if (_contextFactory == null)
        {
            throw new InvalidOperationException(
                "This sample source was built over an in-memory collection and has no EF context factory.");
        }

        return _contextFactory.CreateDbContext();
    }
    #endregion

    #region ISampleSource
    /// <summary>
    /// Returns the ordered set of channels present in this session. Channels are deduped by
    /// <c>(DeviceName, DeviceSerialNo, ChannelName)</c>; the <see cref="ChannelDescriptor.ChannelType"/>
    /// is taken from the first observed sample for that channel. Both the in-memory and DB paths use
    /// the same dedup logic so the resulting descriptor sets match.
    /// </summary>
    public IReadOnlyList<ChannelDescriptor> GetChannels()
    {
        if (_channelsCache != null)
        {
            return _channelsCache;
        }

        if (_inMemorySamples != null)
        {
            _channelsCache = _inMemorySamples
                .GroupBy(s => new { s.DeviceName, s.DeviceSerialNo, s.ChannelName })
                .Select(g => new ChannelDescriptor(
                    g.Key.DeviceName,
                    g.Key.DeviceSerialNo,
                    g.Key.ChannelName,
                    g.First().Type))
                .OrderBy(c => c.DeviceName)
                .ThenBy(c => c.DeviceSerialNo)
                .ThenBy(c => c.ChannelName)
                .ToList();
            return _channelsCache;
        }

        using var context = CreateContext();
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        // DISTINCT over the four columns instead of GROUP BY + MIN aggregate: SQLite executes
        // the former ~4x faster on million-sample sessions (measured on real device data), and
        // a channel virtually always has a single Type, so the result set is the same size.
        // The (pathological) multi-type collapse and the ordering happen client-side on the
        // handful of resulting rows — ordinal comparison matches SQLite's BINARY collation so
        // the column order (and therefore the CSV bytes) is unchanged.
        _channelsCache = context.Samples
            .AsNoTracking()
            .Where(s => s.LoggingSessionID == _session.ID)
            .Select(s => new { s.DeviceName, s.DeviceSerialNo, s.ChannelName, s.Type })
            .Distinct()
            .AsEnumerable()
            .GroupBy(s => new { s.DeviceName, s.DeviceSerialNo, s.ChannelName })
            .Select(g => new ChannelDescriptor(
                g.Key.DeviceName,
                g.Key.DeviceSerialNo,
                g.Key.ChannelName,
                g.Min(s => s.Type)))
            .OrderBy(c => c.DeviceName, StringComparer.Ordinal)
            .ThenBy(c => c.DeviceSerialNo, StringComparer.Ordinal)
            .ThenBy(c => c.ChannelName, StringComparer.Ordinal)
            .ToList();
        return _channelsCache;
    }

    /// <summary>
    /// Returns the total sample count for this session. Used by core's <see cref="CsvExporter"/>
    /// to drive progress reporting. Honors <paramref name="cancellationToken"/> on the DB path.
    /// </summary>
    public async ValueTask<int> GetSampleCountAsync(CancellationToken cancellationToken = default)
    {
        if (_countCache.HasValue)
        {
            return _countCache.Value;
        }

        if (_inMemorySamples != null)
        {
            _countCache = _inMemorySamples.Count;
            return _countCache.Value;
        }

        await using var context = CreateContext();
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        _countCache = await context.Samples
            .AsNoTracking()
            .CountAsync(s => s.LoggingSessionID == _session.ID, cancellationToken)
            .ConfigureAwait(false);
        return _countCache.Value;
    }

    /// <summary>
    /// Streams all samples for this session in ascending timestamp order. The EF path enumerates
    /// the query synchronously (row-by-row, without materializing the result set) inside the async
    /// iterator: SQLite's provider is synchronous under the hood, so EF's async query pipeline adds
    /// only per-row overhead — measurably slower on million-sample sessions. Cancellation is honored
    /// via per-row token checks, matching the legacy exporter's responsiveness.
    /// </summary>
    public async IAsyncEnumerable<SampleRow> StreamSamples([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_inMemorySamples != null)
        {
            foreach (var s in _inMemorySamples.OrderBy(d => d.TimestampTicks))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new SampleRow(
                    s.TimestampTicks,
                    $"{s.DeviceName}:{s.DeviceSerialNo}:{s.ChannelName}",
                    s.Value);
            }
            yield break;
        }

        await using var context = CreateContext();
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        var query = context.Samples
            .AsNoTracking()
            .Where(s => s.LoggingSessionID == _session.ID)
            .OrderBy(s => s.TimestampTicks)
            .Select(s => new { s.TimestampTicks, s.DeviceName, s.DeviceSerialNo, s.ChannelName, s.Value });

        foreach (var s in query)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new SampleRow(
                s.TimestampTicks,
                $"{s.DeviceName}:{s.DeviceSerialNo}:{s.ChannelName}",
                s.Value);
        }
    }
    #endregion
}
