using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Exporter;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.Test.TestSupport;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Globalization;

namespace Daqifi.Desktop.Test.Exporter;

/// <summary>
/// Export performance characterisation. Timing budgets here run through
/// <see cref="PerformanceBudget"/>: they are reported on every run but only fail the build when
/// <c>DAQIFI_ENFORCE_PERF_ASSERTIONS=1</c>, because wall-clock numbers track machine load rather
/// than the exporter. Output correctness, row counts, memory ceilings and the hang guards are
/// asserted unconditionally.
/// </summary>
[TestClass]
public class ExportPerformanceTests
{
    private TempTestDirectory _testDirectory = null!;

    private string TestDirectoryPath => _testDirectory.FullPath;

    [TestInitialize]
    public void Initialize()
    {
        _testDirectory = new TempTestDirectory("export-perf");
    }

    [TestMethod]
    public void ExportLoggingSession_SmallDataset_BaselinePerformance()
    {
        // Small dataset: 4 channels, 100 samples (400 total samples)
        var samples = GenerateTestDataset(4, 100);
        var results = MeasureExportPerformance(samples, "small");

        Console.WriteLine($"Small Dataset (400 samples): {results.ElapsedMs}ms, {results.MemoryMB}MB");

        // Baseline assertions - should pass easily
        PerformanceBudget.ExpectElapsedUnder(1000, results.ElapsedMs, "Small dataset (400 samples) export");
        Assert.IsLessThan(10, results.MemoryMB, "Small dataset should use under 10MB memory");
    }

    [TestMethod]
    public void ExportLoggingSession_MediumDataset_ShowsPerformanceDegradation()
    {
        // Medium dataset: 8 channels, 2000 samples (16,000 total samples)
        var samples = GenerateTestDataset(8, 2000);
        var results = MeasureExportPerformance(samples, "medium");

        Console.WriteLine($"Medium Dataset (16K samples): {results.ElapsedMs}ms, {results.MemoryMB}MB");
        Console.WriteLine($"Samples per second: {16000.0 / results.ElapsedMs * 1000:F0}");

        PerformanceBudget.ExpectElapsedUnder(5000, results.ElapsedMs, "Medium dataset (16K samples) export");
        Assert.IsLessThan(50,
results.MemoryMB, $"Medium dataset used {results.MemoryMB}MB - should be under 50MB");
    }

    [TestMethod]
    public void ExportLoggingSession_LargeDataset_DemonstratesPerformanceProblems()
    {
        // Large dataset: 16 channels, 5000 samples (80,000 total samples)
        // This represents ~8 minutes of data at 100Hz for 16 channels
        var samples = GenerateTestDataset(16, 5000);
        var results = MeasureExportPerformance(samples, "large");

        Console.WriteLine($"Large Dataset (80K samples): {results.ElapsedMs}ms, {results.MemoryMB}MB");
        Console.WriteLine($"Samples per second: {80000.0 / results.ElapsedMs * 1000:F0}");
        Console.WriteLine($"Projected time for 51.8M samples: {results.ElapsedMs * (51800000.0 / 80000) / 1000 / 60:F1} minutes");

        PerformanceBudget.ExpectElapsedUnder(10000, results.ElapsedMs, "Large dataset (80K samples) export");
        Assert.IsLessThan(100,
results.MemoryMB, $"Large dataset used {results.MemoryMB}MB - memory usage too high");

        // Target performance: should process at least 50K samples/second
        var samplesPerSecond = 80000.0 / results.ElapsedMs * 1000;
        PerformanceBudget.ExpectThroughputOver(50000, samplesPerSecond, "Large dataset (80K samples) throughput");
    }

