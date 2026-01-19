using Daqifi.Core.Communication.Transport;
using Daqifi.Core.Device;
using Daqifi.Desktop.DataModel.Device;

namespace Daqifi.Desktop.Device.WiFiDevice;

public class DaqifiStreamingDevice : AbstractStreamingDevice
{
    #region Properties

    public int Port { get; set; }
    public bool IsPowerOn { get; set; }
    public override ConnectionType ConnectionType => ConnectionType.Wifi;
    protected override bool RequestDeviceInfoOnInitialize => false;

    #endregion

    #region Constructor
    public DaqifiStreamingDevice(DeviceInfo deviceInfo)
    {
        Name = deviceInfo.DeviceName;
        DeviceSerialNo = deviceInfo.DeviceSerialNo;
        IpAddress = deviceInfo.IpAddress;
        MacAddress = deviceInfo.MacAddress;
        Port = (int)deviceInfo.Port;
        IsPowerOn = deviceInfo.IsPowerOn;
        IsStreaming = false;
        DeviceVersion = deviceInfo.DeviceVersion;

    }

    #endregion

    #region Override Methods
    public override bool Connect()
    {
        try
        {
            var options = new DeviceConnectionOptions
            {
                DeviceName = string.IsNullOrWhiteSpace(Name) ? "DAQiFi Device" : Name,
                ConnectionRetry = ConnectionRetryOptions.NoRetry,
                InitializeDevice = false
            };

            _coreDevice = DaqifiDeviceFactory.ConnectTcp(IpAddress, Port, options);
            if (!_coreDevice.IsConnected)
            {
                AppLogger.Error($"Failed to connect to DAQiFi device at {IpAddress}:{Port}");
                return false;
            }

            MessageProducer = new CoreMessageProducerAdapter(_coreDevice);
            MessageConsumer = new CoreMessageConsumerAdapter(_coreDevice);
            MessageProducer.Start();
            MessageConsumer.Start();

            InitializeDeviceState();

            // Use async initialization (safe because Connect() is called from Task.Run)
            _coreDevice.InitializeAsync().GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Problem with connecting to DAQiFi Device.");
            return false;
        }
    }

    public override bool Write(string command)
    {
        return true;
    }

    public override bool Disconnect()
    {
        try
        {
            StopStreaming();
            MessageProducer?.Stop();
            MessageConsumer?.Stop();

            // Clear channels to prevent ghost channels on reconnect (Issue #29)
            AppLogger.Information($"Cleared {DataChannels.Count} channels for device {DeviceSerialNo}");
            DataChannels.Clear();

            _coreDevice?.Disconnect();
            _coreDevice?.Dispose();
            _coreDevice = null;
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Problem with Disconnecting from DAQiFi Device.");
            return false;
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
        if (obj is not DaqifiStreamingDevice other) { return false; }
        if (Name != other.Name) { return false; }
        if (IpAddress != other.IpAddress) { return false; }
        if (MacAddress != other.MacAddress) { return false; }
        return true;
    }
    #endregion

    private DaqifiDevice? _coreDevice;
}
