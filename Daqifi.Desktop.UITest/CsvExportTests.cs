using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FlaUI.Core.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daqifi.Desktop.UITest;

/// <summary>
/// Scenario 6 — CSV export of a logged session (issue #555). Drives the real GUI
/// out-of-process: connects to the physically attached device, runs a short logging
/// session (reusing the <see cref="LoggingSessionTests"/> flow), then exports the
/// session to CSV and validates the produced file black-box.
///
/// The export is made deterministic by the <c>DAQIFI_TEST_EXPORT_PATH</c> hook (see
/// <c>AppDataPaths.TestExportPath</c>): with it set, the export commands write straight
/// into a harness-owned temp directory with <b>no</b> native <c>SaveFileDialog</c>, so the
/// test never has to script a modal dialog out-of-process. The harness then reads the CSV
/// from disk and checks structure (header, one field per channel, RFC 4180 consistency),
/// numeric well-formedness (parseable values, monotonic timestamps), and — crucially —
/// that the number of value cells equals the session's persisted sample count (the app logs
/// that count when the session finalizes, giving an out-of-process oracle). Requires a
/// DAQiFi device.
/// </summary>
[TestClass]
public class CsvExportTests : DaqifiAppFixture
{
    #region Constants
    // A gentle frequency keeps the UI responsive for out-of-process automation while the
    // session streams (mirrors LoggingSessionTests; scenario 2 covers the 1000 Hz case).
    private const double TARGET_FREQUENCY_HZ = 100d;

    // Deliberate data-accrual dwell (NOT a readiness wait): let the session stream long
    // enough to persist a meaningful number of samples before stopping. The DatabaseLogger
    // consumer flushes every ~100ms, so a couple of seconds yields a solid batch.
    private static readonly TimeSpan AccrualDwell = TimeSpan.FromSeconds(3);

    // The absolute-time header the exporter writes by default (ExportRelativeTime = false).
    private const string TIME_HEADER = "Time";
    #endregion

    #region Export Hook Wiring
    // Per-test temp directory the child app exports into (one fresh dir per test instance —
    // MSTest news-up the class per test method). Routed to the app via DAQIFI_TEST_EXPORT_PATH.
    private readonly string _exportDir = Path.Combine(
        Path.GetTempPath(), "daqifi_csv_export_test_" + Guid.NewGuid().ToString("N"));

    protected override string? ExportHookDirectory => _exportDir;
    #endregion

    #region Tests
    /// <summary>
    /// Exports a single logged session via its per-row EXPORT button (the UI-invoked variant)
    /// and validates the produced CSV.
    /// </summary>
    [TestMethod]
    [TestCategory("Ui")]
    [TestCategory("RequiresDevice")]
    public void ExportLoggedSession_ProducesValidCsv()
    {
        try
        {
            var (sessionName, sampleCount, channelCount) = RunShortLoggingSession();

            // Act — export the just-finalized session (the newest, last row) via its per-row EXPORT
            // button. With the hook active, this writes "{sessionName}.csv" into _exportDir with no
            // SaveFileDialog; the file-name wait below confirms the right session was exported.
            ExportNewestLoggedSession();

            // Assert — the CSV lands on disk and is well-formed and complete.
            var csvPath = WaitForExportedCsv(sessionName + ".csv", TimeSpan.FromSeconds(30));
            ValidateExportedCsv(csvPath, sampleCount, channelCount);

            // The single-session export must produce exactly that one file in the dir.
            var produced = Directory.GetFiles(_exportDir, "*.csv");
            Assert.AreEqual(
                1, produced.Length,
                "Single-session export should write exactly one CSV into the hook directory, " +
                $"but found {produced.Length}: {string.Join(", ", produced.Select(Path.GetFileName))}.");
        }
        finally
        {
            TryDeleteExportDir();
        }
    }

    /// <summary>
    /// Exports every session at once via EXPORT ALL and validates the just-logged session's
    /// file, proving the hook also covers the "Export All" path.
    /// </summary>
    [TestMethod]
    [TestCategory("Ui")]
    [TestCategory("RequiresDevice")]
    public void ExportAllLoggedSessions_ProducesValidCsv()
    {
        try
        {
            var (sessionName, sampleCount, channelCount) = RunShortLoggingSession();

            // Act — export ALL sessions. With the hook active this writes one "{session}.csv"
            // per session into _exportDir with no FolderBrowserDialog.
            ExportAllLoggedSessions();

            // Assert — the just-finalized session's file appears (Export All writes files
            // session-by-session, so a generous timeout rides out a larger pre-existing DB)
            // and validates identically to the single-session export.
            var csvPath = WaitForExportedCsv(sessionName + ".csv", TimeSpan.FromSeconds(120));
            ValidateExportedCsv(csvPath, sampleCount, channelCount);

            Assert.IsTrue(
                Directory.GetFiles(_exportDir, "*.csv").Length >= 1,
                "Export All produced no CSV files in the hook directory.");
        }
        finally
        {
            TryDeleteExportDir();
        }
    }
    #endregion

