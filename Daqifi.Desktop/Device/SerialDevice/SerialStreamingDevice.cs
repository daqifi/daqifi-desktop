using Daqifi.Desktop.Channel;
using Daqifi.Desktop.DataModel.Channel;
using Daqifi.Desktop.Message.Consumers;
using Daqifi.Desktop.Message.MessageTypes;
using Daqifi.Desktop.Message.Producers;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;

namespace Daqifi.Desktop.Device.SerialDevice
{
    public class SerialStreamingDevice : AbstractStreamingDevice
    {
        #region Private Data
        private readonly List<string> _securityTypes = new List<string> { "None (Open Network)", "WEP-40", "WEP-104", "WPA-PSK Phrase", "WPA-PSK Key" };

        #endregion

        #region Properties
        public SerialPort Port { get; }

        #endregion

        #region Constructor
        public SerialStreamingDevice(string portName)
        {
            Name = portName;
            Port = new SerialPort(portName);

            DataChannels = new List<IChannel>();
        }
        #endregion

        #region Override Methods
        public override bool Connect()
        {
            try
            {
                Port.Open();
                MessageProducer = new MessageProducer(Port.BaseStream);
                TurnOffEcho();
                StopStreaming();
                MessageConsumer = new MessageConsumer(Port.BaseStream);
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
                Port.Close();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public override void InitializeStreaming()
        {
            MessageProducer.SendAsync(ScpiMessagePoducer.StartStreaming(StreamingFrequency));
            IsStreaming = true;
        }

        public override void StopStreaming()
        {
            IsStreaming = false;
            MessageProducer.SendAsync(ScpiMessagePoducer.StopStreaming);
            _firstTime = null;
        }

        private void TurnOffEcho()
        {
            MessageProducer.SendAsync(ScpiMessagePoducer.Echo(-1));
        }

        public override void InitializeDeviceState()
        {
            MessageConsumer.OnMessageReceived += HandleStatusMessageReceived;
            MessageProducer.SendAsync(ScpiMessagePoducer.SystemInfo);
        }

        public override void SetAdcMode(IChannel channel, AdcMode mode)
        {
            switch (mode)
            {
                case AdcMode.Differential:
                    MessageProducer.SendAsync(ScpiMessagePoducer.ConfigureAdcMode(channel.Index, 0));
                    break;
                case AdcMode.SingleEnded:
                    MessageProducer.SendAsync(ScpiMessagePoducer.ConfigureAdcMode(channel.Index, 1));
                    break;
            }
        }

        public override void SetAdcRange(int range)
        {
            switch (range)
            {
                case 5:
                    MessageProducer.SendAsync(ScpiMessagePoducer.ConfigureAdcRange(0));
                    AdcRange = 0;
                    break;
                case 10:
                    MessageProducer.SendAsync(ScpiMessagePoducer.ConfigureAdcRange(1));
                    AdcRange = 1;
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
                    foreach (var activeChannel in activeAnalogChannels)
                    {
                        channelSetByte = channelSetByte | (1 << activeChannel.Index);
                    }

                    //Add Channel Bit to the Channel Set Byte
                    channelSetByte = channelSetByte | (1 << newChannel.Index);

                    //Convert to a string
                    string channelSetString = Convert.ToString(channelSetByte);

                    //Send the command to add the channel
                    MessageProducer.SendAsync(ScpiMessagePoducer.ConfigureAdcChannels(channelSetString));
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
                    MessageProducer.SendAsync(ScpiMessagePoducer.ConfigureAdcChannels(channelSetString));

                    break;
            }
        }

        public override void UpdateNetworkConfiguration()
        {
            if (IsStreaming) StopStreaming();
            Thread.Sleep(100);
            MessageProducer.SendAsync(ScpiMessagePoducer.SetSsid(NetworkConfiguration.Ssid));
            Thread.Sleep(100);
            MessageProducer.SendAsync(ScpiMessagePoducer.SetSecurity(SecurityTypes.IndexOf(NetworkConfiguration.SecurityType)));
            Thread.Sleep(100);
            MessageProducer.SendAsync(ScpiMessagePoducer.SetPassword(NetworkConfiguration.Password));
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
                    MessageProducer.SendAsync(ScpiMessagePoducer.SetVoltageLevel(channel.Index, value));
                    break;
                case ChannelType.Digital:
                    MessageProducer.SendAsync(ScpiMessagePoducer.SetDioPortState(channel.Index, value));
                    break;
            }
        }

        public override void SetChannelDirection(IChannel channel, ChannelDirection direction)
        {
            switch (direction)
            {
                case ChannelDirection.Input:
                    MessageProducer.SendAsync(ScpiMessagePoducer.SetDioPortDirection(channel.Index, 0));
                    break;
                case ChannelDirection.Output:
                    MessageProducer.SendAsync(ScpiMessagePoducer.SetDioPortDirection(channel.Index, 1));
                    break;
            }
        }

        public override void Reboot()
        {
            MessageProducer.SendAsync(ScpiMessagePoducer.Reboot);
        }
        #endregion
    }
}
