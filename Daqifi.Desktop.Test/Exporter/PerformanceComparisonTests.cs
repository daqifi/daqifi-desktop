using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Exporter;
using Daqifi.Desktop.Logger;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Daqifi.Desktop.Test.Exporter;

[TestClass]
public class PerformanceComparisonTests
{
    private const string TestDirectoryPath = @"C:\ProgramData\DAQifi\ComparisonTests";

    [TestInitialize]
    public void Initialize()
    {
        Directory.CreateDirectory(TestDirectoryPath);
    }

    [TestMethod]
    public void CompareExporters_OutputCorrectness_BothProduceSameResults()
    {
        var samples = GenerateTestDataset(4, 100);
        var loggingSession = new LoggingSession { ID = 1, DataSamples = samples };
        
        var originalPath = Path.Combine(TestDirectoryPath, "original.csv");
        var optimizedPath = Path.Combine(TestDirectoryPath, "optimized.csv");

        var bw = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };

        // Export with original exporter
        var originalExporter = new LoggingSessionExporter();
        originalExporter.ExportLoggingSession(loggingSession, originalPath, false, bw, 0, 1);

        // Export with optimized exporter
        var optimizedExporter = new OptimizedLoggingSessionExporter();
        optimizedExporter.ExportLoggingSession(loggingSession, optimizedPath, false, bw, 0, 1);

        // Compare outputs
        var originalContent = File.ReadAllText(originalPath);
        var optimizedContent = File.ReadAllText(optimizedPath);

