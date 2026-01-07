using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Device.SerialDevice;
using Daqifi.Desktop.Logger;
using System.ComponentModel;
using System.Management;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Daqifi.Desktop;

public partial class ConnectionManager : ObservableObject
{
    #region Private Variables
    private readonly ManagementEventWatcher _deviceRemovedWatcher;
    #endregion

    #region Properties
    [ObservableProperty]
    private DAQifiConnectionStatus _connectionStatus = DAQifiConnectionStatus.Disconnected;

    [ObservableProperty]
    private List<IStreamingDevice> _connectedDevices;

    [ObservableProperty]
    private bool _isDisconnected = true;

    [ObservableProperty]
    private bool _notifyConnection;

    public string ConnectionStatusString { get; set; } = "Disconnected";

    /// <summary>
    /// Callback for handling duplicate device situations.
    /// Should return the user's choice on how to handle the duplicate.
    /// </summary>
    public Func<DuplicateDeviceCheckResult, DuplicateDeviceAction> DuplicateDeviceHandler { get; set; }

    /// <summary>
    /// Tracks the device currently undergoing firmware update to suppress disconnect notifications.
    /// </summary>
    public IStreamingDevice? DeviceBeingUpdated { get; set; }

    #endregion

    partial void OnConnectionStatusChanged(DAQifiConnectionStatus value)
    {
        UpdateStatusString();
        IsDisconnected = value != DAQifiConnectionStatus.Connected;
    }

    #region Singleton Constructor / Initalization
    private static readonly ConnectionManager instance = new();

    private ConnectionManager()
    {
        ConnectedDevices = new List<IStreamingDevice>();

        try
        {
            // EventType 3 is Device Removal
            var deviceRemovedQuery = new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 3");

            _deviceRemovedWatcher = new ManagementEventWatcher(deviceRemovedQuery);
            _deviceRemovedWatcher.EventArrived += (sender, eventArgs) => CheckIfSerialDeviceWasRemoved();
            _deviceRemovedWatcher.Start();
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error(ex, "Failed to initialize ManagementEventWatcher: " + ex.Message);
        }

    }

    public static ConnectionManager Instance => instance;

    #endregion

    public async Task Connect(IStreamingDevice device)
    {
        try
        {
            ConnectionStatus = DAQifiConnectionStatus.Connecting;
            
            // Check for duplicate device before connecting
            var duplicateResult = CheckForDuplicateDevice(device);
            if (duplicateResult.IsDuplicate)
            {
                if (DuplicateDeviceHandler != null)
                {
                    var action = DuplicateDeviceHandler(duplicateResult);
                    switch (action)
                    {
                        case DuplicateDeviceAction.KeepExisting:
                            ConnectionStatus = DAQifiConnectionStatus.AlreadyConnected;
                            return;
                        case DuplicateDeviceAction.Cancel:
                            ConnectionStatus = DAQifiConnectionStatus.Disconnected;
                            return;
                        case DuplicateDeviceAction.SwitchToNew:
                            // Disconnect the existing device and continue with connection
                            Disconnect(duplicateResult.ExistingDevice);
                            break;
                    }
                }
                else
                {
                    // No handler set, default behavior is to reject the duplicate
                    ConnectionStatus = duplicateResult.ExistingDevice != null ? DAQifiConnectionStatus.AlreadyConnected : DAQifiConnectionStatus.Error;
                    return;
                }
            }
            
            var isConnected = await Task.Run(() => device.Connect());
            if (!isConnected)
            {
                ConnectionStatus = DAQifiConnectionStatus.Error;
                return;
            }
            
            // Check again after connection (in case serial number wasn't available before connect)
            var postConnectDuplicateResult = CheckForDuplicateDevice(device);
            if (postConnectDuplicateResult.IsDuplicate)
            {
                // Disconnect the device we just connected since it's a duplicate
                device.Disconnect();
                ConnectionStatus = postConnectDuplicateResult.ExistingDevice != null ? DAQifiConnectionStatus.AlreadyConnected : DAQifiConnectionStatus.Error;
                return;
            }
            
            ConnectedDevices.Add(device);
            await Task.Delay(1000);
            OnPropertyChanged("ConnectedDevices");
            ConnectionStatus = DAQifiConnectionStatus.Connected;
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error(ex, "Failed to Connect in Connection");
            ConnectionStatus = DAQifiConnectionStatus.Error;
        }
    }

    public void Disconnect(IStreamingDevice device)
    {
        try
        {
            device.Disconnect();
            ConnectedDevices.Remove(device);
            OnPropertyChanged("ConnectedDevices");
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error(ex, "Failed in Disconnect");
        }
    }

