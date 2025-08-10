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
public class OptimizedExporterValidationTests
{
    private const string TestDirectoryPath = @"C:\ProgramData\DAQifi\OptimizedTests";

    [TestInitialize]
    public void Initialize()
    {
        Directory.CreateDirectory(TestDirectoryPath);
    }

    [TestMethod]
    public void OptimizedExporter_BasicExport_ProducesCorrectOutput()
    {
        var samples = GenerateTestDataset(3, 50);
        var loggingSession = new LoggingSession { ID = 1, DataSamples = samples };
        
        var exportPath = Path.Combine(TestDirectoryPath, "basic_test.csv");
        var bw = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };

        var exporter = new OptimizedLoggingSessionExporter();
        exporter.ExportLoggingSession(loggingSession, exportPath, false, bw, 0, 1);

        // Verify output structure and content
        Assert.IsTrue(File.Exists(exportPath), "Export file should be created");
        
        var content = File.ReadAllText(exportPath);
        Assert.IsTrue(content.StartsWith("Time,"), "Should start with time header");
        
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual(51, lines.Length); // Header + 50 data rows
        
        Assert.IsTrue(content.Contains("TestDevice:TEST001:Channel 1"), "Should contain expected channel names");
    }

    [TestMethod]
    public void OptimizedExporter_RelativeTime_ProducesCorrectFormat()
    {
        var samples = GenerateTestDataset(2, 20);
        var loggingSession = new LoggingSession { ID = 1, DataSamples = samples };
        
        var exportPath = Path.Combine(TestDirectoryPath, "relative_time_test.csv");
        var bw = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };

        var exporter = new OptimizedLoggingSessionExporter();
        exporter.ExportLoggingSession(loggingSession, exportPath, true, bw, 0, 1);

        var content = File.ReadAllText(exportPath);
        Assert.IsTrue(content.StartsWith("Relative Time (s),"), "Should start with relative time header");
        Assert.IsTrue(content.Contains("0.000,"), "Should contain relative time starting from 0");
    }

    [TestMethod]
    public void OptimizedExporter_LargeDataset_PerformsWell()
    {
        var samples = GenerateTestDataset(8, 2000); // 16,000 samples
        var loggingSession = new LoggingSession { ID = 1, DataSamples = samples };
        
        var exportPath = Path.Combine(TestDirectoryPath, "large_test.csv");
        var bw = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };

        var stopwatch = Stopwatch.StartNew();
        var exporter = new OptimizedLoggingSessionExporter();
        exporter.ExportLoggingSession(loggingSession, exportPath, false, bw, 0, 1);
        stopwatch.Stop();

        Assert.IsTrue(File.Exists(exportPath), "Export file should be created");
        
        var lines = File.ReadAllLines(exportPath);
        Assert.AreEqual(2001, lines.Length); // Header + 2000 data rows
        
        Console.WriteLine($"Large dataset export took: {stopwatch.ElapsedMilliseconds}ms");
        
        // Should be reasonably fast for this size
        Assert.IsTrue(stopwatch.ElapsedMilliseconds < 5000, 
            $"Large dataset export should complete in under 5 seconds. Actual: {stopwatch.ElapsedMilliseconds}ms");
    }

    [TestMethod]
    public void OptimizedExporter_NonUniformData_HandlesCorrectly()
    {
        var samples = GenerateNonUniformTestDataset();
        var loggingSession = new LoggingSession { ID = 1, DataSamples = samples };
        
        var exportPath = Path.Combine(TestDirectoryPath, "nonuniform_test.csv");
        var bw = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };

        var exporter = new OptimizedLoggingSessionExporter();
        exporter.ExportLoggingSession(loggingSession, exportPath, false, bw, 0, 1);

        var content = File.ReadAllText(exportPath);
        
        Assert.IsTrue(File.Exists(exportPath), "Export file should be created");
        Assert.IsTrue(content.Contains(",,"), "Should contain empty cells for missing data");
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

    [TestCleanup]
    public void CleanUp()
    {
        if (Directory.Exists(TestDirectoryPath))
        {
            Directory.Delete(TestDirectoryPath, true);
        }
    }
}