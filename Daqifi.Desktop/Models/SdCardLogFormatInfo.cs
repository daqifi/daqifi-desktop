using Daqifi.Core.Device.SdCard;

namespace Daqifi.Desktop.Models;

/// <summary>
/// User-facing presentation for the SD card log formats Core can parse.
/// </summary>
/// <remarks>
/// Core's <see cref="SdCardFileParserFactory"/> is the single authority on which extensions are
/// recognized and which format each maps to; this type only supplies the display text and the
/// file-dialog filter built from that list. Keeping the extension list out of the desktop means a
/// format Core gains is offered for import automatically, and one it drops stops being offered —
/// neither needs an edit here.
/// </remarks>
public static class SdCardLogFormatInfo
{
    #region Constants
    /// <summary>Label used when Core cannot parse the file's extension.</summary>
    public const string UNKNOWN_FORMAT_DISPLAY = "Unknown";
    #endregion

    #region Public Methods
    /// <summary>
    /// Maps a file name to a user-facing format label, or
    /// <see cref="UNKNOWN_FORMAT_DISPLAY"/> when Core does not recognize the extension.
    /// </summary>
    /// <param name="fileName">The file name or path to label.</param>
    public static string DisplayNameFor(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            !SdCardFileParserFactory.TryDetectFormat(fileName, out var format))
        {
            return UNKNOWN_FORMAT_DISPLAY;
        }

        return format switch
        {
            SdCardLogFormat.Protobuf => "Protobuf",
            SdCardLogFormat.Json => "JSON",
            SdCardLogFormat.Csv => "CSV",
            // A format Core recognizes but this app has no label for yet — show the enum name
            // rather than claiming the file is unimportable.
            _ => format.ToString()
        };
    }

    /// <summary>
    /// Builds an <c>OpenFileDialog.Filter</c> covering every format Core can parse: one combined
    /// entry, one entry per format, then "All Files".
    /// </summary>
    public static string BuildOpenFileDialogFilter()
    {
        // "*.bin" rather than a bare ".bin" so the label lookup's Path.GetExtension sees a file
        // name that has an extension, not a name that merely starts with a dot.
        var patterns = SdCardFileParserFactory.SupportedExtensions
            .Select(extension => $"*{extension}")
            .ToList();

        var combined = string.Join(";", patterns);
        var perFormat = string.Join("|", patterns
            .Select(pattern => $"{DisplayNameFor(pattern)} ({pattern})|{pattern}"));

        return $"SD Card Log Files ({combined})|{combined}|{perFormat}|All Files (*.*)|*.*";
    }
    #endregion
}
