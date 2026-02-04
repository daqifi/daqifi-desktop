using Daqifi.Core.Communication.Consumers;
using Daqifi.Core.Communication.Messages;
using Daqifi.Desktop.Common.Loggers;

namespace Daqifi.Desktop.IO.Messages.Consumers;

public sealed class MessageConsumer : IMessageConsumer, IDisposable
{
    #region Private Data
    private readonly AppLogger _appLogger = AppLogger.Instance;
    private readonly ProtobufMessageParser _parser;
    private StreamMessageConsumer<DaqifiOutMessage> _consumer;
    private Stream _dataStream;
    private bool _disposed;
    #endregion

    #region Constructors
    public MessageConsumer(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _dataStream = stream;
        _parser = new ProtobufMessageParser();
        _consumer = BuildConsumer(_dataStream);
    }
    #endregion

    #region Properties
    public bool Running
    {
        get => _consumer.IsRunning;
        set
        {
            if (value)
            {
                Start();
            }
            else
            {
                Stop();
            }
        }
    }

    public Stream DataStream
    {
        get => _dataStream;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (ReferenceEquals(_dataStream, value))
            {
                return;
            }

            if (_consumer.IsRunning)
            {
                throw new InvalidOperationException("Cannot change DataStream while the consumer is running.");
            }

            ReplaceConsumer(value);
        }
    }
    #endregion

    #region Events
    public event OnMessageReceivedHandler? OnMessageReceived;
    #endregion

    #region IMessageConsumer implementation
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _consumer.Start();
    }

    public void Stop()
    {
        try
        {
            _consumer.StopSafely();
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Failed to stop MessageConsumer");
        }
    }

    public void NotifyMessageReceived(object sender, MessageEventArgs<object> e)
    {
        OnMessageReceived?.Invoke(sender, e);
    }

    public void Run()
    {
        Start();
    }
    #endregion

    #region Public Methods
    public void ClearBuffer()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _consumer.ClearBuffer();
    }
    #endregion

    #region Private Methods
    private StreamMessageConsumer<DaqifiOutMessage> BuildConsumer(Stream stream)
    {
        var consumer = new StreamMessageConsumer<DaqifiOutMessage>(stream, _parser);
        consumer.MessageReceived += OnCoreMessageReceived;
        consumer.ErrorOccurred += OnCoreError;
        return consumer;
    }

    private void ReplaceConsumer(Stream stream)
    {
        _consumer.MessageReceived -= OnCoreMessageReceived;
        _consumer.ErrorOccurred -= OnCoreError;
        _consumer.Dispose();

        _dataStream = stream;
        _consumer = BuildConsumer(stream);
    }

    private void OnCoreMessageReceived(object? sender, MessageReceivedEventArgs<DaqifiOutMessage> e)
    {
        var messageArgs = new MessageEventArgs<object>(e.Message);
        NotifyMessageReceived(this, messageArgs);
    }

    private void OnCoreError(object? sender, MessageConsumerErrorEventArgs e)
    {
        _appLogger.Error(e.Error, "Error occurred in core MessageConsumer");
    }
    #endregion

    #region IDisposable
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _consumer.MessageReceived -= OnCoreMessageReceived;
        _consumer.ErrorOccurred -= OnCoreError;
        _consumer.Dispose();
        _disposed = true;
    }
    #endregion
}
