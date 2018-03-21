using System;
using System.Collections.Generic;
using System.IO;
using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Exporter;
using Daqifi.Desktop.Logger;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daqifi.Desktop.Test.Exporter
{
    [TestClass]
    public class LoggingSessionExporterTests
    {
        private const string ExporFilePath = "C:\\ProgramData\\DAQifi\\Tests\\exportfile.csv";
        private DateTime _firstTime = new DateTime(2018,2,9,1,3,30);
        private DateTime _secondTime = new DateTime(2018, 2, 9, 1, 3, 31);

        [TestMethod]
        public void ExportLoggingSession_ValidInput_CorrectOutput()
        {
            var loggingSession = new LoggingSession(1)
            {
                ID = 1,
                Channels = new List<IChannel>()
                {
                    new DigitalChannel(){ID = 1, Name = "Channel 1"},
                    new DigitalChannel(){ID = 2, Name = "Channel 2"},
                },
                DataSamples = new List<DataSample>()
                {
                    new DataSample() {ID = 1, LoggingSessionID = 1, ChannelName = "Channel 1", TimestampTicks = _firstTime.Ticks ,Value = 0.01},
                    new DataSample() {ID = 2, LoggingSessionID = 1, ChannelName = "Channel 2", TimestampTicks = _firstTime.Ticks, Value = 0.02},
                    new DataSample() {ID = 3, LoggingSessionID = 1, ChannelName = "Channel 1", TimestampTicks = _secondTime.Ticks, Value = 0.03},
                    new DataSample() {ID = 4, LoggingSessionID = 1, ChannelName = "Channel 2", TimestampTicks = _secondTime.Ticks, Value = 0.04}
                }
            };

            var exporter = new LoggingSessionExporter();
            
            exporter.ExportLoggingSession(loggingSession, ExporFilePath);

            var expectedOutput = "time,Channel 1,Channel 2\n";
            expectedOutput += "2/09/2018 1:02:30 AM,0.01,0.02";
            expectedOutput += "2/09/2018 1:02:31 AM,0.03,0.04";

            var actualOutput = File.ReadAllText(ExporFilePath);

            Assert.AreEqual(expectedOutput, actualOutput, false);
        }
    }
}
