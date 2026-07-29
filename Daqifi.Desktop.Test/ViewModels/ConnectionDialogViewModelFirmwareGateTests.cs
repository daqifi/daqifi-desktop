using Daqifi.Desktop.Device;
using Daqifi.Desktop.Device.Firmware;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.ViewModels;
using Moq;
using System.Collections.ObjectModel;
using System.Reflection;

namespace Daqifi.Desktop.Test.ViewModels;

/// <summary>
/// Verifies the connection dialog suspends its serial + WiFi discovery while a firmware update is in
/// progress (issue #738): the per-cycle SerialDeviceFinder opens every DAQiFi VID/PID COM port each
/// pass, and a probe landing in Core's post-flash JumpingToApp reconnect window steals the
/// re-enumerating port and strands the update in a timeout. The dialog gates its <c>Start*Discovery</c>
/// on <see cref="ConnectionManager.IsFirmwareUpdateInProgress"/> so a dialog opened mid-flash never
/// starts a finder.
/// <para>
/// It also covers the second pause reason (issue #777): a dialog-driven HID bootloader flash never sets
/// <c>ConnectionManager.DeviceBeingUpdated</c>, so a coordinator auto-update of a <em>different</em>
/// device ending mid-write used to restart discovery inside the window <c>ConnectHid</c> deliberately
/// quiesced. Both reasons must gate the restart, and — the mirror-image risk — discovery must still come
/// back once both have cleared, in either order.
/// </para>
/// </summary>
[TestClass]
public class ConnectionDialogViewModelFirmwareGateTests
{
    private Func<DuplicateDeviceCheckResult, DuplicateDeviceAction>? _originalDuplicateDeviceHandler;

    [TestInitialize]
    public void TestInitialize()
    {
        _originalDuplicateDeviceHandler = ConnectionManager.Instance.DuplicateDeviceHandler;
        ConnectionManager.Instance.DuplicateDeviceHandler = null;
        ConnectionManager.Instance.DeviceBeingUpdated = null;
    }

    [TestCleanup]
    public void TestCleanup()
    {
        ConnectionManager.Instance.DeviceBeingUpdated = null;
        ConnectionManager.Instance.DuplicateDeviceHandler = _originalDuplicateDeviceHandler;
    }

    [TestMethod]
    public void StartSerialDiscovery_DoesNotStartFinder_WhileFirmwareUpdateInProgress()
    {
        using var viewModel = CreateViewModel();
        ConnectionManager.Instance.DeviceBeingUpdated = CreateUsbDevice();

        InvokePrivate(viewModel, "StartSerialDiscovery");

        // No finder created => no COM ports opened/probed during the flash.
        Assert.IsNull(GetPrivateField(viewModel, "_serialFinder"),
            "Serial discovery must not start while a firmware update is in progress.");
    }

    [TestMethod]
    public void StartWiFiDiscovery_DoesNotStartFinder_WhileFirmwareUpdateInProgress()
    {
        using var viewModel = CreateViewModel();
        ConnectionManager.Instance.DeviceBeingUpdated = CreateUsbDevice();

        InvokePrivate(viewModel, "StartWiFiDiscovery");

        Assert.IsNull(GetPrivateField(viewModel, "_wifiFinder"),
            "WiFi discovery must not start while a firmware update is in progress.");
    }

    [TestMethod]
    public void StartConnectionFinders_StartsNothing_WhileFirmwareUpdateInProgress()
    {
        // The user opens the connection dialog mid-flash (the issue #738 breadcrumb sequence): the
        // public entry point that kicks off discovery must create neither finder.
        using var viewModel = CreateViewModel();
        ConnectionManager.Instance.DeviceBeingUpdated = CreateUsbDevice();

        viewModel.StartConnectionFinders();

        Assert.IsNull(GetPrivateField(viewModel, "_serialFinder"));
        Assert.IsNull(GetPrivateField(viewModel, "_wifiFinder"));
    }

