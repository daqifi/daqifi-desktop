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
using System.Text;

namespace Daqifi.Desktop.Test.Exporter;

[TestClass]
public class OptimizedLoggingSessionExporterTests
{
    private const string ExportFileName = "testExportFile.csv";
    private static readonly string TestDirectoryPath = Path.Combine(Path.GetTempPath(), "DAQifi", "Tests");

    private readonly DateTime _firstTime = new(2018, 2, 9, 1, 3, 30);
    private readonly DateTime _secondTime = new(2018, 2, 9, 1, 3, 31);

    private List<Channel.Channel> _channels;
    private List<DataSample> _dataSamples;

    [TestInitialize]
    public void Initialize()
    {
        Directory.CreateDirectory(TestDirectoryPath);

        _channels =
        [
            new Channel.Channel { ID = 1, Name = "Channel 1" },
            new Channel.Channel { ID = 2, Name = "Channel 2" },
            new Channel.Channel { ID = 2, Name = "Channel 3" }
        ];

        _dataSamples =
        [
            new DataSample
            {
                ID = 1, DeviceName = "device1", DeviceSerialNo = "123", LoggingSessionID = 1,
                ChannelName = "Channel 1", TimestampTicks = _firstTime.Ticks, Value = 0.01
            },
            new DataSample
            {
                ID = 2, DeviceName = "device1", DeviceSerialNo = "123", LoggingSessionID = 1,
                ChannelName = "Channel 2", TimestampTicks = _firstTime.Ticks, Value = 0.02
            },
            new DataSample
            {
                ID = 3, DeviceName = "device1", DeviceSerialNo = "123", LoggingSessionID = 1,
                ChannelName = "Channel 3", TimestampTicks = _firstTime.Ticks, Value = 0.03
            },
            new DataSample
            {
                ID = 4, DeviceName = "device1", DeviceSerialNo = "123", LoggingSessionID = 1,
                ChannelName = "Channel 1", TimestampTicks = _secondTime.Ticks, Value = 0.04
            },
            new DataSample
            {
                ID = 5, DeviceName = "device1", DeviceSerialNo = "123", LoggingSessionID = 1,
                ChannelName = "Channel 2", TimestampTicks = _secondTime.Ticks, Value = 0.05
            },
            new DataSample
            {
                ID = 6, DeviceName = "device1", DeviceSerialNo = "123", LoggingSessionID = 1,
                ChannelName = "Channel 3", TimestampTicks = _secondTime.Ticks, Value = 0.06
            }
        ];
    }

    [TestMethod]
    public void ExportLoggingSession_ValidInput_ExportsCorrectOutput()
    {
        var loggingSession = new LoggingSession
        {
            ID = 1,
            Channels = _channels,
            DataSamples = _dataSamples
        };

        var exporter = new OptimizedLoggingSessionExporter();
        var exportFilePath = Path.Combine(TestDirectoryPath, ExportFileName);
            
        var bw = new BackgroundWorker
        {
            WorkerReportsProgress = true,
            WorkerSupportsCancellation = true
        };

        exporter.ExportLoggingSession(loggingSession, exportFilePath, false, bw, 0, 0);

        var expectedOutput = "Time,device1:123:Channel 1,device1:123:Channel 2,device1:123:Channel 3\r\n";
        expectedOutput += "2018-02-09T01:03:30.0000000,0.01,0.02,0.03\r\n";
        expectedOutput += "2018-02-09T01:03:31.0000000,0.04,0.05,0.06\r\n";

        var actualOutput = File.ReadAllText(exportFilePath);

        Assert.AreEqual(expectedOutput, actualOutput, false);
    }

