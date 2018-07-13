using Daqifi.Desktop.Common.Loggers;
using System.IO;
using System.Threading;

namespace Daqifi.Desktop.IO.Messages.Consumers
{
    public abstract class AbstractMessageConsumer : IMessageConsumer
    {
        #region Private Data

        private Thread _consumerThread;
        private volatile bool _running;
        #endregion

        #region Properties
        public AppLogger AppLogger = AppLogger.Instance;

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
            _consumerThread.Join(1000);
        }

        public void NotifyMessageReceived(object sender, MessageEventArgs e)
        {
            OnMessageReceived?.Invoke(sender, e);
        }

        public abstract void Run();
        #endregion
    }
}