    [TestMethod]
    [TestCategory("Documentation")]
    public void DocumentPerformanceImprovements_OriginalVsOptimized()
    {
        // This test documents the performance improvements achieved by replacing
        // LoggingSessionExporter with OptimizedLoggingSessionExporter

        Console.WriteLine("=== PERFORMANCE IMPROVEMENT DOCUMENTATION ===");
        Console.WriteLine("GitHub Issue #188 - Export Performance Optimization Results:");
        Console.WriteLine("");
        Console.WriteLine("BEFORE (Original LoggingSessionExporter):");
        Console.WriteLine("- 51.8M samples took ~75 minutes to export");
        Console.WriteLine("- Used >32GB memory (loaded all data into memory)");
        Console.WriteLine("- File.AppendAllText() called for every timestamp (~1000+ file operations)");
        Console.WriteLine("- Linear memory growth with dataset size");
        Console.WriteLine("");
        Console.WriteLine("AFTER (OptimizedLoggingSessionExporter):");
        Console.WriteLine("- 10x+ speed improvement achieved in testing");
        Console.WriteLine("- Memory capped at reasonable levels with streaming processing");
        Console.WriteLine("- Buffered file I/O reduces operations dramatically");
        Console.WriteLine("- Identical CSV output maintained");
        Console.WriteLine("");
        Console.WriteLine("PRODUCTION DEPLOYMENT:");
        Console.WriteLine("- LoggingSessionExporter.cs removed from codebase");
        Console.WriteLine("- ExportDialogViewModel updated to use OptimizedLoggingSessionExporter");
        Console.WriteLine("- All export operations now benefit from optimization");

        // Regression guard for the claim above: the legacy exporter must stay deleted from the
        // production assembly, otherwise the un-optimized export path could be wired back up.
        Assert.IsNull(
            typeof(OptimizedLoggingSessionExporter).Assembly.GetType("Daqifi.Desktop.Exporter.LoggingSessionExporter"),
            "LoggingSessionExporter was replaced by OptimizedLoggingSessionExporter (issue #188) and must not return.");
    }


    [TestMethod]
    [TestCategory("Production")]
    [Timeout(120_000)] // Hard-cap: if a regression causes a true hang, fail fast instead of blocking the suite.
    public void OptimizedExporter_Issue18Shape_LargeExport_CompletesWithoutStalling()
    {
        // Regression guard for daqifi/daqifi-desktop#18 ("Large Data Export Fails").
        // Original report: 48hr × 16ch @ 10Hz export appeared to stall after ~60s.
        // We exercise the production path — OptimizedLoggingSessionExporter backed by a real
        // SQLite LoggingContext (ExportFromDatabase → StreamDataToFile), which is the code
        // path ExportDialogViewModel hits. Sample count is scaled down to keep CI cost low
        // while still being large enough to meaningfully stress the streaming writer and the
        // per-timestamp bucketing loop.
        const int CHANNEL_COUNT = 16;
        const int TIMESTAMP_COUNT = 6_250; // 16 × 6,250 = 100,000 samples
        const int EXPECTED_SAMPLES = CHANNEL_COUNT * TIMESTAMP_COUNT;

        using var factory = new TempSqliteLoggingContextFactory();
        const int SESSION_ID = 1;
        SeedSessionWithSamples(factory, SESSION_ID, CHANNEL_COUNT, TIMESTAMP_COUNT);

        var exportFilePath = Path.Combine(TestDirectoryPath, "issue18_db_export.csv");
        var exporter = new OptimizedLoggingSessionExporter(factory);
        var progress = new Progress<int>();

        // Cancel after 90s so a true hang inside the streaming loop trips cancellation
        // before the MSTest [Timeout] kills the whole run.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var initialMemory = GC.GetTotalMemory(false);
        var stopwatch = Stopwatch.StartNew();

        exporter.ExportLoggingSession(
            new LoggingSession { ID = SESSION_ID, Name = "issue18" },
            exportFilePath,
            exportRelativeTime: false,
            progress,
            sessionIndex: 0,
            totalSessions: 1,
            cts.Token);

        stopwatch.Stop();
        var memoryUsedMb = Math.Max(0, GC.GetTotalMemory(false) - initialMemory) / 1024 / 1024;
        var samplesPerSecond = stopwatch.ElapsedMilliseconds > 0
            ? EXPECTED_SAMPLES / (stopwatch.ElapsedMilliseconds / 1000.0)
            : double.PositiveInfinity;

        Console.WriteLine(FormattableString.Invariant($"Issue #18 DB-path shape ({EXPECTED_SAMPLES:N0} samples, {CHANNEL_COUNT}ch): {stopwatch.ElapsedMilliseconds}ms, {memoryUsedMb}MB, {samplesPerSecond:F0} samples/sec"));

        Assert.IsFalse(cts.IsCancellationRequested,
            "Export was cancelled by the 90s watchdog — indicates a stall regression (issue #18).");

        // The original failure was the export appearing to give up after ~60s, so the stall guard
        // has to bite below that: the 90s watchdog and the 120s [Timeout] only catch a true hang,
        // and a 60s export is the regression itself, not a hang. This export measures ~2s, so a
        // 45s ceiling is ~25x headroom — unreachable by machine load, and still well under the
        // failure it guards.
        Assert.IsLessThan(45_000, stopwatch.ElapsedMilliseconds,
            $"Export took {stopwatch.ElapsedMilliseconds}ms — regression in streaming export path (issue #18).");

        // Finer-grained signal on top of that ceiling: reported every run, enforced only under
        // DAQIFI_ENFORCE_PERF_ASSERTIONS, so a merely-slow machine reports without failing.
        PerformanceBudget.ExpectElapsedUnder(30_000, stopwatch.ElapsedMilliseconds,
            "Issue #18 DB-path export (100K samples)");

        // Streaming export should keep memory roughly flat regardless of sample count.
        Assert.IsLessThan(200, memoryUsedMb,
            $"Export used {memoryUsedMb}MB — streaming path may be materializing data (issue #18).");

        // Verify the file actually contains all timestamps (header + one row per timestamp).
        Assert.IsTrue(File.Exists(exportFilePath), "Export file should exist");
        var lineCount = File.ReadLines(exportFilePath).Count();
        Assert.AreEqual(TIMESTAMP_COUNT + 1, lineCount,
            $"Expected {TIMESTAMP_COUNT + 1} lines (header + {TIMESTAMP_COUNT} timestamp rows), got {lineCount}.");
    }

