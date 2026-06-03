using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daqifi.Desktop.UITest;

/// <summary>
/// Scenario 6 — Profiles: save, activate, delete. Drives the real GUI out-of-process against an
/// attached device through the full lifecycle of a saved configuration preset:
///
/// 1. Configure a known state (a sampling frequency + a set of active analog channels).
/// 2. <b>Save</b> it as a profile — once by capturing the live device settings
///    (<c>SaveCurrentSettingsCommand</c>) and once via the new-profile form
///    (<c>SaveNewProfileCommand</c>) — and assert the profile appears in the list.
/// 3. Change the device configuration to something clearly different.
/// 4. <b>Activate</b> the saved profile (<c>ActivateProfileCommand</c>) and assert the captured
///    channel + frequency intent is re-applied to the device, verified through the Channels pane
///    "n / N ACTIVE" indicator and the per-device frequency flyout (ground-truth UI, not internals).
/// 5. <b>Delete</b> the profile (<c>DeleteProfileCommand</c>) and assert it leaves the list.
///
/// All assertions read from visible UI via UI Automation. Each test is self-contained: it launches
/// a fresh app (base fixture), records the profile count up-front, and removes any profile it
/// creates, asserting the list returns to its original membership. Requires a DAQiFi device.
/// </summary>
[TestClass]
public class ProfilesTests : DaqifiAppFixture
{
    #region Constants
    // The captured-then-reapplied frequency and the deliberately different "changed" frequency.
    // Both values are exercised by the other scenarios (100 Hz / 1000 Hz), so both are known to be
    // settable on the device; they are far apart so re-application is unambiguous.
    private const double CapturedFrequencyHz = 100d;
    private const double ChangedFrequencyHz = 1000d;

    private static readonly TimeSpan UiSettleTimeout = TimeSpan.FromSeconds(15);
    #endregion

    [TestMethod]
    [TestCategory("Ui")]
    [TestCategory("RequiresDevice")]
    public void SaveActivateDelete_ProfileRoundTrips()
    {
        // Arrange — connect to the physically attached device.
        var transport = ResolveTransport();
        ConnectFirstDevice(transport);

        // Configure a known state: a captured frequency + a set of active analog channels.
        var capturedFrequency = SetSamplingFrequency(CapturedFrequencyHz);
        var capturedChannels = EnableAllAnalogChannels();
        Assert.IsTrue(
            capturedChannels > 0,
            "Pre-condition failed: no analog channels became active, so there is nothing to capture.");

        // Baseline membership before we add ours (the test DB persists profiles across runs).
        var profilesBefore = GetProfileCount();

        // Act (SAVE) — snapshot the live device settings into a new profile via "CAPTURE FROM
        // CURRENT SETTINGS" in the new-profile drawer.
        OpenNewProfileDrawer();
        SetNewProfileName($"UITest Profile {DateTime.Now:HHmmss}");
        SaveCurrentSettingsAsProfile();

        // Assert — the new profile appears in the list.
        WaitForProfileCount(profilesBefore + 1, UiSettleTimeout);

        // Change the device configuration to something clearly different from the saved profile:
        // clear all channels (N -> 0) and set a different frequency.
        ClearAllChannels();
        var changedFrequency = SetSamplingFrequency(ChangedFrequencyHz);
        Assert.AreNotEqual(
            capturedFrequency,
            changedFrequency,
            0.5,
            "Pre-condition failed: the 'changed' frequency must differ from the captured one for " +
            "re-application to be observable.");
        Assert.AreEqual(
            0,
            GetActiveAnalogChannelCount(),
            "Pre-condition failed: channels were not cleared before activation.");

        // Act (ACTIVATE) — open the saved profile and activate it.
        OpenLastProfileEditDrawer();
        ActivateSelectedProfile();

        // Assert (re-application, ground-truth UI) — the device's frequency and active analog
        // channels match the saved profile again.
        var reappliedFrequency = WaitForSamplingFrequency(capturedFrequency, UiSettleTimeout);
        Assert.AreEqual(
            capturedFrequency,
            reappliedFrequency,
            0.5,
            $"Activation did not re-apply the captured frequency ({capturedFrequency} Hz); the " +
            $"device flyout read {reappliedFrequency} Hz.");
        WaitForActiveAnalogChannelCount(capturedChannels, UiSettleTimeout);

        // Act (DELETE) — deactivate (delete is blocked while active) then delete the profile.
        OpenLastProfileEditDrawer();
        DeactivateSelectedProfile();
        DeleteSelectedProfile();

        // Assert — the profile is gone; the list is back to its original membership.
        WaitForProfileCount(profilesBefore, UiSettleTimeout);

        // Per-test independence: the base fixture's [TestCleanup] closes the app (disconnecting
        // the device). A fresh app instance is launched per test.
    }

    [TestMethod]
    [TestCategory("Ui")]
    [TestCategory("RequiresDevice")]
    public void CreateProfileViaForm_AppearsAndDeletes()
    {
        // Arrange — connect to the physically attached device.
        var transport = ResolveTransport();
        ConnectFirstDevice(transport);

        var profilesBefore = GetProfileCount();

        // Act (SAVE via form) — open the new-profile drawer, name it, select the connected
        // device, and save via "SAVE PROFILE" (SaveNewProfileCommand).
        OpenNewProfileDrawer();
        SetNewProfileName($"UITest Form {DateTime.Now:HHmmss}");
        SelectFirstNewProfileDevice();
        SaveNewProfileFromForm();

        // Assert — the new profile appears in the list.
        WaitForProfileCount(profilesBefore + 1, UiSettleTimeout);

        // Act (DELETE) — the profile was never activated, so it deletes directly.
        OpenLastProfileEditDrawer();
        DeleteSelectedProfile();

        // Assert — the list is back to its original membership.
        WaitForProfileCount(profilesBefore, UiSettleTimeout);
    }
}
