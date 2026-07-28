using System.Globalization;

namespace Daqifi.Desktop.Models;

public class SdCardFile
{
    /// <summary>
    /// The name of the file on the SD card
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// The created date of the file
    /// </summary>
    public DateTime CreatedDate { get; set; }

    /// <summary>
    /// A user-facing display value for CreatedDate.
    /// </summary>
    public string CreatedDateDisplay => CreatedDate == DateTime.MinValue
        ? "Unknown"
        // User-facing display text, so the current UI culture is the correct format provider.
        : CreatedDate.ToString("g", CultureInfo.CurrentCulture);

    /// <summary>
    /// Gets a user-facing format label based on the file extension.
    /// </summary>
    public string FormatDisplay => SdCardLogFormatInfo.DisplayNameFor(FileName);
}
