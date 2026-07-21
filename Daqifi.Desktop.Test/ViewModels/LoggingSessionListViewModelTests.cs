using System.Collections.ObjectModel;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace Daqifi.Desktop.Test.ViewModels;

/// <summary>
/// Behavior contract for <see cref="LoggingSessionListViewModel"/> — the logged-data pane's
/// session-list actions extracted from <c>DaqifiViewModel</c> (issue #592). The list is driven
/// through a lightweight <see cref="FakeLoggingSessionListHost"/> with no WPF dependency; the
/// "delete all" storage purge is exercised against a real temp SQLite database. Covers display,
/// export, delete-one, delete-all (including the "stop logging" guard), and the
/// collection-changed notifications.
/// </summary>
[TestClass]
public class LoggingSessionListViewModelTests
{
    private static readonly string[] ExpectedPurgeCallLog =
        ["Suspend", "ClearBuffer", "DiscardPendingBatch", "Resume"];

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

    #region Display

    [TestMethod]
    public async Task DisplaySession_NullSession_ClearsPlotAndDoesNotTouchBusy()
    {
        var host = new FakeLoggingSessionListHost();
        var list = CreateList(host);

        await list.DisplaySessionAsync(null);

        Assert.AreEqual(1, host.ClearPlotCount);
        Assert.AreEqual(0, host.DisplayOnPlotCount);
        Assert.IsNull(host.SelectedLoggingSession);
        CollectionAssert.AreEqual(new List<bool>(), host.BusyHistory,
            "A null session must short-circuit before the busy flag is ever set.");
    }

    [TestMethod]
    public async Task DisplaySession_LoadsSessionUnderBusyFlag_AndClearsBusyWhenDone()
    {
        var host = new FakeLoggingSessionListHost();
        var list = CreateList(host);
        var session = new LoggingSession { ID = 7, Name = "Bench Run" };

        await list.DisplaySessionAsync(session);

        Assert.AreSame(session, host.SelectedLoggingSession);
        Assert.AreSame(session, host.DisplayedSession);
        Assert.AreEqual(1, host.DisplayOnPlotCount);
        Assert.AreEqual("Loading Bench Run", host.BusyReasonHistory[0]);
        CollectionAssert.Contains(host.BusyHistory, true, "The busy flag must be raised during the load.");
        Assert.IsFalse(host.IsLoggedDataBusyValue, "The busy flag must be cleared once the load completes.");
        Assert.AreEqual(string.Empty, host.LoggedDataBusyReasonValue);
    }

    [TestMethod]
    public async Task DisplaySession_LoadThrows_LogsErrorAndStillClearsBusy()
    {
        var appLogger = new Mock<IAppLogger>();
        var host = new FakeLoggingSessionListHost
        {
            DisplayOnPlotAction = _ => throw new InvalidOperationException("plot blew up")
        };
        var list = CreateList(host, appLogger.Object);
        var session = new LoggingSession { ID = 9, Name = "Faulty" };

        await list.DisplaySessionAsync(session);

        Assert.IsFalse(host.IsLoggedDataBusyValue, "The busy flag must be cleared even when the load throws.");
        Assert.AreEqual(string.Empty, host.LoggedDataBusyReasonValue);
        appLogger.Verify(l => l.Error(It.IsAny<Exception>(),
            It.Is<string>(m => m.Contains("Failed to display logging session 9"))), Times.Once);
    }

    #endregion

    #region Export

    [TestMethod]
    public async Task ExportSession_NullSession_LogsErrorAndShowsNoDialog()
    {
        var appLogger = new Mock<IAppLogger>();
        var host = new FakeLoggingSessionListHost();
        var list = CreateList(host, appLogger.Object);

        await list.ExportSessionAsync(null);

        Assert.AreEqual(0, host.ExportSessionIds.Count);
        Assert.IsNull(host.SelectedLoggingSession);
        appLogger.Verify(l => l.Error("Error exporting logging session"), Times.Once);
    }

