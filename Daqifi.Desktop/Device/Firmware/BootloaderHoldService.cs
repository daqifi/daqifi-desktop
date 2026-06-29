using Daqifi.Core.Communication.Transport;
using Daqifi.Core.Device.Discovery;
using Daqifi.Desktop.Common.Loggers;

namespace Daqifi.Desktop.Device.Firmware;

/// <summary>
/// Default <see cref="IBootloaderHoldService"/> implementation. Holds the shared exclusive HID transport
/// open with a continuously-pending interrupt-IN read to keep a sitting PIC32 bootloader out of USB
/// selective-suspend (the cause of the #568 wedge). See <see cref="IBootloaderHoldService"/> for the
/// full rationale.
/// </summary>
public sealed class BootloaderHoldService : IBootloaderHoldService, IDisposable
{
    #region Constants
    // VID/PID of the PIC32 HID bootloader. Single source of truth: Core's HID finder defaults
    // (0x04D8 / 0x003C).
    private const int BOOTLOADER_VENDOR_ID = HidDeviceFinder.DefaultVendorId;
    private const int BOOTLOADER_PRODUCT_ID = HidDeviceFinder.DefaultProductId;

    /// <summary>
    /// Default per-read timeout for the keep-alive poll. Short enough that a read is pending
    /// essentially continuously (so Windows never starts the USB selective-suspend idle timer), long
    /// enough to avoid a tight busy-loop. A sitting bootloader sends nothing, so each read times out
    /// and is immediately re-issued.
    /// </summary>
    public static readonly TimeSpan DefaultKeepAliveReadTimeout = TimeSpan.FromSeconds(1);
    #endregion

    #region Private Fields
    private readonly IHidTransport _transport;
    private readonly IAppLogger _logger;
    private readonly TimeSpan _keepAliveReadTimeout;
    private readonly string? _devicePath;
    private readonly string? _deviceName;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CancellationTokenSource? _keepAliveCts;
    private Task? _keepAliveTask;
    private volatile bool _stopKeepAlive;
    private bool _holding;
    private bool _disposed;
    #endregion

    #region Constructor
    /// <summary>
    /// Creates the hold service over the shared HID transport.
    /// </summary>
    /// <param name="transport">
    /// The shared bootloader HID transport (the same DI singleton the flasher uses, configured for
    /// exclusive access). Holding this transport open also locks every other user-mode opener out for
    /// the duration of the hold (the A2 stray-write guard).
    /// </param>
    /// <param name="logger">Application logger for diagnostics.</param>
    /// <param name="keepAliveReadTimeout">
    /// Per-read keep-alive timeout. Null uses <see cref="DefaultKeepAliveReadTimeout"/>; tests pass a
    /// small value to make the loop observable quickly.
    /// </param>
    /// <param name="devicePath">
    /// OS HID device path to target. When non-null the hold opens this exact device via
    /// <see cref="IHidTransport.ConnectByPathAsync"/> — the watcher uses this to hold one specific
    /// bootloader among several identical ones. When null the hold opens the first VID/PID match
    /// (the single-device behavior).
    /// </param>
    /// <param name="deviceName">Friendly device name surfaced in the UI; null when unknown.</param>
    public BootloaderHoldService(
        IHidTransport transport,
        IAppLogger logger,
        TimeSpan? keepAliveReadTimeout = null,
        string? devicePath = null,
        string? deviceName = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _keepAliveReadTimeout = keepAliveReadTimeout ?? DefaultKeepAliveReadTimeout;
        _devicePath = devicePath;
        _deviceName = deviceName;
    }
    #endregion

    #region Public Methods
    /// <inheritdoc />
    public bool IsHolding => Volatile.Read(ref _holding);

    /// <inheritdoc />
    public string? DevicePath => _devicePath;

    /// <inheritdoc />
    public string? DeviceName => _deviceName;

    /// <inheritdoc />
    public event EventHandler? HoldDropped;

