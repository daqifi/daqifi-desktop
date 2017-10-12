using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HidLibrary;

namespace Daqifi.Desktop.Bootloader
{
    public class Pic32Bootloader : ObservableObject, IBootloader
    {
        private readonly HidDevice _device;
        private string _version;
        private bool _attached;

        public string Version {
            get => _version;
            set
            {
                _version = value;
                NotifyPropertyChanged("Version");
            } }

        public Pic32Bootloader()
        {
            const int vendorId = 0x4D8;
            const int productId = 0x03C;

            _device = HidDevices.Enumerate(vendorId, productId).FirstOrDefault();
            if (_device == null)
            {
                // TODO Handle
            }

            _device.Inserted += HandleDeviceInserted;
            _device.Removed += HandleDeviceRemoved;
            _device.OpenDevice();
            _device.MonitorDeviceEvents = true;
        }

        #region IBootloader Methods

        public void RequestVersion()
        {
            // Send Request
            var messageProducer = new Pic32BootloaderMessageProducer();
            var requestVersionMessage = messageProducer.CreateRequestVersionMessage();
            
            _device.ReadReport(HandleVersionReceived);
            _device.Write(requestVersionMessage);

        }

        /// <summary>
        /// [<SOH>…]<SOH><0x03>[<HEX_RECORD>…]<CRCL><CRCH><EOT>
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public bool LoadFirmware(string filePath)
        {
            var messageProducer = new Pic32BootloaderMessageProducer();

            // Create Message
            var asciiLines = File.ReadAllLines(filePath);
            var hexRecords = new List<byte[]>();
            //var asciiData = asciiLines.Select(line => line.Remove(0, 1)).ToList();

            // Convert ASCII Data to Hex Data
            foreach (var line in asciiLines)
            {
                var hexLine = new List<byte>();

                // If the line doesn't start with ":", it's not valid
                if (line[0] != ':') throw new Exception();

                // If the line doesn't contain an odd number of values, it's not valid
                if (line.Length %2 != 1) throw new Exception();

                // Get two ascii characters and convert them to a hex value
                // Skip the first item as it is ":" and should be ignored
                for (var i = 1; i < line.Length; i+=2)
                {
                    var asciiCharacters = new []{line[i], line[i+1]};
                    hexLine.Add(Convert.ToByte(int.Parse(new string(asciiCharacters), NumberStyles.HexNumber)));
                }

                hexRecords.Add(hexLine.ToArray());
            }

            _device.ReadReport(HandleProgramFlashResponseReceived);

            //// Send 10 hex at a time
            //while (hexList.Any())
            //{
            //    byte[] send;
            //    if (hexList.Count >= 11)
            //    {
            //        send = Combine(hexList.(11).ToArray());
            //    }
            //    else
            //    {
            //        send = Combine(hexRecords.Take(hexRecords.Count).ToArray());
            //    }
            //    var loadFirmwareMessage = messageProducer.CreateProgramFlashMessage(send);

            //    while(loadFirmwareMessage.Any())
            //    {
            //        if (loadFirmwareMessage.Length >= 64)
            //        {
            //            _device.WriteFeatureData(loadFirmwareMessage.Take(64).ToArray());
            //        }
            //        else
            //        {
            //            _device.Write(loadFirmwareMessage.Take(loadFirmwareMessage.Length).ToArray());
            //        }
            //    }

            //    _device.Write(loadFirmwareMessage);
            //}

            for (var i = 0; i < hexRecords.Count; i++)
            {
                var hexRecord = hexRecords[i];
                if (!_attached) return false;

                // Send a hex record
                var loadFirmwareMessage = messageProducer.CreateProgramFlashMessage(hexRecord);
                _device.Write(loadFirmwareMessage);
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

        private void HandleVersionReceived(HidReport report)
        {
            if (!_attached) return;

            var staus = report.ReadStatus;

            var deviceData = report.Data;
            var consumer = new Pic32BootloaderMessageConsumer();
            Version = consumer.DecodeVersionResponse(deviceData);
        }

        private void HandleProgramFlashResponseReceived(HidReport report)
        {
            if (!_attached) return;

            var status = report.ReadStatus;
            var deviceData = report.Data;
            var consumer = new Pic32BootloaderMessageConsumer();
            var success = consumer.DecodeProgramFlashResponse(deviceData);

            if (status != HidDeviceData.ReadStatus.NotConnected)
            {
                _device.ReadReport(HandleProgramFlashResponseReceived);
            }
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



    }
}
