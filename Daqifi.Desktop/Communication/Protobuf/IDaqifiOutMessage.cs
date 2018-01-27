using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Google.ProtocolBuffers;
using Google.ProtocolBuffers.Descriptors;

namespace Daqifi.Desktop.Communication.Protobuf
{
    public interface IDaqifiOutMessage
    {
        DaqifiOutMessage DefaultInstanceForType { get; }
        bool HasMsgTimeStamp { get; }
        uint MsgTimeStamp { get; }
        IList<int> AnalogInDataList { get; }
        int AnalogInDataCount { get; }
        IList<float> AnalogInDataFloatList { get; }
        int AnalogInDataFloatCount { get; }
        IList<uint> AnalogInDataTsList { get; }
        int AnalogInDataTsCount { get; }
        bool HasDigitalData { get; }
        ByteString DigitalData { get; }
        IList<uint> DigitalDataTsList { get; }
        int DigitalDataTsCount { get; }
        IList<uint> AnalogOutDataList { get; }
        int AnalogOutDataCount { get; }
        bool HasDeviceStatus { get; }
        uint DeviceStatus { get; }
        bool HasPwrStatus { get; }
        uint PwrStatus { get; }
        bool HasBattStatus { get; }
        uint BattStatus { get; }
        bool HasTempStatus { get; }
        int TempStatus { get; }
        bool HasTimestampFreq { get; }
        uint TimestampFreq { get; }
        bool HasAnalogInPortNum { get; }
        uint AnalogInPortNum { get; }
        bool HasAnalogInPortNumPriv { get; }
        uint AnalogInPortNumPriv { get; }
        bool HasAnalogInPortType { get; }
        ByteString AnalogInPortType { get; }
        bool HasAnalogInPortRse { get; }
        ByteString AnalogInPortRse { get; }
        bool HasAnalogInPortEnabled { get; }
        ByteString AnalogInPortEnabled { get; }
        IList<float> AnalogInPortRangeList { get; }
        int AnalogInPortRangeCount { get; }
        IList<float> AnalogInPortRangePrivList { get; }
        int AnalogInPortRangePrivCount { get; }
        bool HasAnalogInRes { get; }
        uint AnalogInRes { get; }
        bool HasAnalogInResPriv { get; }
        uint AnalogInResPriv { get; }
        IList<float> AnalogInCalMList { get; }
        int AnalogInCalMCount { get; }
        IList<float> AnalogInCalBList { get; }
        int AnalogInCalBCount { get; }
        IList<float> AnalogInCalMPrivList { get; }
        int AnalogInCalMPrivCount { get; }
        IList<float> AnalogInCalBPrivList { get; }
        int AnalogInCalBPrivCount { get; }
        bool HasDigitalPortNum { get; }
        uint DigitalPortNum { get; }
        bool HasDigitalPortType { get; }
        ByteString DigitalPortType { get; }
        bool HasDigitalPortDir { get; }
        ByteString DigitalPortDir { get; }
        bool HasAnalogOutPortNum { get; }
        uint AnalogOutPortNum { get; }
        bool HasAnalogOutPortType { get; }
        ByteString AnalogOutPortType { get; }
        bool HasAnalogOutRes { get; }
        uint AnalogOutRes { get; }
        bool HasAnalogOutPortRange { get; }
        float AnalogOutPortRange { get; }
        bool HasIpAddr { get; }
        ByteString IpAddr { get; }
        bool HasNetMask { get; }
        ByteString NetMask { get; }
        bool HasGateway { get; }
        ByteString Gateway { get; }
        bool HasPrimaryDns { get; }
        ByteString PrimaryDns { get; }
        bool HasSecondaryDns { get; }
        ByteString SecondaryDns { get; }
        bool HasMacAddr { get; }
        ByteString MacAddr { get; }
        bool HasIpAddrV6 { get; }
        ByteString IpAddrV6 { get; }
        bool HasSubPreLengthV6 { get; }
        ByteString SubPreLengthV6 { get; }
        bool HasGatewayV6 { get; }
        ByteString GatewayV6 { get; }
        bool HasPrimaryDnsV6 { get; }
        ByteString PrimaryDnsV6 { get; }
        bool HasSecondaryDnsV6 { get; }
        ByteString SecondaryDnsV6 { get; }
        bool HasEui64 { get; }
        ByteString Eui64 { get; }
        bool HasHostName { get; }
        string HostName { get; }
        bool HasDevicePort { get; }
        uint DevicePort { get; }
        bool HasFriendlyDeviceName { get; }
        string FriendlyDeviceName { get; }
        bool HasSsid { get; }
        string Ssid { get; }
        bool HasWifiSecurityMode { get; }
        uint WifiSecurityMode { get; }
        IList<string> AvSsidList { get; }
        int AvSsidCount { get; }
        bool HasAvSsidStrength { get; }
        ByteString AvSsidStrength { get; }
        bool HasAvSsidSecurityMode { get; }
        ByteString AvSsidSecurityMode { get; }
        bool HasDevicePn { get; }
        string DevicePn { get; }
        bool HasDeviceHwRev { get; }
        string DeviceHwRev { get; }
        bool HasDeviceFwRev { get; }
        string DeviceFwRev { get; }
        MessageDescriptor DescriptorForType { get; }
        IDictionary<FieldDescriptor, object> AllFields { get; }
        UnknownFieldSet UnknownFields { get; }
        bool IsInitialized { get; }
        int SerializedSize { get; }
        int GetAnalogInData(int index);
        float GetAnalogInDataFloat(int index);
        uint GetAnalogInDataTs(int index);
        uint GetDigitalDataTs(int index);
        uint GetAnalogOutData(int index);
        float GetAnalogInPortRange(int index);
        float GetAnalogInPortRangePriv(int index);
        float GetAnalogInCalM(int index);
        float GetAnalogInCalB(int index);
        float GetAnalogInCalMPriv(int index);
        float GetAnalogInCalBPriv(int index);
        string GetAvSsid(int index);
        DaqifiOutMessage.Builder ToBuilder();
        DaqifiOutMessage.Builder CreateBuilderForType();
        bool HasField(FieldDescriptor field);
        int GetRepeatedFieldCount(FieldDescriptor field);
        object this[FieldDescriptor field, int index] { get; }
        object this[FieldDescriptor field] { get; }
        string ToString();
        void PrintTo(TextWriter writer);
        bool Equals(object other);
        int GetHashCode();
        void WriteTo(ICodedOutputStream output);
        ByteString ToByteString();
        byte[] ToByteArray();
        void WriteTo(Stream output);
        void WriteDelimitedTo(Stream output);
    }
}
