using System;
using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daqifi.Desktop.UITest;

/// <summary>
/// Live Graph RATE chip seeding (issue #686). Connects to the attached device and starts
/// streaming <b>without ever touching the FREQUENCY slider</b>, then reads the RATE chip
/// straight from the UI. Before the fix, <c>SelectedStreamingFrequency</c>'s backing field
/// only changed via the slider's write-through, so the chip stayed at "0 Hz" even though the
/// device was already streaming at its real (device-reported) rate. Drives the real GUI
/// out-of-process; asserts only through visible UI. Requires a DAQiFi device.
/// </summary>
[TestClass]
public class RateChipTests : DaqifiAppFixture
{
    [TestMethod]
    [TestCategory("Ui")]
    [TestCategory("RequiresDevice")]
    public void SelectedStreamingFrequency_ShowsDeviceRate_WithoutTouchingFrequencySlider()
    {
        // Arrange — connect to the attached device. Deliberately skip SetSamplingFrequency:
        // the whole point of issue #686 is that the chip must be correct even when the user
        // never opens the frequency flyout.
        var transport = ResolveTransport();
        ConnectFirstDevice(transport);

        var activeChannels = EnableAllAnalogChannels();
        Assert.IsTrue(
            activeChannels > 0,
            "Pre-condition failed: no analog channels became active.");

        // Act — start streaming, then read the RATE chip.
        StartLogging();
        var rateText = GetStreamRateText();

        // Assert — the chip must not read "0 Hz". Parse the numeric prefix (format is
        // "<N> Hz", see LiveGraphPane.xaml's two Runs) and assert it's a positive rate.
        var parts = rateText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.IsTrue(
            parts.Length >= 1 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var rateHz),
            $"RATE chip text '{rateText}' did not parse as '<number> Hz'.");

        double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedRateHz);
        Assert.IsTrue(
            parsedRateHz >= 1,
            $"RATE chip showed {rateText} immediately after connecting/streaming, without the " +
            "FREQUENCY slider ever being touched. Expected it to be seeded from the device's " +
            "actual streaming frequency (>= 1 Hz), not stuck at 0 Hz (issue #686).");

        // Cross-check against the per-device settings flyout, which reads the same underlying
        // device property through a different binding path — they should agree.
        StopLogging();
        var sliderFrequency = GetSamplingFrequency();
        Assert.AreEqual(
            sliderFrequency,
            parsedRateHz,
            0.5,
            $"RATE chip ({parsedRateHz} Hz) should match the device's actual sampling " +
            $"frequency as read from the settings flyout ({sliderFrequency} Hz).");

        // Per-test independence: the base fixture's [TestCleanup] closes the app,
        // which disconnects the device. A fresh app instance is launched per test.
    }
}
