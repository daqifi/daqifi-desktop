using Daqifi.Desktop.DataModel.Network;
using Daqifi.Desktop.IO.Messages.Producers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text;

namespace Daqifi.Desktop.IO.Test.Messages.Producers
{
    [TestClass]
    public class ScipMessageProducerTests
    {
        #region System Commands
        [TestMethod]
        public void RebootCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessagePoducer.Reboot;
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:REboot";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void SystemInfoCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessagePoducer.SystemInfo;
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:SYSInfoPB?";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void EchoCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessagePoducer.Echo(1);
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
            var actualCommand = ScpiMessagePoducer.BatteryLevel;
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:BAT:LEVel?";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void DeviceOnCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessagePoducer.DeviceOn;
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
            var actualCommand = ScpiMessagePoducer.StartStreaming(100);
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:StartStreamData 100";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void StopStreamingCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessagePoducer.StopStreaming;
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:StopStreamData";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void SetProtobufStreamFormatCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessagePoducer.SetProtobufStreamFormat;
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:STReam:FORmat 0";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void SetJsonStreamFormatCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessagePoducer.SetJsonStreamFormat;
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:STReam:FORmat 1";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        #endregion

        #region ADC Commands
        [TestMethod]
        public void ConfigureAdcModeCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessagePoducer.ConfigureAdcMode(5,1);
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "CONFigure:ADC:SINGleend 5,1";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void ConfigureAdcRangeCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessagePoducer.ConfigureAdcRange(10);
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "CONFigure:ADC:RANGe 10";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void EnableAdcChannelsCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessagePoducer.EnableAdcChannels("0001010100");
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "ENAble:VOLTage:DC 0001010100";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void SetVoltageLevelCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessagePoducer.SetVoltageLevel(7,5);
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
            var actualCommand = ScpiMessagePoducer.SetDioPortDirection(8, 1);
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "DIO:PORt:DIRection 8,1";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void SetDioPortStateCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessagePoducer.SetDioPortState(9, 0);
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "DIO:PORt:STATe 9,0";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void EnableDioPortsCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessagePoducer.EnableDioPorts();
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "DIO:PORt:ENAble 1";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void DisableDioPortsCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessagePoducer.DisableDioPorts();
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
            var actualCommand = ScpiMessagePoducer.SetWifiMode(WifiMode.ExistingNetwork);
            var actualCommandRawData = actualCommand.GetBytes();
            var expectedCommandText = $"SYSTem:COMMunicate:LAN:NETType {(int)WifiMode.ExistingNetwork}";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void SetSsidCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessagePoducer.SetSsid("My Network");
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:COMMunicate:LAN:SSID \"My Network\"";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void SetSecurityCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessagePoducer.SetSecurity(WifiSecurityType.WpaPskKey);
            var actualCommandRawData = actualCommand.GetBytes();
            var expectedCommandText = $"SYSTem:COMMunicate:LAN:SECurity {(int)WifiSecurityType.WpaPskKey}";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void SetPasswordCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessagePoducer.SetPassword("super secret password");
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:COMMunicate:LAN:PASs \"super secret password\"";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void ApplyLanPortsCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessagePoducer.ApplyLan();
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:COMMunicate:LAN:APPLY";
            var expectedCommandRawData = GetBytes(expectedCommandText);

            CollectionAssert.AreEqual(expectedCommandRawData, actualCommandRawData);
        }

        [TestMethod]
        public void SaveLanPortsCommand_ReturnsCorrectCommand()
        {
            var actualCommand = ScpiMessagePoducer.SaveLan();
            var actualCommandRawData = actualCommand.GetBytes();
            const string expectedCommandText = "SYSTem:COMMunicate:LAN:SAVE";
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
