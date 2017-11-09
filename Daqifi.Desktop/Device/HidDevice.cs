using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Message;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using HidLibrary;

namespace DAQifi.Desktop.Device
{
    public class HidDevice : IDevice
    {
        #region Private Data

        #endregion

        public HidLibrary.HidDevice Device { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        public HidDevice(HidLibrary.HidDevice device)
        {
            Device = device;
        }

        public bool Connect()
        {
            //_device.OpenDevice();
            //return _device.IsConnected;\
            return true;
        }

        public bool Disconnect()
        {
            //_device.CloseDevice();
            //return !_device.IsConnected;
            return false;
        }

        #region Not Implemented Device Property and Methods
        public int Id { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public string Name { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public string AdcRangeText { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public int StreamingFrequency { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public IMessageConsumer MessageConsumer { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public IMessageProducer MessageProducer { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public List<IChannel> DataChannels { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public void InitializeDeviceState()
        {
            throw new NotImplementedException();
        }

        public void InitializeStreaming()
        {
            throw new NotImplementedException();
        }

        public void Reboot()
        {
            throw new NotImplementedException();
        }

        public void RemoveChannel(IChannel channel)
        {
            throw new NotImplementedException();
        }

        public void SetAdcMode(IChannel channel, AdcMode mode)
        {
            throw new NotImplementedException();
        }

        public void SetAdcRange(int range)
        {
            throw new NotImplementedException();
        }

        public void SetChannelDirection(IChannel channel, ChannelDirection direction)
        {
            throw new NotImplementedException();
        }

        public void SetChannelOutputValue(IChannel channel, double value)
        {
            throw new NotImplementedException();
        }

        public void StopStreaming()
        {
            throw new NotImplementedException();
        }

        public void UpdateFirmware(byte[] data)
        {
            throw new NotImplementedException();
        }

        public void UpdateNetworkConfiguration()
        {
            throw new NotImplementedException();
        }

        public void AddChannel(IChannel channel)
        {
            throw new NotImplementedException();
        }
        #endregion
    }
}
