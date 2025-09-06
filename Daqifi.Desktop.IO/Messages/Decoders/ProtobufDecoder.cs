using System.Globalization;

namespace Daqifi.Desktop.IO.Messages.Decoders;

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
                ipAddressString += ipAddressBytes[i].ToString(CultureInfo.InvariantCulture);
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
        return protobufMessage.MacAddr.Length < 0
            ? string.Empty
            : BitConverter.ToString(protobufMessage.MacAddr.ToByteArray());
    }
}