using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.ViewModels;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Daqifi.Desktop.Test.ViewModels;

/// <summary>
/// Covers the export dialog's three-state machine (Configure / Exporting / Done) and the
/// Export command gating. Uses the internal factory-injecting constructor so the dialog can
/// be exercised without the App/DI container.
/// </summary>
[TestClass]
public class ExportDialogViewModelTests
{
    private static readonly string TestDirectoryPath =
        Path.Combine(Path.GetTempPath(), "DAQiFi", "ExportDialogViewModelTests");

    [TestInitialize]
    public void Setup() => Directory.CreateDirectory(TestDirectoryPath);

    [TestCleanup]
    public void Cleanup()
    {
        // Remove exported CSVs / temp DBs so repeated runs don't accumulate files under %TEMP%.
        try
        {
            if (Directory.Exists(TestDirectoryPath)) { Directory.Delete(TestDirectoryPath, recursive: true); }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static ExportDialogViewModel CreateViewModel(IDbContextFactory<LoggingContext> factory = null!)
        => new(factory ?? new Mock<IDbContextFactory<LoggingContext>>().Object, sessionId: 1);

    #region IsConfiguring (which of the three states is shown)

    [TestMethod]
    public void IsConfiguring_IsTrue_OnNewViewModel()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        Assert.IsTrue(vm.IsConfiguring);
        Assert.IsFalse(vm.IsExporting);
        Assert.IsFalse(vm.IsExportComplete);
    }

    [TestMethod]
    public void IsConfiguring_IsFalse_WhileExporting()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.IsExporting = true;

        // Assert
        Assert.IsFalse(vm.IsConfiguring);
    }

    [TestMethod]
    public void IsConfiguring_IsFalse_WhenComplete()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.IsExportComplete = true;

