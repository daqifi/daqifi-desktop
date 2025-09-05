using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Daqifi.Desktop.Device.SerialDevice;

public sealed class UsbDevice : IDisposable
{
    private IntPtr _hDevInfo;
    private SpDevinfoData _data;

    private UsbDevice(IntPtr hDevInfo, SpDevinfoData data)
    {
        _hDevInfo = hDevInfo;
        _data = data;
    }

    public static UsbDevice Get(string pnpDeviceId)
    {
        ArgumentNullException.ThrowIfNull(pnpDeviceId);

        var hDevInfo = SetupDiGetClassDevs(IntPtr.Zero, pnpDeviceId, IntPtr.Zero, Digcf.DigcfAllclasses | Digcf.DigcfDeviceinterface);
        if (hDevInfo == InvalidHandleValue)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var data = new SpDevinfoData();
        data.cbSize = Marshal.SizeOf(data);
        if (SetupDiEnumDeviceInfo(hDevInfo, 0, ref data))
            return new UsbDevice(hDevInfo, data) { PnpDeviceId = pnpDeviceId };
        var err = Marshal.GetLastWin32Error();
        if (err == ErrorNoMoreItems)
            return null;
        throw new Win32Exception(err);
    }

    public void Dispose()
    {
        if (_hDevInfo == IntPtr.Zero) return;
        SetupDiDestroyDeviceInfoList(_hDevInfo);
        _hDevInfo = IntPtr.Zero;
    }

    public string PnpDeviceId { get; private set; }

    public string ParentPnpDeviceId
    {
        get
        {
            if (IsVistaOrHigher)
                return GetStringProperty(Devpropkey.DEVPKEY_Device_Parent);

            var cr = CM_Get_Parent(out var parent, _data.DevInst, 0);
            if (cr != 0)
                throw new Exception("CM Error:" + cr);

            return GetDeviceId(parent);
        }
    }

    public string BusReportedDeviceDescription
    {
        get
        {
            if (IsVistaOrHigher)
                return GetStringProperty(Devpropkey.DEVPKEY_Device_BusReportedDeviceDesc);

            throw new NotImplementedException("USB is only supported on Windows Vista or Higher");
        }
    }

    private static string GetDeviceId(uint inst)
    {
        var buffer = Marshal.AllocHGlobal(MaxDeviceIdLen + 1);
        var cr = CM_Get_Device_ID(inst, buffer, MaxDeviceIdLen + 1, 0);
        if (cr != 0)
            throw new Exception("CM Error:" + cr);

        try
        {
            return Marshal.PtrToStringAnsi(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public string[] ChildrenPnpDeviceIds
    {
        get
        {
            if (IsVistaOrHigher)
                return GetStringListProperty(Devpropkey.DEVPKEY_Device_Children);

            var cr = CM_Get_Child(out var child, _data.DevInst, 0);
            if (cr != 0)
                return new string[0];

            var ids = new List<string> { GetDeviceId(child) };
            while (true)
            {
                cr = CM_Get_Sibling(out child, child, 0);
                if (cr != 0)
                    return ids.ToArray();

                ids.Add(GetDeviceId(child));
            }
        }
    }

    private static bool IsVistaOrHigher => Environment.OSVersion.Platform == PlatformID.Win32NT && Environment.OSVersion.Version.CompareTo(new Version(6, 0)) >= 0;

    private const int InvalidHandleValue = -1;
    private const int ErrorNoMoreItems = 259;
    private const int MaxDeviceIdLen = 200;

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public int cbSize;
        private readonly Guid ClassGuid;
        public readonly uint DevInst;
        private readonly IntPtr Reserved;
    }

    [Flags]
    private enum Digcf : uint
    {
        DigcfAllclasses = 0x00000004,
        DigcfDeviceinterface = 0x00000010
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SpDevinfoData deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string enumerator, IntPtr hwndParent, Digcf flags);

    [DllImport("setupapi.dll")]
    private static extern int CM_Get_Parent(out uint pdnDevInst, uint dnDevInst, uint ulFlags);

    [DllImport("setupapi.dll")]
    private static extern int CM_Get_Device_ID(uint dnDevInst, IntPtr buffer, int bufferLen, uint ulFlags);

    [DllImport("setupapi.dll")]
    private static extern int CM_Get_Child(out uint pdnDevInst, uint dnDevInst, uint ulFlags);

    [DllImport("setupapi.dll")]
    private static extern int CM_Get_Sibling(out uint pdnDevInst, uint dnDevInst, uint ulFlags);

    [DllImport("setupapi.dll")]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    // vista and higher
    [DllImport("setupapi.dll", SetLastError = true, EntryPoint = "SetupDiGetDevicePropertyW")]
    private static extern bool SetupDiGetDeviceProperty(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData, ref Devpropkey propertyKey, out int propertyType, IntPtr propertyBuffer, int propertyBufferSize, out int requiredSize, int flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct Devpropkey
    {
        private Guid fmtid;
        private uint pid;

        // from devpkey.h
        public static readonly Devpropkey DEVPKEY_Device_Parent = new() { fmtid = new Guid("{4340A6C5-93FA-4706-972C-7B648008A5A7}"), pid = 8 };
        public static readonly Devpropkey DEVPKEY_Device_Children = new() { fmtid = new Guid("{4340A6C5-93FA-4706-972C-7B648008A5A7}"), pid = 9 };
        // 0x540b947e, 0x8b40, 0x45bc, 0xa8, 0xa2, 0x6a, 0x0b, 0x89, 0x4c, 0xbd, 0xa2, 4
        public static readonly Devpropkey DEVPKEY_Device_BusReportedDeviceDesc = new() { fmtid = new Guid("{540B947E-8B40-45BC-A8A2-6A0B894CBDA2}"), pid = 4 };
    }

    private string[] GetStringListProperty(Devpropkey key)
    {
        SetupDiGetDeviceProperty(_hDevInfo, ref _data, ref key, out _, IntPtr.Zero, 0, out var size, 0);
        if (size == 0)
            return new string[0];

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!SetupDiGetDeviceProperty(_hDevInfo, ref _data, ref key, out _, buffer, size, out size, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var strings = new List<string>();
            var current = buffer;
            while (true)
            {
                var s = Marshal.PtrToStringUni(current);
                if (string.IsNullOrEmpty(s))
                    break;

                strings.Add(s);
                current += (1 + s.Length) * 2;
            }
            return strings.ToArray();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private string GetStringProperty(Devpropkey key)
    {
        SetupDiGetDeviceProperty(_hDevInfo, ref _data, ref key, out var type, IntPtr.Zero, 0, out var size, 0);
        if (size == 0)
            return null;

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!SetupDiGetDeviceProperty(_hDevInfo, ref _data, ref key, out type, buffer, size, out size, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}