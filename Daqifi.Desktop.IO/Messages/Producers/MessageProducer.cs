using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.IO.Messages.MessageTypes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Daqifi.Desktop.IO.Messages.Producers
{
    public class MessageProducer : IMessageProducer
    {
        private Thread _producerThread;
        private readonly Queue<IMessage> _messageQueue = new Queue<IMessage>();
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
            _isRunning = true;
            _producerThread = new Thread(Run) { IsBackground = true };
            _producerThread.Start();
        }

        public void Stop()
        {
            _isRunning = false;
            _producerThread.Join(1000);
            _messageQueue.Clear();
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
                    if (!_messageQueue.Any()) continue;

                    var message = _messageQueue.Dequeue();
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
