using Daqifi.Desktop.DataModel.Network;
using Daqifi.Desktop.IO.Messages.MessageTypes;

namespace Daqifi.Desktop.IO.Messages.Producers
{
    public static class ScpiMessageProducer
    {
        #region System Commands

        public static IMessage Reboot => new ScpiMessage("SYSTem:REboot");
        public static IMessage SystemInfo => new ScpiMessage("SYSTem:SYSInfoPB?");

        public static IMessage ForceBootloader => new ScpiMessage("SYSTem:FORceBoot");

        public static IMessage Echo(int echo)
        {
            return new ScpiMessage($"SYSTem:ECHO {echo}");
        }

        #endregion

        #region SD Card Logging Commands

        public static IMessage EnableSdCard => new ScpiMessage("SYSTem:STORage:SD:ENAble 1");
        public static IMessage DisableSdCard => new ScpiMessage("SYSTem:STORage:SD:ENAble 0");
        public static IMessage GetSdLoggingState => new ScpiMessage("SYSTem:STORage:SD:LOGging?");
        public static IMessage GetSdFileList => new ScpiMessage("SYSTem:STORage:SD:LIST?");
        
        public static IMessage GetSdFile(string fileName)
        {
            return new ScpiMessage($"SYSTem:STORage:SD:GET \"{fileName}\"");
        }
        
        public static IMessage SetSdLoggingFileName(string fileName)
        {
            return new ScpiMessage($"SYSTem:STORage:SD:LOGging \"{fileName}\"");
        }

        #endregion

        #region Power Commands

        public static IMessage BatteryLevel => new ScpiMessage("SYSTem:BAT:LEVel?");

        public static IMessage DeviceOn => new ScpiMessage("SYSTem:POWer:STATe 1");
        #endregion

        #region Streaming Commands

        public static IMessage StartStreaming(int frequency)
        {
            return new ScpiMessage($"SYSTem:StartStreamData {frequency}");
        }

        public static IMessage StopStreaming => new ScpiMessage("SYSTem:StopStreamData");

        public static IMessage SetProtobufStreamFormat => new ScpiMessage("SYSTem:STReam:FORmat 0");

        public static IMessage SetJsonStreamFormat => new ScpiMessage("SYSTem:STReam:FORmat 1");

        public static IMessage GetStreamFormat => new ScpiMessage("SYSTem:STReam:FORmat?");

        #endregion

        #region ADC Commands
        public static IMessage ConfigureAdcMode(int channel, int direction)
        {
            return new ScpiMessage($"CONFigure:ADC:SINGleend {channel},{direction}");
        }

        public static IMessage ConfigureAdcRange(int range)
        {
            return new ScpiMessage($"CONFigure:ADC:RANGe {range}");
        }

        public static IMessage EnableAdcChannels(string channelSetString)
        {
            return new ScpiMessage($"ENAble:VOLTage:DC {channelSetString}");
        }

        public static IMessage SetVoltageLevel(int channel, double value)
        {
            return new ScpiMessage($"SOURce:VOLTage:LEVel {channel},{value}");
        }

        #endregion

        #region DIO

        public static IMessage SetDioPortDirection(int channel, int direction)
        {
            return new ScpiMessage($"DIO:PORt:DIRection {channel},{direction}");
        }

        public static IMessage SetDioPortState(int channel, double value)
        {
            return new ScpiMessage($"DIO:PORt:STATe {channel},{value}");
        }

        public static IMessage EnableDioPorts()
        {
            return new ScpiMessage("DIO:PORt:ENAble 1");
        }

        public static IMessage DisableDioPorts()
        {
            return new ScpiMessage("DIO:PORt:ENAble 0");
        }

        #endregion

        #region Communication
        public static IMessage SetWifiMode(WifiMode wifiMode)
        {
            return new ScpiMessage($"SYSTem:COMMunicate:LAN:NETType {(int)wifiMode}");
        }

        public static IMessage SetSsid(string ssid)
        {
            return new ScpiMessage($"SYSTem:COMMunicate:LAN:SSID \"{ssid}\"");
        }

        public static IMessage SetSecurity(WifiSecurityType wifiSecurityType)
        {
            return new ScpiMessage($"SYSTem:COMMunicate:LAN:SECurity {(int)wifiSecurityType}");
        }

        public static IMessage SetPassword(string password)
        {
            return new ScpiMessage($"SYSTem:COMMunicate:LAN:PASs \"{password}\"");
        }
        
        public static IMessage DisableLan => new ScpiMessage("SYSTem:COMMunicate:LAN:ENAbled 0");
        
        public static IMessage EnableLan => new ScpiMessage("SYSTem:COMMunicate:LAN:ENAbled 1");
        
        public static IMessage ApplyLan => new ScpiMessage("SYSTem:COMMunicate:LAN:APPLY");

        public static IMessage SaveLan => new ScpiMessage("SYSTem:COMMunicate:LAN:SAVE");
        
        public static IMessage SetLanFWUpdateMode => new ScpiMessage("SYSTem:COMMUnicate:LAN:FWUpdate");
        
        public static IMessage GetWiFiModuleVersion => new ScpiMessage("SYSTem:COMMunicate:LAN:GETChipINFO");
        
        #endregion
        
        #region USB
        public static IMessage SetUsbTransparencyMode(int mode)
        {
            return new ScpiMessage($"SYSTem:USB:SetTransparentMode {mode}");
        }
        #endregion
    }
}