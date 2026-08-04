using Daqifi.Core.Device.SdCard;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.Loggers;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Daqifi.Desktop.Test.Loggers;

/// <summary>
/// Exercises the SD card import pipeline against a real (tiny) SQLite database
/// with synthetic parsed sessions, covering the timestamp-quality diagnostics
/// added for the issue #572 follow-up: collapsed-timestamp files still import,
/// but the result flags the degenerate time axis so callers can warn the user.
/// The bulk-insert pipeline is the subject under test, so the database is real
/// (file-backed temp SQLite, hermetic per test) rather than mocked; the class
/// is categorized accordingly. It still runs in the fast unit gate.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class SdCardSessionImporterTests : IDisposable
{
    private static readonly DateTime BaseTime = new(2026, 6, 9, 12, 0, 0, DateTimeKind.Utc);

    private TempSqliteLoggingContextFactory _factory = null!;
    private SdCardSessionImporter _importer = null!;

    [TestInitialize]
    public void Setup()
    {
        _factory = new TempSqliteLoggingContextFactory();
        _importer = new SdCardSessionImporter(_factory);
    }

    // MSTest disposes the test-class instance after each test; IDisposable (rather than
    // [TestCleanup]) is what satisfies CA1001 for the owned SQLite context factory.
    public void Dispose()
    {
        _factory.Dispose();
        GC.SuppressFinalize(this);
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
        // Arrange - the issue #572 shape: no entry carries a device timestamp, so Core substitutes
        // the session base time into all of them. HasDeviceTimestamp is what says so
        // (daqifi-core#303); sharing a timestamp is the symptom, not the signal — real samples can
        // legitimately repeat a tick, which is why the old inference needed a tolerance margin.
        var entries = new List<SdCardLogEntry>();
        for (var i = 0; i < 50; i++)
        {
            entries.Add(new SdCardLogEntry(BaseTime, [1.0 + i, 2.0 + i], 0u, null)
            {
                HasDeviceTimestamp = false
            });
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

    [TestMethod]
    public async Task ImportFromDeviceAsync_WhenTheDeviceServesAListedZeroByteFile_ImportsAnEmptySession()
    {
        // Arrange — a 0-byte log, which an interrupted logging session routinely leaves on a FAT
        // card. Core v1.4.0 discriminates it from a wedged SD subsystem using the directory
        // listing's reported size and returns it as a legitimate empty download rather than raising
        // SdCardEmptyTransferException (daqifi-core#398 gap 2), so the importer's own "0 bytes"
        // guard would now reject a file Core deliberately let through — and, because that reached
        // the batch as a per-file failure, cost the user a skipped file per empty log (issue #780).
        var tempPath = Path.Combine(Path.GetTempPath(), $"daqifi_empty_{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(tempPath, []);

        var device = new Mock<IStreamingDevice>();
        device.Setup(d => d.DeviceSerialNo).Returns("DAQ-TEST-001");
        device
            .Setup(d => d.DownloadSdCardFileAsync(
                It.IsAny<string>(), It.IsAny<IProgress<SdCardTransferProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SdCardDownloadResult("log_20260609_120000.bin", 0, TimeSpan.Zero, tempPath));

        try
        {
            // Act
            var result = await _importer.ImportFromDeviceAsync(device.Object, "log_20260609_120000.bin");

            // Assert
            Assert.IsNotNull(result.Session);
            using var context = _factory.CreateDbContext();
            var samples = context.Samples.AsNoTracking()
                .Where(s => s.LoggingSessionID == result.Session.ID)
                .ToList();
            Assert.AreEqual(0, samples.Count, "An empty log imports as an empty session, not as a failure.");
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
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
                if (File.Exists(_dbPath))
                {
                    File.Delete(_dbPath);
                }
            }
            catch (Exception ex)
            {
                // Best-effort cleanup of a temp file; never fail the test run.
                Console.WriteLine($"Cleanup of test database '{_dbPath}' failed: {ex.Message}");
            }
        }
    }
}
