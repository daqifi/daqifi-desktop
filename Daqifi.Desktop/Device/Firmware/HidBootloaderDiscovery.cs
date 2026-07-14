using Daqifi.Core.Device.Discovery;
using Daqifi.Desktop.Common.Loggers;

namespace Daqifi.Desktop.Device.Firmware;

/// <summary>
/// Default <see cref="IBootloaderDiscovery"/> implementation: wraps Core's <see cref="HidDeviceFinder"/> in a
/// <see cref="ContinuousDeviceFinder"/> and forwards every discovered HID bootloader as a
/// <see cref="BootloaderDiscoveredEventArgs"/>. Unlike the connection dialog's old one-shot discovery,
/// this keeps polling so additional hot-plugged bootloaders are surfaced too; the watcher holds each one
/// exclusively the moment it appears, so the finder's per-pass open attempts on an already-held device
/// fail harmlessly (a refused exclusive open sends no I/O to the device).
/// </summary>
public sealed class HidBootloaderDiscovery : IBootloaderDiscovery, IDisposable
{
    #region Constants
    /// <summary>Default pause between discovery passes.</summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);

    // Bound for Stop()'s wait on the scan loop's exit. Matches ContinuousDiscoveryOptions' default
    // PassTimeout — the longest a single in-flight pass is ever allowed to run — plus slack for the
    // loop to observe cancellation and unwind.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);
    #endregion

    #region Private Fields
    private readonly IAppLogger _logger;
    private readonly TimeSpan _pollInterval;
    private readonly Func<HidDeviceFinder> _finderFactory;
    private readonly object _sync = new();

    private ContinuousDeviceFinder? _finder;
    private bool _disposed;
    #endregion

    /// <inheritdoc />
    public event EventHandler<BootloaderDiscoveredEventArgs>? BootloaderDiscovered;

    /// <summary>Creates the discovery source.</summary>
    /// <param name="logger">Application logger for diagnostics.</param>
    /// <param name="pollInterval">Pause between discovery passes; null uses <see cref="DefaultPollInterval"/>.</param>
    /// <param name="finderFactory">
    /// Factory for the underlying Core HID finder; a fresh one is created per Start/Stop cycle (the finder
    /// is disposed on Stop). Null uses the production <c>HidDeviceFinder</c>; tests inject a fake.
    /// </param>
    public HidBootloaderDiscovery(
        IAppLogger logger,
        TimeSpan? pollInterval = null,
        Func<HidDeviceFinder>? finderFactory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pollInterval = pollInterval ?? DefaultPollInterval;
        _finderFactory = finderFactory ?? (() => new HidDeviceFinder());
    }

    /// <inheritdoc />
    public void Start()
    {
        lock (_sync)
        {
            if (_disposed || _finder != null)
            {
                return;
            }

            var finder = new ContinuousDeviceFinder(
                _finderFactory(),
                new ContinuousDiscoveryOptions { Interval = _pollInterval });
            finder.DeviceDiscovered += OnDeviceDiscovered;
            finder.ScanError += OnScanError;
            _finder = finder;
            finder.Start();
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        ContinuousDeviceFinder? finder;
        lock (_sync)
        {
            finder = _finder;
            _finder = null;
        }

        if (finder == null)
        {
            return;
        }

        finder.DeviceDiscovered -= OnDeviceDiscovered;
        finder.ScanError -= OnScanError;

        // Block (bounded by the finder's own pass timeout) until the scan loop has actually exited — not
        // just cancellation requested — so BootloaderWatcher's use of Stop() to pause discovery around a
        // flash can rely on discovery being fully quiesced before handing HID I/O to the flasher.
        // StopAsync is ConfigureAwait(false) throughout, so waiting on it here cannot deadlock even when
        // Stop() runs on the UI thread.
        try
        {
            if (!finder.StopAsync().Wait(StopTimeout))
            {
                _logger.Warning("Timed out waiting for bootloader discovery to stop.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error stopping bootloader discovery");
        }
        finally
        {
            finder.Dispose();
        }
    }

    private void OnDeviceDiscovered(object? sender, DeviceDiscoveredEventArgs e)
    {
        try
        {
            var info = e.DeviceInfo;
            if (string.IsNullOrWhiteSpace(info.DevicePath))
            {
                return;
            }

            BootloaderDiscovered?.Invoke(this, new BootloaderDiscoveredEventArgs(info.DevicePath, info.Name));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling bootloader discovery");
        }
    }

    private void OnScanError(object? sender, ContinuousDiscoveryErrorEventArgs e)
    {
        _logger.Error(e.Exception, "Error in bootloader discovery loop");
    }

    /// <summary>Stops discovery and releases the finder.</summary>
    public void Dispose()
    {
        // Mark disposed atomically with the same lock Start() gates on, before tearing down the finder.
        // Setting _disposed after Stop() (as opposed to before) would leave a window where a concurrent
        // Start() could observe _disposed == false and _finder == null and spin up a new finder that
        // outlives this Dispose() call.
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Stop();
    }
}
