namespace Daqifi.Desktop.Models;

public class SdCardFile
{
    /// <summary>
    /// The name of the file on the SD card
    /// </summary>
    public string FileName { get; init; }

    /// <summary>
    /// The created date of the file
    /// </summary>
    public DateTime CreatedDate { get; set; }

    /// <summary>
    /// The type of the file based on extension and content
    /// </summary>
    public SdCardFileType FileType { get; set; } = SdCardFileType.Unknown;

    /// <summary>
    /// The size of the file in bytes (optional, may not be available from device)
    /// </summary>
    public long? FileSizeBytes { get; set; }
}