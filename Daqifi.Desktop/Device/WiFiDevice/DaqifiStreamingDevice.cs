using Daqifi.Desktop.DataModel.Device;
using Daqifi.Desktop.IO.Messages.Consumers;
using Daqifi.Desktop.IO.Messages.Producers;
using System.Net.Sockets;
using Daqifi.Core.Communication.Messages;

namespace Daqifi.Desktop.Device.WiFiDevice;

public class DaqifiStreamingDevice : AbstractStreamingDevice
{
    #region Properties

    public TcpClient Client { get; set; }
    public string IpAddress { get; set; }
    public string MacAddress { get; set; }
    public int Port { get; set; }
    public bool IsPowerOn { get; set; }
    public string DeviceSerialNo { get; set; }
    public string DeviceVersion { get; set; }
    public override ConnectionType ConnectionType => ConnectionType.Wifi;
    

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
            // Phase 1 Integration: Use existing transport with new Core 0.4.0 ScpiMessageProducer
            // CoreDeviceAdapter doesn't exist yet in Core 0.4.0, so we use the proven transport layer
            
            Client = new TcpClient();
            Client.Connect(IpAddress, Port);
            var networkStream = Client.GetStream();

            // Create message producer and consumer using existing proven implementation
            MessageProducer = new MessageProducer(networkStream);
            MessageProducer.Start();

            MessageConsumer = new MessageConsumer(networkStream);
            ((MessageConsumer)MessageConsumer).IsWifiDevice = true;
            MessageConsumer.Start();

            // Send device initialization commands using new Core ScpiMessageProducer
            TurnOffEcho();
            StopStreaming();
            TurnDeviceOn();
            SetProtobufMessageFormat();

            InitializeDeviceState();
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
        try
        {
            // Use existing MessageProducer with new Core ScpiMessage
            if (MessageProducer != null)
            {
                var scpiMessage = new ScpiMessage(command);
                MessageProducer.Send(scpiMessage);
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"Failed to write command: {command}");
            return false;
        }
    }

    public override bool Disconnect()
    {
        try
        {
            StopStreaming();
            
            // Clean up message components
            MessageProducer?.Stop();
            MessageConsumer?.Stop();
            
            // Close TCP client
            if (Client != null)
            {
                Client.Close();
                Client.Dispose();
                Client = null;
            }
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Problem with Disconnecting from DAQifi Device.");
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
        if (!(obj is DaqifiStreamingDevice other)) { return false; }
        if (Name != other.Name) { return false; }
        if (IpAddress != other.IpAddress) { return false; }
        if (MacAddress != other.MacAddress) { return false; }
        return true;
    }
    #endregion

}