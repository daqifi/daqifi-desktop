using System.Collections.ObjectModel;
using Daqifi.Desktop.Common.Loggers;

namespace Daqifi.Desktop.Device.Firmware;

/// <summary>
/// Default <see cref="IBootloaderWatcher"/> implementation. Runs continuous HID-bootloader discovery and,
/// for every bootloader it finds, opens an exclusive per-device hold (its own transport, keyed by device
/// path) with a keep-alive read so Windows USB selective-suspend can't wedge it before flashing
/// (daqifi-nyquist-firmware#568). All mutating operations are serialized through <see cref="_gate"/>; the
/// bound <see cref="Bootloaders"/> collection is mutated on the UI thread.
/// </summary>
public sealed class BootloaderWatcher : IBootloaderWatcher, IDisposable
{
    #region Private Fields
    private readonly IBootloaderDiscovery _discovery;
    private readonly Func<string, string?, IBootloaderHoldService> _holdFactory;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Source of truth, keyed by device path. Mirrored into Bootloaders for the UI.
    private readonly Dictionary<string, IBootloaderHoldService> _holds = new(StringComparer.Ordinal);

    private bool _started;
    private bool _disposed;

    // The single device currently being flashed (its hold is released so the flasher can open it); new
    // grabs for this path are suppressed until the flash lease is disposed.
    private string? _flashingPath;

    // Set while an auto-update is in progress: discovery is paused and new grabs are suppressed, but
    // existing holds stay alive so other sitting bootloaders remain wedge-proof.
    private bool _grabSuppressed;
    #endregion

    #region Constructor
    /// <summary>Creates the watcher.</summary>
    /// <param name="discovery">Continuous HID-bootloader discovery source.</param>
    /// <param name="holdFactory">
    /// Creates a per-device hold (device path, friendly name) → hold service. Each hold owns its own
    /// exclusive HID transport so holding/flashing one device never disturbs the others.
    /// </param>
    /// <param name="logger">Application logger for diagnostics.</param>
    public BootloaderWatcher(
        IBootloaderDiscovery discovery,
        Func<string, string?, IBootloaderHoldService> holdFactory,
        IAppLogger logger)
    {
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _holdFactory = holdFactory ?? throw new ArgumentNullException(nameof(holdFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Bootloaders = new ReadOnlyObservableCollection<HeldBootloader>(_bootloaders);
    }
    #endregion

    #region IBootloaderWatcher
    // Internal mutable backing; the public surface is a read-only view so external code (the bound
    // dialog) can observe the list but cannot mutate the watcher's state.
    private readonly ObservableCollection<HeldBootloader> _bootloaders = [];

    /// <inheritdoc />
    public ReadOnlyObservableCollection<HeldBootloader> Bootloaders { get; }

    /// <inheritdoc />
    public event EventHandler<BootloaderHoldDroppedEventArgs>? HoldDropped;

    /// <inheritdoc />
    public void Start()
    {
        if (_disposed || _started)
        {
            return;
        }

        _started = true;
        _discovery.BootloaderDiscovered += OnBootloaderDiscovered;
        _discovery.Start();
        _logger.Information("Bootloader watcher started; holding every detected HID bootloader app-wide.");
    }

    /// <inheritdoc />
    public async Task<IAsyncDisposable> PrepareFlashAsync(string devicePath)
    {
        ArgumentNullException.ThrowIfNull(devicePath);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Pause discovery and mark this device as the flash target so it isn't re-grabbed while the
            // flasher owns it. Other holds are left untouched (they stay wedge-proof).
            _discovery.Stop();
            _flashingPath = devicePath;

            if (_holds.TryGetValue(devicePath, out var hold))
            {
                // Release ONLY the target so the flasher's transport can open it by path. Graceful release
                // (no HoldDropped); the hold object is kept so the lease can re-grab the same device after.
                await hold.ReleaseAsync().ConfigureAwait(false);
                _logger.Information($"Released hold on {devicePath} for flashing; other bootloaders stay held.");
            }
        }
        finally
        {
            _gate.Release();
        }

        return new WatcherLease(() => ResumeAfterFlashAsync(devicePath));
    }

    /// <inheritdoc />
    public async Task<IAsyncDisposable> SuspendDiscoveryAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _discovery.Stop();
            _grabSuppressed = true;
            _logger.Information("Bootloader watcher discovery suspended for an auto-update; existing holds kept.");
        }
        finally
        {
            _gate.Release();
        }

