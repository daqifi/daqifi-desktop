using Daqifi.Desktop.IO.Messages.MessageTypes;
using Daqifi.Desktop.IO.Messages.Producers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Linq;
using System.Threading;

namespace Daqifi.Desktop.IO.Test.Messages.Producers
{
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
            // Has an intneral queue that that it processes at a set interval
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
    }
}
