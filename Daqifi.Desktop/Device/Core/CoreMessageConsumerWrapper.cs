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
            AppLogger.Instance.Information($"[CORE_WRAPPER] OnCoreMessageReceived called - sender: {sender?.GetType()?.Name ?? "null"}");
            
            // Log the raw message type and content
            AppLogger.Instance.Information($"[CORE_WRAPPER] Received message from Core: {e.Message?.Data?.GetType()?.Name ?? "null"}");
            
            // Handle case where we already have a DaqifiOutMessage protobuf object (CoreDeviceAdapter already parsed it)
            if (e.Message?.Data is DaqifiOutMessage outMessage)
            {
                AppLogger.Instance.Information($"[CORE_WRAPPER] Received parsed DaqifiOutMessage directly from CoreDeviceAdapter");
                
                // Log detailed port information for diagnostics
                // These should be simple integer properties, not arrays, based on IsValidStatusMessage check
                AppLogger.Instance.Information($"[CORE_WRAPPER] DigitalPortNum: {outMessage.DigitalPortNum}");
                AppLogger.Instance.Information($"[CORE_WRAPPER] AnalogInPortNum: {outMessage.AnalogInPortNum}");
                AppLogger.Instance.Information($"[CORE_WRAPPER] AnalogOutPortNum: {outMessage.AnalogOutPortNum}");
                
                // The legacy IsValidStatusMessage checks these exact conditions:
                // (message.DigitalPortNum != 0 || message.AnalogInPortNum != 0 || message.AnalogOutPortNum != 0)
                bool isValid = (outMessage.DigitalPortNum != 0 || outMessage.AnalogInPortNum != 0 || outMessage.AnalogOutPortNum != 0);
                AppLogger.Instance.Information($"[CORE_WRAPPER] Would pass IsValidStatusMessage: {isValid}");
                
                // Also log other key fields that might indicate device status
                if (!string.IsNullOrEmpty(outMessage.DevicePn))
                {
                    AppLogger.Instance.Information($"[CORE_WRAPPER] Device Part Number: {outMessage.DevicePn}");
                }
                if (outMessage.DeviceSn != 0)
                {
                    AppLogger.Instance.Information($"[CORE_WRAPPER] Device Serial Number: {outMessage.DeviceSn}");
                }
                if (!string.IsNullOrEmpty(outMessage.DeviceFwRev))
                {
                    AppLogger.Instance.Information($"[CORE_WRAPPER] Device Firmware Version: {outMessage.DeviceFwRev}");
                }
                
                // Wrap in ProtobufMessage like the original MessageConsumer does
                var protobufMessage = new ProtobufMessage(outMessage);
                var desktopArgs = new MessageEventArgs<object>(protobufMessage);
                OnMessageReceived?.Invoke(sender, desktopArgs);
                return;
            }
            
            // Handle case where we have string data (SCPI responses)
            if (e.Message?.Data is string textMessage)
            {
                AppLogger.Instance.Information($"[CORE_WRAPPER] Received text message: {textMessage.Substring(0, Math.Min(100, textMessage.Length))}");
                // Text messages are typically SCPI responses, not protobuf data
                // Don't try to parse as protobuf - CoreDeviceAdapter already handles that
                // Just log for now - most device discovery relies on protobuf messages
                return;
            }
            
            AppLogger.Instance.Warning($"[CORE_WRAPPER] Received unknown message type: {e.Message?.Data?.GetType()?.FullName}");
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