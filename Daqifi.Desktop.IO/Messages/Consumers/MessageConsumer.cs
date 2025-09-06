using Daqifi.Core.Communication.Messages;
using Google.Protobuf;

namespace Daqifi.Desktop.IO.Messages.Consumers;

public class MessageConsumer : AbstractMessageConsumer
{
    #region Private Data
    private bool _isDisposed;
    private readonly CancellationTokenSource _cancellationTokenSource;
    #endregion

    #region Properties
    public bool IsWifiDevice { get; set; }
    #endregion

    #region Constructors
    public MessageConsumer(Stream stream)
    {
        DataStream = stream;
        _cancellationTokenSource = new CancellationTokenSource();
    }
    #endregion

    #region AbstractMessageConsumer overrides
    public override void Run()
    {
        while (Running && !_isDisposed)
        {
            try
            {
                if (DataStream == null || !DataStream.CanRead)
                {
                    break;
                }

                // Create a linked token that combines our cancellation with a timeout
                using var timeoutSource = new CancellationTokenSource(1000); // 1 second timeout
                using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, timeoutSource.Token);
                try
                {
                    var outMessage = DaqifiOutMessage.Parser.ParseDelimitedFrom(DataStream);
                    if (outMessage != null)
                    {
                        var protobufMessage = new ProtobufMessage(outMessage);
                        var daqMessage = new MessageEventArgs<object>(protobufMessage);
                        NotifyMessageReceived(this, daqMessage);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Check if we should exit
                    if (_isDisposed || !Running)
                    {
                        break;
                    }
                    // Otherwise, just continue to the next iteration
                    continue;
                }
                catch (InvalidProtocolBufferException)
                {
                    // Protocol buffer error - likely due to incomplete or corrupted data
                    // Just continue to next iteration
                    continue;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Failed in Message Consumer Run");
                if (_isDisposed || !Running)
                {
                    break;
                }
                Thread.Sleep(100); // Brief pause before retrying
            }
        }
    }

    public override void Stop()
    {
        try
        {
            _isDisposed = true;
            _cancellationTokenSource.Cancel();
            base.Stop();
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to stop MessageConsumer");
        }
    }

    public void ClearBuffer()
    {
        if (DataStream == null || !DataStream.CanRead)
        {
            return;
        }

        var buffer = new byte[1024];
        try
        {
            // Try to read any remaining data with a short timeout
            using var cts = new CancellationTokenSource(100); // 100ms timeout
            while (true)
            {
                if (cts.Token.IsCancellationRequested)
                {
                    break;
                }

                var bytesRead = DataStream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                {
                    break; // No more data available
                }
            }
        }
        catch (Exception)
        {
            // Ignore any errors during buffer clearing
        }
    }
    #endregion
}