    [TestMethod]
    public void ExportLoggingSession_NonUniformData_ExportsCorrectOutput()
    {
        _dataSamples.Remove(_dataSamples.First(d => d.ID == 5));

        var loggingSession = new LoggingSession
        {
            ID = 1,
            Channels = _channels,
            DataSamples = _dataSamples
        };


        var exporter = new OptimizedLoggingSessionExporter();
        var exportFilePath = Path.Combine(TestDirectoryPath, ExportFileName);

        var bw = new BackgroundWorker
        {
            WorkerReportsProgress = true,
            WorkerSupportsCancellation = true
        };

        exporter.ExportLoggingSession(loggingSession, exportFilePath, false, bw, 0, 0);

        var expectedOutput = "Time,device1:123:Channel 1,device1:123:Channel 2,device1:123:Channel 3\r\n";
        expectedOutput += "2018-02-09T01:03:30.0000000,0.01,0.02,0.03\r\n";
        expectedOutput += "2018-02-09T01:03:31.0000000,0.04,,0.06\r\n";

        var actualOutput = File.ReadAllText(exportFilePath);
        Assert.AreEqual(expectedOutput, actualOutput, false);
    }

    [TestMethod]
    public void ExportLoggingSession_NoSamples_NoFileIsExported()
    {
        var loggingSession = new LoggingSession
        {
            ID = 1,
            Channels = _channels,
            DataSamples = new List<DataSample>()
        };

        var exporter = new OptimizedLoggingSessionExporter();
        var exportFilePath = Path.Combine(TestDirectoryPath, ExportFileName);

        var bw = new BackgroundWorker
        {
            WorkerReportsProgress = true,
            WorkerSupportsCancellation = true
        };
            
        exporter.ExportLoggingSession(loggingSession, exportFilePath, false, bw, 0, 0);

        Assert.IsFalse(File.Exists(exportFilePath));
    }

    [TestMethod]
    public void ExportLoggingSession_RelativeTime_ExportsCorrectOutput()
    {
        var loggingSession = new LoggingSession
        {
            ID = 1,
            Channels = _channels,
            DataSamples = _dataSamples
        };

        var exporter = new OptimizedLoggingSessionExporter();
        var exportFilePath = Path.Combine(TestDirectoryPath, "relative_" + ExportFileName);
            
        var bw = new BackgroundWorker
        {
            WorkerReportsProgress = true,
            WorkerSupportsCancellation = true
        };

        exporter.ExportLoggingSession(loggingSession, exportFilePath, true, bw, 0, 0);

        var expectedOutput = "Relative Time (s),device1:123:Channel 1,device1:123:Channel 2,device1:123:Channel 3\r\n";
        expectedOutput += "0.000,0.01,0.02,0.03\r\n";
        expectedOutput += "1.000,0.04,0.05,0.06\r\n";

        var actualOutput = File.ReadAllText(exportFilePath);

        Assert.AreEqual(expectedOutput, actualOutput, false);
    }

    [TestMethod]
    public void ExportLoggingSession_LargeDataset_ExportsCorrectOutput()
    {
        // Create a larger dataset: 4 channels, 1000 samples (4000 total samples)
        var largeDataSamples = GenerateLargeDataset(4, 1000);
        
        var loggingSession = new LoggingSession
        {
            ID = 1,
            Channels = _channels,
            DataSamples = largeDataSamples
        };

        var exporter = new OptimizedLoggingSessionExporter();
        var exportFilePath = Path.Combine(TestDirectoryPath, "large_" + ExportFileName);
            
        var bw = new BackgroundWorker
        {
            WorkerReportsProgress = true,
            WorkerSupportsCancellation = true
        };

        exporter.ExportLoggingSession(loggingSession, exportFilePath, false, bw, 0, 0);

        // Verify file exists and has correct structure
        Assert.IsTrue(File.Exists(exportFilePath));
        
        var lines = File.ReadAllLines(exportFilePath);
        Assert.AreEqual(1001, lines.Length); // Header + 1000 data rows
        
        // Verify header
        Assert.IsTrue(lines[0].StartsWith("Time,"));
        
        // Verify first data row format
        var firstDataRow = lines[1].Split(',');
        Assert.AreEqual(5, firstDataRow.Length); // Time + 4 channels
    }