    [TestMethod]
    public async Task ExportSession_ShowsExportDialogForThatSession()
    {
        var host = new FakeLoggingSessionListHost();
        var list = CreateList(host);
        var session = new LoggingSession { ID = 42, Name = "Export Me" };

        await list.ExportSessionAsync(session);

        Assert.AreSame(session, host.SelectedLoggingSession);
        CollectionAssert.AreEqual(new List<int> { 42 }, host.ExportSessionIds);
    }

    [TestMethod]
    public async Task ExportAll_NoSessions_LogsErrorAndShowsNoDialog()
    {
        var appLogger = new Mock<IAppLogger>();
        var host = new FakeLoggingSessionListHost();
        var list = CreateList(host, appLogger.Object);

        await list.ExportAllSessionsAsync();

        Assert.AreEqual(0, host.ExportAllInvocations.Count);
        appLogger.Verify(l => l.Error("Error exporting all logging sessions"), Times.Once);
    }

    [TestMethod]
    public async Task ExportAll_ShowsExportDialogForEverySession()
    {
        var host = new FakeLoggingSessionListHost();
        host.LoggingSessions.Add(new LoggingSession { ID = 1, Name = "A" });
        host.LoggingSessions.Add(new LoggingSession { ID = 2, Name = "B" });
        var list = CreateList(host);

        await list.ExportAllSessionsAsync();

        Assert.AreEqual(1, host.ExportAllInvocations.Count);
        CollectionAssert.AreEqual(new List<int> { 1, 2 }, host.ExportAllInvocations[0].Select(s => s.ID).ToList());
    }

    #endregion

    #region Delete one

    [TestMethod]
    public async Task DeleteSession_NullSession_LogsErrorAndDoesNotConfirm()
    {
        var appLogger = new Mock<IAppLogger>();
        var host = new FakeLoggingSessionListHost();
        var list = CreateList(host, appLogger.Object);

        await list.DeleteSessionAsync(null);

        Assert.AreEqual(0, host.ConfirmCallCount);
        Assert.AreEqual(0, host.DeleteFromDatabaseCount);
        appLogger.Verify(l => l.Error("Error deleting logging session: Invalid object provided."), Times.Once);
    }

    [TestMethod]
    public async Task DeleteSession_NotConfirmed_DoesNotDeleteOrModifyCollection()
    {
        var host = new FakeLoggingSessionListHost { ConfirmResult = false };
        var session = new LoggingSession { ID = 3, Name = "Keep Me" };
        host.LoggingSessions.Add(session);
        var list = CreateList(host);

        await list.DeleteSessionAsync(session);

        Assert.AreEqual(1, host.ConfirmCallCount);
        Assert.AreEqual(0, host.DeleteFromDatabaseCount);
        CollectionAssert.Contains(host.LoggingSessions, session);
        CollectionAssert.AreEqual(new List<bool>(), host.BusyHistory,
            "Busy must not be raised when the user cancels the confirmation.");
    }

    [TestMethod]
    public async Task DeleteSession_Confirmed_DeletesRemovesAndNotifies()
    {
        var host = new FakeLoggingSessionListHost { ConfirmResult = true };
        var session = new LoggingSession { ID = 5, Name = "Delete Me" };
        host.LoggingSessions.Add(session);
        var list = CreateList(host);

        await list.DeleteSessionAsync(session);

        Assert.AreSame(session, host.SelectedLoggingSession);
        Assert.AreEqual(1, host.DeleteFromDatabaseCount);
        Assert.AreSame(session, host.DeletedFromDatabaseSession);
        CollectionAssert.DoesNotContain(host.LoggingSessions, session);
        Assert.IsTrue(host.NotifyCount >= 1, "Removing a session must raise the change notification.");
        Assert.IsFalse(host.IsLoggedDataBusyValue, "Busy must clear after the delete completes.");
        Assert.AreEqual(string.Empty, host.LoggedDataBusyReasonValue);
    }

