namespace Daqifi.Desktop.Bootloader;

public static class Pic32BootloaderMessageProducer
{
    #region Private Data
    private const byte StartOfHeader = 0x01;
    private const byte EndOfTransmission = 0x04;
    private const byte DataLinkEscape = 0x10;
    private const byte RequestVersionCommand = 0x01;
    private const byte EraseFlashCommand = 0x02;
    private const byte ProgramFlashCommand = 0x03;
    private const byte ReadCrcCommand = 0x04;
    private const byte JumpToApplicationCommand = 0x05;
    #endregion

    #region Message Produce Methods
    public static byte[] CreateRequestVersionMessage()
    {
        return ConstructDataPacket(RequestVersionCommand);
    }

    public static byte[] CreateEraseFlashMessage()
    {
        return ConstructDataPacket(EraseFlashCommand);
    }

    public static byte[] CreateProgramFlashMessage(byte[] hexRecord)
    {
        var command = new List<byte> {ProgramFlashCommand};
        command.AddRange(hexRecord);
        return ConstructDataPacket(command.ToArray());
    }

    public static byte[] CreateReadCrcMessage()
    {
        throw new NotImplementedException();
    }

    public static byte[] CreateJumpToApplicationMessage()
    {
        return ConstructDataPacket(JumpToApplicationCommand);
    }
    #endregion

    #region Helper Methods
    private static byte[] ConstructDataPacket(byte command)
    {
        return ConstructDataPacket([command]);
    }

    private static byte[] ConstructDataPacket(byte[] command)
    {
        var packet = new List<byte>();
        var crc = new Crc16(command);

        var commandAndCrc = new List<byte>();
        commandAndCrc.AddRange(command);
        commandAndCrc.Add(crc.Low);
        commandAndCrc.Add(crc.High);

        packet.Add(StartOfHeader);

        // Add DLE whenever SOH, EOT, or DLE are present
        foreach (var item in commandAndCrc)
        {
            if (item == StartOfHeader || item == EndOfTransmission || item == DataLinkEscape)
            {
                packet.Add(DataLinkEscape);
            }
            packet.Add(item);
        }

        packet.Add(EndOfTransmission);
        return packet.ToArray();
    }
    #endregion
}