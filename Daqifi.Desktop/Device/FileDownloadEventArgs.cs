using System;

namespace Daqifi.Desktop.Device
{
    public class FileDownloadEventArgs : EventArgs
    {
        public string Content { get; }

        public FileDownloadEventArgs(string content)
        {
            Content = content;
        }
    }
} 