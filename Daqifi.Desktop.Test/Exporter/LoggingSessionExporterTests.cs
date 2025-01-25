using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Exporter;
using Daqifi.Desktop.Logger;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace Daqifi.Desktop.Test.Exporter
{
    [TestClass]
    public class LoggingSessionExporterTests
    {
        private const string ExportFileName = "testExportFile.csv";
        private const string TestDirectoryPath = @"C:\ProgramData\DAQifi\Tests";

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
            var loggingSession = new LoggingSession()
            {
                ID = 1,
                Channels = _channels,
                DataSamples = _dataSamples
            };

            var exporter = new LoggingSessionExporter();
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

            var loggingSession = new LoggingSession()
            {
                ID = 1,
                Channels = _channels,
                DataSamples = _dataSamples
            };


            var exporter = new LoggingSessionExporter();
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
            var loggingSession = new LoggingSession()
            {
                ID = 1,
                Channels = _channels,
                DataSamples = new List<DataSample>()
            };

            var exporter = new LoggingSessionExporter();
            var exportFilePath = Path.Combine(TestDirectoryPath, ExportFileName);

            var bw = new BackgroundWorker
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };
            
            exporter.ExportLoggingSession(loggingSession, exportFilePath, false, bw, 0, 0);

            Assert.IsFalse(File.Exists(exportFilePath));
        }

        [TestCleanup]
        public void CleanUp()
        {
            var exportFilePath = Path.Combine(TestDirectoryPath, ExportFileName);

            if (File.Exists(exportFilePath))
            {
                File.Delete(exportFilePath);
            }
        }
    }
}
