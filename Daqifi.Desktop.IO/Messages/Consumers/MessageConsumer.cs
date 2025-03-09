using Daqifi.Desktop.IO.Messages.MessageTypes;
using Google.Protobuf;
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
            while (Running && !_isDisposed)
            {
                try
                {
                    if (DataStream != null && DataStream.CanRead)
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
                    if (_isDisposed) break;
                }
                catch (IOException ex) when (ex.Message.Contains("aborted because of either a thread exit or an application request"))
                {
                    AppLogger.Error(ex, "I/O operation aborted: {0}");
                    if (_isDisposed) break;
                }
                catch (IOException ex)
                {
                    AppLogger.Error(ex, "IO error while reading from the transport: {0}");
                    if (_isDisposed) break;
                }
                catch (Exception ex)
                {
                    AppLogger.Error(ex, "Failed in Message Consumer Run: {0}");
                    if (_isDisposed) break;
                }
            }
        }

        public override void Stop()
        {
            try
            {
                _isDisposed = true;
                base.Stop();
                
                // Give the thread a chance to exit gracefully
                Thread.Sleep(100);
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
