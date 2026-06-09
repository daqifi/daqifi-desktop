using Daqifi.Core.Device.SdCard;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.Loggers;
using Microsoft.EntityFrameworkCore;

namespace Daqifi.Desktop.Test.Loggers;

/// <summary>
/// Exercises the SD card import pipeline against a real (tiny) SQLite database
/// with synthetic parsed sessions, covering the timestamp-quality diagnostics
/// added for the issue #572 follow-up: collapsed-timestamp files still import,
/// but the result flags the degenerate time axis so callers can warn the user.
/// </summary>
[TestClass]
public class SdCardSessionImporterTests
{
    private static readonly DateTime BaseTime = new(2026, 6, 9, 12, 0, 0, DateTimeKind.Utc);

    private TempSqliteLoggingContextFactory _factory;
    private SdCardSessionImporter _importer;

    [TestInitialize]
    public void Setup()
    {
        _factory = new TempSqliteLoggingContextFactory();
        _importer = new SdCardSessionImporter(_factory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _factory.Dispose();
    }

    [TestMethod]
    public async Task ImportSessionAsync_AdvancingTimestamps_ImportsWithoutWarning()
    {
        // Arrange - 10 entries x 2 analog channels at 10 Hz
        var entries = new List<SdCardLogEntry>();
        for (var i = 0; i < 10; i++)
        {
            entries.Add(new SdCardLogEntry(
                BaseTime.AddMilliseconds(i * 100), [1.0 + i, 2.0 + i], 0u, null));
        }

        var logSession = new SdCardLogSession("log_20260609_120000.bin", BaseTime, null, AsAsync(entries));

        // Act
        var result = await _importer.ImportSessionAsync(logSession, null, null, CancellationToken.None);

        // Assert
        Assert.IsFalse(result.TimestampQuality.HasDegenerateTimeAxis);
        Assert.IsNull(result.TimestampQuality.BuildUserWarning());

        using var context = _factory.CreateDbContext();
        var samples = context.Samples.AsNoTracking()
            .Where(s => s.LoggingSessionID == result.Session.ID)
            .ToList();
        Assert.AreEqual(20, samples.Count);
        Assert.AreEqual(10, samples.Select(s => s.TimestampTicks).Distinct().Count());

        var persisted = context.Sessions.AsNoTracking().Single(s => s.ID == result.Session.ID);
        Assert.AreEqual(20, persisted.SampleCount);
    }

    [TestMethod]
    public async Task ImportSessionAsync_CollapsedTimestamps_ImportsAndFlagsDegenerateTimeAxis()
    {
        // Arrange - the issue #572 shape: every entry shares one timestamp
        // (messages without msg_time_stamp collapse onto the parser's base time)
        var entries = new List<SdCardLogEntry>();
        for (var i = 0; i < 50; i++)
        {
            entries.Add(new SdCardLogEntry(BaseTime, [1.0 + i, 2.0 + i], 0u, null));
        }

        var logSession = new SdCardLogSession("log_20260609_120000.bin", BaseTime, null, AsAsync(entries));

        // Act
        var result = await _importer.ImportSessionAsync(logSession, null, null, CancellationToken.None);

        // Assert - data is kept (the viewer tolerates it) but the result warns
        Assert.IsTrue(result.TimestampQuality.HasFlatTimeAxis);
        Assert.IsTrue(result.TimestampQuality.HasDegenerateTimeAxis);
        Assert.IsNotNull(result.TimestampQuality.BuildUserWarning());

        using var context = _factory.CreateDbContext();
        var samples = context.Samples.AsNoTracking()
            .Where(s => s.LoggingSessionID == result.Session.ID)
            .ToList();
        Assert.AreEqual(100, samples.Count);
        Assert.AreEqual(1, samples.Select(s => s.TimestampTicks).Distinct().Count());
    }

    [TestMethod]
    public async Task ImportSessionAsync_SessionNameOverride_IsApplied()
    {
        // Arrange
        var entries = new List<SdCardLogEntry>
        {
            new(BaseTime, [1.0], 0u, null),
            new(BaseTime.AddMilliseconds(100), [2.0], 0u, null)
        };
        var logSession = new SdCardLogSession("log.bin", BaseTime, null, AsAsync(entries));
        var options = new ImportOptions { SessionNameOverride = "My Import" };

        // Act
        var result = await _importer.ImportSessionAsync(logSession, options, null, CancellationToken.None);

        // Assert
        Assert.AreEqual("My Import", result.Session.Name);
        Assert.IsFalse(result.TimestampQuality.HasDegenerateTimeAxis);
    }

    private static async IAsyncEnumerable<SdCardLogEntry> AsAsync(IEnumerable<SdCardLogEntry> entries)
    {
        foreach (var entry in entries)
        {
            yield return entry;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// A real file-backed SQLite <see cref="IDbContextFactory{TContext}"/>, mirroring the
    /// pattern used by ExportDialogViewModelTests/ExportPerformanceTests: the importer's
    /// bulk-insert pipeline runs against a real (tiny) database rather than a mock.
    /// The .db file is deleted on Dispose.
    /// </summary>
    private sealed class TempSqliteLoggingContextFactory : IDbContextFactory<LoggingContext>, IDisposable
    {
        private readonly string _dbPath;
        private readonly DbContextOptions<LoggingContext> _options;

        public TempSqliteLoggingContextFactory()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"daqifi_sdimport_{Guid.NewGuid():N}.db");
            _options = new DbContextOptionsBuilder<LoggingContext>()
                .UseSqlite($"Data Source={_dbPath}")
                .Options;
            using var ctx = new LoggingContext(_options);
            ctx.Database.EnsureCreated();
        }

        public LoggingContext CreateDbContext() => new(_options);

        public void Dispose()
        {
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (File.Exists(_dbPath)) { File.Delete(_dbPath); }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}
