using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.IO.Messages.MessageTypes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Daqifi.Desktop.IO.Messages.Producers
{
    public class MessageProducer : IMessageProducer
    {
        private Thread _producerThread;
        private ConcurrentQueue<IMessage> _messageQueue;
        private bool _isRunning;

        public Stream DataStream { get; protected set; }

        #region Constructor
        public MessageProducer(Stream stream)
        {
            DataStream = stream;
        }
        #endregion

        public void Start()
        {
            _messageQueue = new ConcurrentQueue<IMessage>();
            _isRunning = true;
            _producerThread = new Thread(Run) { IsBackground = true };
            _producerThread.Start();
        }

        public void Stop()
        {
            _isRunning = false;
            _producerThread.Join(1000);
        }

        public void StopSafely()
        {
            while (!_messageQueue.IsEmpty)
            {
                // Wait for the queue to empty
            }
            Stop();
        }

        public void Send(IMessage message)
        {
            _messageQueue.Enqueue(message);
        }

        public void Run()
        {
            while (_isRunning)
            {
                try
                {
                    Thread.Sleep(100);
                    if (_messageQueue.IsEmpty) continue;

                    if(!_messageQueue.TryDequeue(out var message)) continue;

                    var serializedMessage = message.GetBytes();
                    DataStream.Write(serializedMessage, 0, serializedMessage.Length);
                }
                catch (Exception ex)
                {
                    AppLogger.Instance.Error(ex, "Failed running in Message Producer");
                }
            }
        }
    }
}
