using System.ComponentModel;

namespace Daqifi.Desktop.Bootloader;

public interface IBootloader
{
    bool LoadFirmware(string filePath, BackgroundWorker backgroundWorker);
    void RequestVersion();
    bool EraseFlash();
}