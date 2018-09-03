using Daqifi.Desktop.Channel;
using Daqifi.Desktop.DataModel.Channel;
using Daqifi.Desktop.IO.Messages.MessageTypes;
using Daqifi.Desktop.IO.Messages.Producers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;

namespace Daqifi.Desktop.Test.Device
{
    [TestClass]
    public class AbstractStreamingDeviceTests
    {
        #region Adding Analog Channels
        [TestMethod]
        public void Add1stAnalogChannel_SendsCorrectMessage()
        {
            var actualMessage = GetMessageFromAddingSingleChannel(0, ChannelType.Analog);
            var expectedMessage = new ScpiMessage("ENAble:VOLTage:DC 1");
            CollectionAssert.AreEqual(expectedMessage.GetBytes(), actualMessage.GetBytes());
        }

        [TestMethod]
        public void Add2ndAnalogChannel_SendsCorrectMessage()
        {
            var actualMessage = GetMessageFromAddingSingleChannel(1, ChannelType.Analog);
            var expectedMessage = new ScpiMessage("ENAble:VOLTage:DC 2");
            CollectionAssert.AreEqual(expectedMessage.GetBytes(), actualMessage.GetBytes());
        }

        [TestMethod]
        public void Add3rdAnalogChannel_SendsCorrectMessage()
        {
            var actualMessage = GetMessageFromAddingSingleChannel(2, ChannelType.Analog);
            var expectedMessage = new ScpiMessage("ENAble:VOLTage:DC 4");
            CollectionAssert.AreEqual(expectedMessage.GetBytes(), actualMessage.GetBytes());
        }

        [TestMethod]
        public void Add4thAnalogChannel_SendsCorrectMessage()
        {
            var actualMessage = GetMessageFromAddingSingleChannel(3, ChannelType.Analog);
            var expectedMessage = new ScpiMessage("ENAble:VOLTage:DC 8");
            CollectionAssert.AreEqual(expectedMessage.GetBytes(), actualMessage.GetBytes());
        }

        [TestMethod]
        public void Add5thAnalogChannel_SendsCorrectMessage()
        {
            var actualMessage = GetMessageFromAddingSingleChannel(4, ChannelType.Analog);
            var expectedMessage = new ScpiMessage("ENAble:VOLTage:DC 16");
            CollectionAssert.AreEqual(expectedMessage.GetBytes(), actualMessage.GetBytes());
        }

        [TestMethod]
        public void Add16thAnalogChannel_SendsCorrectMessage()
        {
            var actualMessage = GetMessageFromAddingSingleChannel(15, ChannelType.Analog);
            var expectedMessage = new ScpiMessage("ENAble:VOLTage:DC 32768");
            CollectionAssert.AreEqual(expectedMessage.GetBytes(), actualMessage.GetBytes());
        }

        [TestMethod]
        public void Add1stAnd2ndAnalogChannel_SendsCorrectMessage()
        {
            var actualMessage = GetMessagesFromAddingMultipleChannels(new[] {0, 1});
            var expectedMessage = new ScpiMessage("ENAble:VOLTage:DC 3");
            CollectionAssert.AreEqual(expectedMessage.GetBytes(), actualMessage.GetBytes());
        }

        [TestMethod]
        public void Add5thtAnd7thAnalogChannel_SendsCorrectMessage()
        {
            var actualMessage = GetMessagesFromAddingMultipleChannels(new[] { 4, 6 });
            var expectedMessage = new ScpiMessage("ENAble:VOLTage:DC 80");
            CollectionAssert.AreEqual(expectedMessage.GetBytes(), actualMessage.GetBytes());
        }

        [TestMethod]
        public void Add1st4th5th6th7thAnd8thAnalogChannel_SendsCorrectMessage()
        {
            var actualMessage = GetMessagesFromAddingMultipleChannels(new[] { 0, 3, 4, 5, 6, 7 });
            var expectedMessage = new ScpiMessage("ENAble:VOLTage:DC 249");
            CollectionAssert.AreEqual(expectedMessage.GetBytes(), actualMessage.GetBytes());
        }
        #endregion

        #region Adding Digital Channels
        [TestMethod]
        public void Add1stDigitalChannel_SendsCorrectMessage()
        {
            var actualMessage = GetMessageFromAddingSingleChannel(0, ChannelType.Digital);
            var expectedMessage = new ScpiMessage("DIO:PORt:ENAble 1");
            CollectionAssert.AreEqual(expectedMessage.GetBytes(), actualMessage.GetBytes());
        }
        #endregion

        private static IMessage GetMessageFromAddingSingleChannel(int channelIndex, ChannelType channelType)
        {
            // Mock out a message procducer
            IMessage message = null;
            var mockedMessageProducer = new Mock<IMessageProducer>();
            mockedMessageProducer.Setup(p => p.Send(It.IsAny<IMessage>()))
                .Callback<IMessage>(r => message = r);

            // Mock out a channel
            var mockedChannel = new Mock<IChannel>();
            mockedChannel.SetupAllProperties();
            mockedChannel.Setup(c => c.Type).Returns(channelType);
            mockedChannel.Setup(c => c.Index).Returns(channelIndex);

            // Initalize a streaming device with the message producer
            var device = new DummyAbstractStreamingDevice
            {
                MessageProducer = mockedMessageProducer.Object,
                DataChannels = new List<IChannel>
                {
                    mockedChannel.Object
                }
            };

            device.AddChannel(mockedChannel.Object);

            return message;
        }

        private static IMessage GetMessagesFromAddingMultipleChannels(int[] channelIndices)
        {
            // Mock out a message procducer
            IMessage message = null;
            var mockedMessageProducer = new Mock<IMessageProducer>();
            mockedMessageProducer.Setup(p => p.Send(It.IsAny<IMessage>()))
                .Callback<IMessage>(r => message = r);

            var mockedChannels = new List<Mock<IChannel>>();
            foreach (var channelIndex in channelIndices)
            {
                // Mock out a channel
                var mockedChannel = new Mock<IChannel>();
                mockedChannel.SetupAllProperties();
                mockedChannel.Setup(c => c.Type).Returns(ChannelType.Analog);
                mockedChannel.Setup(c => c.Index).Returns(channelIndex);
                mockedChannels.Add(mockedChannel);
            }

            // Initalize a streaming device with the message producer
            var device = new DummyAbstractStreamingDevice
            {
                MessageProducer = mockedMessageProducer.Object,
                DataChannels = new List<IChannel>()
            };

            foreach (var mockedChannel in mockedChannels)
            {
                device.DataChannels.Add(mockedChannel.Object);
            }

            foreach (var mockedChannel in mockedChannels)
            {
                device.AddChannel(mockedChannel.Object);
            }

            return message;
        }

        // todo test add channels and delete channels (DI for message producers)
    }
}
