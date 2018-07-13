using System;
using Daqifi.Desktop.IO.Messages.MessageTypes;

namespace Daqifi.Desktop.IO.Messages.Decoders
{
    public static class ProtobufDecoder
    {
        public static string GetIpAddressString(IDaqifiOutMessage protobufMessage)
        {
            var ipAddressString = string.Empty;

            if (!protobufMessage.HasIpAddr) return ipAddressString;

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

        public static string GetMacAddressString(IDaqifiOutMessage protobufMessage)
        {
            var macAddress = string.Empty;

            if (!protobufMessage.HasMacAddr) return macAddress;

            macAddress = BitConverter.ToString(protobufMessage.MacAddr.ToByteArray());

            return macAddress;
        }
    }
}
