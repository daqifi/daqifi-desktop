using System.Collections.Generic;
using Google.ProtocolBuffers;

public interface IDaqifiOutMessage
{
    int AnalogInCalBCount { get; }
    IList<float> AnalogInCalBList { get; }
    int AnalogInCalBPrivCount { get; }
    IList<float> AnalogInCalBPrivList { get; }
    int AnalogInCalMCount { get; }
    IList<float> AnalogInCalMList { get; }
    int AnalogInCalMPrivCount { get; }
    IList<float> AnalogInCalMPrivList { get; }
    int AnalogInDataCount { get; }
    int AnalogInDataFloatCount { get; }
    IList<float> AnalogInDataFloatList { get; }
    IList<int> AnalogInDataList { get; }
    int AnalogInDataTsCount { get; }
    IList<uint> AnalogInDataTsList { get; }
    int AnalogInIntScaleMCount { get; }
    IList<float> AnalogInIntScaleMList { get; }
    int AnalogInIntScaleMPrivCount { get; }
    IList<float> AnalogInIntScaleMPrivList { get; }
    int AnalogInPortAvRangeCount { get; }
    IList<float> AnalogInPortAvRangeList { get; }
    int AnalogInPortAvRangePrivCount { get; }
    IList<float> AnalogInPortAvRangePrivList { get; }
    ByteString AnalogInPortAvRse { get; }
    ByteString AnalogInPortEnabled { get; }
    uint AnalogInPortNum { get; }
    uint AnalogInPortNumPriv { get; }
    int AnalogInPortRangeCount { get; }
    IList<float> AnalogInPortRangeList { get; }
    int AnalogInPortRangePrivCount { get; }
    IList<float> AnalogInPortRangePrivList { get; }
    ByteString AnalogInPortRse { get; }
    ByteString AnalogInPortType { get; }
    uint AnalogInRes { get; }
    uint AnalogInResPriv { get; }
    int AnalogOutDataCount { get; }
    IList<uint> AnalogOutDataList { get; }
    int AnalogOutPortAvRangeCount { get; }
    IList<float> AnalogOutPortAvRangeList { get; }
    uint AnalogOutPortNum { get; }
    float AnalogOutPortRange { get; }
    ByteString AnalogOutPortType { get; }
    uint AnalogOutRes { get; }
    int AvSsidCount { get; }
    IList<string> AvSsidList { get; }
    int AvSsidStrengthCount { get; }
    IList<uint> AvSsidStrengthList { get; }
    int AvWifiInfModeCount { get; }
    IList<uint> AvWifiInfModeList { get; }
    int AvWifiSecurityModeCount { get; }
    IList<uint> AvWifiSecurityModeList { get; }
    uint BattStatus { get; }
    DaqifiOutMessage DefaultInstanceForType { get; }
    string DeviceFwRev { get; }
    string DeviceHwRev { get; }
    string DevicePn { get; }
    uint DevicePort { get; }
    ulong DeviceSn { get; }
    uint DeviceStatus { get; }
    ByteString DigitalData { get; }
    int DigitalDataTsCount { get; }
    IList<uint> DigitalDataTsList { get; }
    ByteString DigitalPortDir { get; }
    uint DigitalPortNum { get; }
    ByteString DigitalPortType { get; }
    ByteString Eui64 { get; }
    string FriendlyDeviceName { get; }
    ByteString Gateway { get; }
    ByteString GatewayV6 { get; }
    bool HasAnalogInPortAvRse { get; }
    bool HasAnalogInPortEnabled { get; }
    bool HasAnalogInPortNum { get; }
    bool HasAnalogInPortNumPriv { get; }
    bool HasAnalogInPortRse { get; }
    bool HasAnalogInPortType { get; }
    bool HasAnalogInRes { get; }
    bool HasAnalogInResPriv { get; }
    bool HasAnalogOutPortNum { get; }
    bool HasAnalogOutPortRange { get; }
    bool HasAnalogOutPortType { get; }
    bool HasAnalogOutRes { get; }
    bool HasBattStatus { get; }
    bool HasDeviceFwRev { get; }
    bool HasDeviceHwRev { get; }
    bool HasDevicePn { get; }
    bool HasDevicePort { get; }
    bool HasDeviceSn { get; }
    bool HasDeviceStatus { get; }
    bool HasDigitalData { get; }
    bool HasDigitalPortDir { get; }
    bool HasDigitalPortNum { get; }
    bool HasDigitalPortType { get; }
    bool HasEui64 { get; }
    bool HasFriendlyDeviceName { get; }
    bool HasGateway { get; }
    bool HasGatewayV6 { get; }
    bool HasHostName { get; }
    bool HasIpAddr { get; }
    bool HasIpAddrV6 { get; }
    bool HasMacAddr { get; }
    bool HasMsgTimeStamp { get; }
    bool HasNetMask { get; }
    bool HasPrimaryDns { get; }
    bool HasPrimaryDnsV6 { get; }
    bool HasPwrStatus { get; }
    bool HasSecondaryDns { get; }
    bool HasSecondaryDnsV6 { get; }
    bool HasSsid { get; }
    bool HasSsidStrength { get; }
    bool HasSubPreLengthV6 { get; }
    bool HasTempStatus { get; }
    bool HasTimestampFreq { get; }
    bool HasWifiInfMode { get; }
    bool HasWifiSecurityMode { get; }
    string HostName { get; }
    ByteString IpAddr { get; }
    ByteString IpAddrV6 { get; }
    bool IsInitialized { get; }
    ByteString MacAddr { get; }
    uint MsgTimeStamp { get; }
    ByteString NetMask { get; }
    ByteString PrimaryDns { get; }
    ByteString PrimaryDnsV6 { get; }
    uint PwrStatus { get; }
    ByteString SecondaryDns { get; }
    ByteString SecondaryDnsV6 { get; }
    int SerializedSize { get; }
    string Ssid { get; }
    uint SsidStrength { get; }
    ByteString SubPreLengthV6 { get; }
    int TempStatus { get; }
    uint TimestampFreq { get; }
    uint WifiInfMode { get; }
    uint WifiSecurityMode { get; }

    DaqifiOutMessage.Builder CreateBuilderForType();
    float GetAnalogInCalB(int index);
    float GetAnalogInCalBPriv(int index);
    float GetAnalogInCalM(int index);
    float GetAnalogInCalMPriv(int index);
    int GetAnalogInData(int index);
    float GetAnalogInDataFloat(int index);
    uint GetAnalogInDataTs(int index);
    float GetAnalogInIntScaleM(int index);
    float GetAnalogInIntScaleMPriv(int index);
    float GetAnalogInPortAvRange(int index);
    float GetAnalogInPortAvRangePriv(int index);
    float GetAnalogInPortRange(int index);
    float GetAnalogInPortRangePriv(int index);
    uint GetAnalogOutData(int index);
    float GetAnalogOutPortAvRange(int index);
    string GetAvSsid(int index);
    uint GetAvSsidStrength(int index);
    uint GetAvWifiInfMode(int index);
    uint GetAvWifiSecurityMode(int index);
    uint GetDigitalDataTs(int index);
    DaqifiOutMessage.Builder ToBuilder();
    void WriteTo(ICodedOutputStream output);
}