    public void Reboot(IStreamingDevice device)
    {
        try
        {
            device.Reboot();
            ConnectedDevices.Remove(device);
            OnPropertyChanged("ConnectedDevices");
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error(ex, "Failed in Reboot");
        }
    }

    public void UpdateStatusString()
    {
        ConnectionStatusString = ConnectionStatus switch
        {
            DAQifiConnectionStatus.Disconnected => "Disconnected",
            DAQifiConnectionStatus.Connecting => "Connecting",
            DAQifiConnectionStatus.Connected => "Connected",
            DAQifiConnectionStatus.Error => "Error",
            DAQifiConnectionStatus.AlreadyConnected => "AlreadyConnected",
            _ => "Error"
        };
    }

    private void CheckIfSerialDeviceWasRemoved()
    {
        NotifyConnection = false;
        var bw = new BackgroundWorker();
        bw.DoWork += delegate
        {
            var availableSerialPorts = SerialDeviceHelper.GetAvailableDaqifiPorts();
            var devicesToRemove = new List<SerialStreamingDevice>();
            var lDevicesToRemove = new List<IStreamingDevice>();

            if (availableSerialPorts.Length == 0)
            {
                foreach (var device in ConnectedDevices)
                {
                    lDevicesToRemove.Add(device);
                }
            }
            else
            {
                foreach (var device in ConnectedDevices)
                {
                    if (device is SerialStreamingDevice serialDevice)
                    {
                        if (!availableSerialPorts.Contains(serialDevice.Port.PortName))
                        {
                            devicesToRemove.Add(serialDevice);
                        }
                    }
                }
            }
            foreach (var serialDevice in devicesToRemove)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(delegate
                {
                    Disconnect(serialDevice);
                });
            }
            foreach (var serialDevice in lDevicesToRemove)
            {
                if (!NotifyConnection)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(delegate
                    {
                        foreach (var channel in serialDevice.DataChannels)
                        {
                            LoggingManager.Instance.Unsubscribe(channel);
                        }
                        Disconnect(serialDevice);
                        // Only notify if this isn't an intentional disconnect during firmware update
                        if (DeviceBeingUpdated == null || DeviceBeingUpdated.Name != serialDevice.Name)
                        {
                            NotifyConnection = true;
                        }
                    });
                }
            }
        };

        bw.RunWorkerAsync();
    }

    /// <summary>
    /// Checks if a device is already connected by comparing serial numbers.
    /// </summary>
    /// <param name="newDevice">The device to check for duplicates</param>
    /// <returns>A result indicating if the device is a duplicate and which existing device it matches</returns>
    private DuplicateDeviceCheckResult CheckForDuplicateDevice(IStreamingDevice newDevice)
    {
        // If device doesn't have a serial number, we can't check for duplicates reliably
        if (string.IsNullOrWhiteSpace(newDevice.DeviceSerialNo))
        {
            AppLogger.Instance.Information($"Device {newDevice.Name} has no serial number - cannot check for duplicates");
            return new DuplicateDeviceCheckResult { IsDuplicate = false };
        }

        // Check if any existing device has the same serial number
        var existingDevice = ConnectedDevices.FirstOrDefault(d => 
            !string.IsNullOrWhiteSpace(d.DeviceSerialNo) && 
            d.DeviceSerialNo.Equals(newDevice.DeviceSerialNo, StringComparison.OrdinalIgnoreCase));

        if (existingDevice != null)
        {
            var newDeviceInterface = newDevice.ConnectionType == ConnectionType.Usb ? "USB" : "WiFi";
            var existingDeviceInterface = existingDevice.ConnectionType == ConnectionType.Usb ? "USB" : "WiFi";
            
            AppLogger.Instance.Information($"Duplicate device detected: Device already connected via {existingDeviceInterface}, attempted to add via {newDeviceInterface}");
            
            return new DuplicateDeviceCheckResult 
            { 
                IsDuplicate = true, 
                ExistingDevice = existingDevice,
                NewDevice = newDevice,
                NewDeviceInterface = newDeviceInterface,
                ExistingDeviceInterface = existingDeviceInterface
            };
        }

        return new DuplicateDeviceCheckResult { IsDuplicate = false };
    }
}

/// <summary>
/// Result of checking for duplicate devices
/// </summary>
public class DuplicateDeviceCheckResult
{
    public bool IsDuplicate { get; set; }
    public IStreamingDevice ExistingDevice { get; set; }
    public IStreamingDevice NewDevice { get; set; }
    public string NewDeviceInterface { get; set; }
    public string ExistingDeviceInterface { get; set; }
}

/// <summary>
/// Actions that can be taken when a duplicate device is detected
/// </summary>
public enum DuplicateDeviceAction
{
    KeepExisting,
    SwitchToNew,
    Cancel
}

public enum DAQifiConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Error,
    AlreadyConnected
}