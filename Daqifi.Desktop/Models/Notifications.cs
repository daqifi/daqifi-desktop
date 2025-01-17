using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Daqifi.Desktop.Models
{
    public class Notifications
    {
        public bool isFirmwareUpdate {  get; set; }

        public string DeviceSerialNo { get; set; }

        public string Message { get; set; }

        public string Link { get; set; }
    }
}