    [TestMethod]
    public async Task DeleteSession_DatabaseThrows_LogsErrorAndKeepsSession()
    {
        var appLogger = new Mock<IAppLogger>();
        var host = new FakeLoggingSessionListHost
        {
            ConfirmResult = true,
            DeleteFromDatabaseAction = _ => throw new InvalidOperationException("db locked")
        };
        var session = new LoggingSession { ID = 8, Name = "Stubborn" };
        host.LoggingSessions.Add(session);
        var list = CreateList(host, appLogger.Object);

        await list.DeleteSessionAsync(session);

        CollectionAssert.Contains(host.LoggingSessions, session,
            "A failed database delete must not remove the session from the bound collection.");
        Assert.IsFalse(host.IsLoggedDataBusyValue, "Busy must clear even when the database delete throws.");
        appLogger.Verify(l => l.Error(It.IsAny<Exception>(),
            It.Is<string>(m => m.Contains("Failed to delete session 8 from database"))), Times.Once);
    }

    #endregion

    #region Delete all

    [TestMethod]
    public async Task DeleteAll_NoSessions_DoesNothing()
    {
        var host = new FakeLoggingSessionListHost();
        var list = CreateList(host);

        await list.DeleteAllSessionsAsync();

        Assert.AreEqual(0, host.MessageCallCount);
        Assert.AreEqual(0, host.ConfirmCallCount);
        Assert.AreEqual(0, host.SuspendConsumerCount);
    }

    [TestMethod]
    public async Task DeleteAll_WhileLogging_ShowsStopLoggingMessageAndDoesNotPurge()
    {
        var host = new FakeLoggingSessionListHost { IsLoggingActive = true };
        host.LoggingSessions.Add(new LoggingSession { ID = 1, Name = "A" });
        var list = CreateList(host);

        await list.DeleteAllSessionsAsync();

        Assert.AreEqual(1, host.MessageCallCount);
        Assert.AreEqual("Cannot Delete", host.LastMessageTitle);
        Assert.AreEqual(0, host.ConfirmCallCount, "The destructive confirm must not appear while logging is active.");
        Assert.AreEqual(0, host.SuspendConsumerCount, "Storage must not be purged while logging is active.");
        Assert.AreEqual(1, host.LoggingSessions.Count);
    }

    [TestMethod]
    public async Task DeleteAll_NotConfirmed_DoesNotPurge()
    {
        var host = new FakeLoggingSessionListHost { ConfirmResult = false };
        host.LoggingSessions.Add(new LoggingSession { ID = 1, Name = "A" });
        var list = CreateList(host);

        await list.DeleteAllSessionsAsync();

        Assert.AreEqual(1, host.ConfirmCallCount);
        Assert.AreEqual(0, host.SuspendConsumerCount);
        Assert.AreEqual(1, host.LoggingSessions.Count);
    }

    [TestMethod]
    public async Task DeleteAll_Confirmed_PurgesStorageClearsCollectionAndResetsPlot()
    {
        var host = new FakeLoggingSessionListHost { ConfirmResult = true };
        host.LoggingSessions.Add(new LoggingSession { ID = 1, Name = "A" });
        host.LoggingSessions.Add(new LoggingSession { ID = 2, Name = "B" });

        var loggedErrors = new List<string>();
        var appLogger = new Mock<IAppLogger>();
        appLogger.Setup(l => l.Error(It.IsAny<Exception>(), It.IsAny<string>()))
            .Callback<Exception, string>((e, m) => loggedErrors.Add($"{m}: {e}"));

        var dbPath = NewTempDbPath();
        using var factory = new TempSqliteLoggingContextFactory(dbPath);
        var list = new LoggingSessionListViewModel(host, () => factory, dbPath, appLogger.Object);

        await list.DeleteAllSessionsAsync();

        Assert.AreEqual(0, loggedErrors.Count, $"Purge logged errors: {string.Join(" || ", loggedErrors)}");

        // The consumer thread must be suspended around the purge and resumed afterward.
        Assert.AreEqual(1, host.SuspendConsumerCount);
        Assert.AreEqual(1, host.ClearBufferCount);
        Assert.AreEqual(1, host.DiscardPendingBatchCount,
            "The purge must discard any retained retry batch so it cannot repopulate the wiped database.");
        Assert.AreEqual(1, host.ResumeConsumerCount);

        // Order matters: the discard must happen inside the suspend window, after the buffer is
        // cleared and before the consumer resumes — otherwise a stranded batch could slip through.
        CollectionAssert.AreEqual(
            ExpectedPurgeCallLog,
            host.ConsumerCallLog,
            "The purge must suspend, clear the buffer, discard the retained batch, then resume — in order.");
        Assert.IsTrue(host.SuspendBeforeResume, "Consumer must be suspended before it is resumed.");

        // The database file is deleted and re-migrated, the bound collection is emptied, and the plot reset.
        Assert.IsTrue(File.Exists(dbPath), "The purge must recreate the database schema.");
        Assert.AreEqual(0, host.LoggingSessions.Count);
        Assert.IsTrue(host.NotifyCount >= 1);
        Assert.AreEqual(1, host.ClearPlotCount);
        Assert.IsFalse(host.IsLoggedDataBusyValue, "Busy must clear after the purge completes.");
    }

