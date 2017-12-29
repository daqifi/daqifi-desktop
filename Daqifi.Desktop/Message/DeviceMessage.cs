using Daqifi.Desktop.Device;
using Daqifi.Desktop.Device.WiFiDevice;
using System;
using System.Text;

namespace Daqifi.Desktop.Message
{
    public class DeviceMessage : AbstractMessage
    {
        #region Properties
        public IDevice Device { get; }

        #endregion

        public DeviceMessage(DaqifiOutMessage message)
        {
            Data = message;
            var name = message.HostName;

            var ipAddress  = "";
            var macAddress = BitConverter.ToString(message.MacAddr.ToByteArray());
            
            var ipAddressBytes = message.IpAddr.ToByteArray();
            
            for(var i = 0; i < ipAddressBytes.Length; i++)
            {
                if(i == ipAddressBytes.Length -1)
                {
                    ipAddress += ipAddressBytes[i].ToString();
                }
                else
                {
                     ipAddress += ipAddressBytes[i] + "." ; 
                }
            }

            Device = new DaqifiStreamingDevice(name, macAddress, ipAddress);

            if(message.HasSsid)
            {
                (Device as DaqifiStreamingDevice).NetworkConfiguration.SSID = message.Ssid;
            }
        }

        public override byte[] GetBytes()
        {
            return Encoding.ASCII.GetBytes((string)Data);
        }
    }
}
