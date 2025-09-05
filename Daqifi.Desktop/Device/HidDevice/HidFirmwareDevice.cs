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

    public bool Disconnect()
    {
        //_device.CloseDevice();
        //return !_device.IsConnected;
        return false;
    }

    #region Not Implemented Device Property and Methods
    public int Id { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public void Reboot()
    {
        throw new NotImplementedException();
    }

    public void UpdateFirmware(byte[] data)
    {
        throw new NotImplementedException();
    }

    #endregion
}