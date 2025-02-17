using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text;
using Daqifi.Desktop.DataModel.Network;
using Daqifi.Desktop.IO.Messages.Producers;

namespace Daqifi.Desktop.IO.Test.Messages.Producers
{
    [TestClass]
    public class ScipMessageProducerTests
    {
        #region System Commands
        [TestMethod]
        public void RebootCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessageProducer.Reboot;
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:REboot";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void SystemInfoCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessageProducer.SystemInfo;
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:SYSInfoPB?";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void EchoCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessageProducer.Echo(1);
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:ECHO 1";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }
        #endregion

        #region Power Commands
        [TestMethod]
        public void BatteryLevelCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessageProducer.BatteryLevel;
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:BAT:LEVel?";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void DeviceOnCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessageProducer.DeviceOn;
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:POWer:STATe 1";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }
        #endregion

        #region Streaming Commands
        [TestMethod]
        public void StartStreamingCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessageProducer.StartStreaming(100);
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:StartStreamData 100";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void StopStreamingCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessageProducer.StopStreaming;
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:StopStreamData";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void SetProtobufStreamFormatCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessageProducer.SetProtobufStreamFormat;
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:STReam:FORmat 0";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void SetJsonStreamFormatCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessageProducer.SetJsonStreamFormat;
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:STReam:FORmat 1";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void GetStreamFormatCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessageProducer.GetStreamFormat;
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:STReam:FORmat?";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }
        #endregion

        #region ADC Commands
        [TestMethod]
        public void ConfigureAdcModeCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessageProducer.ConfigureAdcMode(5,1);
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "CONFigure:ADC:SINGleend 5,1";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void ConfigureAdcRangeCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessageProducer.ConfigureAdcRange(10);
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "CONFigure:ADC:RANGe 10";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void EnableAdcChannelsCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessageProducer.EnableAdcChannels("0001010100");
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "ENAble:VOLTage:DC 0001010100";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void SetVoltageLevelCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessageProducer.SetVoltageLevel(7,5);
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SOURce:VOLTage:LEVel 7,5";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }
        #endregion

        #region DIO Commands
        [TestMethod]
        public void SetDioPortDirectionCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessageProducer.SetDioPortDirection(8, 1);
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "DIO:PORt:DIRection 8,1";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void SetDioPortStateCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessageProducer.SetDioPortState(9, 0);
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "DIO:PORt:STATe 9,0";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void EnableDioPortsCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessageProducer.EnableDioPorts();
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "DIO:PORt:ENAble 1";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void DisableDioPortsCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessageProducer.DisableDioPorts();
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "DIO:PORt:ENAble 0";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }
        #endregion

        #region Communication Commands
        [TestMethod]
        public void SetWifiModeortsCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessageProducer.SetWifiMode(WifiMode.ExistingNetwork);
            var actualCommandRawData = actualCommand.GetBytes();
            var expectedCommandText = $"SYSTem:COMMunicate:LAN:NETType {(int)WifiMode.ExistingNetwork}";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void SetSsidCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessageProducer.SetSsid("My Network");
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:COMMunicate:LAN:SSID \"My Network\"";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void SetSecurityCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessageProducer.SetSecurity(WifiSecurityType.WpaPskPhrase);
            var actualCommandRawData = actualCommand.GetBytes();
            var expectedCommandText = $"SYSTem:COMMunicate:LAN:SECurity {(int)WifiSecurityType.WpaPskPhrase}";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void SetPasswordCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessageProducer.SetPassword("super secret password");
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:COMMunicate:LAN:PASs \"super secret password\"";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void ApplyLanPortsCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessageProducer.ApplyLan;
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:COMMunicate:LAN:APPLY";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void SaveLanPortsCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessageProducer.SaveLan;
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:COMMunicate:LAN:SAVE";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }
        #endregion

        #region SD Card Logging Commands
        [TestMethod]
        public void EnableSdLoggingCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessageProducer.EnableSdLogging;
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:STORage:SD:LOGging 1";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void DisableSdLoggingCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessageProducer.DisableSdLogging;
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:STORage:SD:LOGging 0";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void GetSdLoggingStateCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessageProducer.GetSdLoggingState;
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:STORage:SD:LOGging?";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void GetSdFileListCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessageProducer.GetSdFileList;
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:STORage:SD:LIST?";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void GetSdFileCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessageProducer.GetSdFile;
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:STORage:SD:GET";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }
        #endregion

        public byte[] GetBytes(string data)
        {
            return Encoding.ASCII.GetBytes(data + "\r\n");
        }
    }
}
