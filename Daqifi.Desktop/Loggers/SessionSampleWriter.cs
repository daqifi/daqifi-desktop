using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Common.Loggers;
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace Daqifi.Desktop.Logger;

/// <summary>
/// Owns the sample write path extracted from <see cref="DatabaseLogger"/> (issue #592): a
/// producer/consumer buffer, the background consumer thread that bulk-inserts buffered
/// <see cref="DataSample"/>s into SQLite, and the suspend/resume/clear controls the session
/// list relies on when purging storage.
/// <para>
/// The database context factory and application logger are constructor-injected, so the writer
/// has no dependency on WPF or on desktop singletons (<c>App.ServiceProvider</c>,
/// <c>AppLogger.Instance</c>) and is unit-testable in isolation. <see cref="DatabaseLogger"/> is
/// the composition root: it builds the writer with the factory it already receives and delegates
/// its <c>Log</c>/<c>ClearBuffer</c>/<c>WaitForIdle</c>/<c>SuspendConsumer</c>/<c>ResumeConsumer</c>
/// members straight through.
/// </para>
/// </summary>
public sealed class SessionSampleWriter : IDisposable
{
    #region Constants
    /// <summary>
    /// Upper bound, in consumer poll cycles (~100ms each), on the exponential backoff applied to a
    /// persistently failing batch during normal background operation. Caps the re-attempt interval at
    /// roughly 5s so a permanently broken database (disk full, file locked) is retried at a low rate
    /// instead of on every poll, without ever abandoning the batch, and so a transient failure while
    /// logging keeps at most ~5s of samples in memory before they land. While a <see cref="WaitForIdle"/>
    /// caller is waiting for the flush, the backoff is overridden entirely (see <see cref="_expediteRetry"/>)
    /// so a recovered database is committed at the full poll rate within the caller's timeout.
    /// </summary>
    private const int MAX_RETRY_BACKOFF_POLLS = 50;
    #endregion

    #region Private Fields
    private readonly BlockingCollection<DataSample> _buffer = new();
    private readonly ManualResetEventSlim _consumerGate = new(true);
    private readonly CancellationTokenSource _consumerCts = new();
    private readonly IDbContextFactory<LoggingContext> _loggingContext;
    private readonly IAppLogger _appLogger;
    private readonly Thread _consumerThread;
    private volatile bool _disposed;
    private volatile bool _consumerBusy;

    /// <summary>
    /// Number of samples drained from the buffer that have not yet been committed — non-zero only
    /// while a batch is in flight or has been retained for retry after a failed commit. Read by
    /// <see cref="WaitForIdle"/> so it cannot report idle while unsaved rows are stranded in the
    /// consumer's local batch (a failed batch lives outside <see cref="_buffer"/>, so the buffer
    /// count alone cannot see it).
    /// </summary>
    private volatile int _pendingRetryCount;

    /// <summary>
    /// Consumer poll cycles still to skip before the next bulk-insert attempt of a failed batch — the
    /// exponential backoff that throttles retries on a persistently failing database. Written only by
    /// the consumer thread; exposed (read-only) for tests via <see cref="PollsUntilRetry"/>.
    /// </summary>
    private volatile int _pollsUntilRetry;

    /// <summary>
    /// Set by <see cref="WaitForIdle"/> while a caller is waiting for the buffer to flush. The consumer
    /// ignores <see cref="_pollsUntilRetry"/> (the retry backoff) while this is set, so a database that
    /// recovers during the wait is committed at the full poll rate rather than after a multi-second
    /// cooldown that could outlast the caller's timeout and persist a sample undercount.
    /// </summary>
    private volatile bool _expediteRetry;
    #endregion

    #region Constructor
    /// <summary>
    /// Creates the writer and starts its background consumer thread immediately, matching the
    /// startup timing of the original inline implementation in <see cref="DatabaseLogger"/>.
    /// </summary>
    /// <param name="loggingContext">Factory for the logging database context used by the bulk insert.</param>
    /// <param name="appLogger">Application logger used to report consumer-thread failures.</param>
    public SessionSampleWriter(IDbContextFactory<LoggingContext> loggingContext, IAppLogger appLogger)
    {
        _loggingContext = loggingContext ?? throw new ArgumentNullException(nameof(loggingContext));
        _appLogger = appLogger ?? throw new ArgumentNullException(nameof(appLogger));

        _consumerThread = new Thread(Consumer) { IsBackground = true };
        _consumerThread.Start();
    }
    #endregion