    private static void SeedSessionWithSamples(TempSqliteLoggingContextFactory factory, int sessionId, int channelCount, int timestampCount)
    {
        using var context = factory.CreateDbContext();
        context.Sessions.Add(new LoggingSession
        {
            ID = sessionId,
            Name = "issue18",
            SessionStart = new DateTime(2018, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        context.SaveChanges();

        // EF.AddRange is too slow for 100K rows; drop to raw ADO.NET in a single transaction.
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) connection.Open();
        using var tx = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO Samples (LoggingSessionID, DeviceName, DeviceSerialNo, ChannelName, TimestampTicks, Value, Color, Type) " +
            "VALUES ($sid, $dn, $sn, $cn, $ts, $v, '', 0)";
        var pSid = cmd.CreateParameter(); pSid.ParameterName = "$sid"; cmd.Parameters.Add(pSid);
        var pDn = cmd.CreateParameter(); pDn.ParameterName = "$dn"; cmd.Parameters.Add(pDn);
        var pSn = cmd.CreateParameter(); pSn.ParameterName = "$sn"; cmd.Parameters.Add(pSn);
        var pCn = cmd.CreateParameter(); pCn.ParameterName = "$cn"; cmd.Parameters.Add(pCn);
        var pTs = cmd.CreateParameter(); pTs.ParameterName = "$ts"; cmd.Parameters.Add(pTs);
        var pV = cmd.CreateParameter(); pV.ParameterName = "$v"; cmd.Parameters.Add(pV);

        var baseTime = new DateTime(2018, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var t = 0; t < timestampCount; t++)
        {
            var ticks = baseTime.AddMilliseconds(t * 10).Ticks;
            for (var c = 1; c <= channelCount; c++)
            {
                pSid.Value = sessionId;
                pDn.Value = "PerfTestDevice";
                pSn.Value = "PERF001";
                pCn.Value = $"Channel {c}";
                pTs.Value = ticks;
                pV.Value = Math.Sin(t * 0.01 * c) * c;
                cmd.ExecuteNonQuery();
            }
        }
        tx.Commit();
    }

    private sealed class TempSqliteLoggingContextFactory : IDbContextFactory<LoggingContext>, IDisposable
    {
        private readonly string _dbPath;
        private readonly DbContextOptions<LoggingContext> _options;

        public TempSqliteLoggingContextFactory()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"daqifi_issue18_{Guid.NewGuid():N}.db");
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
                if (File.Exists(_dbPath)) File.Delete(_dbPath);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    [TestMethod]
    [TestCategory("Production")]
    public void OptimizedExporter_LargeDataset_MeetsPerformanceTargets()
    {
        // Test the optimized exporter that is now used in production
        var samples = GenerateTestDataset(16, 3000); // 48,000 samples
        var loggingSession = new LoggingSession { ID = 1, DataSamples = samples };

        var exportFilePath = Path.Combine(TestDirectoryPath, "optimized_production_test.csv");
        var progress = new Progress<int>();

        // Measure optimized exporter performance
        var initialMemory = GC.GetTotalMemory(true);
        var stopwatch = Stopwatch.StartNew();

        var optimizedExporter = new OptimizedLoggingSessionExporter();
        optimizedExporter.ExportLoggingSession(loggingSession, exportFilePath, false, progress, 0, 1, CancellationToken.None);

        stopwatch.Stop();
        var finalMemory = GC.GetTotalMemory(false);
        var memoryUsed = Math.Max(0, finalMemory - initialMemory) / 1024 / 1024;

        Console.WriteLine("Optimized Export Results:");
        Console.WriteLine($"Time: {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"Memory: {memoryUsed}MB");

        var samplesPerSecond = stopwatch.ElapsedMilliseconds > 0 ? 48000.0 / stopwatch.ElapsedMilliseconds * 1000 : double.PositiveInfinity;
        Console.WriteLine($"Samples per second: {(samplesPerSecond == double.PositiveInfinity ? "∞" : samplesPerSecond.ToString("F0", CultureInfo.InvariantCulture))}");

        // Verify file was created and has correct structure
        Assert.IsTrue(File.Exists(exportFilePath), "Export file should be created");

        var lines = File.ReadAllLines(exportFilePath);
        Assert.IsGreaterThan(1, lines.Length, "Export should contain header and data rows");

        // Performance targets for production deployment
        PerformanceBudget.ExpectThroughputOver(50000, samplesPerSecond,
            "Production optimized exporter (48K samples) throughput");

        // Memory should be reasonable for this dataset size
        Assert.IsLessThan(100,
memoryUsed, $"Production optimized exporter should use <100MB for 48K samples. Actual: {memoryUsed}MB");
    }

    private static List<DataSample> GenerateTestDataset(int channelCount, int samplesPerChannel)
    {
        var samples = new List<DataSample>();
        var baseTime = new DateTime(2018, 1, 1, 0, 0, 0);

        // Generate time-series data
        for (var timeStep = 0; timeStep < samplesPerChannel; timeStep++)
        {
            var timestamp = baseTime.AddMilliseconds(timeStep * 10); // 100Hz equivalent

            for (var channel = 1; channel <= channelCount; channel++)
            {
                samples.Add(new DataSample
                {
                    ID = timeStep * channelCount + channel,
                    DeviceName = "PerfTestDevice",
                    DeviceSerialNo = "PERF001",
                    LoggingSessionID = 1,
                    ChannelName = $"Channel {channel}",
                    TimestampTicks = timestamp.Ticks,
                    Value = Math.Sin(timeStep * 0.01 * channel) * channel + Random.Shared.NextDouble()
                });
            }
        }

        return samples;
    }

    private (long ElapsedMs, long MemoryMB) MeasureExportPerformance(List<DataSample> samples, string testName)
    {
        var exportFilePath = Path.Combine(TestDirectoryPath, $"{testName}_export.csv");

        var loggingSession = new LoggingSession
        {
            ID = 1,
            DataSamples = samples
        };

        var exporter = new OptimizedLoggingSessionExporter();
        var progress = new Progress<int>();

        // Force garbage collection before measurement
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var initialMemory = GC.GetTotalMemory(false);
        var stopwatch = Stopwatch.StartNew();

        exporter.ExportLoggingSession(loggingSession, exportFilePath, false, progress, 0, 0, CancellationToken.None);

        stopwatch.Stop();
        var finalMemory = GC.GetTotalMemory(false);
        var memoryUsed = Math.Max(0, finalMemory - initialMemory);

        return (stopwatch.ElapsedMilliseconds, memoryUsed / 1024 / 1024);
    }

    [TestCleanup]
    public void CleanUp()
    {
        _testDirectory?.Delete();
    }
}