    #region Scenario Helpers
    /// <summary>
    /// Connects, configures logging (frequency + analog channels), runs a short session, stops
    /// it, and waits for the app's session-finalize log line. Returns the finalized session's
    /// name (<c>Session_{id}</c>), its persisted sample count, and the active analog channel
    /// count. Fails the test if no data accrued (an empty session is discarded, never finalized).
    /// </summary>
    private (string sessionName, long sampleCount, int channelCount) RunShortLoggingSession()
    {
        var transport = ResolveTransport();
        ConnectFirstDevice(transport);

        SetSamplingFrequency(TARGET_FREQUENCY_HZ);
        var channelCount = EnableAllAnalogChannels();
        Assert.IsTrue(channelCount > 0, "Pre-condition failed: no analog channels became active.");

        StartLogging();
        // Let the session stream a short, fixed window so a meaningful sample batch persists.
        System.Threading.Thread.Sleep((int)AccrualDwell.TotalMilliseconds);
        StopLogging();

        // The app logs "Persisted sample count N for session S" once the session is finalized
        // (after every buffered sample is flushed). That gives us the exact session id and the
        // authoritative sample count to cross-check the CSV against.
        var (sessionId, sampleCount) = WaitForPersistedSampleCount(TimeSpan.FromSeconds(30));
        Assert.IsTrue(
            sessionId > 0 && sampleCount > 0,
            "The logging session did not finalize with a positive sample count " +
            $"(sessionId={sessionId}, sampleCount={sampleCount}). No data accrued during the run.");

        return ($"Session_{sessionId}", sampleCount, channelCount);
    }
    #endregion

    #region CSV File Helpers
    /// <summary>
    /// Polls until <paramref name="fileName"/> appears in the export directory with a stable,
    /// non-zero size (the exporter writes through a 1 MB buffer flushed on close, so it can be
    /// briefly 0 bytes or growing), then returns its full path. Fails the test on timeout.
    /// </summary>
    private string WaitForExportedCsv(string fileName, TimeSpan timeout)
    {
        var path = Path.Combine(_exportDir, fileName);
        var lastLength = -1L;

        var settled = Retry.WhileFalse(
            () =>
            {
                if (!File.Exists(path))
                {
                    return false;
                }

                var length = new FileInfo(path).Length;
                if (length <= 0)
                {
                    return false;
                }

                // Require the size to be unchanged across two consecutive polls so a partially
                // flushed file is never read mid-write.
                if (length == lastLength)
                {
                    return true;
                }

                lastLength = length;
                return false;
            },
            timeout: timeout,
            interval: TimeSpan.FromMilliseconds(200),
            throwOnTimeout: false,
            ignoreException: true).Result;

        Assert.IsTrue(
            settled && File.Exists(path) && new FileInfo(path).Length > 0,
            $"Exported CSV '{fileName}' did not appear (stable and non-empty) in '{_exportDir}' " +
            $"within {timeout.TotalSeconds:N0}s. The export hook may not have run or wrote elsewhere.");

        return path;
    }

    private void TryDeleteExportDir()
    {
        try
        {
            if (Directory.Exists(_exportDir))
            {
                Directory.Delete(_exportDir, recursive: true);
            }
        }
        catch
        {
            // Temp-dir cleanup is best-effort; a leaked dir under %TEMP% is harmless.
        }
    }
    #endregion

    #region CSV Validation
    /// <summary>
    /// Validates an exported CSV black-box against the session's persisted sample count:
    /// <list type="bullet">
    /// <item>header is well-formed — <c>Time</c> plus one <c>Device:Serial:Channel</c> column
    /// per channel, with no formula-injection prefix;</item>
    /// <item>every data row has exactly the header's field count (RFC 4180 consistency — a
    /// strict parser handles any quoting, so an unescaped delimiter would mismatch and fail);</item>
    /// <item>the time column parses as a round-trip <see cref="DateTime"/> and is non-decreasing;</item>
    /// <item>every non-empty value cell parses as a finite invariant-culture <see cref="double"/>;</item>
    /// <item>the total non-empty value cells equal <paramref name="expectedSampleCount"/> — each
    /// persisted sample becomes exactly one cell, so this proves no rows were dropped or duplicated.</item>
    /// </list>
    /// </summary>
    private void ValidateExportedCsv(string path, long expectedSampleCount, int activeChannelCount)
    {
        // File.ReadAllText auto-detects and strips a UTF-8 BOM, so the header parses cleanly.
        var text = File.ReadAllText(path);
        Assert.IsFalse(string.IsNullOrWhiteSpace(text), $"Exported CSV '{path}' is empty.");

        var records = ParseCsv(text, ',');
        Assert.IsTrue(
            records.Count >= 2,
            $"Exported CSV '{path}' has no data rows (records={records.Count}). Expected a header " +
            "plus at least one data row.");

        // --- Header ---
        var header = records[0];
        Assert.IsTrue(header.Length >= 2, $"CSV header has too few columns ({header.Length}); expected Time + channels.");
        Assert.AreEqual(TIME_HEADER, header[0], $"CSV header's first column should be '{TIME_HEADER}'.");

        var channelCount = header.Length - 1;
        Assert.IsTrue(channelCount >= 1, "CSV header has no channel columns.");
        TestContext?.WriteLine(
            $"CSV: {records.Count - 1} data rows × {channelCount} channel columns " +
            $"(active analog channels reported by UI: {activeChannelCount}).");

        for (var c = 1; c < header.Length; c++)
        {
            var name = header[c];
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(name),
                $"CSV channel column {c} has an empty header.");
            Assert.IsTrue(
                name.Contains(':'),
                $"CSV channel header '{name}' is not in the expected 'Device:Serial:Channel' form.");
            // Formula-injection mitigation (Core 0.22.0 hardening): no header cell may begin with
            // a spreadsheet formula trigger. Our headers never legitimately do, so this guards
            // against corruption/injection regressions.
            Assert.IsFalse(
                name.Length > 0 && (name[0] is '=' or '+' or '-' or '@'),
                $"CSV channel header '{name}' starts with a formula-injection character.");
        }

