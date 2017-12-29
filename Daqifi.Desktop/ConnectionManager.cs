using Daqifi.Desktop.Device;
using Daqifi.Desktop.Loggers;
using System;
using System.Collections.Generic;

namespace Daqifi.Desktop
{
    public class ConnectionManager : ObservableObject
    {
        #region Private Variables
        private DAQifiConnectionStatus _connectionStatus = DAQifiConnectionStatus.Disconnected;
        private string _connectionStatusString = "Disconnected";
        private List<IStreamingDevice> _connectedDevices;
        private bool _isDisconnected = true;
        #endregion

        #region Properties
        public DAQifiConnectionStatus ConnectionStatus
        {
            get { return _connectionStatus; }
            set
            {
                _connectionStatus = value;
                UpdateStatusString();
                NotifyPropertyChanged("ConnectionStatus");

                IsDisconnected = _connectionStatus != DAQifiConnectionStatus.Connected;
            }
        }

        public string ConnectionStatusString
        {
            get => _connectionStatusString;
            set { _connectionStatusString = value; }
        }

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
            get { return _isDisconnected; }
            set 
            { 
                if(value != _isDisconnected)
                {
                    _isDisconnected = value;
                    NotifyPropertyChanged("IsDisconnected");
                }
            }
        }
        #endregion

        #region Singleton Constructor / Initalization
        private static readonly ConnectionManager instance = new ConnectionManager();

        private ConnectionManager()
        {
            ConnectedDevices = new List<IStreamingDevice>();
        }

        public static ConnectionManager Instance => instance;

        #endregion

        public void Connect(IStreamingDevice device)
        {
            try
            {
                ConnectionStatus = DAQifiConnectionStatus.Connecting;
                if (!device.Connect()) return;
                ConnectedDevices.Add(device);
                NotifyPropertyChanged("ConnectedDevices");
                ConnectionStatus = DAQifiConnectionStatus.Connected;
                //TODO do we do something if the attempt to connect fails?
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
            switch(ConnectionStatus)
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
            }
        }
    }

    public enum DAQifiConnectionStatus
    {
        Disconnected,
        Connecting,
        Connected,
        Error
    }
}
