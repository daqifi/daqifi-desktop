using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Exporter;
using Daqifi.Desktop.Logger;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Daqifi.Desktop.Test.Exporter;

[TestClass]
public class ExportPerformanceTests
{
    private const string TestDirectoryPath = @"C:\ProgramData\DAQifi\PerformanceTests";

    [TestInitialize]
    public void Initialize()
    {
        Directory.CreateDirectory(TestDirectoryPath);
    }

    [TestMethod]
    public void ExportLoggingSession_SmallDataset_BaselinePerformance()
    {
        // Small dataset: 4 channels, 100 samples (400 total samples)
        var samples = GenerateTestDataset(4, 100);
        var results = MeasureExportPerformance(samples, "small");

        Console.WriteLine($"Small Dataset (400 samples): {results.ElapsedMs}ms, {results.MemoryMB}MB");
        
        // Baseline assertions - should pass easily
        Assert.IsTrue(results.ElapsedMs < 1000, "Small dataset should export in under 1 second");
        Assert.IsTrue(results.MemoryMB < 10, "Small dataset should use under 10MB memory");
    }

    [TestMethod]
    public void ExportLoggingSession_MediumDataset_ShowsPerformanceDegradation()
    {
        // Medium dataset: 8 channels, 2000 samples (16,000 total samples)  
        var samples = GenerateTestDataset(8, 2000);
        var results = MeasureExportPerformance(samples, "medium");

        Console.WriteLine($"Medium Dataset (16K samples): {results.ElapsedMs}ms, {results.MemoryMB}MB");
        Console.WriteLine($"Samples per second: {16000.0 / results.ElapsedMs * 1000:F0}");
        
        // These will likely fail with current implementation, demonstrating performance issues
        Assert.IsTrue(results.ElapsedMs < 5000, 
            $"Medium dataset took {results.ElapsedMs}ms - should be under 5 seconds");
        Assert.IsTrue(results.MemoryMB < 50, 
            $"Medium dataset used {results.MemoryMB}MB - should be under 50MB");
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
        
        // These assertions will fail with current implementation, proving the performance problem
        Assert.IsTrue(results.ElapsedMs < 10000, 
            $"Large dataset took {results.ElapsedMs}ms - performance issues detected");
        Assert.IsTrue(results.MemoryMB < 100, 
            $"Large dataset used {results.MemoryMB}MB - memory usage too high");
        
        // Target performance: should process at least 50K samples/second
        var samplesPerSecond = 80000.0 / results.ElapsedMs * 1000;
        Assert.IsTrue(samplesPerSecond > 50000, 
            $"Processing rate {samplesPerSecond:F0} samples/second is too slow");
    }

