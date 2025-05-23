﻿using Daqifi.Desktop.Common.Loggers;
using System.Collections.Concurrent;
using Daqifi.Core.Communication.Messages;

namespace Daqifi.Desktop.IO.Messages.Producers;

public class MessageProducer : IMessageProducer
{
    private Thread _producerThread;
    private ConcurrentQueue<IOutboundMessage<string>> _messageQueue;
    private bool _isRunning;
    private Stream DataStream { get; }

    #region Constructor
    public MessageProducer(Stream stream)
    {
        DataStream = stream;
    }
    #endregion

    public void Start()
    {
        _messageQueue = new ConcurrentQueue<IOutboundMessage<string>>();
        _isRunning = true;
        _producerThread = new Thread(Run) { IsBackground = true };
        _producerThread.Start();
    }

    public void Stop()
    {
        _isRunning = false;
        _messageQueue = new ConcurrentQueue<IOutboundMessage<string>>();
        _producerThread.Join(1000);
    }

    public void StopSafely()
    {
        const int timeoutMs = 1000;
        var startTime = DateTime.Now;
        while (!_messageQueue.IsEmpty)
        {
            if ((DateTime.Now - startTime).TotalMilliseconds > timeoutMs)
            {
                AppLogger.Instance.Warning($"MessageProducer.StopSafely timed out after {timeoutMs}ms with {_messageQueue.Count} messages remaining. Clearing queue.");
                Stop();
                break;
            }
            Thread.Sleep(10);
        }
        Stop();
    }

    public void Send(IOutboundMessage<string> message)
    {
        _messageQueue.Enqueue(message);
    }

    private void Run()
    {
        while (_isRunning)
        {
            try
            {
                Thread.Sleep(100);
                if (_messageQueue.IsEmpty)
                {
                    continue;
                }

                if (!_messageQueue.TryDequeue(out var message))
                {
                    continue;
                }

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