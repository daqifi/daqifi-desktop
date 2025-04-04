using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Linq;
using System.Threading;
using Daqifi.Core.Communication.Messages;
using Daqifi.Desktop.IO.Messages.Producers;

namespace Daqifi.Desktop.IO.Test.Messages.Producers;

[TestClass]
public class MessageProducerTests
{
    [TestMethod]
    public void SendSingleMessage_CorrectMessageIsSent()
    {
        // Crate the message producer with a memory stream
        var stream = new MemoryStream();
        var messageProducer = new MessageProducer(stream);

        // Start the message producer
        messageProducer.Start();
        var messageToSend = new ScpiMessage("Test");
        messageProducer.Send(messageToSend);

        // Sleep to allow th message producer to send the message
        // Has an intneral queue that it processes at a set interval
        Thread.Sleep(1000);

        // Get the result
        var actualResult = stream.ToArray();

        // Compare
        CollectionAssert.AreEqual(messageToSend.GetBytes(), actualResult);
    }

    [TestMethod]
    public void SendMultipleMessage_CorrectMessagesAreSent()
    {
        // Crate the message producer with a memory stream
        var stream = new MemoryStream();
        var messageProducer = new MessageProducer(stream);

        // Start the message producer
        messageProducer.Start();
        var message1ToSend = new ScpiMessage("Test1");
        var message2ToSend = new ScpiMessage("Test2");
        messageProducer.Send(message1ToSend);
        messageProducer.Send(message2ToSend);

        // Sleep to allow th message producer to send the message
        // Has an intneral queue that that it processes at a set interval
        Thread.Sleep(1000);

        // Get the result
        var actualResult = stream.ToArray();

        var expectedResult = message1ToSend.GetBytes().Concat(message2ToSend.GetBytes()).ToArray();

        // Compare
        CollectionAssert.AreEqual(expectedResult, actualResult);
    }

    [TestMethod]
    public void SendMultipleMessage_ShutdownProducerSafely_AllMessagesAreSent()
    {
        // Crate the message producer with a memory stream
        var stream = new MemoryStream();
        var messageProducer = new MessageProducer(stream);

        // Start the message producer
        messageProducer.Start();
        var message1ToSend = new ScpiMessage("Test1");
        var message2ToSend = new ScpiMessage("Test2");
        var message3ToSend = new ScpiMessage("Test3");
        var message4ToSend = new ScpiMessage("Test4");
        var message5ToSend = new ScpiMessage("Test5");
        messageProducer.Send(message1ToSend);
        messageProducer.Send(message2ToSend);
        messageProducer.Send(message3ToSend);
        messageProducer.Send(message4ToSend);
        messageProducer.Send(message5ToSend);

        messageProducer.StopSafely();

        // Get the result
        var actualResult = stream.ToArray();

        var expectedResult = message1ToSend.GetBytes()
            .Concat(message2ToSend.GetBytes())
            .Concat(message3ToSend.GetBytes())
            .Concat(message4ToSend.GetBytes())
            .Concat(message5ToSend.GetBytes()).ToArray();

        // Compare
        CollectionAssert.AreEqual(expectedResult, actualResult);
    }
}