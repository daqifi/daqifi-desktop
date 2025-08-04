using Daqifi.Desktop.IO.Messages.Consumers;
using Daqifi.Core.Integration.Desktop;
using Daqifi.Core.Communication.Consumers;
using Daqifi.Desktop.IO.Messages;
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
        
        // Bridge Core events to desktop events
        if (_coreAdapter.MessageConsumer != null)
        {
            _coreAdapter.MessageConsumer.MessageReceived += OnCoreMessageReceived;
        }
    }

    public bool Running { get; set; }
    
    public Stream DataStream { get; set; } = Stream.Null; // Not used with CoreDeviceAdapter
    
    public event OnMessageReceivedHandler OnMessageReceived = delegate { };

    public void Start()
    {
        Running = true;
        // CoreDeviceAdapter handles starting automatically in Connect()
    }

    public void Stop()
    {
        Running = false;
        // CoreDeviceAdapter handles stopping in Disconnect()
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