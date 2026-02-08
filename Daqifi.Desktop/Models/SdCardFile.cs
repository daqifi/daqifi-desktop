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
    /// A user-facing display value for CreatedDate.
    /// </summary>
    public string CreatedDateDisplay => CreatedDate == DateTime.MinValue
        ? "Unknown"
        : CreatedDate.ToString("g");
}