    [TestMethod]
    public async Task DeleteAll_WhenPurgeFails_DoesNotDiscardRetainedBatch_AndKeepsSessions()
    {
        var host = new FakeLoggingSessionListHost { ConfirmResult = true };
        host.LoggingSessions.Add(new LoggingSession { ID = 1, Name = "A" });

        // A context factory that cannot create a context models a purge that fails to recreate the
        // database (e.g. the file is locked). The recreate step throws, so the purge does not complete.
        var failingFactory = new Mock<IDbContextFactory<LoggingContext>>();
        failingFactory.Setup(f => f.CreateDbContext()).Throws(new IOException("database file is in use"));

        var dbPath = NewTempDbPath();
        var list = new LoggingSessionListViewModel(host, () => failingFactory.Object, dbPath, Mock.Of<IAppLogger>());

        await list.DeleteAllSessionsAsync();

        // The consumer must still be suspended and resumed around the (failed) purge...
        Assert.AreEqual(1, host.SuspendConsumerCount);
        Assert.AreEqual(1, host.ResumeConsumerCount, "The consumer must always be resumed, even on failure.");

        // ...but the retained retry batch must NOT be discarded: the database is still intact and the
        // sessions remain, so dropping the batch would silently lose unflushed samples.
        Assert.AreEqual(0, host.DiscardPendingBatchCount,
            "A failed purge must not discard the retained retry batch.");
        Assert.AreEqual(1, host.LoggingSessions.Count, "A failed purge must leave the sessions intact.");
    }

    #endregion

    #region Collection-changed notifications

    [TestMethod]
    public void AttachedCollectionChange_RaisesNotification()
    {
        var host = new FakeLoggingSessionListHost();
        var list = CreateList(host);
        var collection = new ObservableCollection<LoggingSession>();

        list.AttachCollection(collection);
        var baseline = host.NotifyCount;

        collection.Add(new LoggingSession { ID = 1, Name = "A" });
        Assert.AreEqual(baseline + 1, host.NotifyCount);

        collection.RemoveAt(0);
        Assert.AreEqual(baseline + 2, host.NotifyCount);
    }

    [TestMethod]
    public void Reattaching_UnsubscribesFromThePreviousCollection()
    {
        var host = new FakeLoggingSessionListHost();
        var list = CreateList(host);
        var first = new ObservableCollection<LoggingSession>();
        var second = new ObservableCollection<LoggingSession>();

        list.AttachCollection(first);
        list.AttachCollection(second);
        var baseline = host.NotifyCount;

        // Mutating the now-detached first collection must not notify.
        first.Add(new LoggingSession { ID = 1, Name = "A" });
        Assert.AreEqual(baseline, host.NotifyCount);

        // The currently-attached collection still notifies.
        second.Add(new LoggingSession { ID = 2, Name = "B" });
        Assert.AreEqual(baseline + 1, host.NotifyCount);
    }

