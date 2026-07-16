using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daqifi.Desktop.UITest;

/// <summary>
/// Channel math (scaling expression) end-to-end (issue #556). Drives the real GUI against the
/// attached device: enables an analog channel, runs a brief logging session (the device only
/// streams samples — and so only updates a tile's displayed value — while a session is active),
/// and sets the channel's SCALING expression to a known transform (<c>x * 10</c>) through the
/// channel-settings drawer. Asserts the channel's displayed value scales accordingly, proving
/// the NCalc expression in <c>AbstractChannel.ActiveSample</c> really runs against live streamed
/// samples, not just in isolation. Also covers the invalid-expression path: scaling must disable
/// itself (raw values keep flowing) rather than freeze or corrupt the tile. Self-cleans the
/// logging session it creates.
/// </summary>
[TestClass]
public class ChannelMathTests : DaqifiAppFixture
{
    /// <summary>
    /// Sets <c>x * 10</c> on an enabled analog channel while it streams and asserts the
    /// displayed tile value scales to ~10x its pre-scaling baseline, with no INVALID EXPRESSION
    /// warning.
    /// </summary>
    [TestMethod]
    [TestCategory("Ui")]
    [TestCategory("RequiresDevice")]
    public void ChannelMath_AppliesXTimes10Scaling_ScalesDisplayedValue()
    {
        // Arrange — connect and enable the analog channels.
        var transport = ResolveTransport();
        ConnectFirstDevice(transport);
        var activeCount = EnableAllAnalogChannels();
        Assert.IsTrue(activeCount > 0, "Expected at least one active analog channel to test scaling on.");

        // Cleanup flags: only undo what actually started, so a failure partway through setup
        // doesn't skip cleanup for the steps that DID run (and doesn't attempt to undo ones that
        // didn't).
        var loggingStarted = false;
        var drawerOpened = false;
        try
        {
            // Arrange (cont'd) — start a brief logging session so the device actually streams
            // samples (tile values are otherwise frozen/empty), then open the first analog
            // channel's settings drawer.
            StartLogging();
            loggingStarted = true;
            var channelName = OpenFirstChannelSettingsDrawer();
            drawerOpened = true;

            // Act — capture the raw (unscaled) baseline, apply x * 10 scaling (the expression box
            // is disabled until scaling is switched on — IsEnabled binds to IsScalingActive, so
            // the toggle must go first), and capture the scaled reading the same way.
            var baseline = SampleFirstAnalogChannelValue();
            SetScalingActiveInDrawer(true);
            SetScaleExpressionInDrawer("x * 10");
            WaitForInvalidExpressionWarning(shown: false, TimeSpan.FromSeconds(5));
            var scaled = SampleFirstAnalogChannelValue();

            // Assert — the expression is valid; no "INVALID EXPRESSION" warning.
            Assert.IsFalse(
                IsInvalidExpressionWarningShown(),
                $"'x * 10' should be a valid expression for channel '{channelName}', but the drawer " +
                "shows INVALID EXPRESSION.");

            // Assert — the displayed value scaled ~10x. Tolerance is the larger of a fixed floor
            // (covers a near-zero baseline, where x*10 noise is still small in absolute terms)
            // and a fraction of the expected magnitude (covers a baseline far from zero, where
            // ADC noise scales with the reading).
            var expected = baseline * 10.0;
            var tolerance = Math.Max(0.5, Math.Abs(expected) * 0.25);
            Assert.AreEqual(
                expected,
                scaled,
                tolerance,
                $"Channel '{channelName}' scaled reading ({scaled:F3} V) is not ~10x its baseline " +
                $"({baseline:F3} V, expected ~{expected:F3} V) within tolerance {tolerance:F3} V.");
        }
        finally
        {
            if (drawerOpened)
            {
                CloseChannelSettingsDrawer();
            }

            if (loggingStarted)
            {
                StopLogging();
                DeleteNewestLoggedSession();
            }
        }
    }

    /// <summary>
    /// Sets a deliberately invalid expression on an enabled analog channel while it streams and
    /// asserts the drawer surfaces the INVALID EXPRESSION warning while the displayed value keeps
    /// tracking raw (unscaled) samples — proving <c>HasValidExpression == false</c> disables
    /// scaling without freezing or corrupting the tile.
    /// </summary>
    [TestMethod]
    [TestCategory("Ui")]
    [TestCategory("RequiresDevice")]
    public void ChannelMath_InvalidExpression_DisablesScaling_RawValuesFlow()
    {
        // Arrange — connect and enable the analog channels.
        var transport = ResolveTransport();
        ConnectFirstDevice(transport);
        var activeCount = EnableAllAnalogChannels();
        Assert.IsTrue(activeCount > 0, "Expected at least one active analog channel to test scaling on.");

        // Cleanup flags: only undo what actually started (cf. the sibling test above).
        var loggingStarted = false;
        var drawerOpened = false;
        try
        {
            // Arrange (cont'd) — start a brief logging session and open the drawer.
            StartLogging();
            loggingStarted = true;
            var channelName = OpenFirstChannelSettingsDrawer();
            drawerOpened = true;

            // Act — capture the raw baseline, then turn scaling on with a deliberately invalid
            // expression and capture the reading again while it is active.
            var baseline = SampleFirstAnalogChannelValue();
            SetScalingActiveInDrawer(true);
            SetScaleExpressionInDrawer("x *");
            WaitForInvalidExpressionWarning(shown: true, TimeSpan.FromSeconds(5));
            var whileInvalid = SampleFirstAnalogChannelValue();

            // Assert — the drawer surfaced the invalid-expression warning (HasValidExpression
            // false).
            Assert.IsTrue(
                IsInvalidExpressionWarningShown(),
                $"Channel '{channelName}' with an invalid expression ('x *') should show " +
                "INVALID EXPRESSION, but the warning is not present.");

            // Assert — raw (unscaled) values are still flowing: the reading tracks the original
            // baseline (ratio ~1x), not a 10x-or-error transform, and scaling did not freeze the
            // tile.
            var tolerance = Math.Max(0.5, Math.Abs(baseline) * 0.5);
            Assert.AreEqual(
                baseline,
                whileInvalid,
                tolerance,
                $"Channel '{channelName}' should keep showing raw values while its expression is " +
                $"invalid, but the reading ({whileInvalid:F3} V) diverged from the pre-scaling " +
                $"baseline ({baseline:F3} V) beyond tolerance {tolerance:F3} V.");
        }
        finally
        {
            if (drawerOpened)
            {
                CloseChannelSettingsDrawer();
            }

            if (loggingStarted)
            {
                StopLogging();
                DeleteNewestLoggedSession();
            }
        }
    }
}
