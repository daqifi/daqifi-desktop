using Daqifi.Desktop.IO.Messages.Consumers;
using Daqifi.Core.Integration.Desktop;
using Daqifi.Core.Communication.Consumers;
using Daqifi.Desktop.IO.Messages;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Core.Communication.Messages;
using Google.Protobuf;
using System.IO;
using System.Text;

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
        try
        {
            // Log the raw message type and content
            AppLogger.Instance.Information($"[CORE_WRAPPER] Received message from Core: {e.Message?.Data?.GetType()?.Name ?? "null"}");
            
            // Handle case where we already have a DaqifiOutMessage protobuf object
            if (e.Message?.Data is DaqifiOutMessage outMessage)
            {
                AppLogger.Instance.Information($"[CORE_WRAPPER] Received parsed DaqifiOutMessage directly");
                // Wrap in ProtobufMessage like the original MessageConsumer does
                var protobufMessage = new ProtobufMessage(outMessage);
                var desktopArgs = new MessageEventArgs<object>(protobufMessage);
                OnMessageReceived?.Invoke(sender, desktopArgs);
                return;
            }
            
            // Handle case where we have raw string data (could be binary protobuf)
            if (e.Message?.Data is string rawMessage)
            {
                AppLogger.Instance.Information($"[CORE_WRAPPER] Raw message content (first 100 chars): {rawMessage.Substring(0, Math.Min(100, rawMessage.Length))}");
                
                // Try to parse the string as a protobuf message like the original MessageConsumer
                try
                {
                    // Convert string to bytes and parse as DaqifiOutMessage
                    var messageBytes = Encoding.UTF8.GetBytes(rawMessage);
                    using var stream = new MemoryStream(messageBytes);
                    var parsedMessage = DaqifiOutMessage.Parser.ParseDelimitedFrom(stream);
                    
                    if (parsedMessage != null)
                    {
                        // Wrap in ProtobufMessage like the original MessageConsumer does
                        var protobufMessage = new ProtobufMessage(parsedMessage);
                        var desktopArgs = new MessageEventArgs<object>(protobufMessage);
                        OnMessageReceived?.Invoke(sender, desktopArgs);
                        AppLogger.Instance.Information($"[CORE_WRAPPER] Successfully parsed protobuf message from string");
                        return;
                    }
                }
                catch (Exception parseEx)
                {
                    AppLogger.Instance.Warning($"[CORE_WRAPPER] Failed to parse as protobuf: {parseEx.Message}");
                    
                    // The raw message might contain device info as plain text, let's check for it
                    if (rawMessage.Contains("NYQUIST") || rawMessage.Contains("DAQiFi"))
                    {
                        AppLogger.Instance.Information($"[CORE_WRAPPER] Message appears to contain device info");
                        // This might be status message data that needs to be handled differently
                    }
                }
            }
            
            // Fall back to passing the original message
            var fallbackArgs = new MessageEventArgs<object>(e.Message);
            OnMessageReceived?.Invoke(sender, fallbackArgs);
            AppLogger.Instance.Information($"[CORE_WRAPPER] Passed message as fallback");
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error(ex, $"[CORE_WRAPPER] Error processing message from Core");
        }
    }

    public void Dispose()
    {
        if (_coreAdapter.MessageConsumer != null)
        {
            _coreAdapter.MessageConsumer.MessageReceived -= OnCoreMessageReceived;
        }
    }
}