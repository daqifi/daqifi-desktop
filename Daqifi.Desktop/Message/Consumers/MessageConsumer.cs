using Daqifi.Desktop.Message.MessageTypes;
using System;
using System.IO;
using System.Linq;

namespace Daqifi.Desktop.Message.Consumers
{
    public class MessageConsumer : AbstractMessageConsumer
    {
        #region Private Data
        private bool _isDisposed;
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
            var buffer = new byte[512];

            while (Running)
            {
                try
                {
                    // TODO this should and used to work but no longer does. Investigate why
                    //var outMessage = DaqifiOutMessage.ParseDelimitedFrom(DataStream);
                    //var protobufMessage = new ProtobufMessage(outMessage);
                    //var daqMessage = new MessageEventArgs(protobufMessage);
                    //NotifyMessageReceived(this, daqMessage);

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
