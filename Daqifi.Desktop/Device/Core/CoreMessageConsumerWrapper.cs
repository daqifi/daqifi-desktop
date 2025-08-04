using Daqifi.Desktop.IO.Messages.Consumers;
using Daqifi.Core.Integration.Desktop;
using Daqifi.Core.Communication.Consumers;
using Daqifi.Desktop.IO.Messages;
using Daqifi.Desktop.Common.Loggers;
using System.IO;

namespace Daqifi.Desktop.Device.Core;

/// <summary>
/// Temporary wrapper that adapts CoreDeviceAdapter's MessageConsumer to the existing desktop IMessageConsumer interface.
/// This will be removed once AbstractStreamingDevice is updated to use Core interfaces directly.
/// </summary>
public class CoreMessageConsumerWrapper : IMessageConsumer
{
    private readonly CoreDeviceAdapter _coreAdapter;

    public CoreMessageConsumerWrapper(CoreDeviceAdapter coreAdapter)
    {
        _coreAdapter = coreAdapter ?? throw new ArgumentNullException(nameof(coreAdapter));
        
        // Note: MessageConsumer will be null until CoreDeviceAdapter.Connect() is called
        // We'll subscribe to events in Start() method instead
    }

    public bool Running { get; set; }
    
    public Stream DataStream { get; set; } = Stream.Null; // Not used with CoreDeviceAdapter
    
    public event OnMessageReceivedHandler OnMessageReceived = delegate { };

    public void Start()
    {
        Running = true;
        
        // Subscribe to events now that CoreDeviceAdapter.Connect() has been called
        if (_coreAdapter.MessageConsumer != null)
        {
            _coreAdapter.MessageConsumer.MessageReceived += OnCoreMessageReceived;
            AppLogger.Instance.Information($"[CORE_WRAPPER] Subscribed to MessageReceived events");
        }
        else
        {
            AppLogger.Instance.Warning($"[CORE_WRAPPER] MessageConsumer is null, cannot subscribe to events");
        }
    }

    public void Stop()
    {
        Running = false;
        
        // Unsubscribe from events
        if (_coreAdapter.MessageConsumer != null)
        {
            _coreAdapter.MessageConsumer.MessageReceived -= OnCoreMessageReceived;
        }
    }

    public void NotifyMessageReceived(object sender, MessageEventArgs<object> e)
    {
        OnMessageReceived?.Invoke(sender, e);
    }

    public void Run()
    {
        // CoreDeviceAdapter handles running internally
        // No action needed here for compatibility
    }

    private void OnCoreMessageReceived(object? sender, MessageReceivedEventArgs<object> e)
    {
        // Convert Core event to desktop event format
        AppLogger.Instance.Information($"[CORE_WRAPPER] Received message from Core: {e.Message?.Data?.GetType()?.Name ?? "null"}");
        var desktopArgs = new MessageEventArgs<object>(e.Message);
        OnMessageReceived?.Invoke(sender, desktopArgs);
    }

    public void Dispose()
    {
        if (_coreAdapter.MessageConsumer != null)
        {
            _coreAdapter.MessageConsumer.MessageReceived -= OnCoreMessageReceived;
        }
    }
}