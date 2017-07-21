using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Message;
using HidLibrary;

namespace DAQifi.Desktop.Device
{
    public class UsbHidDevice : IDevice
    {
        #region Private Data
        private const int VendorId = 0x4D8;
        private const int ProductId = 0x03C;
        private readonly HidDevice _device;
        #endregion

        public event PropertyChangedEventHandler PropertyChanged;

        public UsbHidDevice()
        {
            _device = HidDevices.Enumerate(VendorId, ProductId).FirstOrDefault();
        }

        public bool Connect()
        {
            _device.OpenDevice();
            return _device.IsConnected;
        }

        public bool Disconnect()
        {
            _device.CloseDevice();
            return !_device.IsConnected;
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