    /// <inheritdoc />
    public async Task BeginHoldAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_holding)
            {
                // Already holding with a live keep-alive loop — nothing to do.
                if (_keepAliveTask is { IsCompleted: false })
                {
                    return;
                }

                // The keep-alive loop exited early (device I/O error / surprise removal) but the hold
                // state was never cleared. Tear the stale hold down here so we re-establish it below
                // instead of no-opping and leaving the device unprotected from selective-suspend.
                _logger.Information("HID bootloader keep-alive had stopped; re-establishing the hold.");
                await StopKeepAliveAsync(hard: true).ConfigureAwait(false);
                try
                {
                    await _transport.DisconnectAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Warning(
                        ex, "Error disconnecting a stale HID bootloader hold before re-establishing.");
                }

                _holding = false;
            }

            try
            {
                // Grab the bootloader's HID handle. ExclusiveAccess is set on the transport
                // (FirmwareUpdateServiceConfig.CreateBootloaderHidTransport), so this also locks out
                // every other user-mode opener for the duration of the hold. A connect over an
                // already-connected transport (e.g. the handle the flasher just left open) returns
                // immediately, in which case we simply (re)start the keep-alive over it.
                if (_devicePath != null)
                {
                    // Multi-device: target this exact bootloader by path. Identical bootloaders share
                    // VID/PID and have no serial, so the path is the only discriminator.
                    await _transport
                        .ConnectByPathAsync(_devicePath, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await _transport
                        .ConnectAsync(BOOTLOADER_VENDOR_ID, BOOTLOADER_PRODUCT_ID, null, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                // No bootloader present (a just-flashed device is now in application mode) or the open
                // was refused. Holding is best-effort — stay un-held; the flash path's own connect +
                // handshake retry still covers a device that shows up or recovers later.
                _logger.Warning(ex, "Could not open the HID bootloader to hold it; continuing without a hold.");
                return;
            }

            _stopKeepAlive = false;
            _keepAliveCts = new CancellationTokenSource();
            _keepAliveTask = Task.Run(() => KeepAliveLoopAsync(_keepAliveCts.Token));
            _holding = true;

            _logger.Information(
                "Holding the HID bootloader handle (keep-alive read pending) so Windows USB selective-suspend " +
                "cannot wedge it before flashing (daqifi-nyquist-firmware#568).");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task PauseForFlashAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_holding)
            {
                return;
            }

            // Graceful stop: signal the loop and let its in-flight read complete naturally (within one
            // keep-alive timeout) so no orphaned read IRP is left pending to swallow the flasher's first
            // response. Deliberately do NOT disconnect — leave the handle OPEN and warm. The flasher's
            // ConnectToBootloaderWithRetryAsync sees it connected, closes+reopens it back-to-back (no
            // idle window, so no #568 wedge) and flashes. Handing it a warm handle is the point of the hold.
            await StopKeepAliveAsync(hard: false).ConfigureAwait(false);
            _holding = false;

            _logger.Information("Paused the HID bootloader hold for flashing; handle left open for the flasher.");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task ReleaseAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Hard stop (no flash follows): cancel any in-flight read and close the handle so the device
            // is never left held after the dialog goes away.
            await StopKeepAliveAsync(hard: true).ConfigureAwait(false);

            try
            {
                await _transport.DisconnectAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error while releasing the HID bootloader handle.");
            }

            _holding = false;
        }
        finally
        {
            _gate.Release();
        }
    }
    #endregion

    #region Private Methods
    private async Task KeepAliveLoopAsync(CancellationToken cancellationToken)
    {
        // Keep an interrupt-IN read continuously pending. A pending read IRP keeps the USB link active so
        // Windows never selective-suspends the device — the suspend whose reopen-without-SET_CONFIGURATION
        // wedges the bootloader (#568). An *idle* open handle alone does NOT prevent suspend; the pending
        // read does. Reads carry no payload TO the device, so unlike a write they can never be mis-parsed
        // as a stray command — this is the one form of I/O that is safe to direct at a sitting bootloader.
        var droppedByError = false;
        var stopWasRequested = false;
        while (!cancellationToken.IsCancellationRequested && !_stopKeepAlive)
        {
            try
            {
                // A sitting bootloader sends nothing unsolicited, so this normally times out; that is the
                // expected, healthy case. Any bytes returned are discarded (no command is in flight).
                await _transport.ReadAsync(_keepAliveReadTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Expected: idle bootloader, no data. Re-issue immediately to keep a read pending.
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // The handle went away (device detached / flashed / surprise-removed). Stop quietly; the
                // flash path re-discovers and reconnects on its own.
                _logger.Warning(ex, "HID bootloader keep-alive read failed; ending the hold.");
                droppedByError = true;
                // Snapshot at failure time: a concurrent PauseForFlash/Release could flip _stopKeepAlive
                // between here and the post-loop check, which would otherwise swallow a genuine drop.
                stopWasRequested = _stopKeepAlive;
                break;
            }
        }

        // Notify the watcher only when the device dropped out from under us — never on a requested stop
        // (PauseForFlash/Release set _stopKeepAlive). Raise off this task's thread so a HoldDropped handler
        // that disposes this hold (Dispose awaits this very task) cannot deadlock on itself.
        if (droppedByError && !stopWasRequested)
        {
            var handler = HoldDropped;
            if (handler != null)
            {
                _ = Task.Run(() => handler(this, EventArgs.Empty));
            }
        }
    }

    /// <summary>
    /// Stops the keep-alive loop and awaits its completion. When <paramref name="hard"/> is true the
    /// in-flight read is cancelled immediately; when false it is allowed to drain naturally (so the
    /// flasher inherits a quiescent handle with no orphaned read IRP). Must be called under <see cref="_gate"/>.
    /// </summary>
    private async Task StopKeepAliveAsync(bool hard)
    {
        // Await the loop to completion — deliberately, not with an "abandon on timeout" fallback. The
        // wait is already self-bounding: each keep-alive read is capped by the transport's own read
        // timeout (_keepAliveReadTimeout, ~1s), so once _stopKeepAlive is set the in-flight read
        // returns/throws and the loop exits within that window. Abandoning a still-running read would be
        // worse than waiting: the orphaned read keeps the shared transport's I/O lock, so the flasher's
        // reconnect would race/block on it anyway — the exact orphaned-read hand-off hazard this drain
        // exists to prevent. The hard path additionally cancels so the in-flight read aborts at once.
        _stopKeepAlive = true;

        var cts = _keepAliveCts;
        if (hard)
        {
            try { cts?.Cancel(); }
            catch (ObjectDisposedException) { /* already torn down */ }
        }

        var task = _keepAliveTask;
        if (task != null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error while stopping the HID bootloader keep-alive loop.");
            }
        }

        _keepAliveTask = null;
        _keepAliveCts?.Dispose();
        _keepAliveCts = null;
    }
    #endregion

    #region IDisposable
    /// <summary>
    /// Tears down the keep-alive loop and disposes the owned transport. Each hold owns a fresh transport
    /// (the watcher's factory news one up per device), so disposing it here is what deterministically
    /// closes the exclusive HID handle — without it, a surprise-removal drop would leave the handle open
    /// until GC finalization, transiently locking out a bootloader that re-appears at the same path and
    /// defeating the very wedge protection (#568) this hold provides.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopKeepAlive = true;

        try { _keepAliveCts?.Cancel(); }
        catch (ObjectDisposedException) { /* already torn down */ }

        try { _keepAliveTask?.Wait(TimeSpan.FromSeconds(2)); }
        catch (Exception) { /* best-effort during shutdown */ }

        // Dispose the owned transport (closes its exclusive HID handle) once the keep-alive read is no
        // longer in flight against it.
        try { _transport.Dispose(); }
        catch (Exception ex) { _logger.Warning(ex, "Error disposing the HID bootloader transport."); }

        _keepAliveCts?.Dispose();
        _keepAliveCts = null;
        _gate.Dispose();
    }
    #endregion
}
