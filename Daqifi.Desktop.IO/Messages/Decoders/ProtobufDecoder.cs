using Daqifi.Desktop.IO.Messages.MessageTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Daqifi.Desktop.IO.Messages.Decoders
{
    public static class ProtobufDecoder
    {

        public static string GetIpAddressString(DaqifiOutMessage protobufMessage)
        {
            var ipAddressString = string.Empty;

            if (protobufMessage.IpAddr.Length < 0) { return ipAddressString; }
            var ipAddressBytes = protobufMessage.IpAddr.ToByteArray();
            for (var i = 0; i < ipAddressBytes.Length; i++)
            {
                if (i == ipAddressBytes.Length - 1)
                {
                    ipAddressString += ipAddressBytes[i].ToString();
                }
                else
                {
                    ipAddressString += ipAddressBytes[i] + ".";
                }
            }

            return ipAddressString;
        }

        public static string GetMacAddressString(DaqifiOutMessage protobufMessage)
        {
            var macAddress = string.Empty;

            if (protobufMessage.MacAddr.Length < 0) { return macAddress; }

            macAddress = BitConverter.ToString(protobufMessage.MacAddr.ToByteArray());

            return macAddress;
        }
    }
}
