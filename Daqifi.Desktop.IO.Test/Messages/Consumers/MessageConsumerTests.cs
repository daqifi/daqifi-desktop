using Daqifi.Core.Communication.Messages;
using Daqifi.Desktop.IO.Messages.Consumers;
using Google.Protobuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Daqifi.Desktop.IO.Test.Messages.Consumers;

[TestClass]
public class MessageConsumerTests
{
    [TestMethod]
    public void Start_WhenStreamContainsDelimitedMessage_RaisesMessageReceived()
    {
        var message = new DaqifiOutMessage
        {
            MsgTimeStamp = 1
        };

        var payload = CreateDelimitedMessage(message);
        using var stream = new MemoryStream(payload);
        using var consumer = new MessageConsumer(stream);

        IInboundMessage<object>? received = null;
        using var receivedSignal = new ManualResetEventSlim(false);

        consumer.OnMessageReceived += (_, args) =>
        {
            received = args.Message;
            receivedSignal.Set();
        };

        consumer.Start();

        var signaled = receivedSignal.Wait(TimeSpan.FromSeconds(1));
        consumer.Stop();

        Assert.IsTrue(signaled, "Expected MessageConsumer to raise OnMessageReceived.");
        Assert.IsNotNull(received);
        Assert.IsInstanceOfType<DaqifiOutMessage>(received.Data);
        Assert.AreEqual((uint)1, ((DaqifiOutMessage)received.Data).MsgTimeStamp);
    }

    private static byte[] CreateDelimitedMessage(DaqifiOutMessage message)
    {
        var payload = message.ToByteArray();
        var prefix = EncodeVarint(payload.Length);
        return prefix.Concat(payload).ToArray();
    }

    private static byte[] EncodeVarint(int value)
    {
        var bytes = new List<byte>();
        var unsignedValue = (uint)value;

        while (unsignedValue > 0x7F)
        {
            bytes.Add((byte)((unsignedValue & 0x7F) | 0x80));
            unsignedValue >>= 7;
        }

        bytes.Add((byte)unsignedValue);
        return bytes.ToArray();
    }
}
