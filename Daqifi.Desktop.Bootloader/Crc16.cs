namespace Daqifi.Desktop.Bootloader;

public class Crc16
{
    private readonly ushort[] _table =
    [
        0x0000, 0x1021, 0x2042, 0x3063, 0x4084, 0x50a5, 0x60c6, 0x70e7,
        0x8108, 0x9129, 0xa14a, 0xb16b, 0xc18c, 0xd1ad, 0xe1ce, 0xf1ef
    ];

    public ushort Crc { get; private set; }
    public byte Low => (byte) (Crc & 0xff);
    public byte High => (byte) (Crc >> 8);

    public Crc16(byte[] data)
    {
        CalculateCrc(data);
    }

    private void CalculateCrc(byte[] data)
    {
        ushort crc = 0;
        foreach (var item in data)
        {
            var i = (uint)((crc >> 12) ^ (item >> 4));
            crc = (ushort)(_table[i & 0x0F] ^ (crc << 4));
            i = (uint)((crc >> 12) ^ (item >> 0));
            crc = (ushort)(_table[i & 0x0F] ^ (crc << 4));
        }
        Crc = crc;
    }
}