using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Device.SerialDevice;
using Daqifi.Desktop.Logger;
using System.ComponentModel;
using System.Management;

namespace Daqifi.Desktop;

public class ConnectionManager : ObservableObject
{
    #region Private Variables
    private DAQifiConnectionStatus _connectionStatus = DAQifiConnectionStatus.Disconnected;
    private List<IStreamingDevice> _connectedDevices;
    private bool _isDisconnected = true;
    private bool _notifyConnection = false;
    private readonly ManagementEventWatcher _deviceRemovedWatcher;
    #endregion

    #region Properties

    private DAQifiConnectionStatus ConnectionStatus
    {
        get => _connectionStatus;
        set
        {
            _connectionStatus = value;
            UpdateStatusString();
            NotifyPropertyChanged("ConnectionStatus");

            IsDisconnected = _connectionStatus != DAQifiConnectionStatus.Connected;
        }
    }

    public string ConnectionStatusString { get; set; } = "Disconnected";

    public List<IStreamingDevice> ConnectedDevices
    {
        get => _connectedDevices;
        set
        {
            _connectedDevices = value;
            NotifyPropertyChanged("ConnectedDevices");
        }
    }

    public bool IsDisconnected
    {
        get => _isDisconnected;
        set
        {
            if (value != _isDisconnected)
            {
                _isDisconnected = value;
                NotifyPropertyChanged("IsDisconnected");
            }
        }
    }
    public bool NotifyConnection
    {
        get => _notifyConnection;
        set
        {
            if (value != _notifyConnection)
            {
                _notifyConnection = value;
                NotifyPropertyChanged("NotifyConnection");
            }
        }
    }
    #endregion

    #region Singleton Constructor / Initalization
    private static readonly ConnectionManager instance = new ConnectionManager();

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
            bool isConnected = await Task.Run(() => device.Connect());
            if (!isConnected)
            {
                ConnectionStatus = DAQifiConnectionStatus.Error;
                return;
            }
            ConnectedDevices.Add(device);
            await Task.Delay(1000);
            NotifyPropertyChanged("ConnectedDevices");
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
            NotifyPropertyChanged("ConnectedDevices");
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
            NotifyPropertyChanged("ConnectedDevices");
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error(ex, "Failed in Reboot");
        }
    }

    public void UpdateStatusString()
    {
        switch (ConnectionStatus)
        {
            case DAQifiConnectionStatus.Disconnected:
                ConnectionStatusString = "Disconnected";
                break;
            case DAQifiConnectionStatus.Connecting:
                ConnectionStatusString = "Connecting";
                break;
            case DAQifiConnectionStatus.Connected:
                ConnectionStatusString = "Connected";
                break;
            case DAQifiConnectionStatus.Error:
                ConnectionStatusString = "Error";
                break;
            case DAQifiConnectionStatus.AlreadyConnected:
                ConnectionStatusString = "AlreadyConnected";
                break;
            default:
                ConnectionStatusString = "Error";
                break;
        }
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