    [TestMethod]
    public void AttachingTheSameCollectionTwice_DoesNotDoubleSubscribe()
    {
        var host = new FakeLoggingSessionListHost();
        var list = CreateList(host);
        var collection = new ObservableCollection<LoggingSession>();

        list.AttachCollection(collection);
        list.AttachCollection(collection);
        var baseline = host.NotifyCount;

        collection.Add(new LoggingSession { ID = 1, Name = "A" });
        Assert.AreEqual(baseline + 1, host.NotifyCount,
            "Re-attaching the same collection must not add a second subscription.");
    }

    [TestMethod]
    public void DetachCollection_UnsubscribesFromTheObservedCollection()
    {
        var host = new FakeLoggingSessionListHost();
        var list = CreateList(host);
        var collection = new ObservableCollection<LoggingSession>();

        list.AttachCollection(collection);
        list.DetachCollection();
        var baseline = host.NotifyCount;

        // After detaching, mutations of the previously-observed collection must not notify.
        collection.Add(new LoggingSession { ID = 1, Name = "A" });
        Assert.AreEqual(baseline, host.NotifyCount);
    }

    [TestMethod]
    public void DetachCollection_WhenNothingAttached_IsSafeNoOp()
    {
        var host = new FakeLoggingSessionListHost();
        var list = CreateList(host);

        // Mirrors the non-window-init path where AttachCollection was never called.
        list.DetachCollection();

        // The no-op detach must leave the list usable: a later attach still wires notifications.
        var collection = new ObservableCollection<LoggingSession>();
        list.AttachCollection(collection);
        collection.Add(new LoggingSession { ID = 1, Name = "A" });
        Assert.AreEqual(1, host.NotifyCount);
    }

    #endregion

    #region Helpers

    private LoggingSessionListViewModel CreateList(FakeLoggingSessionListHost host, IAppLogger? appLogger = null)
    {
        // The factory resolver throws if invoked: only the delete-all path needs it, and those tests
        // build the list with a real temp factory explicitly.
        return new LoggingSessionListViewModel(
            host,
            () => throw new InvalidOperationException("Logging context factory should not be resolved in this test."),
            NewTempDbPath(),
            appLogger ?? Mock.Of<IAppLogger>());
    }

