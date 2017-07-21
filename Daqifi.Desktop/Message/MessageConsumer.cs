using System;
using System.IO;

namespace Daqifi.Desktop.Message
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
            while (Running)
            {
                try
                {
                    //Blocks until the DAQ sends a message
                    var daqMessage = new MessageEventArgs(new ProtobufMessage(WiFiDAQOutMessage.ParseDelimitedFrom(DataStream)));
                    NotifyMessageReceived(this, daqMessage);
                }
                catch (Exception ex)
                {
                    if(_isDisposed)
                    {
                        return;
                    }
                    AppLogger.Error(ex, "Failed in HandleCommunication");
                }
            }
        }

        public override void Stop()
        {
            _isDisposed = true;
            DataStream.Close();
            base.Stop();
        }
        #endregion
    }
}
