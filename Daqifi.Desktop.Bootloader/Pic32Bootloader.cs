using HidLibrary;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Daqifi.Desktop.Bootloader;

public partial class Pic32Bootloader : ObservableObject, IBootloader, IDisposable
{
    #region Private Data
    // Memory address range that we omit changing so we can preserve calibration values.
    private const uint BeginProtectedAddress = 0x1D1E0000;
    private const uint EndProtectedAddress = 0x1D200000;

    private readonly HidFastReadDevice _hidDevice;
    [ObservableProperty]
    private string _version;
    private bool _disposed;
    private ushort _baseAddress;

    #endregion

    #region Constructor
    public Pic32Bootloader(HidFastReadDevice hidHidDevice)
    {
        _hidDevice = hidHidDevice;
        OpenDevice();
    }
    #endregion 

    #region IBootloader Methods

    private readonly ManualResetEvent _resetEvent = new(false);
    private const string VersionErrorMessage = "Error communicating with device.  \nPlease disconnect / connect USB from your computer and try again.";
    public void RequestVersion()
    {
        ThreadPool.QueueUserWorkItem(RequestVersionDoWork);

        var result = WaitHandle.WaitAny([_resetEvent], 10 * 1000);

        if (result == WaitHandle.WaitTimeout)
        {
            Version = VersionErrorMessage;
        }
    }

    private void RequestVersionDoWork(object stateInfo)
    {
        var requestVersionMessage = Pic32BootloaderMessageProducer.CreateRequestVersionMessage();
        var outputReport = CreateDeviceOutputReport(requestVersionMessage);

        _hidDevice.WriteReport(outputReport);

        var inputReport = _hidDevice.ReadReport();
        var version = Pic32BootloaderMessageConsumer.DecodeVersionResponse(inputReport.Data);
        Version = version.Equals("error", StringComparison.OrdinalIgnoreCase) ? VersionErrorMessage : version;

        _resetEvent.Set();
    }

    public void JumpToApplication()
    {
        var jumpToApplicationMessage = Pic32BootloaderMessageProducer.CreateJumpToApplicationMessage();
        var outputReport = CreateDeviceOutputReport(jumpToApplicationMessage);

        _hidDevice.WriteReport(outputReport);
    }

    public bool EraseFlash()
    {
        var eraseFlashMessage = Pic32BootloaderMessageProducer.CreateEraseFlashMessage();
        var outputReport = CreateDeviceOutputReport(eraseFlashMessage);

        _hidDevice.WriteReport(outputReport);

        var inputReport = _hidDevice.ReadReport();
        return Pic32BootloaderMessageConsumer.DecodeEraseFlashResponse(inputReport.Data);
    }

    /// <summary>
    /// [<SOH>…]<SOH><0x03>[<HEX_RECORD>…]<CRCL><CRCH><EOT>
    /// </summary>
    /// <param name="filePath"></param>
    /// <returns></returns>
    public bool LoadFirmware(string filePath, BackgroundWorker backgroundWorker)
    {
        var hexRecords = GetHexRecordsFromFile(filePath);

        if (!EraseFlash())
        {
            throw new InvalidDataException("There was a problem erasing the flash");
        }

        for (var i = 0; i < hexRecords.Count; i++)
        {
            backgroundWorker.ReportProgress(i * 100 / hexRecords.Count);

            // Send a hex record
            var loadFirmwareMessage = Pic32BootloaderMessageProducer.CreateProgramFlashMessage(hexRecords[i]);
            var outputReport = CreateDeviceOutputReport(loadFirmwareMessage);

            _hidDevice.WriteReport(outputReport);

            var report = _hidDevice.FastReadReport();
            var successfulResponse = Pic32BootloaderMessageConsumer.DecodeProgramFlashResponse(report.Data);

            if (!successfulResponse)
            {
                throw new InvalidDataException("The response from the device was invalid.  Expected a program flash response");
            }
        }

        JumpToApplication();

        return true;
    }

    private List<byte[]> GetHexRecordsFromFile(string filePath)
    {
        // Create Message
        var asciiLines = File.ReadAllLines(filePath);
        var hexRecords = new List<byte[]>();

        // Convert ASCII Data to Hex Data
        foreach (var line in asciiLines)
        {
            var hexLine = new List<byte>();

            // If the line doesn't start with ":", it's not valid
            if (line[0] != ':') throw new InvalidDataException($"The hex record at line {line} doesn't start with the colon character \":\"");

            // If the line doesn't contain an odd number of values, it's not valid
            if (line.Length % 2 != 1) throw new InvalidDataException($"The hex record at line {line} doesn't contain an odd number of values");

            // Get two ascii characters and convert them to a hex value
            // Skip the first item as it is ":" and should be ignored
            for (var i = 1; i < line.Length; i += 2)
            {
                var asciiCharacters = new[] { line[i], line[i + 1] };
                hexLine.Add(Convert.ToByte(int.Parse(new string(asciiCharacters), NumberStyles.HexNumber)));
            }

            // Skip if this is in a protected memory range
            if (IsProtectedHexRecord(hexLine.ToArray()))
            {
                continue;
            }

            hexRecords.Add(hexLine.ToArray());
        }

        return hexRecords;
    }

    private bool IsProtectedHexRecord(byte[] hexRecord)
    {
        var offsetAddressArray = hexRecord.Skip(1).Take(2).ToArray();
        var recordType = hexRecord[3];
        var dataArray = hexRecord.Skip(4).Take(hexRecord.Length - 5).ToArray();

        // Set Base Address
        if (recordType == 0x04)
        {
            if (BitConverter.IsLittleEndian) Array.Reverse(dataArray);
            _baseAddress = BitConverter.ToUInt16(dataArray, 0);
        }
        // Data
        else if (recordType == 0x00)
        {
            if (BitConverter.IsLittleEndian) Array.Reverse(offsetAddressArray);
            var offsetAddress = BitConverter.ToUInt16(offsetAddressArray, 0);
            var hexRecordAddress = (_baseAddress << 16) | offsetAddress;

            if (hexRecordAddress >= BeginProtectedAddress && hexRecordAddress <= EndProtectedAddress)
            {
                return true;
            }
        }
        return false;
    }

    private void OpenDevice()
    {
        _hidDevice.OpenDevice();
    }

    private void CloseDevice()
    {
        _hidDevice.CloseDevice();
    }

    private HidReport CreateDeviceOutputReport(byte[] data)
    {
        if (data.Length > _hidDevice.Capabilities.OutputReportByteLength)
            throw new InvalidOperationException("Data size exceed the output report length of the device");

        var dataBuffer = new byte[_hidDevice.Capabilities.OutputReportByteLength];
        for (var i = 0; i < data.Length; i++)
        {
            dataBuffer[i] = data[i];
        }

        var outputReport = _hidDevice.CreateReport();
        outputReport.Data = dataBuffer;

        return outputReport;
    }
    #endregion

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            CloseDevice();
        }

        _disposed = true;
    }
}