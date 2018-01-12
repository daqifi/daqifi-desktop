﻿using System.Runtime.Remoting.Messaging;
using System.Text;

namespace Daqifi.Desktop.Message.MessageTypes
{
    public static class ScpiMessagePoducer
    {
        //{.pattern = "SYSTem:ERRor?", .callback = SCPI_SystemErrorNextQ,},
        //{.pattern = "SYSTem:ERRor:NEXT?", .callback = SCPI_SystemErrorNextQ,},
        //{.pattern = "SYSTem:ERRor:COUNt?", .callback = SCPI_SystemErrorCountQ,},
        //{.pattern = "SYSTem:VERSion?", .callback = SCPI_SystemVersionQ,},
        //{.pattern = "STATus:QUEStionable?", .callback = SCPI_StatusQuestionableEventQ,},
        //{.pattern = "STATus:QUEStionable:EVENt?", .callback = SCPI_StatusQuestionableEventQ,},
        //{.pattern = "STATus:QUEStionable:ENABle", .callback = SCPI_StatusQuestionableEnable,},
        //{.pattern = "STATus:QUEStionable:ENABle?", .callback = SCPI_StatusQuestionableEnableQ,},
        //{.pattern = "STATus:PRESet", .callback = SCPI_StatusPreset,},

        //// Intentionally(?) not implemented (stubbed out in original firmware))
        //{.pattern = "STATus:OPERation?", .callback = SCPI_NotImplemented, },
        //{.pattern = "STATus:OPERation:EVENt?", .callback = SCPI_NotImplemented, },
        //{.pattern = "STATus:OPERation:CONDition?", .callback = SCPI_NotImplemented, },
        //{.pattern = "STATus:OPERation:ENABle", .callback = SCPI_NotImplemented, },
        //{.pattern = "STATus:OPERation:ENABle?", .callback = SCPI_NotImplemented, },
        //{.pattern = "STATus:QUEStionable:CONDition?", .callback = SCPI_NotImplemented, },
        //{.pattern = "SYSTem:COMMunication:TCPIP:CONTROL?", .callback = SCPI_NotImplemented, },

        //// Power
        //{.pattern = "SYSTem:BAT:STAT?", .callback = SCPI_BatteryStatusGet, },
        //{.pattern = "SYSTem:BAT:LEVel?", .callback = SCPI_BatteryLevelGet, },
        //{.pattern = "SYSTem:POWer:STATe?", .callback = SCPI_GetPowerState, },
        //{.pattern = "SYSTem:POWer:STATe", .callback = SCPI_SetPowerState, },

        //// DIO
        //{.pattern = "DIO:PORt:DIRection?", .callback = SCPI_GPIODirectionGet, },
        //{.pattern = "DIO:PORt:STATe?", .callback = SCPI_GPIOStateGet, },
        //{.pattern = "DIO:PORt:ENAble", .callback = SCPI_GPIOEnableSet, },
        //{.pattern = "DIO:PORt:ENAble?", .callback = SCPI_GPIOEnableGet, },

        //// Wifi
        //{.pattern = "SYSTem:COMMunicate:LAN:ENAbled?", .callback = SCPI_LANEnabledGet, },
        //{.pattern = "SYSTem:COMMunicate:LAN:ENAbled", .callback = SCPI_LANEnabledSet, },
        //{.pattern = "SYSTem:COMMunicate:LAN:NETType?", .callback = SCPI_LANNetTypeGet, },
        //{.pattern = "SYSTem:COMMunicate:LAN:NETType", .callback = SCPI_LANNetTypeSet, },
        //{.pattern = "SYSTem:COMMunicate:LAN:IPV6?", .callback = SCPI_LANIpv6Get, },
        //{.pattern = "SYSTem:COMMunicate:LAN:IPV6", .callback = SCPI_LANIpv6Set, },
        //{.pattern = "SYSTem:COMMunicate:LAN:ADDRess?", .callback = SCPI_LANAddrGet, },
        //{.pattern = "SYSTem:COMMunicate:LAN:ADDRess", .callback = SCPI_LANAddrSet, },
        //{.pattern = "SYSTem:COMMunicate:LAN:MASK?", .callback = SCPI_LANMaskGet, },
        //{.pattern = "SYSTem:COMMunicate:LAN:MASK", .callback = SCPI_LANMaskSet, },
        //{.pattern = "SYSTem:COMMunicate:LAN:GATEway?", .callback = SCPI_LANGatewayGet, },
        //{.pattern = "SYSTem:COMMunicate:LAN:GATEway", .callback = SCPI_LANGatewaySet, },
        //{.pattern = "SYSTem:COMMunicate:LAN:DNS1?", .callback = SCPI_LANDns1Get, },
        //{.pattern = "SYSTem:COMMunicate:LAN:DNS1", .callback = SCPI_LANDns1Set, },
        //{.pattern = "SYSTem:COMMunicate:LAN:DNS2?", .callback = SCPI_LANDns2Get, },
        //{.pattern = "SYSTem:COMMunicate:LAN:DNS2", .callback = SCPI_LANDns2Set, },
        //{.pattern = "SYSTem:COMMunicate:LAN:MAC?", .callback = SCPI_LANMacGet, },
        //{.pattern = "SYSTem:COMMunicate:LAN:MAC", .callback = SCPI_LANMacSet, },
        //{.pattern = "SYSTem:COMMunicate:LAN:CONnected?", .callback = SCPI_NotImplemented, },
        //{.pattern = "SYSTem:COMMunicate:LAN:HOST?", .callback = SCPI_LANHostnameGet, },
        //{.pattern = "SYSTem:COMMunicate:LAN:HOST", .callback = SCPI_LANHostnameSet, },
        //{.pattern = "SYSTem:COMMunicate:LAN:Ssid?", .callback = SCPI_LANSsidGet, },
        //{.pattern = "SYSTem:COMMunicate:LAN:SECurity?", .callback = SCPI_LANSecurityGet, },
        //{.pattern = "SYSTem:COMMunicate:LAN:PASSCHECK", .callback = SCPI_LANPasskeyCheck, },
        //{.pattern = "SYSTem:COMMunicate:LAN:DISPlay", .callback = SCPI_NotImplemented, },
        //{.pattern = "SYSTem:COMMunicate:LAN:APPLY", .callback = SCPI_LANSettingsApply, },
        //{.pattern = "SYSTem:COMMunicate:LAN:LOAD", .callback = SCPI_LANSettingsLoad, },
        //{.pattern = "SYSTem:COMMunicate:LAN:SAVE", .callback = SCPI_LANSettingsSave, },
        //{.pattern = "SYSTem:COMMunicate:LAN:FACRESET", .callback = SCPI_LANSettingsFactoryLoad, },
        //{.pattern = "SYSTem:COMMunicate:LAN:CLEAR", .callback = SCPI_LANSettingsClear, },

