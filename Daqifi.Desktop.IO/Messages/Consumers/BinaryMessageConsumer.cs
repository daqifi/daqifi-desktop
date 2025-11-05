using Daqifi.Core.Communication.Messages;
using Timer = System.Threading.Timer;

namespace Daqifi.Desktop.IO.Messages.Consumers;

/// <summary>
/// Message consumer for reading binary data (e.g., file downloads)
/// </summary>
public class BinaryMessageConsumer : AbstractMessageConsumer
{
    #region Private Fields
    private bool _isDisposed;
    private readonly MemoryStream _memoryStream;
    private readonly Timer _processTimer;
    private const int PROCESS_DELAY_MS = 500; // Wait for more data before processing
    private const int MAX_FILE_SIZE = 100 * 1024 * 1024; // 100 MB limit
    #endregion

    #region Constructor
    public BinaryMessageConsumer(Stream stream)
    {
        DataStream = stream;
        _memoryStream = new MemoryStream();
        _processTimer = new Timer(ProcessAccumulatedData, null, Timeout.Infinite, Timeout.Infinite);
    }
    #endregion

    #region Public Methods
    public override void Run()
    {
        var buffer = new byte[8192]; // Larger buffer for binary data

        while (Running && !_isDisposed)
        {
            try
            {
                // Check if we should stop
                if (_isDisposed || !Running || DataStream == null || !DataStream.CanRead)
                {
                    break;
                }

                int bytesRead;
                try
                {
                    bytesRead = DataStream.Read(buffer, 0, buffer.Length);
                }
                catch
                {
                    // Any read error should cause us to exit
                    break;
                }

                if (bytesRead > 0)
                {
                    lock (_memoryStream)
                    {
                        // Check if we're exceeding max file size
                        if (_memoryStream.Length + bytesRead > MAX_FILE_SIZE)
                        {
                            AppLogger.Error($"Binary data exceeds maximum size of {MAX_FILE_SIZE} bytes");
                            break;
                        }

                        _memoryStream.Write(buffer, 0, bytesRead);
                    }

                    // Reset the timer each time we get data
                    _processTimer.Change(PROCESS_DELAY_MS, Timeout.Infinite);
                }
                else
                {
                    // No data available, check if we should stop
                    if (_isDisposed || !Running)
                    {
                        break;
                    }
                    Thread.Sleep(10);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Error reading binary data");
                break; // Any error should cause us to exit
            }
        }

        // Ensure we process any remaining data before exiting
        ProcessAccumulatedData(null);
    }

    public override void Stop()
    {
        try
        {
            _isDisposed = true;
            if (_processTimer != null)
            {
                _processTimer.Change(Timeout.Infinite, Timeout.Infinite);
                _processTimer.Dispose();
            }

            // Process any remaining data before stopping
            ProcessAccumulatedData(null);

            base.Stop();

            // Clean up memory stream
            lock (_memoryStream)
            {
                _memoryStream.Dispose();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to stop BinaryMessageConsumer");
        }
    }
    #endregion

    #region Private Methods
    private void ProcessAccumulatedData(object? state)
    {
        try
        {
            byte[] accumulated;
            lock (_memoryStream)
            {
                if (_memoryStream.Length == 0)
                {
                    return;
                }

                accumulated = _memoryStream.ToArray();
                _memoryStream.SetLength(0); // Clear the stream
            }

            // Log the size of received data
            AppLogger.Information($"Received binary data: {accumulated.Length} bytes");

            // Send the accumulated binary data as one message
            var binaryMessage = new BinaryMessage(accumulated);
            var messageArgs = new MessageEventArgs<object>(binaryMessage);
            NotifyMessageReceived(this, messageArgs);
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Error processing accumulated binary data");
        }
    }
    #endregion
}

/// <summary>
/// Represents a binary message containing raw byte data
/// </summary>
public class BinaryMessage : IInboundMessage<byte[]>
{
    public byte[] Data { get; }

    public BinaryMessage(byte[] data)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
    }
}
