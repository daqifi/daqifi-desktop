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
            var isConnected = await Task.Run(() => device.Connect());
            if (!isConnected)
            {
                ConnectionStatus = DAQifiConnectionStatus.Error;
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
                        NotifyConnection = true;
                    });
                }
            }
        };

        bw.RunWorkerAsync();
    }
}

public enum DAQifiConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Error,
    AlreadyConnected
}