        // --- Data rows ---
        var nonEmptyValueCells = 0L;
        DateTime? previousTimestamp = null;

        for (var r = 1; r < records.Count; r++)
        {
            var row = records[r];
            Assert.AreEqual(
                header.Length, row.Length,
                $"CSV row {r} has {row.Length} fields but the header has {header.Length}. " +
                "An unescaped delimiter or quote would corrupt the column structure (RFC 4180).");

            // Time column: round-trip ISO 8601 ("O") and non-decreasing.
            Assert.IsTrue(
                DateTime.TryParseExact(
                    row[0], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp),
                $"CSV row {r} time field '{row[0]}' is not a round-trippable timestamp.");
            if (previousTimestamp.HasValue)
            {
                Assert.IsTrue(
                    timestamp >= previousTimestamp.Value,
                    $"CSV timestamps are not monotonically non-decreasing at row {r} " +
                    $"('{row[0]}' < '{previousTimestamp.Value:O}').");
            }
            previousTimestamp = timestamp;

            // Value columns: each non-empty cell is a finite invariant-culture double.
            for (var c = 1; c < row.Length; c++)
            {
                if (string.IsNullOrEmpty(row[c]))
                {
                    continue;
                }

                Assert.IsTrue(
                    double.TryParse(row[c], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    && double.IsFinite(value),
                    $"CSV row {r}, column {c} value '{row[c]}' is not a finite number.");
                nonEmptyValueCells++;
            }
        }

        // --- Completeness: one value cell per persisted sample ---
        Assert.IsTrue(nonEmptyValueCells > 0, "CSV contains no value data.");
        Assert.AreEqual(
            expectedSampleCount, nonEmptyValueCells,
            $"CSV value-cell count ({nonEmptyValueCells}) does not match the session's persisted " +
            $"sample count ({expectedSampleCount}). Each sample should export to exactly one cell; " +
            "a mismatch means rows were dropped, duplicated, or corrupted during export.");
    }

    /// <summary>
    /// Minimal RFC 4180 parser: splits <paramref name="text"/> into records of fields, honoring
    /// quoted fields (which may contain the delimiter, newlines, and <c>""</c>-escaped quotes).
    /// Accepts <c>\r\n</c>, <c>\n</c>, and bare <c>\r</c> record separators and ignores a trailing
    /// newline. Returns one <c>string[]</c> per record.
    /// </summary>
    private static List<string[]> ParseCsv(string text, char delimiter)
    {
        var records = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var i = 0;

        void EndField()
        {
            fields.Add(field.ToString());
            field.Clear();
        }

        void EndRecord()
        {
            EndField();
            records.Add(fields.ToArray());
            fields.Clear();
        }

        while (i < text.Length)
        {
            var ch = text[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                        continue;
                    }

                    inQuotes = false;
                    i++;
                    continue;
                }

                field.Append(ch);
                i++;
                continue;
            }

            if (ch == '"')
            {
                inQuotes = true;
                i++;
            }
            else if (ch == delimiter)
            {
                EndField();
                i++;
            }
            else if (ch == '\r')
            {
                EndRecord();
                i += (i + 1 < text.Length && text[i + 1] == '\n') ? 2 : 1;
            }
            else if (ch == '\n')
            {
                EndRecord();
                i++;
            }
            else
            {
                field.Append(ch);
                i++;
            }
        }

        // Flush a final record that wasn't terminated by a trailing newline.
        if (field.Length > 0 || fields.Count > 0)
        {
            EndRecord();
        }

        return records;
    }
    #endregion
}
