using System.IO;
using Daqifi.Core.Device;
using Daqifi.Desktop.IO.Messages;
using Daqifi.Desktop.IO.Messages.Consumers;

namespace Daqifi.Desktop.Device.WiFiDevice;

public sealed class CoreMessageConsumerAdapter : IMessageConsumer
{
    private readonly DaqifiDevice _device;
    private bool _running;

    public CoreMessageConsumerAdapter(DaqifiDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        DataStream = Stream.Null;
    }

    public bool Running
    {
        get => _running;
        set => _running = value;
    }

    public Stream DataStream { get; set; }

    public event OnMessageReceivedHandler OnMessageReceived;

    public void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _device.MessageReceived += OnDeviceMessageReceived;
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        _device.MessageReceived -= OnDeviceMessageReceived;
    }

    public void NotifyMessageReceived(object sender, MessageEventArgs<object> e)
    {
        OnMessageReceived?.Invoke(sender, e);
    }

    public void Run()
    {
        // Core device owns the background consumer loop.
    }

    private void OnDeviceMessageReceived(object? sender, Daqifi.Core.Device.MessageReceivedEventArgs e)
    {
        if (!_running)
        {
            return;
        }

        var args = new MessageEventArgs<object>(e.Message);
        NotifyMessageReceived(this, args);
    }
}
