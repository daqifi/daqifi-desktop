using Daqifi.Desktop.IO.Messages.Producers;
using Daqifi.Core.Communication.Messages;
using Daqifi.Core.Integration.Desktop;

namespace Daqifi.Desktop.Device.Core;

/// <summary>
/// Temporary wrapper that adapts CoreDeviceAdapter's MessageProducer to the existing desktop IMessageProducer interface.
/// This will be removed once AbstractStreamingDevice is updated to use Core interfaces directly.
/// </summary>
public class CoreMessageProducerWrapper : IMessageProducer
{
    private readonly CoreDeviceAdapter _coreAdapter;

    public CoreMessageProducerWrapper(CoreDeviceAdapter coreAdapter)
    {
        _coreAdapter = coreAdapter ?? throw new ArgumentNullException(nameof(coreAdapter));
    }

    public void Start()
    {
        // CoreDeviceAdapter handles starting automatically in Connect()
        // No action needed here for compatibility
    }

    public void Stop()
    {
        // CoreDeviceAdapter handles stopping in Disconnect()
        // No action needed here for compatibility
    }

    public void StopSafely()
    {
        // CoreDeviceAdapter handles safe stopping internally
        // No action needed here for compatibility
    }

    public void Send(IOutboundMessage<string> message)
    {
        if (_coreAdapter.MessageProducer != null)
        {
            _coreAdapter.MessageProducer.Send(message);
        }
    }
}