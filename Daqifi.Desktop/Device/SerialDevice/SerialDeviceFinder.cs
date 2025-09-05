using System.ComponentModel;
using System.Management;
using Daqifi.Desktop.Common.Loggers;

namespace Daqifi.Desktop.Device.SerialDevice;

public class SerialDeviceFinder : IDeviceFinder
{
    #region Private Data
    private string[] _serialPorts = { };
    private static ManagementEventWatcher _deviceAddedWatcher;
    private static ManagementEventWatcher _deviceRemovedWatcher;
    private readonly AppLogger AppLogger = AppLogger.Instance;
    public event OnDeviceFoundHandler OnDeviceFound;
    public event OnDeviceRemovedHandler OnDeviceRemoved;
    #endregion

    #region Constructor
    public SerialDeviceFinder()
    {
        WireUpSerialPortChanges();
    }
    #endregion

    private void WireUpSerialPortChanges()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                // EventType 2 is Device Arrival
                var deviceAddedQuery = new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 2");

                // EventType 3 is Device Removal
                var deviceRemovedQuery = new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 3");

                _deviceAddedWatcher = new ManagementEventWatcher(deviceAddedQuery);
                _deviceRemovedWatcher = new ManagementEventWatcher(deviceRemovedQuery);

                _deviceAddedWatcher.EventArrived += (sender, eventArgs) => RaisePortsChangedIfNecessary();
                _deviceRemovedWatcher.EventArrived += (sender, eventArgs) => RaisePortsChangedIfNecessary();

                _deviceAddedWatcher.Start();
                _deviceRemovedWatcher.Start();
            }
            catch (ManagementException ex)
            {
                //AppLogger.Instance.Error(ex, "Error setting up device watchers: " + ex.Message, ex);
            }
            catch (Exception ex)
            {

            }
        }
        else
        {

        }
    }


    private void RaisePortsChangedIfNecessary()
    {
        var bw = new BackgroundWorker();
        bw.DoWork += delegate
        {
            lock (_serialPorts)
            {
                var availableSerialPorts = SerialDeviceHelper.GetAvailableDaqifiPorts();
                var addedPorts = availableSerialPorts.Except(_serialPorts);
                var removedPorts = _serialPorts.Except(availableSerialPorts);

                foreach (var portName in addedPorts)
                {
                    var device = new SerialStreamingDevice(portName);

                    // Immediately notify device found so it appears in UI right away
                    NotifyDeviceFound(this, device);

                    // Try to get device information in background and update UI when complete
                    Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(500); // Brief delay to let UI settle
                            if (device.TryGetDeviceInfo())
                            {
                                // Notify again with updated device info
                                // This will trigger UI refresh with the new information
                                NotifyDeviceUpdated(this, device);
                            }
                        }
                        catch (Exception ex)
                        {
                            // Log but don't fail - device still shows with port name only
                            AppLogger.Warning($"Failed to retrieve device info for port {portName}: {ex.Message}");
                        }
                    });
                }

                foreach (var portName in removedPorts)
                {
                    var device = new SerialStreamingDevice(portName);
                    NotifyDeviceRemoved(this, device);
                }

                _serialPorts = availableSerialPorts;
            }
        };

        bw.RunWorkerAsync();

    }

    public void Start()
    {
        try
        {
            RaisePortsChangedIfNecessary();

            _deviceAddedWatcher?.Start();
            _deviceRemovedWatcher?.Start();
        }
        catch (Exception ex)
        {

        }
    }

    public void Stop()
    {
        try
        {
            _deviceAddedWatcher?.Stop();
            _deviceRemovedWatcher?.Stop();
        }
        catch (Exception ex)
        {

        }
    }

    public void NotifyDeviceFound(object sender, IDevice device)
    {
        OnDeviceFound?.Invoke(sender, device);
    }

    public void NotifyDeviceRemoved(object sender, IDevice device)
    {
        OnDeviceRemoved?.Invoke(sender, device);
    }

    public void NotifyDeviceUpdated(object sender, IDevice device)
    {
        // Trigger a device removal and re-addition to refresh the UI
        OnDeviceRemoved?.Invoke(sender, device);
        OnDeviceFound?.Invoke(sender, device);
    }
}