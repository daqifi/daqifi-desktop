using Daqifi.Core.Device.Discovery;
using Daqifi.Desktop.Common.Loggers;

namespace Daqifi.Desktop.Device.Firmware;

/// <summary>
/// Default <see cref="IBootloaderDiscovery"/> implementation: a continuous loop over Core's
/// <see cref="HidDeviceFinder"/> that forwards every matching HID bootloader as a
/// <see cref="BootloaderDiscoveredEventArgs"/>. Unlike the connection dialog's old one-shot discovery,
/// this keeps polling so additional hot-plugged bootloaders are surfaced too; the watcher holds each one
/// exclusively the moment it appears, so the finder's per-cycle open attempts on an already-held device
/// fail harmlessly (a refused exclusive open sends no I/O to the device).
/// </summary>
public sealed class HidBootloaderDiscovery : IBootloaderDiscovery, IDisposable
{
    #region Constants
    /// <summary>Default pause between discovery cycles.</summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);
    #endregion

    #region Private Fields
    private readonly IAppLogger _logger;
    private readonly TimeSpan _pollInterval;
    private readonly Func<HidDeviceFinder> _finderFactory;
    private readonly object _sync = new();

    private HidDeviceFinder? _finder;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private bool _disposed;
    #endregion

    /// <inheritdoc />
    public event EventHandler<BootloaderDiscoveredEventArgs>? BootloaderDiscovered;

    /// <summary>Creates the discovery source.</summary>
    /// <param name="logger">Application logger for diagnostics.</param>
    /// <param name="pollInterval">Pause between discovery cycles; null uses <see cref="DefaultPollInterval"/>.</param>
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
            if (_disposed || _loopTask is { IsCompleted: false })
            {
                return;
            }

            // Restart-after-stop: dispose the prior finder/CTS before replacing them so we never leak a
            // subscribed finder or an undisposed CancellationTokenSource.
            if (_finder != null)
            {
                _finder.DeviceDiscovered -= OnDeviceDiscovered;
                _finder.Dispose();
            }
            _cts?.Dispose();

            _finder = _finderFactory();
            _cts = new CancellationTokenSource();
            _finder.DeviceDiscovered += OnDeviceDiscovered;
            _loopTask = RunAsync(_finder, _cts.Token);
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        lock (_sync)
        {
            _cts?.Cancel();

            if (_finder != null)
            {
                _finder.DeviceDiscovered -= OnDeviceDiscovered;
                _finder.Dispose();
                _finder = null;
            }

            _cts?.Dispose();
            _cts = null;

            // Clear the task reference (without blocking on it — Stop() may run on the UI thread) so a
            // subsequent Start() restarts discovery immediately. The abandoned loop owns its own
            // (now-cancelled, now-disposed) finder via a parameter, so it drains and exits on its own; a
            // fresh Start() spins up a new finder/CTS and is not gated on the old loop completing.
            _loopTask = null;
        }
    }

    private async Task RunAsync(HidDeviceFinder finder, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await finder.DiscoverAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopped.
        }
        catch (ObjectDisposedException)
        {
            // Expected when the finder is disposed during a discovery cycle.
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in bootloader discovery loop");
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

    /// <summary>Stops discovery and releases the finder.</summary>
    public void Dispose()
    {
        Stop();
        _disposed = true;
    }
}
