using System.Diagnostics;
using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Logger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using ChannelType = Daqifi.Core.Channel.ChannelType;

namespace Daqifi.Desktop.Test.Loggers;

/// <summary>
/// Behavior contract for <see cref="SessionSampleWriter"/> — the SQLite sample write path extracted
/// from <c>DatabaseLogger</c> (issue #592). The writer is driven directly against a real temp SQLite
/// <see cref="IDbContextFactory{TContext}"/> (no mock DB), so the producer/consumer buffer, the
/// background bulk-insert, and the suspend/resume/clear controls the session-list purge relies on are
/// all exercised end-to-end. Covers: buffered samples reach the database after <c>WaitForIdle</c>,
/// <c>SuspendConsumer</c> halts draining while <c>ResumeConsumer</c> resumes it, <c>ClearBuffer</c>
/// drops pending samples, and <c>Dispose</c> stops the consumer thread cleanly and is idempotent.
/// </summary>
[TestClass]
public class SessionSampleWriterTests
{
    private const string Serial = "9090684023231015079";
    private const long Tick = 638_000_000_000_000_000;
    private const int SessionId = 1;

    private readonly List<string> _tempDbPaths = [];

    [TestCleanup]
    public void TestCleanup()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var path in _tempDbPaths)
        {
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                try
                {
                    if (File.Exists(path + suffix))
                    {
                        File.Delete(path + suffix);
                    }
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }
        }
    }

    [TestMethod]
    public void EnqueuedSamples_ArePersisted_AfterWaitForIdle()
    {
        using var factory = NewFactory();
        SeedSession(factory);
        using var writer = new SessionSampleWriter(factory, Mock.Of<IAppLogger>());

        for (var i = 0; i < 25; i++)
        {
            writer.Add(MakeSample("AI0", i));
        }

        writer.WaitForIdle(TimeSpan.FromSeconds(5));

        Assert.AreEqual(25, CountSamples(factory),
            "Every enqueued sample must be flushed to the database by the time WaitForIdle returns.");
    }

    [TestMethod]
    public void SuspendConsumer_HaltsDraining_AndResumeConsumerResumesIt()
    {
        using var factory = NewFactory();
        SeedSession(factory);
        using var writer = new SessionSampleWriter(factory, Mock.Of<IAppLogger>());

        // Suspend before enqueueing: the consumer must park at the gate and never open a DB
        // connection while suspended.
        writer.SuspendConsumer();

        for (var i = 0; i < 10; i++)
        {
            writer.Add(MakeSample("AI0", i));
        }

        // Give the consumer several poll cycles; while suspended it must not drain anything.
        Thread.Sleep(500);
        Assert.AreEqual(0, CountSamples(factory),
            "A suspended consumer must not persist any buffered samples.");

        // Resuming lets the parked consumer drain the backlog.
        writer.ResumeConsumer();
        writer.WaitForIdle(TimeSpan.FromSeconds(5));

        Assert.AreEqual(10, CountSamples(factory),
            "Resuming the consumer must flush the samples buffered while it was suspended.");
    }

    [TestMethod]
    public void ClearBuffer_DropsPendingSamples()
    {
        using var factory = NewFactory();
        SeedSession(factory);
        using var writer = new SessionSampleWriter(factory, Mock.Of<IAppLogger>());

        // Mirror the delete-all purge ordering: suspend, drain the buffer, resume.
        writer.SuspendConsumer();

        for (var i = 0; i < 10; i++)
        {
            writer.Add(MakeSample("AI0", i));
        }

        writer.ClearBuffer();
        writer.ResumeConsumer();
        writer.WaitForIdle(TimeSpan.FromSeconds(2));

        Assert.AreEqual(0, CountSamples(factory),
            "ClearBuffer must drop the pending samples so none of them reach the database.");
    }

    [TestMethod]
    public void Dispose_StopsConsumerThreadCleanly_AndIsIdempotent()
    {
        using var factory = NewFactory();
        SeedSession(factory);
        var writer = new SessionSampleWriter(factory, Mock.Of<IAppLogger>());
        writer.Add(MakeSample("AI0", 1));

        var stopwatch = Stopwatch.StartNew();
        writer.Dispose();
        stopwatch.Stop();

        // Dispose is capped by Join(2s); a clean shutdown wakes the polling thread and returns in
        // well under that. The bound only guards against a pathological overrun — the load-bearing
        // proof that the thread actually stopped is the IsConsumerThreadAlive check below.
        Assert.IsTrue(stopwatch.ElapsedMilliseconds < 2500,
            $"Dispose must not hang; it took {stopwatch.ElapsedMilliseconds}ms.");
        Assert.IsFalse(writer.IsConsumerThreadAlive,
            "Dispose must join the consumer thread so it is no longer running.");

        // Idempotent: a second dispose must not throw on the already-disposed primitives.
        writer.Dispose();

        // And enqueueing after disposal is a silent no-op rather than throwing.
        writer.Add(MakeSample("AI0", 2));
    }

    #region Helpers

    private static void SeedSession(IDbContextFactory<LoggingContext> factory)
    {
        using var context = factory.CreateDbContext();
        context.Sessions.Add(new LoggingSession(SessionId, "writer-test"));
        context.SaveChanges();
    }

    private static int CountSamples(IDbContextFactory<LoggingContext> factory)
    {
        using var context = factory.CreateDbContext();
        return context.Samples.AsNoTracking().Count(s => s.LoggingSessionID == SessionId);
    }

    private static DataSample MakeSample(string channelName, double value)
    {
        return new DataSample
        {
            LoggingSessionID = SessionId,
            ChannelName = channelName,
            DeviceName = "Nq1",
            DeviceSerialNo = Serial,
            Color = "#FFD32F2F",
            Type = ChannelType.Analog,
            Value = value,
            TimestampTicks = Tick
        };
    }

    private TempSqliteLoggingContextFactory NewFactory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"daqifi_samplewriter_{Guid.NewGuid():N}.db");
        _tempDbPaths.Add(path);
        return new TempSqliteLoggingContextFactory(path);
    }

    /// <summary>
    /// A real file-backed SQLite <see cref="IDbContextFactory{TContext}"/> so the consumer's bulk
    /// insert runs through the actual EF-to-SQLite translation, not a mock. Mirrors the production
    /// factory registration (App.xaml.cs) by suppressing the PendingModelChangesWarning. The .db
    /// file is removed by the test cleanup.
    /// </summary>
    private sealed class TempSqliteLoggingContextFactory : IDbContextFactory<LoggingContext>, IDisposable
    {
        private readonly DbContextOptions<LoggingContext> _options;

        public TempSqliteLoggingContextFactory(string dbPath)
        {
            _options = new DbContextOptionsBuilder<LoggingContext>()
                .UseSqlite($"Data Source={dbPath}")
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
                .Options;

            using var ctx = new LoggingContext(_options);
            ctx.Database.EnsureCreated();
        }

        public LoggingContext CreateDbContext() => new(_options);

        public void Dispose() => Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    #endregion
}
