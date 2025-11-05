using Daqifi.Desktop.Models;

namespace Daqifi.Desktop.Services;

/// <summary>
/// Interface for detecting file types from file names and content
/// </summary>
public interface IFileTypeDetector
{
    /// <summary>
    /// Detects the file type based on file name and optionally content
    /// </summary>
    /// <param name="fileName">The name of the file</param>
    /// <param name="fileContent">Optional file content for content-based detection</param>
    /// <returns>The detected file type</returns>
    SdCardFileType DetectFileType(string fileName, byte[]? fileContent = null);
}

/// <summary>
/// Service for detecting file types from file names and content
/// </summary>
public class FileTypeDetector : IFileTypeDetector
{
    #region Constants
    private const int PROTOBUF_FIELD_TAG_THRESHOLD = 10;
    #endregion

    #region Public Methods
    /// <summary>
    /// Detects the file type based on file name and optionally content
    /// </summary>
    /// <param name="fileName">The name of the file</param>
    /// <param name="fileContent">Optional file content for content-based detection</param>
    /// <returns>The detected file type</returns>
    public SdCardFileType DetectFileType(string fileName, byte[]? fileContent = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return SdCardFileType.Unknown;
        }

        // First try extension-based detection
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var typeFromExtension = extension switch
        {
            ".bin" => SdCardFileType.Protobuf,
            ".proto" => SdCardFileType.Protobuf,
            ".pb" => SdCardFileType.Protobuf,
            ".json" => SdCardFileType.Json,
            ".csv" => SdCardFileType.Csv,
            _ => SdCardFileType.Unknown
        };

        // If we have a definitive extension match and no content, return it
        if (typeFromExtension != SdCardFileType.Unknown && fileContent == null)
        {
            return typeFromExtension;
        }

        // If we have content, use content-based detection for verification or unknown extensions
        if (fileContent != null && fileContent.Length > 0)
        {
            var typeFromContent = DetectFromContent(fileContent);

            // If extension says Unknown but content detection found something, use content
            if (typeFromExtension == SdCardFileType.Unknown && typeFromContent != SdCardFileType.Unknown)
            {
                return typeFromContent;
            }

            // If both agree or extension is unknown, use content-based detection
            if (typeFromExtension == typeFromContent || typeFromExtension == SdCardFileType.Unknown)
            {
                return typeFromContent;
            }

            // If they disagree, prefer extension for known types
            return typeFromExtension;
        }

        return typeFromExtension;
    }
    #endregion

    #region Private Methods
    /// <summary>
    /// Detects file type from content analysis
    /// </summary>
    private SdCardFileType DetectFromContent(byte[] content)
    {
        if (content == null || content.Length == 0)
        {
            return SdCardFileType.Unknown;
        }

        // Check for JSON (starts with { or [)
        if (content.Length > 0 && (content[0] == '{' || content[0] == '['))
        {
            return SdCardFileType.Json;
        }

        // Check for CSV (look for common CSV patterns in first few bytes)
        if (IsLikelyCsv(content))
        {
            return SdCardFileType.Csv;
        }

        // Check for Protobuf (binary format with field tags)
        if (IsLikelyProtobuf(content))
        {
            return SdCardFileType.Protobuf;
        }

        return SdCardFileType.Unknown;
    }

    /// <summary>
    /// Checks if content is likely CSV format
    /// </summary>
    private static bool IsLikelyCsv(byte[] content)
    {
        // CSV files are text-based, check first 512 bytes for CSV patterns
        var sampleSize = Math.Min(512, content.Length);
        var sampleText = System.Text.Encoding.UTF8.GetString(content, 0, sampleSize);

        // Look for CSV indicators: commas, newlines, and printable text
        var hasCommas = sampleText.Contains(',');
        var hasNewlines = sampleText.Contains('\n') || sampleText.Contains('\r');
        var isPrintable = sampleText.All(c => c == '\r' || c == '\n' || c == '\t' || (c >= 32 && c < 127));

        return hasCommas && hasNewlines && isPrintable;
    }

    /// <summary>
    /// Checks if content is likely Protobuf format
    /// </summary>
    private static bool IsLikelyProtobuf(byte[] content)
    {
        // Protobuf uses a tag-length-value encoding
        // Tags are typically small integers (field numbers 1-15 use 1 byte)
        // Check for patterns consistent with protobuf encoding

        if (content.Length < 4)
        {
            return false;
        }

        // Count how many bytes look like protobuf field tags
        var fieldTagCount = 0;
        for (var i = 0; i < Math.Min(100, content.Length); i++)
        {
            var b = content[i];
            // Protobuf field tags are wire_type | (field_number << 3)
            // Wire types: 0(varint), 1(64-bit), 2(length-delimited), 5(32-bit)
            // So valid low 3 bits: 0,1,2,5
            var wireType = b & 0x07;
            if (wireType == 0 || wireType == 1 || wireType == 2 || wireType == 5)
            {
                fieldTagCount++;
            }
        }

        // If a significant portion looks like field tags, likely protobuf
        return fieldTagCount > PROTOBUF_FIELD_TAG_THRESHOLD;
    }
    #endregion
}
