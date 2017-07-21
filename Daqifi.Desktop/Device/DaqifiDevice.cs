using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Message;
using DAQifi.Desktop.Message;

namespace Daqifi.Desktop.Device
{
    public class DaqifiDevice : AbstractDevice
    {
        #region Properties
        public TcpClient Client { get; set; }
        public string IPAddress { get; set; }
        public string MACAddress { get; set; }
        #endregion

        #region Constructor
        public DaqifiDevice(string name, string macAddress, string ipAddress)
        {
            Name = name;
            MACAddress = macAddress;
            IPAddress = ipAddress;

            DataChannels = new List<IChannel>();
            IsStreaming = false;
        }
        #endregion

        #region Device Methods
        public override bool Connect()
        {
            try
            {
                Client = new TcpClient(IPAddress, 9760);
                MessageProducer = new MessageProducer(Client.GetStream());
                StopStreaming();
                MessageConsumer = new MessageConsumer(Client.GetStream());
                MessageConsumer.Start();
                InitializeDeviceState();
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Problem with connectiong to DAQDevice.");
                return false;
            }
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
                AppLogger.Error(ex, "Problem with Disconnectiong from DAQDevice.");
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

        public override void SetAdcMode(IChannel channel, AdcMode mode)
        {
            switch(mode)
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
            MessageProducer.SendAsync(new ScpiMessage("system:communicate:lan:security " + SecurityTypes.IndexOf(NetworkConfiguration.SecurityType)));
            Thread.Sleep(100);
            MessageProducer.SendAsync(new ScpiMessage("system:communicate:lan:pass " + NetworkConfiguration.Password));
            Thread.Sleep(100);
            Reboot();
        }

        public override void UpdateFirmware(byte[] data)
        {
            MessageProducer.SendAsync(new RawMessage(data));
        }

        public override void Reboot()
        {
            MessageProducer.SendAsync(new ScpiMessage("system:reboot" ));
        }

        public override void SetChannelOutputValue(IChannel channel, double value)
        {
            switch(channel.Type)
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
           switch(direction)
           {
               case ChannelDirection.Input:
                   MessageProducer.SendAsync(new ScpiMessage("PORt:DIRection " + channel.Index + "," + 0));
                   break;
               case ChannelDirection.Output:
                   MessageProducer.SendAsync(new ScpiMessage("PORt:DIRection " + channel.Index + "," + 1));
                   break;
           }
        }

        public override void InitializeDeviceState()
        {
            MessageConsumer.OnMessageReceived += StatusMessageReceived;
            MessageProducer.SendAsync(new ScpiMessage("SYSTem:SYSInfoPB? 0"));
        }
        #endregion

        #region Private Methods
        private List<IChannel> GetActiveChannels(ChannelType channelType)
        {
            switch(channelType)
            {
                case ChannelType.Analog:
                    return DataChannels.Where(channel => channel.Type == ChannelType.Analog && channel.IsActive).ToList();
            }
            throw new NotImplementedException();
        }
        #endregion

        #region Object overrides
        public override string ToString()
        {
            return Name;
        }

        public override bool Equals(object obj)
        {
            var other = obj as DaqifiDevice;
            if (other == null) return false;
            if (Name != other.Name) return false;
            if (IPAddress != other.IPAddress) return false;
            if (MACAddress != other.MACAddress) return false;
            return true;
        }
        #endregion
    }
}
