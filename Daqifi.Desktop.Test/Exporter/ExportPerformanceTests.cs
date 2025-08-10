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
    public void ExportLoggingSession_MemoryGrowthPattern_ShowsLinearGrowth()
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
        var memoryGrowthRatio = (double)results.Last().MemoryMB / results.First().MemoryMB;
        var dataGrowthRatio = (double)results.Last().SampleCount / results.First().SampleCount;
        
        Console.WriteLine($"Memory growth ratio: {memoryGrowthRatio:F1}x");
        Console.WriteLine($"Data growth ratio: {dataGrowthRatio:F1}x");
        
        // If memory growth is linear with data size, it indicates the problem
        if (memoryGrowthRatio > dataGrowthRatio * 0.8)
        {
            Assert.Fail($"Memory growth ({memoryGrowthRatio:F1}x) is nearly linear with data growth ({dataGrowthRatio:F1}x) - indicates memory inefficiency");
        }
    }

    [TestMethod]
    public void ExportLoggingSession_FileIOPattern_ShowsInefficiency()
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
        
        // Current implementation writes once per timestamp - this should be much lower
        Assert.IsTrue(writeCount < 100, 
            $"Too many file write operations ({writeCount}) - indicates inefficient file I/O");
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
                    Value = Math.Sin(timeStep * 0.01 * channel) * channel + Math.Random.Shared.NextDouble()
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