using System;
using System.Collections.Generic;
using FlaUI.Core.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daqifi.Desktop.UITest;

/// <summary>
/// Scenario 2 — Configure logging. Drives the real GUI out-of-process to set the
/// sampling frequency on the Profiles drawer and to enable a known set of analog
/// channels on the Channels pane. Asserts by reading the values BACK from the
/// visible UI (slider value reflects the set frequency; the chosen channel tiles
/// read as active). Requires a DAQiFi device to be attached.
/// </summary>
[TestClass]
public class ConfigureLoggingTests : DaqifiAppFixture
{
    #region Constants
    private const double TARGET_FREQUENCY_HZ = 1000d;
    private const int CHANNELS_TO_ENABLE = 2;
    #endregion

    [TestMethod]
    [TestCategory("Ui")]
    [TestCategory("RequiresDevice")]
    public void ConfigureLogging_SetsFrequencyAndChannels()
    {
        // Arrange — connect to the physically attached device (reuses Step-3 helper).
        var transport = ResolveTransport();
        ConnectFirstDevice(transport);

        // Act — set a known sampling frequency on the Profiles drawer.
        var readBackFrequency = SetSamplingFrequency(TARGET_FREQUENCY_HZ);

        // Act — enable the first two analog channels on the Channels pane.
        IReadOnlyList<string> enabledChannels = EnableFirstAnalogChannels(CHANNELS_TO_ENABLE);

        // Assert (read back from UI) — frequency reflects the value we set.
        Assert.AreEqual(
            TARGET_FREQUENCY_HZ,
            readBackFrequency,
            0.5,
            $"Sampling frequency read back ({readBackFrequency} Hz) does not match the " +
            $"value set ({TARGET_FREQUENCY_HZ} Hz).");

        // Re-read the slider independently to confirm the value persisted in the UI.
        var persistedFrequency = GetSamplingFrequency();
        Assert.AreEqual(
            TARGET_FREQUENCY_HZ,
            persistedFrequency,
            0.5,
            $"Sampling frequency did not persist in the UI (read {persistedFrequency} Hz).");

        // Assert (read back from UI) — exactly the chosen channels report as active.
        Assert.AreEqual(
            CHANNELS_TO_ENABLE,
            enabledChannels.Count,
            "Did not enable the expected number of analog channels.");

        var allActive = Retry.WhileFalse(
            () => AreChannelsActive(enabledChannels),
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: false);

        Assert.IsTrue(
            allActive.Result,
            $"Not all chosen channels read as active in the UI: " +
            $"{string.Join(", ", enabledChannels)}.");

        // Per-test independence: the base fixture's [TestCleanup] closes the app,
        // which disconnects the device. A fresh app instance is launched per test.
    }
}
