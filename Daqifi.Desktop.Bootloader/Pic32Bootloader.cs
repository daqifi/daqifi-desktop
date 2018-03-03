﻿using HidLibrary;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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

        private readonly HidFastReadDevice _hidDevice;
        private string _version;
        private bool _disposed;
        private ushort _baseAddress;
        private uint _beginProtectedAddress = 0x1D040000;
        private uint _endProtectedAddress = 0x1D057FFF;

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
        public Pic32Bootloader(HidFastReadDevice hidHidDevice)
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

        public void JumpToApplication()
        {
            var messageProducer = new Pic32BootloaderMessageProducer();
            var jumpToApplicationMessage = messageProducer.CreateJumpToApplicationMessage();
            var outputReport = CreateDeviceOutputReport(jumpToApplicationMessage);

            _hidDevice.WriteReport(outputReport);
        }

        public bool EraseFlash()
        {
            var messageProducer = new Pic32BootloaderMessageProducer();
            var eraseFlashMessage = messageProducer.CreateEraseFlashMessage();
            var outputReport = CreateDeviceOutputReport(eraseFlashMessage);

            _hidDevice.WriteReport(outputReport);

            var inputReport = _hidDevice.ReadReport();
            var consumer = new Pic32BootloaderMessageConsumer();
            return consumer.DecodeEraseFlashResponse(inputReport.Data);
        }

        /// <summary>
        /// [<SOH>…]<SOH><0x03>[<HEX_RECORD>…]<CRCL><CRCH><EOT>
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public bool LoadFirmware(string filePath, BackgroundWorker backgroundWorker)
        {
            var hexRecords = GetHexRecordsFromFile(filePath);
            var messageProducer = new Pic32BootloaderMessageProducer();
            var messageConsumer = new Pic32BootloaderMessageConsumer();

            //if (!EraseFlash())
            //{
            //    throw new InvalidDataException("There was a problem erasing the flash");
            //}

            for (var i = 0; i < hexRecords.Count; i++)
            {
                backgroundWorker.ReportProgress(i * 100 / hexRecords.Count);

                // Send a hex record
                var loadFirmwareMessage = messageProducer.CreateProgramFlashMessage(hexRecords[i]);
                var outputReport = CreateDeviceOutputReport(loadFirmwareMessage);

                _hidDevice.WriteReport(outputReport);

                var report = _hidDevice.FastReadReport();
                var successfulResponse = messageConsumer.DecodeProgramFlashResponse(report.Data);

                if(!successfulResponse) throw new InvalidDataException("The response from the device was invalid.  Exepcted a program flash response");
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
                if (IsProtectedHexRecord(hexLine.ToArray())) continue;

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

                if (hexRecordAddress >= _beginProtectedAddress && hexRecordAddress <= _endProtectedAddress)
                {
                    return true;
                }
            }
            return false;
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
}
