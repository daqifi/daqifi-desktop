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

    private static ExportDialogViewModel CreateViewModel(IDbContextFactory<LoggingContext> factory = null)
        => new(factory ?? new Mock<IDbContextFactory<LoggingContext>>().Object, sessionId: 1);

    #region IsConfiguring (which of the three states is shown)

    [TestMethod]
    public void IsConfiguring_IsTrue_OnNewViewModel()
    {
        var vm = CreateViewModel();

        Assert.IsTrue(vm.IsConfiguring);
        Assert.IsFalse(vm.IsExporting);
        Assert.IsFalse(vm.IsExportComplete);
    }

    [TestMethod]
    public void IsConfiguring_IsFalse_WhileExporting()
    {
        var vm = CreateViewModel();

        vm.IsExporting = true;

        Assert.IsFalse(vm.IsConfiguring);
    }

    [TestMethod]
    public void IsConfiguring_IsFalse_WhenComplete()
    {
        var vm = CreateViewModel();

        vm.IsExportComplete = true;

        Assert.IsFalse(vm.IsConfiguring);
    }

    [TestMethod]
    public void IsConfiguring_RaisesPropertyChanged_WhenExportingChanges()
    {
        var vm = CreateViewModel();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.IsExporting = true;

        CollectionAssert.Contains(raised, nameof(ExportDialogViewModel.IsConfiguring));
    }

    [TestMethod]
    public void IsConfiguring_RaisesPropertyChanged_WhenCompleteChanges()
    {
        var vm = CreateViewModel();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.IsExportComplete = true;

        CollectionAssert.Contains(raised, nameof(ExportDialogViewModel.IsConfiguring));
    }

    #endregion

    #region Export command gating (CanExport)

    [TestMethod]
    public void ExportCommand_CannotExecute_WithoutDestination()
    {
        var vm = CreateViewModel();

        Assert.IsFalse(vm.ExportLoggingSessionsCommand.CanExecute(null));
    }

    [TestMethod]
    public void ExportCommand_CanExecute_OnceDestinationIsSet()
    {
        var vm = CreateViewModel();

        vm.ExportFilePath = Path.Combine(TestDirectoryPath, "out.csv");

        Assert.IsTrue(vm.ExportLoggingSessionsCommand.CanExecute(null));
    }

    [TestMethod]
    public void SettingDestination_RaisesCanExecuteChanged_ForExportCommand()
    {
        var vm = CreateViewModel();
        var raised = false;
        vm.ExportLoggingSessionsCommand.CanExecuteChanged += (_, _) => raised = true;

        vm.ExportFilePath = Path.Combine(TestDirectoryPath, "out.csv");

        Assert.IsTrue(raised);
    }

    #endregion

    #region Terminal state transitions

    [TestMethod]
    public async Task Export_OnFailure_ShowsFailedResult()
    {
        // A factory that throws when the export reads the session forces the catch-all path
        // deterministically — no file-system or timing dependence.
        var factory = new Mock<IDbContextFactory<LoggingContext>>();
        factory.Setup(f => f.CreateDbContext()).Throws(new InvalidOperationException("boom"));
        var vm = new ExportDialogViewModel(factory.Object, sessionId: 1)
        {
            ExportFilePath = Path.Combine(TestDirectoryPath, "fail.csv")
        };

        await vm.ExportLoggingSessionsCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.IsExportComplete, "Failure should still land on the result state.");
        Assert.IsFalse(vm.ExportSucceeded);
        Assert.IsFalse(vm.IsExporting);
        Assert.IsFalse(vm.IsConfiguring);
        Assert.AreEqual("Export failed. Please try again.", vm.ExportResultMessage);
    }

    [TestMethod]
    public async Task Export_OnSuccess_ShowsCompleteResultAndWritesFile()
    {
        using var factory = new TempSqliteLoggingContextFactory();
        SeedSession(factory, sessionId: 1);
        var exportPath = Path.Combine(TestDirectoryPath, $"success_{Guid.NewGuid():N}.csv");
        var vm = new ExportDialogViewModel(factory, sessionId: 1) { ExportFilePath = exportPath };

        await vm.ExportLoggingSessionsCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.IsExportComplete);
        Assert.IsTrue(vm.ExportSucceeded);
        Assert.IsFalse(vm.IsExporting);
        Assert.IsFalse(vm.IsConfiguring);
        Assert.AreEqual("Export complete", vm.ExportResultMessage);
        Assert.IsTrue(File.Exists(exportPath), "A successful export should have written the file.");
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
