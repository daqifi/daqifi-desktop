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

        // Act — enable the analog channels on the Channels pane (via SELECT ALL).
        var activeCount = EnableAllAnalogChannels();

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

        // Assert (read back from UI) — analog channels report as active.
        Assert.IsTrue(
            activeCount > 0,
            "Expected analog channels to report active after SELECT ALL, but the " +
            "pane shows none active.");

        // Re-read the active count independently to confirm it persisted in the UI.
        var persistedActive = GetActiveAnalogChannelCount();
        Assert.IsTrue(
            persistedActive > 0,
            $"Active analog channel count did not persist in the UI (read {persistedActive}).");

        // Per-test independence: the base fixture's [TestCleanup] closes the app,
        // which disconnects the device. A fresh app instance is launched per test.
    }
}
