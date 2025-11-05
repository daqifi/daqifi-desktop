namespace Daqifi.Desktop.Models;

/// <summary>
/// Represents the type of file stored on the SD card
/// </summary>
public enum SdCardFileType
{
    /// <summary>
    /// Unknown or unsupported file type
    /// </summary>
    Unknown,

    /// <summary>
    /// Protocol Buffer binary format
    /// </summary>
    Protobuf,

    /// <summary>
    /// JSON text format
    /// </summary>
    Json,

    /// <summary>
    /// CSV text format
    /// </summary>
    Csv
}
