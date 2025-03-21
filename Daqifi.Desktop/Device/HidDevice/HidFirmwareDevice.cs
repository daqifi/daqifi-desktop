using Daqifi.Desktop.Channel;
using Daqifi.Desktop.DataModel.Channel;
using Daqifi.Desktop.IO.Messages.Consumers;
using Daqifi.Desktop.IO.Messages.Producers;
using System.ComponentModel;

namespace Daqifi.Desktop.Device.HidDevice;

public class HidFirmwareDevice : IFirmwareDevice
{

    public HidLibrary.HidFastReadDevice Device { get; }

    public event PropertyChangedEventHandler PropertyChanged;

    public string Name { get; set; }

    public HidFirmwareDevice(HidLibrary.HidFastReadDevice device)
    {
        Device = device;
    }

    public bool Connect()
    {
        //_device.OpenDevice();
        //return _device.IsConnected;\
        return true;
    }

    public bool Write(string command)
    {
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