    [TestMethod]
    public void ExportLoggingSession_PerformanceTest_MeasuresExecutionTime()
    {
        // Create medium dataset: 8 channels, 5000 samples (40,000 total samples)
        var performanceDataSamples = GenerateLargeDataset(8, 5000);
        
        var loggingSession = new LoggingSession
        {
            ID = 1,
            Channels = _channels,
            DataSamples = performanceDataSamples
        };

        var exporter = new OptimizedLoggingSessionExporter();
        var exportFilePath = Path.Combine(TestDirectoryPath, "performance_" + ExportFileName);
            
        var bw = new BackgroundWorker
        {
            WorkerReportsProgress = true,
            WorkerSupportsCancellation = true
        };

        // Measure execution time and memory
        var initialMemory = GC.GetTotalMemory(true);
        var stopwatch = Stopwatch.StartNew();
        
        exporter.ExportLoggingSession(loggingSession, exportFilePath, false, bw, 0, 0);
        
        stopwatch.Stop();
        var finalMemory = GC.GetTotalMemory(false);
        var memoryUsed = finalMemory - initialMemory;

        // Performance assertions - these will fail with current implementation
        // showing the performance problems
        Console.WriteLine($"Export time: {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"Memory used: {memoryUsed / 1024 / 1024}MB");
        Console.WriteLine($"Samples per second: {40000.0 / stopwatch.Elapsed.TotalSeconds:F0}");
        
        // Verify output correctness
        Assert.IsTrue(File.Exists(exportFilePath));
        var lines = File.ReadAllLines(exportFilePath);
        Assert.AreEqual(5001, lines.Length); // Header + 5000 data rows
    }

    [TestMethod]
    public void ExportLoggingSession_MultipleDevices_ExportsCorrectOutput()
    {
        var multiDeviceDataSamples = new List<DataSample>
        {
            new DataSample
            {
                ID = 1, DeviceName = "device1", DeviceSerialNo = "123", LoggingSessionID = 1,
                ChannelName = "Channel 1", TimestampTicks = _firstTime.Ticks, Value = 0.01
            },
            new DataSample
            {
                ID = 2, DeviceName = "device2", DeviceSerialNo = "456", LoggingSessionID = 1,
                ChannelName = "Channel 1", TimestampTicks = _firstTime.Ticks, Value = 0.02
            },
            new DataSample
            {
                ID = 3, DeviceName = "device1", DeviceSerialNo = "123", LoggingSessionID = 1,
                ChannelName = "Channel 2", TimestampTicks = _firstTime.Ticks, Value = 0.03
            }
        };
        
        var loggingSession = new LoggingSession
        {
            ID = 1,
            Channels = _channels,
            DataSamples = multiDeviceDataSamples
        };

        var exporter = new OptimizedLoggingSessionExporter();
        var exportFilePath = Path.Combine(TestDirectoryPath, "multidevice_" + ExportFileName);
            
        var bw = new BackgroundWorker
        {
            WorkerReportsProgress = true,
            WorkerSupportsCancellation = true
        };

        exporter.ExportLoggingSession(loggingSession, exportFilePath, false, bw, 0, 0);

        var actualOutput = File.ReadAllText(exportFilePath);
        
        // Verify channels are sorted properly
        Assert.IsTrue(actualOutput.Contains("device1:123:Channel 1"));
        Assert.IsTrue(actualOutput.Contains("device1:123:Channel 2"));
        Assert.IsTrue(actualOutput.Contains("device2:456:Channel 1"));
        
        var lines = File.ReadAllLines(exportFilePath);
        Assert.AreEqual(2, lines.Length); // Header + 1 data row
    }

    private List<DataSample> GenerateLargeDataset(int channelCount, int samplesPerChannel)
    {
        var samples = new List<DataSample>();
        var baseTime = new DateTime(2018, 2, 9, 1, 0, 0);
        
        for (int timeStep = 0; timeStep < samplesPerChannel; timeStep++)
        {
            var timestamp = baseTime.AddSeconds(timeStep);
            
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
                    Value = Math.Sin(timeStep * 0.1) * channel + channel
                });
            }
        }
        
        return samples;
    }

    [TestCleanup]
    public void CleanUp()
    {
        var testFiles = new[]
        {
            ExportFileName,
            "relative_" + ExportFileName,
            "large_" + ExportFileName,
            "performance_" + ExportFileName,
            "multidevice_" + ExportFileName
        };

        foreach (var fileName in testFiles)
        {
            var exportFilePath = Path.Combine(TestDirectoryPath, fileName);
            if (File.Exists(exportFilePath))
            {
                File.Delete(exportFilePath);
            }
        }
    }
}