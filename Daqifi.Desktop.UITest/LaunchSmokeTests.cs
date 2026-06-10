using System;
using FlaUI.Core.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daqifi.Desktop.UITest;

/// <summary>
/// Smoke test verifying the app launches in unattended test mode and presents a
/// responsive main window. Requires a desktop session; tagged RequiresDevice so it
/// is excluded from the headless unit gate.
/// </summary>
[TestClass]
public class LaunchSmokeTests : DaqifiAppFixture
{
    [TestMethod]
    [TestCategory("Ui")]
    [TestCategory("RequiresDevice")]
    public void Launch_MainWindowAppears_AndIsResponsive()
    {
        // Arrange / Act — launch is performed by the base fixture Setup.

        // Assert: main window resolved.
        Assert.IsNotNull(MainWindow, "Main window was not resolved.");

        // Assert: window becomes responsive (not in a hung/ghost state) within timeout.
        var responsive = Retry.WhileFalse(
            () => MainWindow.IsAvailable && MainWindow.Patterns.Window.IsSupported,
            timeout: TimeSpan.FromSeconds(30),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: false);
        Assert.IsTrue(responsive.Result, "Main window did not become responsive within 30s.");

        // Assert: the window exposes a non-empty title (a basic UIA readability check).
        var hasTitle = Retry.WhileFalse(
            () => !string.IsNullOrWhiteSpace(MainWindow.Title),
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(300),
            throwOnTimeout: false);
        Assert.IsTrue(hasTitle.Result, "Main window title was empty.");
    }
}
