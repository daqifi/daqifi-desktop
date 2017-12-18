
using System.Threading.Tasks;

namespace Daqifi.Desktop.Bootloader
{
    public interface IBootloader
    {
        bool LoadFirmware(string filePath);
        void RequestVersion();
    }
}
