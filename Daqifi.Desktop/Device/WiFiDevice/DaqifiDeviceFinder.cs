using Daqifi.Desktop.Message;
using Daqifi.Desktop.Message.Consumers;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Daqifi.Desktop.Device.WiFiDevice
{
    public class DaqifiDeviceFinder : AbstractMessageConsumer, IDeviceFinder
    {
        #region Private Data
        private readonly byte[] _queryCommand = Encoding.ASCII.GetBytes("Discovery: Who is out there?\r\n");
        #endregion

        #region Properties
        public UdpClient Client { get; }
        public IPEndPoint Destination { get; }
        #endregion

        #region Events
        public event OnDeviceFoundHandler OnDeviceFound;
        public event OnDeviceRemovedHandler OnDeviceRemoved;
        #endregion

        #region Constructor
        public DaqifiDeviceFinder(int broadcastPort)
        {
            try
            {
                Destination = new IPEndPoint(IPAddress.Broadcast, broadcastPort);
                Client = new UdpClient(broadcastPort);
            }
            catch(Exception ex)
            {
                AppLogger.Error(ex, "Error creating streamingDevice listener");
            }
        }
        #endregion

        #region AbstractMessageConsumer overrides
        public override void Run()
        {
            Client.EnableBroadcast = true;
            Client.BeginReceive(OnFinderMessageReceived, null);

            while (Running)
            {
                Client.Send(_queryCommand, _queryCommand.Length, Destination);
                Thread.Sleep(1000);
            }
        }
        #endregion

        public override void Stop()
        {
            try
            {
                if (Client != null)
                {
                    Running = false;
                    Client.Close();
                }
                base.Stop();
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Error Stopping Device Finder");
            }
        }

        private void OnFinderMessageReceived(IAsyncResult res)
        {
            try
            {
                var remoteIpEndPoint = new IPEndPoint(IPAddress.Any, 8000);
                var receivedBytes = Client.EndReceive(res, ref remoteIpEndPoint);

                var receivedText = Encoding.ASCII.GetString(receivedBytes);

                if (!receivedText.Contains("Discovery: Who is out there?") &&
                    !receivedText.Contains("Power event occurred"))
                {
                    try
                    {
                        var message = DaqifiOutMessage.ParseFrom(receivedBytes);
                        if (message.HasHostName)
                        {
                            var device = new DeviceMessage(message).Device;
                            NotifyDeviceFound(this, device);
                        }
                    }
                    catch
                    {
                        var device = new DaqifiStreamingDevice("Test Device", "Test Mac", remoteIpEndPoint.Address.ToString());
                        NotifyDeviceFound(this, device);
                    }
                }
                Client.BeginReceive(OnFinderMessageReceived, null);
            }
            catch (ObjectDisposedException)
            {

            }
            catch (Exception ex)
            {
                var temp = "Test";
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
    }
}
