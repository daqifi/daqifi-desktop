
using System.ComponentModel;
using System.Threading.Tasks;

namespace Daqifi.Desktop.Bootloader
{
    public interface IBootloader
    {
        bool LoadFirmware(string filePath, BackgroundWorker backgroundWorker);
        void RequestVersion();
        bool EraseFlash();
    }
}
