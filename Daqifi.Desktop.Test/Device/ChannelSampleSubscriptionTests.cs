using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Device;
using Daqifi.Core.Communication.Messages;
using Moq;
using System.Reflection;
using CoreChannelDirection = Daqifi.Core.Channel.ChannelDirection;
using CoreSampleReceivedEventArgs = Daqifi.Core.Channel.SampleReceivedEventArgs;

namespace Daqifi.Desktop.Test.Device;

/// <summary>
/// Covers the per-channel Core sample subscriptions <see cref="AbstractStreamingDevice"/> tracks
/// while wiring <c>SampleReceived</c> handlers. <see cref="AbstractChannel"/> overrides equality
/// and hashing on its mutable <c>Name</c>, so the tracking map has to key on reference identity —
/// otherwise two wrappers for the same logical channel share a single entry and the bookkeeping
/// detaches the wrong delegate.
/// </summary>
[TestClass]
public class ChannelSampleSubscriptionTests
{
    private const string SHARED_CHANNEL_NAME = "AI0";

    [TestMethod]
    public void SubscribeChannelSamples_TwoWrappersSharingAName_DetachesEachWrappersOwnHandler()
    {
        // Arrange — two wrappers for the same logical channel, which is what SyncChannelsFromCore
        // produces when Core hands back a different IChannel instance for a key it already has a
        // wrapper for: the replacement is built and subscribed before the trailing cleanup loop
        // unsubscribes the outgoing one.
        var device = new SubscriptionTestDevice();
        var outgoingCore = CreateCoreChannel(out var outgoingAdded, out var outgoingRemoved);
        var replacementCore = CreateCoreChannel(out var replacementAdded, out var replacementRemoved);
        var outgoingWrapper = new AnalogChannel(device, outgoingCore);
        var replacementWrapper = new AnalogChannel(device, replacementCore);

        Assert.AreNotSame(outgoingWrapper, replacementWrapper);
        Assert.IsTrue(
            outgoingWrapper.Equals(replacementWrapper),
            "Precondition: AbstractChannel equality is Name-based, so these two distinct wrappers " +
            "collide as keys in any default-equality dictionary.");

        // Act
        InvokeSubscribeChannelSamples(device, outgoingWrapper, outgoingCore);
        InvokeSubscribeChannelSamples(device, replacementWrapper, replacementCore);
        InvokeUnsubscribeChannelSamples(device, outgoingWrapper, outgoingCore);
        InvokeUnsubscribeChannelSamples(device, replacementWrapper, replacementCore);

        // Assert
        Assert.AreEqual(1, outgoingAdded.Count, "The outgoing wrapper should be subscribed exactly once.");
        Assert.AreEqual(1, replacementAdded.Count, "The replacement wrapper should be subscribed exactly once.");
        CollectionAssert.AreEqual(
            outgoingAdded,
            outgoingRemoved,
            "The outgoing wrapper's own delegate must come off its Core channel. Detaching some " +
            "other wrapper's delegate is a no-op there and leaves the real subscription attached.");
        CollectionAssert.AreEqual(
            replacementAdded,
            replacementRemoved,
            "The replacement wrapper's handler must still be tracked after the outgoing wrapper is " +
            "unsubscribed, so it can be detached in turn instead of leaking.");
    }

    [TestMethod]
    public void SubscribeChannelSamples_SameWrapperSubscribedTwice_DetachesThePreviousHandler()
    {
        // Arrange
        var device = new SubscriptionTestDevice();
        var coreChannel = CreateCoreChannel(out var added, out var removed);
        var wrapper = new AnalogChannel(device, coreChannel);

        // Act — a second subscribe for the very same wrapper reference
        InvokeSubscribeChannelSamples(device, wrapper, coreChannel);
        InvokeSubscribeChannelSamples(device, wrapper, coreChannel);

        // Assert — only one handler can be tracked per wrapper, so the first must be detached
        // rather than left attached and unreachable, which would route every sample twice.
        Assert.AreEqual(2, added.Count);
        CollectionAssert.AreEqual(
            new[] { added[0] },
            removed,
            "Re-subscribing a wrapper must detach the handler it is replacing.");
    }

    /// <summary>
    /// Builds a Core analog channel whose <c>SampleReceived</c> accessors record the exact delegates
    /// handed to them, so a test can tell <em>which</em> handler was attached or detached rather
    /// than just how many calls were made.
    /// </summary>
    private static Daqifi.Core.Channel.IAnalogChannel CreateCoreChannel(
        out List<EventHandler<CoreSampleReceivedEventArgs>> added,
        out List<EventHandler<CoreSampleReceivedEventArgs>> removed)
    {
        var addedHandlers = new List<EventHandler<CoreSampleReceivedEventArgs>>();
        var removedHandlers = new List<EventHandler<CoreSampleReceivedEventArgs>>();

        var coreChannel = new Mock<Daqifi.Core.Channel.IAnalogChannel>();
        coreChannel.SetupGet(channel => channel.Name).Returns(SHARED_CHANNEL_NAME);
        coreChannel.SetupGet(channel => channel.Direction).Returns(CoreChannelDirection.Input);
        coreChannel
            .SetupAdd(channel => channel.SampleReceived += It.IsAny<EventHandler<CoreSampleReceivedEventArgs>>())
            .Callback<EventHandler<CoreSampleReceivedEventArgs>>(addedHandlers.Add);
        coreChannel
            .SetupRemove(channel => channel.SampleReceived -= It.IsAny<EventHandler<CoreSampleReceivedEventArgs>>())
            .Callback<EventHandler<CoreSampleReceivedEventArgs>>(removedHandlers.Add);

        added = addedHandlers;
        removed = removedHandlers;
        return coreChannel.Object;
    }

    private static void InvokeSubscribeChannelSamples(
        AbstractStreamingDevice device,
        IChannel desktopChannel,
        Daqifi.Core.Channel.IChannel coreChannel)
    {
        InvokeChannelSampleMethod(device, "SubscribeChannelSamples", desktopChannel, coreChannel);
    }

    private static void InvokeUnsubscribeChannelSamples(
        AbstractStreamingDevice device,
        IChannel desktopChannel,
        Daqifi.Core.Channel.IChannel coreChannel)
    {
        InvokeChannelSampleMethod(device, "UnsubscribeChannelSamples", desktopChannel, coreChannel);
    }

    /// <summary>
    /// Calls one of the device's private subscription helpers. They are deliberately private —
    /// the production callers are <c>SyncChannelsFromCore</c> and the disconnect teardown — so the
    /// bookkeeping they share is reached by reflection rather than by widening the device's API.
    /// </summary>
    private static void InvokeChannelSampleMethod(
        AbstractStreamingDevice device,
        string methodName,
        IChannel desktopChannel,
        Daqifi.Core.Channel.IChannel coreChannel)
    {
        var method = typeof(AbstractStreamingDevice).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.IsNotNull(method, $"{methodName} was not found on AbstractStreamingDevice.");
        method.Invoke(device, [desktopChannel, coreChannel]);
    }

    private sealed class SubscriptionTestDevice : AbstractStreamingDevice
    {
        public override ConnectionType ConnectionType => ConnectionType.Usb;

        public override bool Connect() => true;

        public override bool Disconnect() => true;

        public override bool Write(string command) => true;

        protected override void SendMessage(IOutboundMessage<string> message)
        {
        }
    }
}