    [TestMethod]
    [TestCategory("PerformanceDemonstration")]
    public void ExportLoggingSession_OriginalExporter_ShowsLinearMemoryGrowth()
    {
        var results = new List<(int SampleCount, long MemoryMB, long ElapsedMs)>();
        
        // Test with increasing dataset sizes to show memory growth pattern
        var testSizes = new[] { 1000, 2000, 4000, 8000 };
        
        foreach (var sampleCount in testSizes)
        {
            // 4 channels for consistency
            var samples = GenerateTestDataset(4, sampleCount / 4);
            var result = MeasureExportPerformance(samples, $"growth_{sampleCount}");
            
            results.Add((sampleCount, result.MemoryMB, result.ElapsedMs));
            Console.WriteLine($"{sampleCount} samples: {result.ElapsedMs}ms, {result.MemoryMB}MB");
            
            // Force garbage collection between tests to get cleaner measurements
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        
        // Check for linear memory growth (bad pattern)
        var firstMemory = results.First().MemoryMB;
        var lastMemory = results.Last().MemoryMB;
        var memoryGrowthRatio = firstMemory > 0 ? (double)lastMemory / firstMemory : double.PositiveInfinity;
        var dataGrowthRatio = (double)results.Last().SampleCount / results.First().SampleCount;
        
        Console.WriteLine($"Memory growth ratio: {(memoryGrowthRatio == double.PositiveInfinity ? "∞" : memoryGrowthRatio.ToString("F1"))}x");
        Console.WriteLine($"Data growth ratio: {dataGrowthRatio:F1}x");
        
        // If memory growth is linear with data size, it indicates the problem
        // NOTE: This test demonstrates the performance problems in the original LoggingSessionExporter
        // It is expected to fail, proving the need for the OptimizedLoggingSessionExporter replacement
        // Handle the case where initial memory is 0 (which indicates infinite growth when memory increases)
        if (firstMemory == 0 && lastMemory > 0)
        {
            Assert.Fail($"DEMONSTRATION: Original exporter memory growth (∞x) shows inefficient loading of all data into memory");
        }
        else if (memoryGrowthRatio > dataGrowthRatio * 0.8 && memoryGrowthRatio != double.PositiveInfinity)
        {
            Assert.Fail($"DEMONSTRATION: Original exporter memory growth ({memoryGrowthRatio:F1}x) is nearly linear with data growth ({dataGrowthRatio:F1}x) - shows memory inefficiency");
        }
    }

    [TestMethod]
    [TestCategory("PerformanceDemonstration")]
    public void ExportLoggingSession_OriginalExporter_ShowsFileIOInefficiency()
    {
        var samples = GenerateTestDataset(4, 1000);
        var exportFilePath = Path.Combine(TestDirectoryPath, "fileio_test.csv");
        
        var loggingSession = new LoggingSession
        {
            ID = 1,
            DataSamples = samples
        };

        var exporter = new LoggingSessionExporter();
        var bw = new BackgroundWorker
        {
            WorkerReportsProgress = true,
            WorkerSupportsCancellation = true
        };

        // Monitor file operations
        var fileWatcher = new FileSystemWatcher(TestDirectoryPath, "fileio_test.csv")
        {
            NotifyFilter = NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };
        
        var writeCount = 0;
        fileWatcher.Changed += (s, e) => writeCount++;

        var stopwatch = Stopwatch.StartNew();
        exporter.ExportLoggingSession(loggingSession, exportFilePath, false, bw, 0, 0);
        stopwatch.Stop();
        
        fileWatcher.EnableRaisingEvents = false;
        fileWatcher.Dispose();

        Console.WriteLine($"File write operations: {writeCount}");
        Console.WriteLine($"Export time: {stopwatch.ElapsedMilliseconds}ms");
        
        // NOTE: This test demonstrates the file I/O inefficiency in the original LoggingSessionExporter  
        // It is expected to fail, proving the need for the OptimizedLoggingSessionExporter replacement
        Assert.IsTrue(writeCount < 100, 
            $"DEMONSTRATION: Original exporter uses too many file write operations ({writeCount}) - shows inefficient file I/O pattern");
    }

    [TestMethod]
    [TestCategory("Production")]
    public void OptimizedExporter_LargeDataset_MeetsPerformanceTargets()
    {
        // Test the optimized exporter that is now used in production
        var samples = GenerateTestDataset(16, 3000); // 48,000 samples
        var loggingSession = new LoggingSession { ID = 1, DataSamples = samples };
        
        var exportFilePath = Path.Combine(TestDirectoryPath, "optimized_production_test.csv");
        var bw = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };

        // Measure optimized exporter performance
        var initialMemory = GC.GetTotalMemory(true);
        var stopwatch = Stopwatch.StartNew();
        
        var optimizedExporter = new OptimizedLoggingSessionExporter();
        optimizedExporter.ExportLoggingSession(loggingSession, exportFilePath, false, bw, 0, 1);
        
        stopwatch.Stop();
        var finalMemory = GC.GetTotalMemory(false);
        var memoryUsed = Math.Max(0, finalMemory - initialMemory) / 1024 / 1024;

        Console.WriteLine($"Optimized Export Results:");
        Console.WriteLine($"Time: {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"Memory: {memoryUsed}MB");
        
        var samplesPerSecond = stopwatch.ElapsedMilliseconds > 0 ? 48000.0 / stopwatch.ElapsedMilliseconds * 1000 : double.PositiveInfinity;
        Console.WriteLine($"Samples per second: {(samplesPerSecond == double.PositiveInfinity ? "∞" : samplesPerSecond.ToString("F0"))}");

        // Verify file was created and has correct structure
        Assert.IsTrue(File.Exists(exportFilePath), "Export file should be created");
        
        var lines = File.ReadAllLines(exportFilePath);
        Assert.IsTrue(lines.Length > 1, "Export should contain header and data rows");
        
        // Performance targets for production deployment
        if (stopwatch.ElapsedMilliseconds > 10) // Only check if measurable
        {
            Assert.IsTrue(samplesPerSecond > 50000, 
                $"Production optimized exporter should process >50K samples/second. Actual: {samplesPerSecond:F0}");
        }
        
        // Memory should be reasonable for this dataset size
        Assert.IsTrue(memoryUsed < 100, 
            $"Production optimized exporter should use <100MB for 48K samples. Actual: {memoryUsed}MB");
    }

    private List<DataSample> GenerateTestDataset(int channelCount, int samplesPerChannel)
    {
        var samples = new List<DataSample>();
        var baseTime = new DateTime(2018, 1, 1, 0, 0, 0);
        
        // Generate time-series data
        for (int timeStep = 0; timeStep < samplesPerChannel; timeStep++)
        {
            var timestamp = baseTime.AddMilliseconds(timeStep * 10); // 100Hz equivalent
            
            for (int channel = 1; channel <= channelCount; channel++)
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

        var exporter = new LoggingSessionExporter();
        var bw = new BackgroundWorker
        {
            WorkerReportsProgress = true,
            WorkerSupportsCancellation = true
        };

        // Force garbage collection before measurement
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var initialMemory = GC.GetTotalMemory(false);
        var stopwatch = Stopwatch.StartNew();
        
        exporter.ExportLoggingSession(loggingSession, exportFilePath, false, bw, 0, 0);
        
        stopwatch.Stop();
        var finalMemory = GC.GetTotalMemory(false);
        var memoryUsed = Math.Max(0, finalMemory - initialMemory);

        return (stopwatch.ElapsedMilliseconds, memoryUsed / 1024 / 1024);
    }

    [TestCleanup]
    public void CleanUp()
    {
        if (Directory.Exists(TestDirectoryPath))
        {
            Directory.Delete(TestDirectoryPath, true);
        }
    }
}