using Daqifi.Core.Device.SdCard;
using Daqifi.Desktop.Models;

namespace Daqifi.Desktop.Test.Models;

/// <summary>
/// Covers the SD card log format presentation that replaced the desktop's own hardcoded
/// <c>.bin</c>/<c>.json</c>/<c>.csv</c> lists (daqifi-core#307). The point of the change is that
/// Core's <see cref="SdCardFileParserFactory"/> — not this app — decides which extensions are
/// importable, so these tests assert the mapping is driven off Core rather than re-asserting a
/// second hardcoded list.
/// </summary>
[TestClass]
public class SdCardLogFormatInfoTests
{
    /// <summary>
    /// The filter literal that lived in <c>DaqifiViewModel.ImportSdCardLogFile</c> before the
    /// switch to Core-driven construction. Pinned so the refactor is provably behavior-preserving.
    /// </summary>
    private const string LEGACY_FILTER =
        "SD Card Log Files (*.bin;*.json;*.csv)|*.bin;*.json;*.csv|" +
        "Protobuf (*.bin)|*.bin|JSON (*.json)|*.json|CSV (*.csv)|*.csv|" +
        "All Files (*.*)|*.*";

    [TestMethod]
    public void BuildOpenFileDialogFilter_MatchesTheFilterItReplaced()
    {
        // Act
        var filter = SdCardLogFormatInfo.BuildOpenFileDialogFilter();

        // Assert
        Assert.AreEqual(LEGACY_FILTER, filter,
            "Building the filter from Core's SupportedExtensions must reproduce the literal it " +
            "replaced — otherwise the import dialog silently changed which files it offers.");
    }

    [TestMethod]
    public void BuildOpenFileDialogFilter_CoversEveryExtensionCoreSupports()
    {
        // Act
        var filter = SdCardLogFormatInfo.BuildOpenFileDialogFilter();

        // Assert — the guarantee that makes this Core-driven rather than a second hardcoded list.
        foreach (var extension in SdCardFileParserFactory.SupportedExtensions)
        {
            StringAssert.Contains(filter, $"*{extension}",
                $"Core parses '{extension}', so the import dialog must offer it.");
        }
    }

    [TestMethod]
    public void DisplayNameFor_LabelsEveryFormatCoreCanDetect()
    {
        // Act & Assert — no extension Core recognizes may fall through to "Unknown", which is
        // what the user sees in the SD card file list's Format column.
        foreach (var extension in SdCardFileParserFactory.SupportedExtensions)
        {
            var display = SdCardLogFormatInfo.DisplayNameFor($"log_20260623_143217{extension}");

            Assert.AreNotEqual(SdCardLogFormatInfo.UNKNOWN_FORMAT_DISPLAY, display,
                $"Core detects a format for '{extension}', so it must have a user-facing label.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(display));
        }
    }

    [TestMethod]
    public void DisplayNameFor_UsesTheLabelsShownBeforeTheCoreSwitch()
    {
        // Assert — the three formats that shipped with the old hardcoded switch keep their exact
        // labels, so the Format column reads the same as it did.
        Assert.AreEqual("Protobuf", SdCardLogFormatInfo.DisplayNameFor("log.bin"));
        Assert.AreEqual("JSON", SdCardLogFormatInfo.DisplayNameFor("log.json"));
        Assert.AreEqual("CSV", SdCardLogFormatInfo.DisplayNameFor("log.csv"));
    }

    [TestMethod]
    public void DisplayNameFor_IsCaseInsensitive()
    {
        // Arrange — firmware has shipped upper-case names on the card before now.
        // Act & Assert
        Assert.AreEqual("Protobuf", SdCardLogFormatInfo.DisplayNameFor("LOG.BIN"));
    }

    [TestMethod]
    public void DisplayNameFor_UnrecognizedExtension_IsUnknown()
    {
        Assert.AreEqual(SdCardLogFormatInfo.UNKNOWN_FORMAT_DISPLAY,
            SdCardLogFormatInfo.DisplayNameFor("readme.txt"));
    }

    [TestMethod]
    public void DisplayNameFor_NameWithNoExtension_IsUnknown()
    {
        Assert.AreEqual(SdCardLogFormatInfo.UNKNOWN_FORMAT_DISPLAY,
            SdCardLogFormatInfo.DisplayNameFor("LOG"));
    }

    [TestMethod]
    public void DisplayNameFor_EmptyOrWhitespaceName_IsUnknownRatherThanThrowing()
    {
        // Core's TryDetectFormat throws on null; an empty or blank name reaching the Format column
        // must degrade to a label, not take down the SD card file list.
        Assert.AreEqual(SdCardLogFormatInfo.UNKNOWN_FORMAT_DISPLAY, SdCardLogFormatInfo.DisplayNameFor(string.Empty));
        Assert.AreEqual(SdCardLogFormatInfo.UNKNOWN_FORMAT_DISPLAY, SdCardLogFormatInfo.DisplayNameFor("   "));
    }

    [TestMethod]
    public void SdCardFile_FormatDisplay_DelegatesToTheSharedMapping()
    {
        // Arrange
        var file = new SdCardFile { FileName = "log_20260623_143217.csv" };

        // Act & Assert
        Assert.AreEqual(SdCardLogFormatInfo.DisplayNameFor(file.FileName), file.FormatDisplay);
        Assert.AreEqual("CSV", file.FormatDisplay);
    }
}