    [TestMethod]
    public void StartConnectionFinders_StartsNothing_WhileHidFirmwareDialogOpen()
    {
        // issue #777: the HID firmware dialog's quiesce window is a pause reason of its own —
        // ConnectionManager knows nothing about a manual bootloader flash.
        var watcher = new FakeBootloaderWatcher();
        using var viewModel = CreateViewModel(watcher);
        SetPrivateField(viewModel, "_hidFirmwareDialogOpen", true);

        viewModel.StartConnectionFinders();

        Assert.IsNull(GetPrivateField(viewModel, "_serialFinder"),
            "Serial discovery must not start while the HID firmware dialog is open.");
        Assert.IsNull(GetPrivateField(viewModel, "_wifiFinder"),
            "WiFi discovery must not start while the HID firmware dialog is open.");
    }

    [TestMethod]
    public void StartConnectionFinders_StartsNothing_WhileBootloaderFlashInProgress()
    {
        // issue #777: a bootloader write can outlive the modal dialog that started it, so the watcher's
        // own in-progress flag has to gate the restart too.
        var watcher = new FakeBootloaderWatcher();
        using var viewModel = CreateViewModel(watcher);
        watcher.SetFlashInProgress(true);

        viewModel.StartConnectionFinders();

        Assert.IsNull(GetPrivateField(viewModel, "_serialFinder"),
            "Serial discovery must not start while a HID bootloader write is in flight.");
        Assert.IsNull(GetPrivateField(viewModel, "_wifiFinder"),
            "WiFi discovery must not start while a HID bootloader write is in flight.");
    }

    [TestMethod]
    public async Task HidFlashWindow_WhenAutoUpdateOfAnotherDeviceEnds_DoesNotRestartDiscovery()
    {
        // The issue #777 sequence: device A is auto-updating while the user opens the HID firmware
        // dialog for device B. A's update finishes mid-window and its FirmwareUpdateInProgressChanged
        // handler ran the restart unconditionally — re-opening every COM port and resuming WiFi
        // broadcasts inside the window ConnectHid drained on purpose.
        var watcher = new FakeBootloaderWatcher();
        using var viewModel = CreateViewModel(watcher);
        ConnectionManager.Instance.DeviceBeingUpdated = CreateUsbDevice();

        var ranInsideWindow = false;
        await InvokeHidFlashWindowAsync(viewModel, () =>
        {
            ranInsideWindow = true;
            Assert.IsTrue(IsDiscoveryPausedForFirmware(viewModel),
                "Discovery must be paused for the whole HID firmware dialog window.");

            // Device A's auto-update finishes. This raises the real event and runs the real handler.
            ConnectionManager.Instance.DeviceBeingUpdated = null;

            Assert.IsNull(GetPrivateField(viewModel, "_serialFinder"),
                "The auto-update ending must not re-open COM ports during the HID flash window.");
            Assert.IsNull(GetPrivateField(viewModel, "_wifiFinder"),
                "The auto-update ending must not resume WiFi broadcasts during the HID flash window.");
            Assert.IsTrue(IsDiscoveryPausedForFirmware(viewModel),
                "The HID firmware dialog must keep discovery paused after the auto-update clears.");

            // Keep one reason active across the window's own exit restart so this unit test never spins
            // up a real COM-port/UDP finder. The resume-after-both-clear behavior is covered separately.
            ConnectionManager.Instance.DeviceBeingUpdated = CreateUsbDevice();
        });

        Assert.IsTrue(ranInsideWindow, "The quiesced window must have run the dialog action.");
        Assert.IsNull(GetPrivateField(viewModel, "_serialFinder"));
        Assert.IsNull(GetPrivateField(viewModel, "_wifiFinder"));
        Assert.IsFalse(GetPrivateFieldValue<bool>(viewModel, "_hidFirmwareDialogOpen"),
            "The HID firmware dialog pause reason must clear when the dialog closes.");
    }

