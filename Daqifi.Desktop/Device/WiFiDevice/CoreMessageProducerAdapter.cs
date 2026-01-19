using Daqifi.Core.Communication.Messages;
using Daqifi.Core.Device;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.IO.Messages.Producers;

namespace Daqifi.Desktop.Device.WiFiDevice;

public sealed class CoreMessageProducerAdapter : IMessageProducer
{
    private readonly DaqifiDevice _device;
    private readonly AppLogger _logger = AppLogger.Instance;

    public CoreMessageProducerAdapter(DaqifiDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
    }

    public void Start()
    {
        // Core device manages the producer lifecycle.
    }

    public void Stop()
    {
        // Core device manages the producer lifecycle.
    }

    public void StopSafely()
    {
        // Core device manages the producer lifecycle.
    }

    public void Send(IOutboundMessage<string> message)
    {
        try
        {
            _device.Send(message);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed sending message via core device.");
        }
    }
}
