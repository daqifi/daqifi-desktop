using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Logger;
using Microsoft.EntityFrameworkCore;
using ChannelType = Daqifi.Core.Channel.ChannelType;

namespace Daqifi.Desktop.Test.Loggers;

/// <summary>
/// Covers the single-timestamp value-spread fallback used when a session has no
/// usable time range (issue #572): the spread must aggregate over every row in
/// the database, not just the rows Phase 1 happened to load. The aggregation now
/// lives in <see cref="SessionDataRepository"/> (extracted from <c>DatabaseLogger</c>, #592).
/// </summary>
[TestClass]
public class SessionDataRepositorySingleTickSpreadTests : IDisposable
{
    private const string Serial = "9090684023231015079";
    private const long Tick = 638_000_000_000_000_000;

    private TempSqliteLoggingContextFactory _factory = null!;

    [TestInitialize]
    public void Setup()
    {
        _factory = new TempSqliteLoggingContextFactory();
    }

    // MSTest disposes the test-class instance after each test; IDisposable (rather than
    // [TestCleanup]) is what satisfies CA1001 for the owned SQLite context factory.
    public void Dispose()
    {
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [TestMethod]
    public void LoadSingleTickValueSpread_AggregatesMinMaxAcrossAllRows()
    {
        // Arrange - extrema deliberately placed in the LAST rows, beyond any
        // load cap, to prove the spread is aggregated over the whole table
        using (var context = _factory.CreateDbContext())
        {
            context.Sessions.Add(new LoggingSession(1, "single-tick"));
            context.SaveChanges();

            for (var i = 0; i < 200; i++)
            {
                context.Samples.Add(MakeSample("AI0", 0.5));
                context.Samples.Add(MakeSample("AI1", 1.5));
            }

            context.Samples.Add(MakeSample("AI0", -4.0));
            context.Samples.Add(MakeSample("AI0", 6.0));
            context.Samples.Add(MakeSample("AI1", 2.5));
            context.SaveChanges();
        }

        var channelKeys = new List<(string deviceSerial, string channelName)>
        {
            (Serial, "AI0"),
            (Serial, "AI1")
        };

        // Act
        var result = SessionDataRepository.LoadSingleTickValueSpread(_factory, 1, channelKeys);

        // Assert - AI0 spans its true extrema; both points sit at delta-time zero
        var ai0 = result[(Serial, "AI0")];
        Assert.AreEqual(2, ai0.Count);
        Assert.AreEqual(-4.0, ai0[0].Y);
        Assert.AreEqual(6.0, ai0[1].Y);
        Assert.AreEqual(0.0, ai0[0].X);
        Assert.AreEqual(0.0, ai0[1].X);

        var ai1 = result[(Serial, "AI1")];
        Assert.AreEqual(2, ai1.Count);
        Assert.AreEqual(1.5, ai1[0].Y);
        Assert.AreEqual(2.5, ai1[1].Y);
    }

    [TestMethod]
    public void LoadSingleTickValueSpread_ConstantValue_ReturnsSinglePoint()
    {
        // Arrange
        using (var context = _factory.CreateDbContext())
        {
            context.Sessions.Add(new LoggingSession(1, "constant"));
            context.SaveChanges();

            for (var i = 0; i < 10; i++)
            {
                context.Samples.Add(MakeSample("AI0", 1.25));
            }

            context.SaveChanges();
        }

        // Act
        var result = SessionDataRepository.LoadSingleTickValueSpread(
            _factory, 1, [(Serial, "AI0")]);

        // Assert
        var points = result[(Serial, "AI0")];
        Assert.AreEqual(1, points.Count);
        Assert.AreEqual(1.25, points[0].Y);
    }

    [TestMethod]
    public void LoadSingleTickValueSpread_OnlyRequestedChannelKeysAreReturned()
    {
        // Arrange - DB contains a channel that discovery did not request, and
        // discovery requested a channel that has no rows
        using (var context = _factory.CreateDbContext())
        {
            context.Sessions.Add(new LoggingSession(1, "keys"));
            context.SaveChanges();

            context.Samples.Add(MakeSample("AI0", 1.0));
            context.Samples.Add(MakeSample("AI9", 9.0));
            context.SaveChanges();
        }

        // Act
        var result = SessionDataRepository.LoadSingleTickValueSpread(
            _factory, 1, [(Serial, "AI0"), (Serial, "AI1")]);

        // Assert
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(1, result[(Serial, "AI0")].Count);
        Assert.AreEqual(0, result[(Serial, "AI1")].Count);
        Assert.IsFalse(result.ContainsKey((Serial, "AI9")));
    }

    private static DataSample MakeSample(string channelName, double value)
    {
        return new DataSample
        {
            LoggingSessionID = 1,
            ChannelName = channelName,
            DeviceName = "Nq1",
            DeviceSerialNo = Serial,
            Color = "#FFD32F2F",
            Type = ChannelType.Analog,
            Value = value,
            TimestampTicks = Tick
        };
    }

    /// <summary>
    /// A real file-backed SQLite <see cref="IDbContextFactory{TContext}"/>, mirroring the
    /// pattern in ExportDialogViewModelTests: the GroupBy aggregation must run through the
    /// actual EF-to-SQLite translation, not a mock. The .db file is deleted on Dispose.
    /// </summary>
    private sealed class TempSqliteLoggingContextFactory : IDbContextFactory<LoggingContext>, IDisposable
    {
        private readonly string _dbPath;
        private readonly DbContextOptions<LoggingContext> _options;

        public TempSqliteLoggingContextFactory()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"daqifi_singletick_{Guid.NewGuid():N}.db");
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