    #region Enqueue
    /// <summary>
    /// Producer. Enqueues a sample for the background consumer to persist. A no-op once the writer
    /// has been disposed.
    /// </summary>
    /// <param name="dataSample">The sample to buffer for insertion.</param>
    public void Add(DataSample dataSample)
    {
        if (_disposed) { return; }

        try
        {
            _buffer.Add(dataSample);
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }
    #endregion

    #region Consumer
    private void Consumer()
    {
        // Persists across loop iterations on purpose: a batch is cleared only after a successful
        // commit, so a batch whose commit failed is retained here and retried on a later pass.
        var samples = new List<DataSample>();
        int bufferCount;

        // Failure-streak length for a persistently failing batch. Loop-local because the consumer is
        // single-threaded. Drives both log-once (only the first failure of a streak is logged) and the
        // exponential backoff (_pollsUntilRetry). Reset to 0 after a successful commit. The backoff
        // counter itself is the field _pollsUntilRetry so WaitForIdle/tests can observe it.
        var consecutiveFailures = 0;

        while (!_consumerCts.IsCancellationRequested)
        {
            try
            {
                Thread.Sleep(100);

                if (_consumerCts.IsCancellationRequested) { break; }

                bufferCount = _buffer.Count;

                // Stay idle only when nothing is buffered AND no previously failed batch is
                // awaiting retry. A retained batch (samples.Count > 0) lives outside the buffer,
                // so without the samples.Count check it would be retried only when *new* samples
                // happened to arrive — stranding unsaved rows indefinitely and letting WaitForIdle
                // report idle while they are still pending.
                if (bufferCount < 1 && samples.Count == 0) { continue; }

                // Wait if the consumer is suspended (e.g. during delete-all)
                _consumerGate.Wait(_consumerCts.Token);

                _consumerBusy = true;
                try
                {
                    // Drain whatever is newly buffered onto the (possibly retained) batch. On a
                    // pure retry pass bufferCount is 0 and the existing batch is re-attempted.
                    for (var i = 0; i < bufferCount; i++)
                    {
                        if (_buffer.TryTake(out var sample)) { samples.Add(sample); }
                    }

                    // Publish the at-risk count before opening the connection so WaitForIdle cannot
                    // observe a momentary all-clear: if the insert below throws, this stays non-zero
                    // until a later pass commits the batch.
                    _pendingRetryCount = samples.Count;

                    // Back off a persistently failing batch: skip this DB attempt while the backoff
                    // is cooling down. New samples were still drained above (so the buffer stays
                    // bounded) and _pendingRetryCount is still set (so WaitForIdle keeps waiting),
                    // we just avoid re-opening a connection and re-failing at the full poll rate.
                    // Exception: while a WaitForIdle caller is waiting (_expediteRetry), ignore the
                    // backoff and attempt every poll so a recovered DB is flushed within its timeout.
                    if (_pollsUntilRetry > 0 && !_expediteRetry)
                    {
                        _pollsUntilRetry--;
                        continue;
                    }

                    using (var context = _loggingContext.CreateDbContext())
                    {
                        // Start a new transaction for bulk insert
                        using var transaction = context.Database.BeginTransaction();
                        // Perform the bulk insert
                        context.BulkInsert(samples);

                        // Commit the transaction after the bulk insert
                        transaction.Commit();

                        // Clear only after a successful commit. If disposal of the transaction or
                        // context throws past this point, the batch is already cleared and
                        // _pendingRetryCount is 0, so it is not re-inserted — re-inserting an
                        // already-committed batch would write duplicate rows. A failure before the
                        // commit leaves both intact, so the loop-local batch is retried on a later
                        // pass (the bufferCount < 1 && samples.Count == 0 gate above lets it run
                        // even when no new samples have arrived).
                        samples.Clear();
                        _pendingRetryCount = 0;

                        // Clear the retry-throttle state and emit a single all-clear on the
                        // proven-commit path — inside the using, before disposal could throw — so a
                        // recovered batch always logs its recovery and re-arms the log-once gate even
                        // if context/transaction Dispose() then fails.
                        if (consecutiveFailures > 0)
                        {
                            _appLogger.Information(
                                $"Consumer thread recovered after {consecutiveFailures} failed batch insert attempt(s).");
                        }
                        consecutiveFailures = 0;
                        _pollsUntilRetry = 0;
                    }
                }
                finally
                {
                    // Reset busy on every exit path (success or exception) so it can never get
                    // stuck true and wedge WaitForIdle. The retained batch is tracked separately
                    // by _pendingRetryCount, which is only cleared after a committed batch.
                    _consumerBusy = false;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (_consumerCts.IsCancellationRequested) { break; }

                // Log only the first failure of a streak. A persistently failing database (disk
                // full, file locked, etc.) would otherwise flood the NLog file — and, because
                // Error() also reports to Sentry, flood Sentry — at the ~10Hz poll rate. The streak
                // resets on the next successful commit, so a later, distinct failure is logged anew.
                consecutiveFailures++;
                if (consecutiveFailures == 1)
                {
                    _appLogger.Error(ex,
                        "Failed in Consumer Thread; retaining the batch for retry. Further " +
                        "consecutive failures of this batch are suppressed until it succeeds.");
                }

                // Re-attempt with bounded exponential backoff (in poll cycles): 1, 2, 4, 8, ...
                // capped at MAX_RETRY_BACKOFF_POLLS, so a permanently failing batch is retried at a
                // decreasing rate instead of on every poll. The shift is clamped to avoid overflow.
                // A waiting WaitForIdle caller overrides this cooldown (see the gate above).
                var shift = Math.Min(consecutiveFailures - 1, 30);
                _pollsUntilRetry = (int)Math.Min(1L << shift, MAX_RETRY_BACKOFF_POLLS);
            }
        }
    }
    #endregion

    #region Buffer Controls
    /// <summary>
    /// Drains the sample buffer to prevent stale data from being inserted after a database reset.
    /// </summary>
    public void ClearBuffer()
    {
        while (_buffer.TryTake(out _))
        {
        }
    }

    /// <summary>
    /// Blocks until the buffered samples have been flushed to the database, or
    /// the timeout elapses. Used by <c>LoggingManager</c> when finalizing a
    /// session so the persisted <c>SampleCount</c> reflects every row that was
    /// actually written, not just the rows the consumer happened to have
    /// drained at the moment Active flipped to false.
    /// </summary>
    /// <param name="timeout">Maximum time to wait for the buffer to drain before returning.</param>
    public void WaitForIdle(TimeSpan timeout)
    {
        // While we wait, tell the consumer to ignore its retry backoff so a stranded batch whose
        // database has recovered is committed at the full poll rate. Without this, the batch's
        // exponential cooldown could exceed this timeout, leaving rows unflushed when the caller
        // (PersistSessionSampleCount) runs its COUNT — persisting a SampleCount undercount that the
        // null-only backfill never repairs.
        _expediteRetry = true;
        try
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (IsDrained())
                {
                    // Sleep one consumer poll interval to ensure no in-flight item
                    // slipped between TryTake and the busy flag being set.
                    Thread.Sleep(120);
                    if (IsDrained()) { return; }
                }
                Thread.Sleep(50);
            }
        }
        finally
        {
            _expediteRetry = false;
        }
    }

