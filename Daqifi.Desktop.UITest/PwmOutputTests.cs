using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daqifi.Desktop.UITest;

/// <summary>
/// PWM output end-to-end (issue #664). Drives the real GUI against the attached device:
/// verifies the drawer's PWM section renders only on PWM-capable channels, stages a duty
/// cycle and the device-wide frequency, enables PWM and asserts the digital
/// direction/output-state controls are suppressed while it runs, checks the tile
/// re-shelves into DIGITAL OUT showing the commanded duty (with the drive toggle hidden),
/// changes the duty live, and disables PWM to get the digital controls back. Every PWM
/// change lands on the device through Core's SetPwmEnabled / SetPwmDutyCycle /
/// SetPwmFrequency — the NLog log is asserted free of the wrapper's failure/ignore lines,
/// so the commands really executed rather than being swallowed.
/// </summary>
[TestClass]
public class PwmOutputTests : DaqifiAppFixture
{
    /// <summary>
    /// A PWM-capable channel (firmware board mask: 0, 3, 4, 5, 6, 7 on every Nyquist
    /// variant). DIO7 is the capable channel nearest the end of the pane, so the
    /// scan-from-the-end drawer targeting finds it quickly.
    /// </summary>
    private const string PWM_CAPABLE_CHANNEL = "DIO7";

    [TestMethod]
    [TestCategory("Ui")]
    [TestCategory("RequiresDevice")]
    public void PwmOutput_EnableAdjustDisableRoundTrip()
    {
        // Arrange — connect to the physically attached device.
        var transport = ResolveTransport();
        ConnectFirstDevice(transport);

        // Assert — capability gating (negative): the last tile is the highest-numbered
        // digital channel (DIO15 on a Nyquist), which is not PWM-capable, so its drawer
        // must not render a PWM section. IsPwmCapable comes from Core's board mask.
        var lastChannelName = OpenLastChannelSettingsDrawer();
        Assert.IsTrue(
            lastChannelName.StartsWith("DI", StringComparison.OrdinalIgnoreCase),
            $"Expected the last channel tile to be a digital channel, but the drawer shows " +
            $"'{lastChannelName}'. Does the attached device report digital channels?");
        Assert.IsNull(
            FindPwmModeToggleOrNull(),
            $"'{lastChannelName}' is not PWM-capable, yet its drawer renders the PWM section.");
        CloseChannelSettingsDrawer();

        // Act — open a PWM-capable channel's drawer; the PWM section must be present.
        OpenChannelSettingsDrawerFor(PWM_CAPABLE_CHANNEL);
        Assert.IsNotNull(
            FindPwmModeToggleOrNull(),
            $"'{PWM_CAPABLE_CHANNEL}' is PWM-capable, yet its drawer has no PWM section.");

        // Act — stage the duty and the device-wide frequency, then enable (the app resends
        // duty → frequency → enable through Core on the way up).
        SetPwmDutyInDrawer(45);
        SetPwmFrequencyInDrawer(100);
        SetPwmModeInDrawer(enabled: true);

        // Assert — while PWM runs, the digital DIRECTION and OUTPUT STATE controls are
        // suppressed (the hardware ignores those writes on a PWM-active channel).
        WaitForDrawerElementGone("DirectionInputRadio",
            "The DIRECTION section should be suppressed while PWM is enabled.");
        WaitForDrawerElementGone("OutputStateHighRadio",
            "The OUTPUT STATE section should be suppressed while PWM is enabled.");

        CloseChannelSettingsDrawer();

        // Assert — the tile re-shelved into DIGITAL OUT and shows the commanded duty on
        // its value line, with the quick drive toggle suppressed.
        WaitForTileValue("PWM 45%", TimeSpan.FromSeconds(15));
        WaitForOutputDriveToggles(expected: 0, TimeSpan.FromSeconds(5));

        // Act + assert — change the duty live while PWM runs; the tile follows.
        OpenChannelSettingsDrawerFor(PWM_CAPABLE_CHANNEL);
        SetPwmDutyInDrawer(80);
        WaitForTileValue("PWM 80%", TimeSpan.FromSeconds(15));

        // Act — disable PWM; the digital controls return in place (drawer still open).
        SetPwmModeInDrawer(enabled: false);
        Assert.IsNotNull(
            FindByAutomationId("DirectionInputRadio", timeoutSeconds: 10),
            "The DIRECTION section should return once PWM is disabled.");
        CloseChannelSettingsDrawer();

        // Assert — the duty readout left the pane: the channel is a plain digital input
        // again (its stored direction was never changed).
        WaitForNoTileValueStartingWith("PWM", TimeSpan.FromSeconds(15));

        // Assert (black-box, negative) — every PWM change above delegated to Core against
        // the live device without the wrapper logging a failure or an ignored command.
        var log = ReadNewLogText();
        StringAssert.DoesNotMatch(
            log,
            new System.Text.RegularExpressions.Regex(
                "Failed to (enable PWM|disable PWM|set PWM duty cycle|set PWM frequency)"),
            "The app logged a PWM command failure — Core delegation did not execute cleanly.");
        StringAssert.DoesNotMatch(
            log,
            new System.Text.RegularExpressions.Regex(
                "Ignored (enable PWM|disable PWM|set PWM duty cycle|set PWM frequency)"),
            "The app ignored a PWM command — the device was not connected when a command fired.");
    }
}