        return new WatcherLease(ResumeDiscoveryAsync);
    }
    #endregion

    #region Resume (lease disposal)
    private async Task ResumeAfterFlashAsync(string devicePath)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _flashingPath = null;

            if (_holds.TryGetValue(devicePath, out var hold))
            {
                // Re-grab the exact device by path. A failed/cancelled flash leaves it a bootloader →
                // re-held. A successful flash left it in application mode → the open fails and we drop it.
                await hold.BeginHoldAsync().ConfigureAwait(false);
                if (!hold.IsHolding)
                {
                    RemoveHold(devicePath, hold);
                    _logger.Information($"{devicePath} is no longer a bootloader after flashing; dropped from the held list.");
                }
            }

            ResumeDiscoveryIfIdle();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ResumeDiscoveryAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _grabSuppressed = false;
            ResumeDiscoveryIfIdle();
            _logger.Information("Bootloader watcher discovery resumed after auto-update.");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Restarts discovery only when no operation still needs it paused. A manual flash
    /// (<see cref="_flashingPath"/>) and an auto-update (<see cref="_grabSuppressed"/>) can overlap on a
    /// multi-device bench; whichever lease disposes first must NOT resume discovery while the other is
    /// still flashing — a live finder cycle could re-open the in-flight device during the flasher's
    /// reconnect window. Must be called under <see cref="_gate"/>.
    /// </summary>
    private void ResumeDiscoveryIfIdle()
    {
        if (!_disposed && _flashingPath == null && !_grabSuppressed)
        {
            _discovery.Start();
        }
    }
    #endregion

    #region Discovery / drop handling
    private void OnBootloaderDiscovered(object? sender, BootloaderDiscoveredEventArgs e)
    {
        // The discovery loop is synchronous; grabbing the hold is async, so dispatch without blocking it.
        RunDetached(HandleDiscoveredAsync(e.DevicePath, e.DeviceName, e.LocationKey), "handling a discovered bootloader");
    }

    private async Task HandleDiscoveredAsync(string devicePath, string? deviceName, string? locationKey)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        IBootloaderHoldService? created = null;
        try
        {
            if (_disposed || _grabSuppressed || devicePath == _flashingPath || _holds.ContainsKey(devicePath))
            {
                return;
            }

            created = _holdFactory(devicePath, deviceName);
            created.HoldDropped += OnHoldDropped;
            await created.BeginHoldAsync().ConfigureAwait(false);

            if (!created.IsHolding)
            {
                // Could not open it (already gone, or transitioned to application mode). Don't list it.
                created.HoldDropped -= OnHoldDropped;
                created.Dispose();
                created = null;
                return;
            }

            _holds[devicePath] = created;
            created = null; // ownership transferred to _holds BEFORE the UI marshal, so a marshal failure
                            // can never make the finally dispose a now-tracked hold (stale-entry bug).
            var displayName = ResolveDisplayName(deviceName, locationKey);
            InvokeOnUiThread(() =>
            {
                // Guard: a callback queued just before Dispose must not re-add a row after teardown.
                if (!_disposed)
                {
                    _bootloaders.Add(new HeldBootloader(devicePath, displayName, locationKey));
                }
            });
            _logger.Information($"Holding HID bootloader {devicePath} ({displayName}).");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Failed to hold discovered bootloader {devicePath}.");
        }
        finally
        {
            // If we created a hold but didn't transfer ownership, tear it down so we don't leak it.
            if (created != null)
            {
                created.HoldDropped -= OnHoldDropped;
                created.Dispose();
            }
            _gate.Release();
        }
    }

    private void OnHoldDropped(object? sender, EventArgs e)
    {
        if (sender is IBootloaderHoldService hold && hold.DevicePath != null)
        {
            RunDetached(HandleHoldDroppedAsync(hold.DevicePath, hold), "handling a dropped bootloader hold");
        }
    }

    private async Task HandleHoldDroppedAsync(string devicePath, IBootloaderHoldService hold)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Only act if this is still the live hold for the path (ignore a late event from a replaced one).
            if (_holds.TryGetValue(devicePath, out var current) && ReferenceEquals(current, hold))
            {
                RemoveHold(devicePath, hold);
                _logger.Information($"Held bootloader {devicePath} dropped (device removed).");
            }
        }
        finally
        {
            _gate.Release();
        }

        HoldDropped?.Invoke(this, new BootloaderHoldDroppedEventArgs(devicePath));
    }

    /// <summary>Removes and disposes a hold and its UI row. Must be called under <see cref="_gate"/>.</summary>
    private void RemoveHold(string devicePath, IBootloaderHoldService hold)
    {
        hold.HoldDropped -= OnHoldDropped;
        _holds.Remove(devicePath);
        hold.Dispose();
        InvokeOnUiThread(() =>
        {
            // Guard: a callback queued just before Dispose must not mutate the list after teardown.
            if (_disposed)
            {
                return;
            }

            var row = _bootloaders.FirstOrDefault(b => string.Equals(b.DevicePath, devicePath, StringComparison.Ordinal));
            if (row != null)
            {
                _bootloaders.Remove(row);
            }
        });
    }
    #endregion

    #region Helpers
    /// <summary>
    /// Picks the label shown for a held bootloader: the friendly device name when discovery could read
    /// one, else the USB physical location when that's available, else a generic fallback. Identical
    /// bootloaders share VID/PID and have no serial (see <see cref="HeldBootloader"/>), so without a
    /// name the location key is the most useful thing to show a user with more than one connected.
    /// </summary>
    private static string ResolveDisplayName(string? deviceName, string? locationKey)
    {
        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            return deviceName!;
        }

        return string.IsNullOrWhiteSpace(locationKey)
            ? "DAQiFi Bootloader"
            : $"Bootloader on USB port {locationKey}";
    }

    /// <summary>
    /// Observes a fire-and-forget task's faults. The handlers carry their own try/catch for expected
    /// failures; this is a backstop so an unexpected fault outside that scope (e.g. the gate already
    /// disposed during shutdown) is logged rather than going unobserved.
    /// </summary>
    private void RunDetached(Task task, string context)
    {
        _ = task.ContinueWith(
            t => _logger.Error(t.Exception!, $"Unhandled exception while {context}."),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private static void InvokeOnUiThread(Action action)
    {
        // No WPF dispatcher in unit tests (Application.Current is null) — run inline. In the app, marshal
        // the bound-collection mutation onto the UI thread. Use the NON-blocking BeginInvoke: these calls
        // happen while _gate is held, and a blocking Dispatcher.Invoke could lock-invert with Dispose()'s
        // synchronous _gate.Wait() (UI thread waits on the gate while a gate-holder waits on the UI
        // thread). BeginInvoke queues the mutation in order without blocking, so no inversion is possible.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        // Non-throwing: during app/dispatcher shutdown BeginInvoke can throw, and these calls run inside
        // the gate-protected handlers — a marshal failure must never propagate (it would strand watcher
        // state). If the dispatcher is gone the bound list simply won't update, which is fine at shutdown.
        try
        {
            if (!dispatcher.HasShutdownStarted)
            {
                dispatcher.BeginInvoke(action);
            }
        }
        catch (Exception)
        {
            // Dispatcher unavailable / shutting down — drop the UI update.
        }
    }
    #endregion

    #region IDisposable
    /// <summary>Stops discovery and releases every held bootloader.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _discovery.BootloaderDiscovered -= OnBootloaderDiscovered;
        _discovery.Stop();
        (_discovery as IDisposable)?.Dispose();

        // Bounded wait so a stuck gate-holder can't hang app shutdown. Whether or not the gate is
        // acquired we STILL dispose the holds (best-effort) — skipping that on timeout would leak their
        // exclusive HID handles. Snapshot the holds first so a racing in-flight handler can't trip the
        // enumeration; release the gate only if we actually took it.
        var gateAcquired = _gate.Wait(TimeSpan.FromSeconds(2));
        if (!gateAcquired)
        {
            _logger.Warning(
                "Timed out acquiring the gate during bootloader watcher dispose; disposing holds best-effort.");
        }

        try
        {
            foreach (var hold in _holds.Values.ToList())
            {
                hold.HoldDropped -= OnHoldDropped;
                hold.Dispose();
            }
            _holds.Clear();
            InvokeOnUiThread(_bootloaders.Clear);
        }
        finally
        {
            if (gateAcquired)
            {
                _gate.Release();
            }
            _gate.Dispose();
        }
    }
    #endregion

    #region Lease
    /// <summary>An <see cref="IAsyncDisposable"/> that runs a resume action exactly once on disposal.</summary>
    private sealed class WatcherLease : IAsyncDisposable
    {
        private Func<Task>? _onDispose;

        public WatcherLease(Func<Task> onDispose) => _onDispose = onDispose;

        public async ValueTask DisposeAsync()
        {
            var action = Interlocked.Exchange(ref _onDispose, null);
            if (action != null)
            {
                await action().ConfigureAwait(false);
            }
        }
    }
    #endregion
}
