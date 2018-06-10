using System;
using System.IO;
using System.Linq;
using Daqifi.Desktop.IO.Messages.MessageTypes;

namespace Daqifi.Desktop.IO.Messages.Consumers
{
    public class MessageConsumer : AbstractMessageConsumer
    {
        #region Private Data
        private bool _isDisposed;
        #endregion

        #region Properties

        public bool IsWifiDevice { get; set; }
    
        #endregion

        #region Constructors
        public MessageConsumer(Stream stream)
        {
            DataStream = stream;
        }
        #endregion

        #region AbstractMessageConsumer overrides
        public override void Run()
        {
            var buffer = new byte[1024];

            while (Running)
            {
                try
                {
                    // TODO something is screwed here!!  For some reason Wifi can't use the normal method
                    // This started with the new firmware, need to figure out why

                    if (IsWifiDevice)
                    {
                        int bytesRead;
                        while ((bytesRead = DataStream.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            var receivedBytes = buffer.Take(bytesRead).ToArray();
                            var stream = new MemoryStream(receivedBytes);
                            var outMessage = DaqifiOutMessage.ParseDelimitedFrom(stream);

                            var protobufMessage = new ProtobufMessage(outMessage);
                            var daqMessage = new MessageEventArgs(protobufMessage);
                            NotifyMessageReceived(this, daqMessage);
                        }
                    }
                    else
                    {
                        var outMessage = DaqifiOutMessage.ParseDelimitedFrom(DataStream);
                        var protobufMessage = new ProtobufMessage(outMessage);
                        var daqMessage = new MessageEventArgs(protobufMessage);
                        NotifyMessageReceived(this, daqMessage);
                    }
                }
                catch (Exception ex)
                {
                    if (_isDisposed)
                    {
                        return;
                    }
                    AppLogger.Error(ex, "Failed in AbstractMessageConsumer Run");
                }
            }
        }

        public override void Stop()
        {
            try
            {
                _isDisposed = true;
                DataStream.Close();
                base.Stop();
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Failed in AbstractMessageConsumer Stop");
            }   
        }
        #endregion
    }
}