    /// <summary>
    /// True only when there is genuinely nothing left to persist: the buffer is empty, the consumer
    /// is not mid-insert, and no failed batch is retained for retry. The last check is what makes
    /// <see cref="WaitForIdle"/> durability-correct — a batch whose commit failed lives in the
    /// consumer's local list, invisible to <see cref="_buffer"/>, and must not be reported as idle.
    /// </summary>
    private bool IsDrained() => _buffer.Count == 0 && !_consumerBusy && _pendingRetryCount == 0;

    /// <summary>
    /// Suspends the background consumer thread so no new database connections are opened.
    /// Must be followed by <see cref="ResumeConsumer"/>.
    /// </summary>
    public void SuspendConsumer()
    {
        _consumerGate.Reset();
        // Give the consumer time to finish any in-flight DB operation
        Thread.Sleep(200);
    }

    /// <summary>
    /// Resumes the background consumer thread after a <see cref="SuspendConsumer"/> call.
    /// </summary>
    public void ResumeConsumer()
    {
        _consumerGate.Set();
    }
    #endregion

    #region Test Seam
    /// <summary>
    /// Whether the background consumer thread is still running. Exposed for tests to confirm that
    /// <see cref="Dispose"/> joins the thread cleanly.
    /// </summary>
    internal bool IsConsumerThreadAlive => _consumerThread.IsAlive;

    /// <summary>
    /// Number of samples drained from the buffer but not yet committed — non-zero while a batch is
    /// in flight or retained for retry after a failed commit. Exposed so failure-injection tests can
    /// observe a stranded batch deterministically rather than racing the consumer's poll interval.
    /// </summary>
    internal int PendingRetryCount => _pendingRetryCount;

    /// <summary>
    /// Consumer poll cycles still to skip before the next retry of a failed batch (the exponential
    /// backoff). Exposed so a test can wait until the backoff has grown before asserting that
    /// <see cref="WaitForIdle"/> overrides it.
    /// </summary>
    internal int PollsUntilRetry => _pollsUntilRetry;
    #endregion

    #region IDisposable
    /// <summary>
    /// Signals the consumer thread to stop, completes the producer side, and waits up to two
    /// seconds for the thread to exit before releasing the buffer and synchronization primitives.
    /// Idempotent.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        _consumerCts.Cancel();
        _buffer.CompleteAdding();
        _consumerThread?.Join(TimeSpan.FromSeconds(2));
        _buffer.Dispose();
        _consumerCts.Dispose();
        _consumerGate.Dispose();
    }
    #endregion
}
