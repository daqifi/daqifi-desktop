using Daqifi.Desktop.Device;
using System;
using System.Text;

namespace Daqifi.Desktop.Message
{
    public class DeviceMessage : AbstractMessage
    {
        #region Properties
        public IDevice Device { get; }

        #endregion

        public DeviceMessage(WiFiDAQOutMessage message)
        {
            Data = message;
            string name = message.HostName;

            string ipAddress  = "";
            string macAddress = BitConverter.ToString(message.MacAddrList[0].ToByteArray());
            
            byte[] ipAddressBytes = message.IpAddrList[0].ToByteArray();
            
            for(int i = 0; i < ipAddressBytes.Length; i++)
            {
                if(i == ipAddressBytes.Length -1)
                {
                    ipAddress += ipAddressBytes[i].ToString();
                }
                else
                {
                     ipAddress += ipAddressBytes[i].ToString() + "." ; 
                }
            }

            Device = new DaqifiDevice(name, macAddress, ipAddress);

            if(message.HasSsid)
            {
                (Device as DaqifiDevice).NetworkConfiguration.SSID = message.Ssid;
            }
        }

        public override byte[] GetBytes()
        {
            return Encoding.ASCII.GetBytes((string)Data);
        }
    }
}
