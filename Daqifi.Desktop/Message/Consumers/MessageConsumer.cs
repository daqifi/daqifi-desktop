using Daqifi.Desktop.Message.MessageTypes;
using System;
using System.Diagnostics;
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
            var buffer = new byte[256];

            while (Running)
            {
                try
                {
                    //var outMessage = DaqifiOutMessage.ParseDelimitedFrom(DataStream);
                    //var protobufMessage = new ProtobufMessage(outMessage);
                    //var daqMessage = new MessageEventArgs(protobufMessage);
                    //NotifyMessageReceived(this, daqMessage);

                    int bytesRead;
                    while ((bytesRead = DataStream.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        Debug.WriteLine("");
                        foreach (var dataByte in buffer)
                        {
                            Debug.Write($"0x{dataByte:X2}, ");
                        }

                        var receivedBytes = buffer.Take(bytesRead).ToArray();

                        var outMessage = DaqifiOutMessage.ParseFrom(receivedBytes);
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
