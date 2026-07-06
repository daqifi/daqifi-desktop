using System;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daqifi.Desktop.UITest;

/// <summary>
/// Digital output end-to-end (issue #663). Drives the real GUI against the attached
/// device: flips a digital channel to OUTPUT through the channel-settings drawer,
/// commands it HIGH there, verifies the tile re-shelves into the DIGITAL OUT section
/// carrying the commanded state on its drive toggle, drives the pin LOW/HIGH from the
/// tile toggle, and returns the channel to INPUT. Every direction/state change lands on
/// the device through Core's SetDioDirection/SetDioValue — the NLog log is asserted
/// free of the wrapper's failure/ignore lines, so the commands really executed against
/// the connected hardware rather than being swallowed.
/// </summary>
[TestClass]
public class DigitalOutputTests : DaqifiAppFixture
{
    [TestMethod]
    [TestCategory("Ui")]
    [TestCategory("RequiresDevice")]
    public void DigitalOutput_DirectionAndDriveRoundTrip()
    {
        // Arrange — connect to the physically attached device.
        var transport = ResolveTransport();
        ConnectFirstDevice(transport);

        // Act — open the settings drawer of the last tile (the highest-numbered digital
        // channel while everything is still in its factory input direction).
        var channelName = OpenLastChannelSettingsDrawer();
        Assert.IsTrue(
            channelName.StartsWith("DI", StringComparison.OrdinalIgnoreCase),
            $"Expected the last channel tile to be a digital channel, but the drawer shows " +
            $"'{channelName}'. Does the attached device report digital channels?");

        // Act — flip the channel to OUTPUT; the OUTPUT STATE section must appear.
        SetChannelDirectionInDrawer(output: true);

        // Act — command the pin HIGH from the drawer.
        SetOutputStateInDrawer(high: true);
        CloseChannelSettingsDrawer();

        // Assert — the tile re-shelved into DIGITAL OUT and carries the drive toggle,
        // which reports the HIGH state commanded in the drawer.
        var toggles = WaitForOutputDriveToggles(expected: 1, TimeSpan.FromSeconds(15));
        var driveToggle = toggles[0].AsToggleButton();
        Assert.AreEqual(
            ToggleState.On,
            driveToggle.Patterns.Toggle.Pattern.ToggleState.Value,
            "The tile drive toggle should report the HIGH state commanded in the drawer.");

        // Act + assert — drive the pin LOW from the tile toggle (the quick path).
        driveToggle.Patterns.Toggle.Pattern.Toggle();
        Retry.WhileFalse(
            () => driveToggle.Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.Off,
            timeout: TimeSpan.FromSeconds(5),
            interval: TimeSpan.FromMilliseconds(200),
            throwOnTimeout: true,
            timeoutMessage: "The drive toggle did not report LOW after toggling.");

        // Act + assert — and back HIGH.
        driveToggle.Patterns.Toggle.Pattern.Toggle();
        Retry.WhileFalse(
            () => driveToggle.Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.On,
            timeout: TimeSpan.FromSeconds(5),
            interval: TimeSpan.FromMilliseconds(200),
            throwOnTimeout: true,
            timeoutMessage: "The drive toggle did not report HIGH after toggling back.");

        // Cleanup within the scenario — return the channel to INPUT so the physical pin
        // stops driving. The tile now lives in the DIGITAL OUT section, which renders
        // last, so its gear is still the last one.
        var reopenedName = OpenLastChannelSettingsDrawer();
        Assert.AreEqual(
            channelName,
            reopenedName,
            "Reopening the last tile's drawer should land on the same (now output) channel.");
        SetChannelDirectionInDrawer(output: false);
        CloseChannelSettingsDrawer();

        // Assert — the drive toggle left the pane: the tile is an input again.
        WaitForOutputDriveToggles(expected: 0, TimeSpan.FromSeconds(15));

        // Assert (black-box, negative) — every direction/state change above delegated to
        // Core against the live device without the wrapper logging a failure or an
        // ignored-command warning.
        var log = ReadNewLogText();
        StringAssert.DoesNotMatch(
            log,
            new System.Text.RegularExpressions.Regex("Failed to (set direction|drive output)"),
            "The app logged a DIO command failure — Core delegation did not execute cleanly.");
        StringAssert.DoesNotMatch(
            log,
            new System.Text.RegularExpressions.Regex("Ignored (set direction|drive output)"),
            "The app ignored a DIO command — the device was not connected when a command fired.");
    }
}