    [TestMethod]
    public void DiscoveryPause_Clears_WhenTheBootloaderFlashFinishesAfterTheAutoUpdate()
    {
        // The mirror-image risk of the fix: a guard that refuses the restart must not leave discovery
        // paused forever. The auto-update clears first, the HID write second — and the watcher's falling
        // edge is what retries the restart.
        var watcher = new FakeBootloaderWatcher();
        using var viewModel = CreateViewModel(watcher);
        ParkDiscoveryDrains(viewModel);

        ConnectionManager.Instance.DeviceBeingUpdated = CreateUsbDevice();
        watcher.SetFlashInProgress(true);
        Assert.IsTrue(IsDiscoveryPausedForFirmware(viewModel));

        ConnectionManager.Instance.DeviceBeingUpdated = null;
        Assert.IsTrue(IsDiscoveryPausedForFirmware(viewModel),
            "Discovery must stay paused while the HID bootloader write is still in flight.");

        watcher.SetFlashInProgress(false);

        Assert.IsFalse(IsDiscoveryPausedForFirmware(viewModel),
            "Discovery must be free to restart once both firmware pause reasons have cleared.");
        Assert.IsTrue(watcher.FlashInProgressSubscriberCount > 0,
            "The dialog must subscribe to the watcher's flash-state event, or the write finishing last " +
            "would leave discovery paused for the rest of the dialog's life.");
    }

    [TestMethod]
    public void DiscoveryPause_Clears_WhenTheAutoUpdateFinishesAfterTheBootloaderFlash()
    {
        // Same as above with the opposite completion order: the connection manager's own end-event is
        // then the retry trigger.
        var watcher = new FakeBootloaderWatcher();
        using var viewModel = CreateViewModel(watcher);
        ParkDiscoveryDrains(viewModel);

        ConnectionManager.Instance.DeviceBeingUpdated = CreateUsbDevice();
        watcher.SetFlashInProgress(true);

        watcher.SetFlashInProgress(false);
        Assert.IsTrue(IsDiscoveryPausedForFirmware(viewModel),
            "Discovery must stay paused while the auto-update is still running.");

        ConnectionManager.Instance.DeviceBeingUpdated = null;

        Assert.IsFalse(IsDiscoveryPausedForFirmware(viewModel),
            "Discovery must be free to restart once both firmware pause reasons have cleared.");
    }

    [TestMethod]
    public void Close_UnsubscribesFromTheWatchersFlashEvent()
    {
        // The watcher is an app-global singleton: a leaked handler would keep a closed dialog alive and
        // let it restart discovery long after it was dismissed.
        var watcher = new FakeBootloaderWatcher();
        var viewModel = CreateViewModel(watcher);
        Assert.IsTrue(watcher.FlashInProgressSubscriberCount > 0);

        viewModel.Dispose();

        Assert.AreEqual(0, watcher.FlashInProgressSubscriberCount,
            "Closing the dialog must unsubscribe it from the app-global watcher's flash event.");
    }

    private static IStreamingDevice CreateUsbDevice()
    {
        var device = new Mock<IStreamingDevice>();
        device.SetupGet(d => d.ConnectionType).Returns(ConnectionType.Usb);
        device.SetupGet(d => d.Name).Returns("UpdatingDevice");
        return device.Object;
    }

    private static ConnectionDialogViewModel CreateViewModel(IBootloaderWatcher? watcher = null)
    {
        var dialogService = new Mock<IDialogService>();
        return new ConnectionDialogViewModel(dialogService.Object, watcher);
    }

    /// <summary>
    /// Parks both discovery drains on a task that never completes so every restart this test triggers
    /// defers instead of creating a real COM-port/UDP finder (see <c>RestartDiscoveryWhenDrained</c>).
    /// The assertions then read the pause gate itself, which is what every <c>Start*Discovery</c>
    /// consults before touching hardware.
    /// </summary>
    private static void ParkDiscoveryDrains(ConnectionDialogViewModel viewModel)
    {
        var neverDrains = new TaskCompletionSource().Task;
        SetPrivateField(viewModel, "_wifiStopTask", neverDrains);
        SetPrivateField(viewModel, "_serialStopTask", neverDrains);
    }

