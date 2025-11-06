using Daqifi.Core.Communication.Messages;
using Daqifi.Desktop.IO.Messages;

namespace Daqifi.Desktop.IO.Messages.Consumers;

/// <summary>
/// Consumes binary message data from a stream (e.g., SD card file downloads)
/// </summary>
public class BinaryMessageConsumer : AbstractMessageConsumer
{
    private bool _isDisposed;
    private readonly MemoryStream _binaryData;
    private const int BUFFER_SIZE = 8192;
    private const int MAX_FILE_SIZE = 100 * 1024 * 1024; // 100 MB max

    public BinaryMessageConsumer(Stream stream)
    {
        DataStream = stream;
        _binaryData = new MemoryStream();
    }

    public override void Run()
    {
        var buffer = new byte[BUFFER_SIZE];
        var totalBytesRead = 0;
        var noDataCounter = 0;
        const int maxNoDataAttempts = 50; // 0.5 seconds of no data before giving up

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
                    // Reset no data counter
                    noDataCounter = 0;

                    // Check if we're about to exceed max file size
                    if (totalBytesRead + bytesRead > MAX_FILE_SIZE)
                    {
                        AppLogger.Error($"File size exceeds maximum allowed size of {MAX_FILE_SIZE} bytes");
                        break;
                    }

                    // Write to our memory stream
                    lock (_binaryData)
                    {
                        _binaryData.Write(buffer, 0, bytesRead);
                    }

                    totalBytesRead += bytesRead;
                }
                else
                {
                    // No data available
                    noDataCounter++;

                    if (noDataCounter >= maxNoDataAttempts)
                    {
                        // No data for a while, assume transfer is complete
                        AppLogger.Information($"No more data available, transfer complete. Total bytes: {totalBytesRead}");
                        break;
                    }

                    // Check if we should stop
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
                break;
            }
        }

        // Send the complete binary data as a message
        ProcessBinaryData();
    }

    private void ProcessBinaryData()
    {
        try
        {
            byte[] data;
            lock (_binaryData)
            {
                data = _binaryData.ToArray();
            }

            if (data.Length == 0)
            {
                AppLogger.Warning("No binary data received");
                return;
            }

            AppLogger.Information($"Received {data.Length} bytes of binary data");

            // Create a BinaryMessage and send it
            var binaryMessage = new BinaryMessage(data);
            var messageArgs = new MessageEventArgs<object>(binaryMessage);
            NotifyMessageReceived(this, messageArgs);
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Error processing binary data");
        }
    }

    public override void Stop()
    {
        try
        {
            _isDisposed = true;

            // Process any remaining data before stopping
            ProcessBinaryData();

            // Clean up
            _binaryData?.Dispose();

            base.Stop();
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to stop BinaryMessageConsumer");
        }
    }
}