        // Assert
        Assert.IsFalse(vm.IsConfiguring);
    }

    [TestMethod]
    public void IsConfiguring_RaisesPropertyChanged_WhenExportingChanges()
    {
        // Arrange
        var vm = CreateViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // Act
        vm.IsExporting = true;

        // Assert
        CollectionAssert.Contains(raised, nameof(ExportDialogViewModel.IsConfiguring));
    }

    [TestMethod]
    public void IsConfiguring_RaisesPropertyChanged_WhenCompleteChanges()
    {
        // Arrange
        var vm = CreateViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // Act
        vm.IsExportComplete = true;

        // Assert
        CollectionAssert.Contains(raised, nameof(ExportDialogViewModel.IsConfiguring));
    }

    #endregion

    #region Export command gating (CanExport)

    [TestMethod]
    public void ExportCommand_CannotExecute_WithoutDestination()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        var canExecute = vm.ExportLoggingSessionsCommand.CanExecute(null);

        // Assert
        Assert.IsFalse(canExecute);
    }

    [TestMethod]
    public void ExportCommand_CanExecute_OnceDestinationIsSet()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.ExportFilePath = Path.Combine(TestDirectoryPath, "out.csv");

        // Assert
        Assert.IsTrue(vm.ExportLoggingSessionsCommand.CanExecute(null));
    }

    [TestMethod]
    public void SettingDestination_RaisesCanExecuteChanged_ForExportCommand()
    {
        // Arrange
        var vm = CreateViewModel();
        var raised = false;
        vm.ExportLoggingSessionsCommand.CanExecuteChanged += (_, _) => raised = true;

        // Act
        vm.ExportFilePath = Path.Combine(TestDirectoryPath, "out.csv");

        // Assert
        Assert.IsTrue(raised);
    }

    #endregion

    #region Terminal state transitions

    [TestMethod]
    public async Task Export_OnFailure_ShowsFailedResult()
    {
        // Arrange — a factory that throws when the export reads the session forces the
        // catch-all path deterministically (no file-system or timing dependence).
        var factory = new Mock<IDbContextFactory<LoggingContext>>();
        factory.Setup(f => f.CreateDbContext()).Throws(new InvalidOperationException("boom"));
        var vm = new ExportDialogViewModel(factory.Object, sessionId: 1)
        {
            ExportFilePath = Path.Combine(TestDirectoryPath, "fail.csv")
        };

        // Act
        await vm.ExportLoggingSessionsCommand.ExecuteAsync(null);

        // Assert
        Assert.IsTrue(vm.IsExportComplete, "Failure should still land on the result state.");
        Assert.IsFalse(vm.ExportSucceeded);
        Assert.IsFalse(vm.IsExporting);
        Assert.IsFalse(vm.IsConfiguring);
        Assert.AreEqual("Export failed. Please try again.", vm.ExportResultMessage);
    }

    [TestMethod]
    public async Task Export_OnSuccess_ShowsCompleteResultAndWritesFile()
    {
        // Arrange
        using var factory = new TempSqliteLoggingContextFactory();
        SeedSession(factory, sessionId: 1);
        var exportPath = Path.Combine(TestDirectoryPath, $"success_{Guid.NewGuid():N}.csv");
        var vm = new ExportDialogViewModel(factory, sessionId: 1) { ExportFilePath = exportPath };

        // Act
        await vm.ExportLoggingSessionsCommand.ExecuteAsync(null);

        // Assert
        Assert.IsTrue(vm.IsExportComplete);
        Assert.IsTrue(vm.ExportSucceeded);
        Assert.IsFalse(vm.IsExporting);
        Assert.IsFalse(vm.IsConfiguring);
        Assert.AreEqual("Export complete", vm.ExportResultMessage);
        Assert.IsTrue(File.Exists(exportPath), "A successful export should have written the file.");
    }

    /// <summary>
    /// Issue #747: the destination CSV is still open in Excel/Spyder. The export must not claim
    /// success, must name the file and say what to do, and must leave the existing file untouched.
    /// </summary>
    [TestMethod]
    public async Task Export_WhenDestinationIsOpenInAnotherProgram_ReportsActionableFailure()
    {
        // Arrange
        using var factory = new TempSqliteLoggingContextFactory();
        SeedSession(factory, sessionId: 1);
        var exportPath = Path.Combine(TestDirectoryPath, $"locked_{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(exportPath, "previous export");
        var vm = new ExportDialogViewModel(factory, sessionId: 1) { ExportFilePath = exportPath };

        // Hold the file the way Excel does: write access, readers allowed.
        using (new FileStream(exportPath, FileMode.Open, FileAccess.Write, FileShare.Read))
        {
            // Act
            await vm.ExportLoggingSessionsCommand.ExecuteAsync(null);
        }

        // Assert
        Assert.IsTrue(vm.IsExportComplete);
        Assert.IsFalse(vm.ExportSucceeded, "A locked destination must not report success.");
        Assert.Contains(Path.GetFileName(exportPath), vm.ExportResultMessage,
            "The message should name the file the user has to close.");
        Assert.Contains("open in another program", vm.ExportResultMessage);
        Assert.AreEqual("previous export", await File.ReadAllTextAsync(exportPath),
            "The pre-flight probe must not truncate the file it could not write.");
    }

    /// <summary>
    /// A session that no longer exists is skipped by the export, so its destination must not be
    /// pre-flighted either — a locked file for a stale id must never block the run.
    /// </summary>
    [TestMethod]
    public async Task Export_WhenSessionIsMissing_LockedDestinationDoesNotBlockTheRun()
    {
        // Arrange — an empty database, so session 1 is never found.
        using var factory = new TempSqliteLoggingContextFactory();
        var exportPath = Path.Combine(TestDirectoryPath, $"stale_{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(exportPath, "held open");
        var vm = new ExportDialogViewModel(factory, sessionId: 1) { ExportFilePath = exportPath };

        using (new FileStream(exportPath, FileMode.Open, FileAccess.Write, FileShare.Read))
        {
            // Act
            await vm.ExportLoggingSessionsCommand.ExecuteAsync(null);
        }

        // Assert
        Assert.IsTrue(vm.ExportSucceeded,
            "A locked destination belonging to a skipped session must not fail the export.");
        Assert.AreEqual("Export complete", vm.ExportResultMessage);
    }

    [TestMethod]
    public async Task Export_AfterFailure_RetryReturnsToConfigureState()
    {
        // Arrange
        var factory = new Mock<IDbContextFactory<LoggingContext>>();
        factory.Setup(f => f.CreateDbContext()).Throws(new InvalidOperationException("boom"));
        var vm = new ExportDialogViewModel(factory.Object, sessionId: 1)
        {
            ExportFilePath = Path.Combine(TestDirectoryPath, "retry.csv")
        };
        await vm.ExportLoggingSessionsCommand.ExecuteAsync(null);

        // Act
        vm.RetryExportCommand.Execute(null);

        // Assert
        Assert.IsTrue(vm.IsConfiguring, "Try Again should bring back the configuration form.");
        Assert.IsFalse(vm.IsExportComplete);
        Assert.IsTrue(vm.ExportLoggingSessionsCommand.CanExecute(null), "Export should be runnable again.");
    }

    #endregion

    #region Helpers

    private static void SeedSession(IDbContextFactory<LoggingContext> factory, int sessionId)
    {
        using var context = factory.CreateDbContext();
        context.Sessions.Add(new LoggingSession
        {
            ID = sessionId,
            Name = "Test",
            SessionStart = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var t = 0; t < 3; t++)
        {
            context.Samples.Add(new DataSample
            {
                LoggingSessionID = sessionId,
                DeviceName = "TestDevice",
                DeviceSerialNo = "TEST001",
                ChannelName = "Channel 1",
                TimestampTicks = baseTime.AddMilliseconds(t * 10).Ticks,
                Value = t,
                Color = ""
            });
        }
        context.SaveChanges();
    }

    /// <summary>
    /// A real file-backed SQLite <see cref="IDbContextFactory{T}"/>. The export success path runs
    /// the full EF + streaming-exporter pipeline, so it is exercised against a real (tiny) database
    /// rather than a mock — mirroring the existing pattern in ExportPerformanceTests. The state-machine
    /// and command-gating tests above use a Moq factory instead. The .db file is deleted on Dispose.
    /// </summary>
    private sealed class TempSqliteLoggingContextFactory : IDbContextFactory<LoggingContext>, IDisposable
    {
        private readonly string _dbPath;
        private readonly DbContextOptions<LoggingContext> _options;

        public TempSqliteLoggingContextFactory()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"daqifi_exportvm_{Guid.NewGuid():N}.db");
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

    #endregion
}
