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
        private HidDevice _device;
        private string _version;

        public string Version {
            get => _version;
            set
            {
                _version = value;
                NotifyPropertyChanged("Version");
            } }    

        #region IBootloader Methods

        public void RequestVersion()
        {
            var messageProducer = new Pic32BootloaderMessageProducer();
            var requestVersionMessage = messageProducer.CreateRequestVersionMessage();

            // Send Request
            const int vendorId = 0x4D8;
            const int productId = 0x03C;

            _device = HidDevices.Enumerate(vendorId, productId).FirstOrDefault();
            if (_device == null)
            {
                // TODO Handle
            }

            _device.OpenDevice();
            _device.ReadReport(OnVersionReceived);

            _device.Write(requestVersionMessage);
        }

        private void OnVersionReceived(HidReport report)
        {
            var deviceData = report.Data;
            var consumer = new Pic32BootloaderMessageConsumer();
            Version = consumer.DecodeMessage(deviceData);
        }
        
        /// <summary>
        /// [<SOH>…]<SOH><0x03>[<HEX_RECORD>…]<CRCL><CRCH><EOT>
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public bool LoadFirmware(string filePath)
        {
            // Create Message
            var loadFirmwareCommand = new byte[] { 0x03 };
            var asciiLines = File.ReadAllLines(filePath);
            var hexRecords = new List<byte[]>();
            var asciiData = asciiLines.Select(line => line.Remove(0, 1)).ToList();

            // Convert ASCII Data to Hex Data
            foreach (var line in asciiData)
            {
                var hexLine = new List<byte>();

                // If the line doesn't start with ":", it's not valid
                if (line[0] != ':') throw new Exception();

                // If the line doesn't contain an odd number of values, it's not valid
                if (line.Length %2 != 1) throw new Exception();

                // Get two ascii characters and convert them to a hex value
                for (var i = 1; i < line.Length; i+=2)
                {
                    var asciiCharacters = new []{line[i], line[i+1]};
                    hexLine.Add(Convert.ToByte(int.Parse(new string(asciiCharacters), NumberStyles.HexNumber)));
                }

                hexRecords.Add(hexLine.ToArray());
            }

            foreach (var hexRecord in hexRecords)
            {
                var data = loadFirmwareCommand.Concat(hexRecord).ToArray();
                var crc = new Crc16(data);

                // Send a hex record
                var loadFirmwareMessage = new List<byte>();
                //loadFirmwareMessage.Add(soh);
                loadFirmwareMessage.AddRange(loadFirmwareCommand);
                loadFirmwareMessage.AddRange(hexRecord);
                loadFirmwareMessage.Add(crc.Low);
                loadFirmwareMessage.Add(crc.High);
                //loadFirmwareMessage.Add(eot);


                // Receieve Response

            }

            //
            //loadFirmwareRequest.Add(soh);
            //loadFirmwareRequest.Add(command);
            //loadFirmwareRequest.AddRange(firmwareData);
            //loadFirmwareRequest.Add(crc.Low);
            //loadFirmwareRequest.Add(crc.High);
            //loadFirmwareRequest.Add(eot);

            // Reset Device
            // Force Firmware Upgrade Mode
            // Build Message
            // Program Flash
            return false;
        }
        #endregion


    }
}