        //// ADC
        //{.pattern = "MEASure:VOLTage:DC?", .callback = SCPI_ADCVoltageGet, },
        //{.pattern = "ENAble:VOLTage:DC", .callback = SCPI_ADCChanEnableSet, },
        //{.pattern = "ENAble:VOLTage:DC?", .callback = SCPI_ADCChanEnableGet, },
        //{.pattern = "CONFigure:ADC:SINGleend?", .callback = SCPI_ADCChanSingleEndGet, },
        //{.pattern = "CONFigure:ADC:RANGe?", .callback = SCPI_ADCChanRangeGet, },
        //{.pattern = "CONFigure:ADC:CHANnel?", .callback = SCPI_NotImplemented, },

        //// SPI
        //{.pattern = "OUTPut:SPI:WRIte", .callback = SCPI_NotImplemented, },

        //// Streaming
        //{.pattern = "SYSTem:StreamData?", .callback = SCPI_IsStreaming, },

        //{.pattern = "SYSTem:STReam:FORmat", .callback = SCPI_SetStreamFormat, }, // 0 = pb = default, 1 = text (json)
        //{.pattern = "SYSTem:STReam:FORmat?", .callback = SCPI_GetStreamFormat, },

        //// Testing
        //{.pattern = "BENCHmark?", .callback = SCPI_NotImplemented, },

        //{.pattern = NULL, .callback = SCPI_NotImplemented, },

        #region System Commands

        public static IMessage Reboot => new ScpiMessage("SYSTem:REboot");
        public static IMessage SystemInfo => new ScpiMessage("SYSTem:SYSInfoPB?");


        public static IMessage Echo(int echo)
        {
            return new ScpiMessage($"SYSTem:ECHO {echo}");
        }

        #endregion

        #region Power Commands

        public static IMessage BatteryLevel => new ScpiMessage("SYSTem:BAT:LEVel?");

        #endregion

        #region Streaming Commands

        public static IMessage StartStreaming(int frequency)
        {
            return new ScpiMessage($"SYSTem:StartStreamData {frequency}");
        }

        public static IMessage StopStreaming => new ScpiMessage("SYSTem:StopStreamData");

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

        public static IMessage ConfigureAdcChannels(string channelSetString)
        {
            return new ScpiMessage($"CONFigure:ADC:CHANnel {channelSetString}");
        }

        public static IMessage SetVoltageLevel(int channel, double value)
        {
            return new ScpiMessage($"SOURce:VOLTage:LEVel {channel},{value}");
        }

        #endregion

        #region DIO

        public static IMessage SetPortDirection(int channel, int direction)
        {
            return new ScpiMessage($"DIO:PORt:DIRection {channel},{direction}");
        }

        public static IMessage SetPortState(int channel, double value)
        {
            return new ScpiMessage($"DIO:PORt:STATe {channel},{value}");
        }

        #endregion

        #region Communication
        public static IMessage SetSsid(string ssid)
        {
            return new ScpiMessage($"SYSTem:COMMunicate:LAN:Ssid {ssid}");
        }

        public static IMessage SetSecurity(int security)
        {
            return new ScpiMessage($"SSYSTem:COMMunicate:LAN:SECurity {security}");
        }

        public static IMessage SetPassword(string password)
        {
            return new ScpiMessage($"SYSTem:COMMunicate:LAN:PASs {password}");
        }
        #endregion
    }
}