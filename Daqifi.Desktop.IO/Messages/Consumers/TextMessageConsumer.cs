using System.Text;
using Daqifi.Core.Communication.Messages;
using Daqifi.Desktop.Common.Loggers;
using Timer = System.Threading.Timer;

namespace Daqifi.Desktop.IO.Messages.Consumers;

public delegate void OnMessageReceivedHandler(object sender, MessageEventArgs<object> e);

public class TextMessageConsumer
{
    #region Private Data
    private readonly AppLogger _appLogger = AppLogger.Instance;
    private Thread? _consumerThread;
    private volatile bool _running;
    private bool _isDisposed;
    private readonly StringBuilder _stringBuilder;
    private readonly Timer _processTimer;
    private const int PROCESS_DELAY_MS = 500; // Wait for more data before processing
    #endregion

    #region Properties
    public bool Running
    {
        get => _running;
        set => _running = value;
    }

    public Stream DataStream { get; set; }
    #endregion

    #region Events
    public event OnMessageReceivedHandler? OnMessageReceived;
    #endregion

    public TextMessageConsumer(Stream stream)
    {
        DataStream = stream;
        _stringBuilder = new StringBuilder();
        _processTimer = new Timer(ProcessAccumulatedData, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        _running = true;
        _consumerThread = new Thread(Run) { IsBackground = true };
        _consumerThread.Start();
    }

    public void Run()
    {
        var buffer = new byte[4096];

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
                    var text = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                    lock (_stringBuilder)
                    {
                        _stringBuilder.Append(text);
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
                _appLogger.Error(ex, "Error reading text response");
                break; // Any error should cause us to exit
            }
        }

        // Ensure we process any remaining data before exiting
        ProcessAccumulatedData(null);
    }

    private void ProcessAccumulatedData(object? state)
    {
        try
        {
            string accumulated;
            lock (_stringBuilder)
            {
                accumulated = _stringBuilder.ToString().Trim();
                _stringBuilder.Clear();
            }

            if (string.IsNullOrEmpty(accumulated))
            {
                return;
            }

            // Log the raw received data for debugging
            _appLogger.Information($"Raw text response: {accumulated}");

            // Send the entire accumulated text as one message
            var textMessage = new TextMessage(accumulated);
            var messageArgs = new MessageEventArgs<object>(textMessage);
            NotifyMessageReceived(this, messageArgs);
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Error processing accumulated text data");
        }
    }

    public void Stop()
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

            _running = false;
            if (_consumerThread is { IsAlive: true })
            {
                if (!_consumerThread.Join(2000))
                {
                    _appLogger.Warning("Consumer thread did not stop within timeout period");
                }
            }
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Failed to stop TextMessageConsumer");
        }
    }

    public void NotifyMessageReceived(object sender, MessageEventArgs<object> e)
    {
        OnMessageReceived?.Invoke(sender, e);
    }
}
