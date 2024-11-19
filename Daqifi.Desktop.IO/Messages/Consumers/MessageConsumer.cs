using Daqifi.Desktop.IO.Messages.MessageTypes;
using Google.Protobuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
                    if (DataStream != null)
                    {
                        var outMessage = DaqifiOutMessage.Parser.ParseDelimitedFrom(DataStream);
                        var protobufMessage = new ProtobufMessage(outMessage);
                        var daqMessage = new MessageEventArgs(protobufMessage);
                        NotifyMessageReceived(this, daqMessage);
                    }
                }
                catch (InvalidProtocolBufferException ex)
                {

                    AppLogger.Error(ex, "Protocol buffer parsing error: {0}");
                    if (_isDisposed)
                    {
                        return;
                    }
                }
                catch (IOException ex) when (ex.Message.Contains("aborted because of either a thread exit or an application request"))
                {

                    AppLogger.Error(ex, "I/O operation aborted: {0}");
                    if (_isDisposed)
                    {
                        return;
                    }
                }
                catch (IOException ex)
                {

                    AppLogger.Error(ex, "IO error while reading from the transport: {0}");
                    if (_isDisposed)
                    {
                        return;
                    }
                }
                catch (Exception ex)
                {

                    AppLogger.Error(ex, "Failed in Message Consumer Run: {0}");
                    if (_isDisposed)
                    {
                        return;
                    }
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