    private string NewTempDbPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"daqifi_sessionlist_{Guid.NewGuid():N}.db");
        _tempDbPaths.Add(path);
        return path;
    }

    /// <summary>
    /// In-memory <see cref="ILoggingSessionListHost"/> for driving the list view model without WPF.
    /// Captures the state writes, dialog/notification calls, and database/plot operations the
    /// session-list actions push, and lets a test stub the confirm result and inject failures.
    /// </summary>
    private sealed class FakeLoggingSessionListHost : ILoggingSessionListHost
    {
        private bool _isLoggedDataBusy;
        private string _loggedDataBusyReason = string.Empty;
        private bool _suspendedAtLeastOnce;

        // get+set implicitly implements the interface's set-only member while letting tests read the
        // captured selection. Starts null (no session selected yet).
        public LoggingSession SelectedLoggingSession { get; set; } = null!;

        public bool IsLoggedDataBusy
        {
            set
            {
                _isLoggedDataBusy = value;
                BusyHistory.Add(value);
            }
        }

        public bool IsLoggedDataBusyValue => _isLoggedDataBusy;

        public string LoggedDataBusyReason
        {
            set
            {
                _loggedDataBusyReason = value;
                BusyReasonHistory.Add(value);
            }
        }

        public string LoggedDataBusyReasonValue => _loggedDataBusyReason;

        public ObservableCollection<LoggingSession> LoggingSessions { get; } = [];

        public bool IsLoggingActive { get; set; }

        public List<bool> BusyHistory { get; } = [];

        public List<string> BusyReasonHistory { get; } = [];

        public int NotifyCount { get; private set; }

        public int ClearPlotCount { get; private set; }

        public int DisplayOnPlotCount { get; private set; }

        public LoggingSession? DisplayedSession { get; private set; }

        public int DeleteFromDatabaseCount { get; private set; }

        public LoggingSession? DeletedFromDatabaseSession { get; private set; }

        public int SuspendConsumerCount { get; private set; }

        public int ResumeConsumerCount { get; private set; }

        public int ClearBufferCount { get; private set; }

        public int DiscardPendingBatchCount { get; private set; }

        public bool SuspendBeforeResume { get; private set; }

        /// <summary>
        /// Ordered log of the consumer-control calls (Suspend/ClearBuffer/DiscardPendingBatch/Resume)
        /// so a test can assert the purge invokes them in the right order, not merely the right counts.
        /// </summary>
        public List<string> ConsumerCallLog { get; } = [];

        public List<int> ExportSessionIds { get; } = [];

        public List<IReadOnlyList<LoggingSession>> ExportAllInvocations { get; } = [];

        public int ConfirmCallCount { get; private set; }

        public bool ConfirmResult { get; set; } = true;

        public int MessageCallCount { get; private set; }

        public string? LastMessageTitle { get; private set; }

        /// <summary>When set, runs in place of recording a successful plot load (used to inject a fault).</summary>
        public Action<LoggingSession>? DisplayOnPlotAction { get; set; }

        /// <summary>When set, runs in place of recording a successful database delete (used to inject a fault).</summary>
        public Action<LoggingSession>? DeleteFromDatabaseAction { get; set; }

        public void NotifyLoggingSessionsChanged() => NotifyCount++;

        public void DisplaySessionOnPlot(LoggingSession session)
        {
            DisplayOnPlotCount++;
            DisplayedSession = session;
            DisplayOnPlotAction?.Invoke(session);
        }

        public void DeleteSessionFromDatabase(LoggingSession session)
        {
            DeleteFromDatabaseCount++;
            DeletedFromDatabaseSession = session;
            DeleteFromDatabaseAction?.Invoke(session);
        }

        public void ClearPlot() => ClearPlotCount++;

        public void SuspendConsumer()
        {
            SuspendConsumerCount++;
            _suspendedAtLeastOnce = true;
            ConsumerCallLog.Add("Suspend");
        }

        public void ResumeConsumer()
        {
            ResumeConsumerCount++;
            if (_suspendedAtLeastOnce)
            {
                SuspendBeforeResume = true;
            }
            ConsumerCallLog.Add("Resume");
        }

        public void ClearBuffer()
        {
            ClearBufferCount++;
            ConsumerCallLog.Add("ClearBuffer");
        }

        public void DiscardPendingBatch()
        {
            DiscardPendingBatchCount++;
            ConsumerCallLog.Add("DiscardPendingBatch");
        }

        public Task ShowExportDialogForSessionAsync(int sessionId)
        {
            ExportSessionIds.Add(sessionId);
            return Task.CompletedTask;
        }

        public Task ShowExportDialogForSessionsAsync(IReadOnlyList<LoggingSession> sessions)
        {
            ExportAllInvocations.Add(sessions);
            return Task.CompletedTask;
        }

        public Task<bool> ShowConfirmAsync(string title, string message, string affirmativeLabel, bool isDestructive)
        {
            ConfirmCallCount++;
            return Task.FromResult(ConfirmResult);
        }

        public Task ShowMessageAsync(string title, string message)
        {
            MessageCallCount++;
            LastMessageTitle = title;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Real-SQLite logging-context factory over a caller-supplied path so the "delete all" storage
    /// purge (which deletes the file and re-migrates the schema) can be exercised end-to-end.
    /// </summary>
    private sealed class TempSqliteLoggingContextFactory : IDbContextFactory<LoggingContext>, IDisposable
    {
        private readonly DbContextOptions<LoggingContext> _options;

        public TempSqliteLoggingContextFactory(string dbPath)
        {
            // Mirror the production factory registration (App.xaml.cs): suppress the
            // PendingModelChangesWarning so Migrate() doesn't throw on model/snapshot drift.
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