    /// <summary>
    /// Runs the dialog's HID firmware-dialog quiesce window, standing in for the modal
    /// <c>ShowDialog</c> with <paramref name="whileOpen"/>.
    /// </summary>
    private static async Task InvokeHidFlashWindowAsync(ConnectionDialogViewModel viewModel, Action whileOpen)
    {
        var method = typeof(ConnectionDialogViewModel).GetMethod(
            "RunWithHidFlashQuiescedAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method, "RunWithHidFlashQuiescedAsync not found.");

        var task = (Task?)method.Invoke(viewModel, [whileOpen]);
        Assert.IsNotNull(task);
        await task;
    }

    private static bool IsDiscoveryPausedForFirmware(ConnectionDialogViewModel viewModel)
    {
        var property = typeof(ConnectionDialogViewModel).GetProperty(
            "IsDiscoveryPausedForFirmware", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(property, "IsDiscoveryPausedForFirmware not found.");
        return (bool)property.GetValue(viewModel)!;
    }

    private static void InvokePrivate(ConnectionDialogViewModel viewModel, string methodName)
    {
        var method = typeof(ConnectionDialogViewModel).GetMethod(
            methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method, $"{methodName} not found.");
        method.Invoke(viewModel, null);
    }

    private static object? GetPrivateField(ConnectionDialogViewModel viewModel, string fieldName)
    {
        var field = typeof(ConnectionDialogViewModel).GetField(
            fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, $"{fieldName} not found.");
        return field.GetValue(viewModel);
    }

    private static T GetPrivateFieldValue<T>(ConnectionDialogViewModel viewModel, string fieldName) =>
        (T)GetPrivateField(viewModel, fieldName)!;

    private static void SetPrivateField(ConnectionDialogViewModel viewModel, string fieldName, object? value)
    {
        var field = typeof(ConnectionDialogViewModel).GetField(
            fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, $"{fieldName} not found.");
        field.SetValue(viewModel, value);
    }

    #region Fakes
    /// <summary>
    /// Minimal <see cref="IBootloaderWatcher"/> stand-in that lets a test drive the flash-in-progress
    /// state the dialog gates on, and reports how many handlers are attached to the change event.
    /// </summary>
    private sealed class FakeBootloaderWatcher : IBootloaderWatcher
    {
        private readonly ObservableCollection<HeldBootloader> _bootloaders = [];
        private EventHandler? _flashInProgressChanged;

        public FakeBootloaderWatcher() => Bootloaders = new ReadOnlyObservableCollection<HeldBootloader>(_bootloaders);

        public ReadOnlyObservableCollection<HeldBootloader> Bootloaders { get; }

        public event EventHandler<BootloaderHoldDroppedEventArgs>? HoldDropped;

        public event EventHandler? FlashInProgressChanged
        {
            add => _flashInProgressChanged += value;
            remove => _flashInProgressChanged -= value;
        }

        public bool IsFlashInProgress { get; private set; }

        public int FlashInProgressSubscriberCount => _flashInProgressChanged?.GetInvocationList().Length ?? 0;

        public void SetFlashInProgress(bool inProgress)
        {
            if (IsFlashInProgress == inProgress)
            {
                return;
            }

            IsFlashInProgress = inProgress;
            _flashInProgressChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Start() { }

        public Task<IAsyncDisposable> PrepareFlashAsync(string devicePath)
        {
            SetFlashInProgress(true);
            return Task.FromResult<IAsyncDisposable>(new FakeLease(() => SetFlashInProgress(false)));
        }

        public Task<IAsyncDisposable> SuspendDiscoveryAsync() =>
            Task.FromResult<IAsyncDisposable>(new FakeLease(() => { }));

        /// <summary>Raises <see cref="HoldDropped"/>; present so the event is not merely unused.</summary>
        public void RaiseHoldDropped(string devicePath) =>
            HoldDropped?.Invoke(this, new BootloaderHoldDroppedEventArgs(devicePath));

        private sealed class FakeLease(Action onDispose) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                onDispose();
                return ValueTask.CompletedTask;
            }
        }
    }
    #endregion
}
