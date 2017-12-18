using HidLibrary;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Daqifi.Desktop.Bootloader
{
    public class Pic32Bootloader : ObservableObject, IBootloader, IDisposable
    {
        #region Private Data
        //private const int VendorId = 0x4D8;
        //private const int ProductId = 0x03C;

        private readonly HidDevice _hidDevice;
        private string _version;
        private bool _attached;
        private bool _disposed;
        #endregion

        #region Properties
        public string Version
        {
            get => _version;
            set
            {
                _version = value;
                NotifyPropertyChanged("Version");
            }
        }
        #endregion

        #region Constructor
        public Pic32Bootloader(HidDevice hidHidDevice)
        {
            _hidDevice = hidHidDevice;
            OpenDevice();
        }
        #endregion 

        #region IBootloader Methods

        public void RequestVersion()
        {
            var messageProducer = new Pic32BootloaderMessageProducer();
            var requestVersionMessage = messageProducer.CreateRequestVersionMessage();
            var outputReport = CreateDeviceOutputReport(requestVersionMessage);

            _hidDevice.WriteReport(outputReport);

            var inputReport = _hidDevice.ReadReport();
            var consumer = new Pic32BootloaderMessageConsumer();
            Version = consumer.DecodeVersionResponse(inputReport.Data);
        }

        /// <summary>
        /// [<SOH>…]<SOH><0x03>[<HEX_RECORD>…]<CRCL><CRCH><EOT>
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public bool LoadFirmware(string filePath)
        {
            var hexRecords = GetHexRecordsFromFile(filePath);
            var messageProducer = new Pic32BootloaderMessageProducer();
            var messageConsumer = new Pic32BootloaderMessageConsumer();

            foreach (var hexRecord in hexRecords)
            {
                // Send a hex record
                var loadFirmwareMessage = messageProducer.CreateProgramFlashMessage(hexRecord);
                var outputReport = CreateDeviceOutputReport(loadFirmwareMessage);

                _hidDevice.WriteReport(outputReport);

                var report = _hidDevice.ReadReport();
                var successfulResponse = messageConsumer.DecodeProgramFlashResponse(report.Data);

                if(!successfulResponse) throw new InvalidDataException("The response from the device was invalid.  Exepcted a program flash response");
            }
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

                hexRecords.Add(hexLine.ToArray());
            }

            return hexRecords;
        }

        public static byte[] Combine(params byte[][] arrays)
        {
            byte[] ret = new byte[arrays.Sum(x => x.Length)];
            int offset = 0;
            foreach (byte[] data in arrays)
            {
                Buffer.BlockCopy(data, 0, ret, offset, data.Length);
                offset += data.Length;
            }
            return ret;
        }

        private void OpenDevice()
        {
            _hidDevice.OpenDevice();
        }

        public void CloseDevice()
        {
            _hidDevice.CloseDevice();
        }

        //private void HandleReportReceived(HidReport report)
        //{
        //    var test = report;
        //}

        //private void HandleVersionReceived(HidReport report)
        //{
        //    if (!_attached) return;

        //    var staus = report.ReadStatus;

        //    var deviceData = report.Data;
        //    var consumer = new Pic32BootloaderMessageConsumer();
        //    Version = consumer.DecodeVersionResponse(deviceData);
        //}

        private void HandleProgramFlashResponseReceived(HidReport report)
        {
            if (!_attached) return;

            var status = report.ReadStatus;
            var deviceData = report.Data;
            var consumer = new Pic32BootloaderMessageConsumer();
            var success = consumer.DecodeProgramFlashResponse(deviceData);

            if (status != HidDeviceData.ReadStatus.NotConnected)
            {
                _hidDevice.ReadReport(HandleProgramFlashResponseReceived);
            }
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

        private void HandleDeviceRemoved()
        {
            _attached = false;
        }

        private void HandleDeviceInserted()
        {
            _attached = true;
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
}
