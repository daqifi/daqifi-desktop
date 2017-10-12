using System;
using System.Data;
using System.Linq;

namespace Daqifi.Desktop.Bootloader
{
    public class Pic32BootloaderMessageConsumer
    {
        #region Private Data
        private const byte StartOfHeader = 0x01;
        private const byte EndOfTransmission = 0x04;
        private const byte DataLinkEscape = 0x10;
        private const byte RequestVersionCommand = 0x01;
        private const byte EraseFlashCommand = 0x02;
        #endregion

        public string DecodeVersionResponse(byte[] data)
        {
            int majorVersion = 0;
            int minorVersion = 0;

            if (data.Length < 2) return "Error";

            // Check if we start correctly
            if (data.First() != StartOfHeader) return "Error";

            // Determine what type of response this is
            // Request Version Response
            if (data[1] == DataLinkEscape && data[2] == RequestVersionCommand)
            {
                var pointer = 3;

                majorVersion = data[pointer] == DataLinkEscape ? data[++pointer] : data[pointer];
                pointer++;
                minorVersion = data[pointer] == DataLinkEscape ? data[++pointer] : data[pointer];
            }

            return $"{majorVersion}.{minorVersion}";
        }

        public bool DecodeProgramFlashResponse(byte[] data)
        {
            // Check if we start correctly
            if (data.First() != StartOfHeader) return false;



            return true;
        }
    }
}