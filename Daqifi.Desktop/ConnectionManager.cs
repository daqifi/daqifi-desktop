using System;
using System.Collections.Generic;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Loggers;

namespace Daqifi.Desktop
{
    public class ConnectionManager : ObservableObject
    {
        #region Private Variables
        private DAQifiConnectionStatus _connectionStatus = DAQifiConnectionStatus.Disconnected;
        private string _connectionStatusString = "Disconnected";
        private List<IDevice> _connectedDevices;
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
            get { return _connectionStatusString; }
            set { _connectionStatusString = value; }
        }

        public List<IDevice> ConnectedDevices
        {
            get { return _connectedDevices; }
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
            ConnectedDevices = new List<IDevice>();
        }

        public static ConnectionManager Instance
        {
            get 
            {
                return instance;
            }
        }
        #endregion

        public void Connect(IDevice device)
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

        public void Disconnect(IDevice device)
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

        public void Reboot(IDevice device)
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
