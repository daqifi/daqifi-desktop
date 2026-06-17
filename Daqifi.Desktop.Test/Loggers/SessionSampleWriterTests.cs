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
/// drops pending samples, <c>Dispose</c> stops the consumer thread cleanly and is idempotent, and —
/// via an injected transient DB failure — a failed batch is retained, keeps <c>WaitForIdle</c> from
/// reporting idle, is retried to completion exactly once (no lost or duplicate rows) on recovery, is
/// logged only once (not on every poll) while it stays stranded on a persistently failing DB, has its
/// retry backoff overridden by a waiting <c>WaitForIdle</c>, and is dropped by <c>DiscardPendingBatch</c>
/// so a delete-all purge is not repopulated.
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

    [TestMethod]
    public void WaitForIdle_DoesNotReportIdle_WhileFailedBatchAwaitsRetry()
    {
        const int sampleCount = 10;
        using var inner = NewFactory();
        SeedSession(inner);

        // The consumer's first commit attempt fails, so the batch is retained for retry. Because
        // that retained batch lives outside the buffer, WaitForIdle must still treat the writer as
        // not-idle until the batch is committed.
        var failing = new FailUntilReleasedContextFactory(inner);
        using var writer = new SessionSampleWriter(failing, Mock.Of<IAppLogger>());

        for (var i = 0; i < sampleCount; i++)
        {
            writer.Add(MakeSample("AI0", i));
        }

        // Wait until the consumer has drained the buffer into a retained batch after the injected
        // failure — nothing should have reached the database.
        Assert.IsTrue(
            WaitUntil(() => writer.PendingRetryCount == sampleCount, TimeSpan.FromSeconds(5)),
            "Consumer should drain the buffer into a retained batch after the injected commit failure.");
        Assert.AreEqual(0, CountSamples(inner),
            "A failed commit must not leave any rows in the database.");

        // With the batch unsaved, WaitForIdle must NOT report idle — it must block for the full
        // timeout. (The pre-fix code inspected only the buffer count and the busy flag, so it
        // returned almost immediately, falsely reporting idle while rows were still pending.)
        var timeout = TimeSpan.FromMilliseconds(400);
        var stopwatch = Stopwatch.StartNew();
        writer.WaitForIdle(timeout);
        stopwatch.Stop();

        Assert.IsTrue(stopwatch.ElapsedMilliseconds >= 300,
            "WaitForIdle must block while a failed batch is pending retry rather than report idle " +
            $"(it returned after only {stopwatch.ElapsedMilliseconds}ms).");

        // Release so the consumer recovers and the writer disposes from a clean state.
        failing.Release();
    }

    [TestMethod]
    public void FailedBatch_IsRetriedAndPersistedExactlyOnce_AfterRecovery()
    {
        const int sampleCount = 10;
        using var inner = NewFactory();
        SeedSession(inner);

        var failing = new FailUntilReleasedContextFactory(inner);
        using var writer = new SessionSampleWriter(failing, Mock.Of<IAppLogger>());

        for (var i = 0; i < sampleCount; i++)
        {
            writer.Add(MakeSample("AI0", i));
        }

        // The first commit attempt fails and the batch is stranded for retry.
        Assert.IsTrue(
            WaitUntil(() => writer.PendingRetryCount == sampleCount, TimeSpan.FromSeconds(5)),
            "Consumer should retain the batch for retry after the injected commit failure.");
        Assert.AreEqual(0, CountSamples(inner), "Nothing should be persisted while the DB is failing.");

        // Recover: the consumer must retry the retained batch on its own, with NO new samples
        // arriving. (The pre-fix code only retried when fresh samples were later enqueued.)
        failing.Release();
        writer.WaitForIdle(TimeSpan.FromSeconds(5));

        Assert.AreEqual(sampleCount, CountSamples(inner),
            "The retained batch must be persisted exactly once after recovery — no lost rows and " +
            "no duplicate rows.");
        Assert.AreEqual(0, writer.PendingRetryCount,
            "A successful commit must clear the pending-retry count.");
    }

    [TestMethod]
    public void PersistentFailure_LogsErrorOnce_NotOnEveryPoll()
    {
        const int sampleCount = 5;
        using var inner = NewFactory();
        SeedSession(inner);

        // A real mock logger (not Mock.Of) so we can count how often Error is invoked.
        var logger = new Mock<IAppLogger>();
        var failing = new FailUntilReleasedContextFactory(inner);
        using var writer = new SessionSampleWriter(failing, logger.Object);

        for (var i = 0; i < sampleCount; i++)
        {
            writer.Add(MakeSample("AI0", i));
        }

        // Strand the batch, then let the consumer keep polling the broken DB for several more
        // ~100ms cycles. Pre-throttle, each poll re-attempted the insert and logged Error — at the
        // poll rate that floods both the NLog file and (via Error -> Sentry) Sentry.
        Assert.IsTrue(
            WaitUntil(() => writer.PendingRetryCount == sampleCount, TimeSpan.FromSeconds(5)),
            "Consumer should strand the batch after the injected commit failure.");
        Thread.Sleep(800); // ~8 poll intervals of sustained failure

        Assert.AreEqual(0, CountSamples(inner), "Nothing should persist while the DB is failing.");
        logger.Verify(
            l => l.Error(It.IsAny<Exception>(), It.IsAny<string>()),
            Times.Once(),
            "A batch that stays stranded on a persistently failing DB must be logged once, " +
            "not on every poll.");

        // Durability is preserved: once the DB recovers the stranded batch still persists exactly
        // once, and recovery adds no further error logs.
        failing.Release();
        writer.WaitForIdle(TimeSpan.FromSeconds(5));

        Assert.AreEqual(sampleCount, CountSamples(inner),
            "The retained batch must still persist after recovery — log throttling must not drop data.");
        logger.Verify(
            l => l.Error(It.IsAny<Exception>(), It.IsAny<string>()),
            Times.Once(),
            "Recovery must not add further error logs.");
    }

    [TestMethod]
    public void WaitForIdle_OverridesRetryBackoff_ToFlushARecoveredBatch()
    {
        const int sampleCount = 5;
        using var inner = NewFactory();
        SeedSession(inner);

        var failing = new FailUntilReleasedContextFactory(inner);
        using var writer = new SessionSampleWriter(failing, Mock.Of<IAppLogger>());

        for (var i = 0; i < sampleCount; i++)
        {
            writer.Add(MakeSample("AI0", i));
        }

        // Let the failure streak grow until the retry backoff is at least ~16 poll cycles (~1.6s).
        // A plain cooldown of that length would NOT re-attempt within the 1s WaitForIdle window below.
        Assert.IsTrue(
            WaitUntil(() => writer.PollsUntilRetry >= 16, TimeSpan.FromSeconds(8)),
            "The retry backoff should grow while the database keeps failing.");

        // The database recovers. WaitForIdle must override the multi-second backoff and flush the
        // batch within its (shorter) window — without the override, the next retry would not fire
        // until the cooldown elapsed, and PersistSessionSampleCount would COUNT an undercount.
        failing.Release();
        writer.WaitForIdle(TimeSpan.FromSeconds(1));

        Assert.AreEqual(sampleCount, CountSamples(inner),
            "WaitForIdle must override the retry backoff so a recovered batch is flushed within its " +
            "window; otherwise the persisted SampleCount would undercount.");
        Assert.AreEqual(0, writer.PendingRetryCount,
            "The flushed batch must clear the pending-retry count.");
    }

    [TestMethod]
    public void DiscardPendingBatch_DropsStrandedBatch_SoAPurgeIsNotRepopulated()
    {
        const int sampleCount = 5;
        using var inner = NewFactory();
        SeedSession(inner);

        var failing = new FailUntilReleasedContextFactory(inner);
        using var writer = new SessionSampleWriter(failing, Mock.Of<IAppLogger>());

        for (var i = 0; i < sampleCount; i++)
        {
            writer.Add(MakeSample("AI0", i));
        }

        // Strand the batch in the consumer's retry list via the injected failure.
        Assert.IsTrue(
            WaitUntil(() => writer.PendingRetryCount == sampleCount, TimeSpan.FromSeconds(5)),
            "Consumer should strand the batch after the injected commit failure.");

        // Mirror the delete-all purge ordering, including the discard step. Unlike ClearBuffer (which
        // empties only the producer buffer), DiscardPendingBatch must drop the stranded batch the
        // consumer is holding so it is not re-inserted into the recreated database on resume — even
        // once the database is healthy again.
        writer.SuspendConsumer();
        writer.ClearBuffer();
        writer.DiscardPendingBatch();
        failing.Release();
        writer.ResumeConsumer();

        writer.WaitForIdle(TimeSpan.FromSeconds(5));

        Assert.AreEqual(0, CountSamples(inner),
            "A discarded stranded batch must NOT be re-inserted after the purge, even once the DB recovers.");
        Assert.AreEqual(0, writer.PendingRetryCount,
            "Discarding the stranded batch must reset the pending-retry count.");
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
    /// Polls <paramref name="condition"/> until it is true or <paramref name="timeout"/> elapses.
    /// Returns the final evaluation so callers can assert on it. Used to observe consumer-thread
    /// state deterministically instead of racing its fixed poll interval.
    /// </summary>
    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) { return true; }
            Thread.Sleep(20);
        }
        return condition();
    }

    /// <summary>
    /// Wraps a real SQLite factory and throws from <see cref="CreateDbContext"/> while armed, then
    /// delegates once <see cref="Release"/> is called — simulating a transient database failure that
    /// later recovers. The consumer opens the context before any insert, so a throw here means the
    /// batch is never partially written; recovery re-inserts it exactly once with no duplicate rows.
    /// Does not own (dispose) the inner factory — the test's own <c>using</c> handles that, and
    /// <see cref="SessionSampleWriter"/> never disposes its factory.
    /// </summary>
    private sealed class FailUntilReleasedContextFactory : IDbContextFactory<LoggingContext>
    {
        private readonly IDbContextFactory<LoggingContext> _inner;
        private volatile bool _failing = true;

        public FailUntilReleasedContextFactory(IDbContextFactory<LoggingContext> inner) => _inner = inner;

        public void Release() => _failing = false;

        public LoggingContext CreateDbContext()
        {
            if (_failing)
            {
                throw new InvalidOperationException("Injected transient database failure.");
            }
            return _inner.CreateDbContext();
        }
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