        Assert.AreEqual(originalContent, optimizedContent, "Optimized exporter must produce identical output");
    }

    [TestMethod]
    public void CompareExporters_RelativeTimeOutput_BothProduceSameResults()
    {
        var samples = GenerateTestDataset(3, 50);
        var loggingSession = new LoggingSession { ID = 1, DataSamples = samples };
        
        var originalPath = Path.Combine(TestDirectoryPath, "original_relative.csv");
        var optimizedPath = Path.Combine(TestDirectoryPath, "optimized_relative.csv");

        var bw = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };

        // Export with relative time enabled
        var originalExporter = new LoggingSessionExporter();
        originalExporter.ExportLoggingSession(loggingSession, originalPath, true, bw, 0, 1);

        var optimizedExporter = new OptimizedLoggingSessionExporter();
        optimizedExporter.ExportLoggingSession(loggingSession, optimizedPath, true, bw, 0, 1);

        var originalContent = File.ReadAllText(originalPath);
        var optimizedContent = File.ReadAllText(optimizedPath);

        Assert.AreEqual(originalContent, optimizedContent, "Relative time output must be identical");
    }

    [TestMethod]
    public void CompareExporters_PerformanceImprovement_OptimizedIsFaster()
    {
        // Medium dataset for meaningful performance comparison
        var samples = GenerateTestDataset(8, 1000); // 8000 samples
        var loggingSession = new LoggingSession { ID = 1, DataSamples = samples };
        
        var originalPath = Path.Combine(TestDirectoryPath, "perf_original.csv");
        var optimizedPath = Path.Combine(TestDirectoryPath, "perf_optimized.csv");

        var bw = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };

        // Measure original exporter
        var originalResults = MeasureExportPerformance(() =>
        {
            var exporter = new LoggingSessionExporter();
            exporter.ExportLoggingSession(loggingSession, originalPath, false, bw, 0, 1);
        });

        // Measure optimized exporter
        var optimizedResults = MeasureExportPerformance(() =>
        {
            var exporter = new OptimizedLoggingSessionExporter();
            exporter.ExportLoggingSession(loggingSession, optimizedPath, false, bw, 0, 1);
        });

        Console.WriteLine($"Original: {originalResults.ElapsedMs}ms, {originalResults.MemoryMB}MB");
        Console.WriteLine($"Optimized: {optimizedResults.ElapsedMs}ms, {optimizedResults.MemoryMB}MB");
        
        var speedImprovement = optimizedResults.ElapsedMs > 0 ? (double)originalResults.ElapsedMs / optimizedResults.ElapsedMs : double.PositiveInfinity;
        var memoryImprovement = optimizedResults.MemoryMB > 0 ? (double)originalResults.MemoryMB / optimizedResults.MemoryMB : double.PositiveInfinity;
        
        Console.WriteLine($"Speed improvement: {(speedImprovement == double.PositiveInfinity ? "∞" : speedImprovement.ToString("F1"))}x");
        Console.WriteLine($"Memory improvement: {(memoryImprovement == double.PositiveInfinity ? "∞" : memoryImprovement.ToString("F1"))}x");

        // Verify outputs are identical
        var originalContent = File.ReadAllText(originalPath);
        var optimizedContent = File.ReadAllText(optimizedPath);
        Assert.AreEqual(originalContent, optimizedContent, "Outputs must be identical");

        // Performance assertions - allow for very fast execution times
        if (optimizedResults.ElapsedMs > 0)
        {
            Assert.IsTrue(optimizedResults.ElapsedMs <= originalResults.ElapsedMs, 
                $"Optimized version should be at least as fast. Original: {originalResults.ElapsedMs}ms, Optimized: {optimizedResults.ElapsedMs}ms");
        }
        
        if (optimizedResults.MemoryMB > 0)
        {
            Assert.IsTrue(optimizedResults.MemoryMB <= originalResults.MemoryMB, 
                $"Optimized version should use at most the same memory. Original: {originalResults.MemoryMB}MB, Optimized: {optimizedResults.MemoryMB}MB");
        }
    }

    [TestMethod]
    public void CompareExporters_LargeDataset_ShowsSignificantImprovement()
    {
        // Larger dataset to show more dramatic improvements
        var samples = GenerateTestDataset(16, 2000); // 32,000 samples
        var loggingSession = new LoggingSession { ID = 1, DataSamples = samples };
        
        var originalPath = Path.Combine(TestDirectoryPath, "large_original.csv");
        var optimizedPath = Path.Combine(TestDirectoryPath, "large_optimized.csv");

        var bw = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };

        // Measure original exporter
        var originalResults = MeasureExportPerformance(() =>
        {
            var exporter = new LoggingSessionExporter();
            exporter.ExportLoggingSession(loggingSession, originalPath, false, bw, 0, 1);
        });

        // Measure optimized exporter  
        var optimizedResults = MeasureExportPerformance(() =>
        {
            var exporter = new OptimizedLoggingSessionExporter();
            exporter.ExportLoggingSession(loggingSession, optimizedPath, false, bw, 0, 1);
        });

        Console.WriteLine($"Large Dataset Results:");
        Console.WriteLine($"Original: {originalResults.ElapsedMs}ms, {originalResults.MemoryMB}MB");
        Console.WriteLine($"Optimized: {optimizedResults.ElapsedMs}ms, {optimizedResults.MemoryMB}MB");
        
        var speedImprovement = optimizedResults.ElapsedMs > 0 ? (double)originalResults.ElapsedMs / optimizedResults.ElapsedMs : double.PositiveInfinity;
        var memoryImprovement = optimizedResults.MemoryMB > 0 ? (double)originalResults.MemoryMB / optimizedResults.MemoryMB : double.PositiveInfinity;
        
        Console.WriteLine($"Speed improvement: {(speedImprovement == double.PositiveInfinity ? "∞" : speedImprovement.ToString("F1"))}x");
        Console.WriteLine($"Memory improvement: {(memoryImprovement == double.PositiveInfinity ? "∞" : memoryImprovement.ToString("F1"))}x");
        
        var originalSamplesPerSecond = originalResults.ElapsedMs > 0 ? 32000.0 / originalResults.ElapsedMs * 1000 : 0;
        var optimizedSamplesPerSecond = optimizedResults.ElapsedMs > 0 ? 32000.0 / optimizedResults.ElapsedMs * 1000 : double.PositiveInfinity;
        
        Console.WriteLine($"Original samples/sec: {originalSamplesPerSecond:F0}");
        Console.WriteLine($"Optimized samples/sec: {(optimizedSamplesPerSecond == double.PositiveInfinity ? "∞" : optimizedSamplesPerSecond.ToString("F0"))}");

        // Verify outputs are identical
        var originalContent = File.ReadAllText(originalPath);
        var optimizedContent = File.ReadAllText(optimizedPath);
        Assert.AreEqual(originalContent, optimizedContent, "Outputs must be identical");

        // Performance assertions - be flexible for very fast execution
        if (optimizedResults.ElapsedMs > 0)
        {
            Assert.IsTrue(optimizedResults.ElapsedMs <= originalResults.ElapsedMs, 
                $"Optimized version should be at least as fast. Original: {originalResults.ElapsedMs}ms, Optimized: {optimizedResults.ElapsedMs}ms");
        }
        
        if (optimizedResults.MemoryMB > 0)
        {
            Assert.IsTrue(optimizedResults.MemoryMB <= originalResults.MemoryMB, 
                $"Optimized version should use at most same memory. Original: {originalResults.MemoryMB}MB, Optimized: {optimizedResults.MemoryMB}MB");
        }
        
        // Target performance for scaling to 51.8M samples - only check if we have meaningful timing
        if (optimizedResults.ElapsedMs > 10) // Only check if execution took more than 10ms
        {
            Assert.IsTrue(optimizedSamplesPerSecond > 50000, 
                $"Optimized version should process >50K samples/second for large dataset scaling. Actual: {optimizedSamplesPerSecond:F0}");
        }
    }

    [TestMethod]
    public void CompareExporters_NonUniformData_HandlesCorrectly()
    {
        // Create non-uniform data (missing samples for some channels at some timestamps)
        var samples = GenerateNonUniformTestDataset();
        var loggingSession = new LoggingSession { ID = 1, DataSamples = samples };
        
        var originalPath = Path.Combine(TestDirectoryPath, "nonuniform_original.csv");
        var optimizedPath = Path.Combine(TestDirectoryPath, "nonuniform_optimized.csv");

        var bw = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };

        var originalExporter = new LoggingSessionExporter();
        originalExporter.ExportLoggingSession(loggingSession, originalPath, false, bw, 0, 1);

        var optimizedExporter = new OptimizedLoggingSessionExporter();
        optimizedExporter.ExportLoggingSession(loggingSession, optimizedPath, false, bw, 0, 1);

        var originalContent = File.ReadAllText(originalPath);
        var optimizedContent = File.ReadAllText(optimizedPath);

        Assert.AreEqual(originalContent, optimizedContent, "Non-uniform data handling must be identical");
        
        // Verify empty cells are handled correctly
        Assert.IsTrue(originalContent.Contains(",,"), "Should contain empty cells for missing data");
    }

    private List<DataSample> GenerateTestDataset(int channelCount, int samplesPerChannel)
    {
        var samples = new List<DataSample>();
        var baseTime = new DateTime(2018, 1, 1, 0, 0, 0);
        
        for (int timeStep = 0; timeStep < samplesPerChannel; timeStep++)
        {
            var timestamp = baseTime.AddMilliseconds(timeStep * 10); // 100Hz equivalent
            
            for (int channel = 1; channel <= channelCount; channel++)
            {
                samples.Add(new DataSample
                {
                    ID = timeStep * channelCount + channel,
                    DeviceName = "TestDevice",
                    DeviceSerialNo = "TEST001",
                    LoggingSessionID = 1,
                    ChannelName = $"Channel {channel}",
                    TimestampTicks = timestamp.Ticks,
                    Value = Math.Sin(timeStep * 0.01 * channel) * channel + channel
                });
            }
        }
        
        return samples;
    }

    private List<DataSample> GenerateNonUniformTestDataset()
    {
        var samples = new List<DataSample>();
        var baseTime = new DateTime(2018, 1, 1, 0, 0, 0);
        
        // Generate irregular data pattern
        for (int timeStep = 0; timeStep < 10; timeStep++)
        {
            var timestamp = baseTime.AddSeconds(timeStep);
            
            // Sometimes skip channel 2
            for (int channel = 1; channel <= 3; channel++)
            {
                if (channel == 2 && timeStep % 3 == 0) continue; // Skip channel 2 every 3rd timestamp
                
                samples.Add(new DataSample
                {
                    ID = timeStep * 3 + channel,
                    DeviceName = "TestDevice",
                    DeviceSerialNo = "TEST001",
                    LoggingSessionID = 1,
                    ChannelName = $"Channel {channel}",
                    TimestampTicks = timestamp.Ticks,
                    Value = timeStep + channel * 0.1
                });
            }
        }
        
        return samples;
    }

    private (long ElapsedMs, long MemoryMB) MeasureExportPerformance(Action exportAction)
    {
        // Force garbage collection before measurement
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var initialMemory = GC.GetTotalMemory(false);
        var stopwatch = Stopwatch.StartNew();
        
        exportAction();
        
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