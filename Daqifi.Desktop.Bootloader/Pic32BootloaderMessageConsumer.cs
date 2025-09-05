namespace Daqifi.Desktop.Bootloader;

public static class Pic32BootloaderMessageConsumer
{
    #region Private Data
    private const byte StartOfHeader = 0x01;
    private const byte EndOfTransmission = 0x04;
    private const byte DataLinkEscape = 0x10;
    private const byte RequestVersionCommand = 0x01;
    private const byte EraseFlashCommand = 0x02;
    private const byte ProgramFlashCommand = 0x03;
    #endregion

    public static string DecodeVersionResponse(byte[] data)
    {
        var majorVersion = 0;
        var minorVersion = 0;

        if (data.Length < 2) return "Error";

        // Check if we start correctly
        if (data[0] != StartOfHeader) return "Error";

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

    public static bool DecodeProgramFlashResponse(byte[] data)
    {
        if (data.Length < 2) return false;

        // Check if we start correctly
        if (data[0] != StartOfHeader) return false;

        // Determine what type of response this is
        // Request Version Response
        return data[1] == ProgramFlashCommand;
    }

    public static bool DecodeEraseFlashResponse(byte[] data)
    {
        if (data.Length < 2) return false;

        // Check if we start correctly
        if (data[0] != StartOfHeader) return false;

        // Determine what type of response this is
        // Erase Flash Response
        return data[1] == EraseFlashCommand;
    }
}