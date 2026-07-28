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
    /// The per-format entries that lived in <c>DaqifiViewModel.ImportSdCardLogFile</c>'s filter
    /// literal before the switch to Core-driven construction. Only these three are the desktop's
    /// to keep stable; which other entries appear — and in what order — is Core's call now, so
    /// they are asserted individually rather than by pinning the whole filter string.
    /// </summary>
    private static readonly string[] LEGACY_FILTER_ENTRIES =
    [
        "Protobuf (*.bin)|*.bin",
        "JSON (*.json)|*.json",
        "CSV (*.csv)|*.csv"
    ];

    [TestMethod]
    public void BuildOpenFileDialogFilter_HasTheShapeTheImportDialogExpects()
    {
        // Arrange — the expectation is derived from Core, so a format Core adds flows through
        // instead of failing CI.
        var patterns = SdCardFileParserFactory.SupportedExtensions
            .Select(extension => $"*{extension}")
            .ToList();
        var combined = string.Join(";", patterns);

        // Act
        var filter = SdCardLogFormatInfo.BuildOpenFileDialogFilter();

        // Assert — a combined "all log files" group first, an All Files escape hatch last, and
        // description/pattern pairs throughout, which is all OpenFileDialog.Filter requires.
        StringAssert.StartsWith(filter, $"SD Card Log Files ({combined})|{combined}|",
            "The dialog must still open on a combined group listing every format Core parses.");
        StringAssert.EndsWith(filter, "|All Files (*.*)|*.*",
            "The dialog must still let the user reach a file Core has no parser for.");
        Assert.AreEqual(0, filter.Split('|').Length % 2,
            "OpenFileDialog.Filter is description/pattern pairs, so its section count must be even.");
    }

    [TestMethod]
    public void BuildOpenFileDialogFilter_CoversEveryExtensionCoreSupports()
    {
        // Act
        var filter = SdCardLogFormatInfo.BuildOpenFileDialogFilter();

        // Assert — the guarantee that makes this Core-driven rather than a second hardcoded list.
        foreach (var extension in SdCardFileParserFactory.SupportedExtensions)
        {
            var pattern = $"*{extension}";

            StringAssert.Contains(filter, $"({pattern})|{pattern}",
                $"Core parses '{extension}', so the import dialog must offer it its own entry.");
        }
    }

    [TestMethod]
    public void BuildOpenFileDialogFilter_KeepsTheEntriesItReplacedVerbatim()
    {
        // Act
        var filter = SdCardLogFormatInfo.BuildOpenFileDialogFilter();

        // Assert — the three formats the hardcoded literal offered are still offered under the
        // same labels and patterns, so the import dialog reads the same as it did.
        foreach (var entry in LEGACY_FILTER_ENTRIES)
        {
            StringAssert.Contains(filter, entry,
                $"The filter this replaced offered '{entry}', so the generated one must too.");
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
        Assert.AreEqual(SdCardLogFormatInfo.UNKNOWN_FORMAT_DISPLAY,
            SdCardLogFormatInfo.DisplayNameFor(string.Empty));
        Assert.AreEqual(SdCardLogFormatInfo.UNKNOWN_FORMAT_DISPLAY,
            SdCardLogFormatInfo.DisplayNameFor("   "));
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
