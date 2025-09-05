using Daqifi.Desktop.Common.Loggers;

namespace Daqifi.Desktop.IO.Messages.Consumers;

public abstract class AbstractMessageConsumer : IMessageConsumer
{
    #region Private Data

    private Thread _consumerThread;
    private volatile bool _running;
    #endregion

    #region Properties

    protected AppLogger AppLogger = AppLogger.Instance;

    public bool Running
    {
        get => _running;
        set => _running = value;
    }

    public Stream DataStream { get; set; }

    #endregion

    #region Events
    public event OnMessageReceivedHandler OnMessageReceived;
    #endregion

    #region IMessageConsumer overrides

    public void Start()
    {
        _running = true;
        _consumerThread = new Thread(Run) {IsBackground = true};
        _consumerThread.Start();
    }

    /// <summary>
    /// Kills the consumer thread marked virtual so the user can perform any other closing actions that might be needed
    /// </summary>
    public virtual void Stop()
    {
        _running = false;

        // Give the thread up to 2 seconds to stop gracefully
        if (_consumerThread != null && _consumerThread.IsAlive)
        {
            // Wait for thread to exit gracefully
            if (!_consumerThread.Join(2000))
            {
                // Log warning if thread didn't stop in time
                AppLogger.Warning("Consumer thread did not stop within timeout period");
            }
        }
    }

    public void NotifyMessageReceived(object sender, MessageEventArgs<object> e)
    {
        OnMessageReceived?.Invoke(sender, e);
    }

    public abstract void Run();

    #endregion
}