using Daqifi.Desktop.IO.Messages.MessageTypes;
using System;
using System.IO;
using System.Linq;
using System.Threading;

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
            while (Running)
            {
                try
                {
                    var outMessage = DaqifiOutMessage.ParseDelimitedFrom(DataStream);
                    var protobufMessage = new ProtobufMessage(outMessage);
                    var daqMessage = new MessageEventArgs(protobufMessage);
                    NotifyMessageReceived(this, daqMessage);
                }
                catch (Exception ex)
                {
                    if (_isDisposed)
                    {
                        return;
                    }
                    AppLogger.Error(ex, "Failed in Message Consumer Run");
                }
            }
        }

        public override void Stop()
        {
            try
            {
                _isDisposed = true;
                base.Stop();
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Failed in AbstractMessageConsumer Stop");
            }   
        }

        public void ClearBuffer()
        {
            var buffer = new byte[1024];
            var bytesRead = DataStream.Read(buffer, 0, buffer.Length) != 0;
        }

        #endregion
    }
}
