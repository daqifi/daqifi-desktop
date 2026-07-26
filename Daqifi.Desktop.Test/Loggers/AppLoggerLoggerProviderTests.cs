using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Loggers;
using Microsoft.Extensions.Logging;
using Moq;

namespace Daqifi.Desktop.Test.Loggers;

/// <summary>
/// Verifies how <see cref="AppLoggerLoggerProvider"/> maps Core's Microsoft.Extensions.Logging entries
/// onto the desktop <see cref="IAppLogger"/> — in particular that firmware-update Errors are NOT
/// forwarded to Sentry (issue #738): the desktop's FirmwareUpdateCoordinator is the single authority on
/// firmware-outcome severity, so Core's own duplicate Error must land in the log file only.
/// </summary>
[TestClass]
public class AppLoggerLoggerProviderTests
{
    [TestMethod]
    public void FirmwareUpdateCategory_ErrorIsForwardedAsWarning_NotSentryError()
    {
        var appLogger = new Mock<IAppLogger>();
        var provider = new AppLoggerLoggerProvider(appLogger.Object);
        var logger = provider.CreateLogger("Daqifi.Core.Firmware.FirmwareUpdateService");

        var ex = new TimeoutException("State 'JumpingToApp' timed out.");
        logger.LogError(ex, "PIC32 firmware update failed in state {State}.", "JumpingToApp");

        // File-only (Warning does not capture to Sentry); never the Sentry-capturing Error path.
        appLogger.Verify(l => l.Warning(It.IsAny<Exception>(), It.IsAny<string>()), Times.Once);
        appLogger.Verify(l => l.Error(It.IsAny<Exception>(), It.IsAny<string>()), Times.Never);
        appLogger.Verify(l => l.Error(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public void NonFirmwareDaqifiCategory_ErrorIsCapturedToSentry()
    {
        // Control: a non-firmware DAQiFi-category Error is still captured to Sentry (AppLogger.Error),
        // so the firmware carve-out is scoped and doesn't silence the rest of our own error logging.
        var appLogger = new Mock<IAppLogger>();
        var provider = new AppLoggerLoggerProvider(appLogger.Object);
        var logger = provider.CreateLogger("Daqifi.Core.Device.DaqifiStreamingDevice");

        var ex = new InvalidOperationException("boom");
        logger.LogError(ex, "Something genuinely broke.");

        appLogger.Verify(l => l.Error(It.IsAny<Exception>(), It.IsAny<string>()), Times.Once);
    }

    [TestMethod]
    public void FirmwareUpdateCategory_WarningStillForwardedAsWarning()
    {
        // A firmware-category Warning was never a Sentry capture; confirm the carve-out doesn't change
        // normal Warning forwarding (e.g. Core's own progress/warns still reach the file).
        var appLogger = new Mock<IAppLogger>();
        var provider = new AppLoggerLoggerProvider(appLogger.Object);
        var logger = provider.CreateLogger("Daqifi.Core.Firmware.FirmwareUpdateService");

        logger.LogWarning("Reconnect attempt failed; retrying.");

        appLogger.Verify(l => l.Warning(It.IsAny<string>()), Times.Once);
        appLogger.Verify(l => l.Error(It.IsAny<string>()), Times.Never);
    }
}
