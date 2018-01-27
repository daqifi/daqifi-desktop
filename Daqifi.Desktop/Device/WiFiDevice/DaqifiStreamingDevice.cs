﻿using Daqifi.Desktop.Channel;
using Daqifi.Desktop.DataModel.Channel;
using Daqifi.Desktop.Message.Consumers;
using Daqifi.Desktop.Message.MessageTypes;
using Daqifi.Desktop.Message.Producers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;

namespace Daqifi.Desktop.Device.WiFiDevice
{
    public class DaqifiStreamingDevice : AbstractStreamingDevice
    {
        #region Properties
        public TcpClient Client { get; set; }
        public string IpAddress { get; set; }
        public string MacAddress { get; set; }
        #endregion

        #region Constructor
        public DaqifiStreamingDevice(string name, string macAddress, string ipAddress)
        {
            Name = name;
            MacAddress = macAddress;
            IpAddress = ipAddress;

            DataChannels = new List<IChannel>();
            IsStreaming = false;
        }
        #endregion

        #region Device Methods
        public override bool Connect()
        {
            try
            {
                Client = new TcpClient(IpAddress, 9760);
                MessageProducer = new MessageProducer(Client.GetStream());
                TurnOffEcho();
                StopStreaming();
                MessageConsumer = new MessageConsumer(Client.GetStream());
                MessageConsumer.Start();
                InitializeDeviceState();
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Problem with connectiong to DAQiFi Device.");
                return false;
            }
        }

        private void TurnOffEcho()
        {
            MessageProducer.SendAsync(ScpiMessagePoducer.Echo(-1));
        }

        public override bool Disconnect()
        {
            try
            {
                StopStreaming();
                MessageConsumer.Stop();
                Client.Close();
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Problem with Disconnectiong from DAQifi Device.");
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
            _previousTimestamp = null;
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
                    var channelSetString = Convert.ToString(channelSetByte);

                    //Send the command to add the channel
                    MessageProducer.SendAsync(ScpiMessagePoducer.ConfigureAdcChannels(channelSetString));
                    break;
                case ChannelType.Digital:
                    MessageProducer.SendAsync(ScpiMessagePoducer.EnableDioPorts());
                    break;
            }

            newChannel.IsActive = true;
        }

        public override void RemoveChannel(IChannel channelToRemove)
        {
            switch (channelToRemove.Type)
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
                    channelSetByte = channelSetByte | (1 >> channelToRemove.Index);

                    //Convert to a string
                    var channelSetString = Convert.ToString(channelSetByte);

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
            MessageProducer.SendAsync(new RawMessage(data));
        }

        public override void Reboot()
        {
            MessageProducer.SendAsync(ScpiMessagePoducer.Reboot);
        }

        public override void SetChannelOutputValue(IChannel channel, double value)
        {
            switch(channel.Type)
            {
                case ChannelType.Analog:
                    MessageProducer.SendAsync(ScpiMessagePoducer.SetVoltageLevel(channel.Index,value));
                    break;
                case ChannelType.Digital:
                    MessageProducer.SendAsync(ScpiMessagePoducer.SetDioPortState(channel.Index,value));
                    break;
            }
        }

        public override void SetChannelDirection(IChannel channel, ChannelDirection direction)
        {
           switch(direction)
           {
               case ChannelDirection.Input:
                   MessageProducer.SendAsync(ScpiMessagePoducer.SetDioPortDirection(channel.Index, 0));
                   break;
               case ChannelDirection.Output:
                   MessageProducer.SendAsync(ScpiMessagePoducer.SetDioPortDirection(channel.Index, 1));
                   break;
           }
        }

        public override void InitializeDeviceState()
        {
            MessageConsumer.OnMessageReceived += HandleStatusMessageReceived;
            MessageProducer.SendAsync(ScpiMessagePoducer.SystemInfo);
        }
        #endregion

        #region Private Methods
        private IEnumerable<IChannel> GetActiveChannels(ChannelType channelType)
        {
            switch(channelType)
            {
                case ChannelType.Analog:
                    return DataChannels.Where(channel => channel.Type == ChannelType.Analog && channel.IsActive).ToList();
                case ChannelType.Digital:
                    return DataChannels.Where(channel => channel.Type == ChannelType.Digital && channel.IsActive).ToList();
                default:
                    throw new NotImplementedException();
            }
        }
        #endregion

        #region Object overrides
        public override string ToString()
        {
            return Name;
        }

        public override bool Equals(object obj)
        {
            var other = obj as DaqifiStreamingDevice;
            if (other == null) return false;
            if (Name != other.Name) return false;
            if (IpAddress != other.IpAddress) return false;
            if (MacAddress != other.MacAddress) return false;
            return true;
        }
        #endregion
    }
}