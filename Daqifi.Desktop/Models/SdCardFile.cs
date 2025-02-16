using System;

namespace Daqifi.Desktop.Models
{
    public class SdCardFile
    {
        /// <summary>
        /// The name of the file on the SD card
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// The size of the file in bytes
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// The created date of the file
        /// </summary>
        public DateTime CreatedDate { get; set; }

        /// <summary>
        /// The formatted size string (e.g., "1.2 MB")
        /// </summary>
        public string FormattedSize
        {
            get
            {
                string[] sizes = { "B", "KB", "MB", "GB" };
                double len = Size;
                int order = 0;
                while (len >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    len = len / 1024;
                }
                return $"{len:0.##} {sizes[order]}";
            }
        }
    }
} 