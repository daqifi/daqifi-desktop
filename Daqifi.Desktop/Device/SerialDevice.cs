﻿using Daqifi.Desktop.Message;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using Daqifi.Desktop.Channel;

namespace Daqifi.Desktop.Device
{
    public class SerialDevice : AbstractDevice
    {
        #region Private Data
        private readonly List<string> _securityTypes = new List<string> { "None (Open Network)", "WEP-40", "WEP-104", "WPA-PSK Phrase", "WPA-PSK Key" };
        private readonly SerialPort _port;
        #endregion

        #region Properties
        public SerialPort Port
        {
            get { return _port; }
        }
        #endregion

        #region Constructor
        public SerialDevice(string portName)
        {
            Name = portName;
            _port = new SerialPort(portName);

            DataChannels = new List<IChannel>();
        }
        #endregion

        #region Override Methods
        public override bool Connect()
        {
            try
            {
                Port.Open();
                MessageProducer = new MessageProducer(_port.BaseStream);
                StopStreaming();
                MessageConsumer = new MessageConsumer(_port.BaseStream);
                MessageConsumer.Start();
                InitializeDeviceState();
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        public override bool Disconnect()
        {
            try
            {
                MessageConsumer.Stop();
                StopStreaming();
                _port.Close();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public override void InitializeStreaming()
        {
            MessageProducer.SendAsync(new ScpiMessage("system:startstreamdata " + StreamingFrequency.ToString()));
            IsStreaming = true;
        }

        public override void StopStreaming()
        {
            IsStreaming = false;
            MessageProducer.SendAsync(new ScpiMessage("system:stopstreamdata"));
            _firstTime = null;
        }

        public override void InitializeDeviceState()
        {
            MessageConsumer.OnMessageReceived += StatusMessageReceived;
            MessageProducer.SendAsync(new ScpiMessage("SYSTem:SYSInfoPB? 0"));
        }

        public override void SetAdcMode(IChannel channel, AdcMode mode)
        {
            switch (mode)
            {
                case AdcMode.Differential:
                    MessageProducer.SendAsync(new ScpiMessage("CONFigure:ADC:SINGleend " + channel.Index + "," + 0));
                    break;
                case AdcMode.SingleEnded:
                    MessageProducer.SendAsync(new ScpiMessage("CONFigure:ADC:SINGleend " + channel.Index + "," + 1));
                    break;
            }
        }

        public override void SetAdcRange(int range)
        {
            switch (range)
            {
                case 5:
                    MessageProducer.SendAsync(new ScpiMessage("CONFigure:ADC:RANGe " + 0));
                    _ADCRange = 0;
                    break;
                case 10:
                    MessageProducer.SendAsync(new ScpiMessage("CONFigure:ADC:RANGe " + 1));
                    _ADCRange = 1;
                    break;
            }
        }

        public override void AddChannel(IChannel newChannel)
        {
            switch (newChannel.Type)
            {
                case ChannelType.Analog:
                    var activeAnalogChannels = GetActiveChannels(ChannelType.Analog);
                    int channelSetByte = 0;

                    //Get Exsiting Channel Set Byte
                    foreach (IChannel activeChannel in activeAnalogChannels)
                    {
                        channelSetByte = channelSetByte | (1 << activeChannel.Index);
                    }

                    //Add Channel Bit to the Channel Set Byte
                    channelSetByte = channelSetByte | (1 << newChannel.Index);

                    //Convert to a string
                    string channelSetString = Convert.ToString(channelSetByte);

                    //Send the command to add the channel
                    MessageProducer.SendAsync(new ScpiMessage("configure:adc:channel " + channelSetString));
                    break;
            }
        }

        public List<IChannel> GetActiveChannels(ChannelType channelType)
        {
            switch (channelType)
            {
                case ChannelType.Analog:
                    return DataChannels.Where(channel => channel.Type == ChannelType.Analog && channel.IsActive).ToList();
            }
            throw new NotImplementedException();
        }

        public override void RemoveChannel(IChannel channelToRemove)
        {
            switch (channelToRemove.Type)
            {
                case ChannelType.Analog:
                    IList<IChannel> activeAnalogChannels = GetActiveChannels(ChannelType.Analog);
                    int channelSetByte = 0;

                    //Get Exsiting Channel Set Byte
                    foreach (var activeChannel in activeAnalogChannels)
                    {
                        channelSetByte = channelSetByte | (1 << activeChannel.Index);
                    }

                    //Add Channel Bit to the Channel Set Byte
                    channelSetByte = channelSetByte | (1 >> channelToRemove.Index);

                    //Convert to a string
                    string channelSetString = Convert.ToString(channelSetByte);

                    //Send the command to add the channel
                    MessageProducer.SendAsync(new ScpiMessage("configure:adc:channel " + channelSetString));

                    break;
            }
        }

        public override void UpdateNetworkConfiguration()
        {
            if (IsStreaming) StopStreaming();
            Thread.Sleep(100);
            MessageProducer.SendAsync(new ScpiMessage("system:communicate:lan:ssid " + NetworkConfiguration.SSID));
            Thread.Sleep(100);
            MessageProducer.SendAsync(new ScpiMessage("system:communicate:lan:security " + _securityTypes.IndexOf(NetworkConfiguration.SecurityType)));
            Thread.Sleep(100);
            MessageProducer.SendAsync(new ScpiMessage("system:communicate:lan:pass " + NetworkConfiguration.Password));
            Thread.Sleep(100);
            Reboot();
        }

        public override void UpdateFirmware(byte[] data)
        {
            
        }

        public override void SetChannelOutputValue(IChannel channel, double value)
        {
            switch (channel.Type)
            {
                case ChannelType.Analog:
                    MessageProducer.SendAsync(new ScpiMessage("SOURce:VOLTage:LEVel " + channel.Index + "," + value));
                    break;
                case ChannelType.Digital:
                    MessageProducer.SendAsync(new ScpiMessage("OUTPut:PORt:STATe " + channel.Index + "," + value));
                    break;
            }
        }

        public override void SetChannelDirection(IChannel channel, ChannelDirection direction)
        {
            switch (direction)
            {
                case ChannelDirection.Input:
                    MessageProducer.SendAsync(new ScpiMessage("PORt:DIRection " + channel.Index + "," + 0));
                    break;
                case ChannelDirection.Output:
                    MessageProducer.SendAsync(new ScpiMessage("PORt:DIRection " + channel.Index + "," + 1));
                    break;
            }
        }

        public override void Reboot()
        {
            MessageProducer.SendAsync(new ScpiMessage("system:reboot"));
        }
        #endregion
    }
}
