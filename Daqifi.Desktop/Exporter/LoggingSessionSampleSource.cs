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
    private readonly IDbContextFactory<LoggingContext> _contextFactory;
    private readonly ICollection<DataSample> _inMemorySamples;
    private IReadOnlyList<ChannelDescriptor> _channelsCache;
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

        using var context = _contextFactory.CreateDbContext();
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        _channelsCache = context.Samples
            .AsNoTracking()
            .Where(s => s.LoggingSessionID == _session.ID)
            .GroupBy(s => new { s.DeviceName, s.DeviceSerialNo, s.ChannelName })
            .Select(g => new
            {
                g.Key.DeviceName,
                g.Key.DeviceSerialNo,
                g.Key.ChannelName,
                Type = g.Min(s => s.Type),
            })
            .OrderBy(s => s.DeviceName)
            .ThenBy(s => s.DeviceSerialNo)
            .ThenBy(s => s.ChannelName)
            .AsEnumerable()
            .Select(s => new ChannelDescriptor(s.DeviceName, s.DeviceSerialNo, s.ChannelName, s.Type))
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

        await using var context = _contextFactory.CreateDbContext();
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        _countCache = await context.Samples
            .AsNoTracking()
            .CountAsync(s => s.LoggingSessionID == _session.ID, cancellationToken)
            .ConfigureAwait(false);
        return _countCache.Value;
    }

    /// <summary>
    /// Streams all samples for this session in ascending timestamp order. The EF path uses
    /// <c>AsAsyncEnumerable</c> so rows are read row-by-row without materializing the whole result set.
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

        await using var context = _contextFactory.CreateDbContext();
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        var query = context.Samples
            .AsNoTracking()
            .Where(s => s.LoggingSessionID == _session.ID)
            .OrderBy(s => s.TimestampTicks)
            .Select(s => new { s.TimestampTicks, s.DeviceName, s.DeviceSerialNo, s.ChannelName, s.Value });

        await foreach (var s in query.AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            yield return new SampleRow(
                s.TimestampTicks,
                $"{s.DeviceName}:{s.DeviceSerialNo}:{s.ChannelName}",
                s.Value);
        }
    }
    #endregion
}
