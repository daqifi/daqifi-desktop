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
    public void UnsubscribeChannelSamples_WhenAnotherWrapperSharesItsName_DetachesItsOwnHandler()
    {
        // Arrange
        var scenario = ArrangeCollidingSubscriptions();

        // Act
        InvokeUnsubscribeChannelSamples(
            scenario.Device, scenario.OutgoingWrapper, scenario.OutgoingCore.Channel);

        // Assert
        CollectionAssert.AreEqual(
            scenario.OutgoingCore.Added,
            scenario.OutgoingCore.Removed,
            "The outgoing wrapper's own delegate must come off its own Core channel. Detaching " +
            "some other wrapper's delegate is a no-op there and leaves the real subscription live " +
            "on a channel that is no longer in DataChannels.");
        Assert.AreEqual(
            0,
            scenario.ReplacementCore.Removed.Count,
            "Unsubscribing the outgoing wrapper must not disturb the replacement's subscription.");
    }

    [TestMethod]
    public void UnsubscribeChannelSamples_AfterAWrapperSharingItsNameWasRemoved_StillTracksItsHandler()
    {
        // Arrange — the outgoing wrapper has already been cleaned up, exactly as the trailing loop
        // in SyncChannelsFromCore does once the replacement is in place.
        var scenario = ArrangeCollidingSubscriptions();
        InvokeUnsubscribeChannelSamples(
            scenario.Device, scenario.OutgoingWrapper, scenario.OutgoingCore.Channel);

        // Act
        InvokeUnsubscribeChannelSamples(
            scenario.Device, scenario.ReplacementWrapper, scenario.ReplacementCore.Channel);

        // Assert
        CollectionAssert.AreEqual(
            scenario.ReplacementCore.Added,
            scenario.ReplacementCore.Removed,
            "The replacement wrapper's handler must survive the outgoing wrapper's cleanup as a " +
            "tracked entry, so it can be detached in turn instead of leaking.");
    }

    [TestMethod]
    public void SubscribeChannelSamples_WhenTheSameWrapperIsAlreadySubscribed_DetachesThePreviousHandler()
    {
        // Arrange
        var device = new SubscriptionTestDevice();
        var coreChannel = CreateCoreChannel();
        var wrapper = new AnalogChannel(device, coreChannel.Channel);
        InvokeSubscribeChannelSamples(device, wrapper, coreChannel.Channel);

        // Act — a second subscribe for the very same wrapper reference
        InvokeSubscribeChannelSamples(device, wrapper, coreChannel.Channel);

        // Assert — only one handler can be tracked per wrapper, so the first must be detached
        // rather than left attached and unreachable, which would route every sample twice.
        Assert.AreEqual(2, coreChannel.Added.Count);
        CollectionAssert.AreEqual(
            new[] { coreChannel.Added[0] },
            coreChannel.Removed,
            "Re-subscribing a wrapper must detach the handler it is replacing.");
    }

    /// <summary>
    /// Builds the state <c>SyncChannelsFromCore</c> reaches when Core hands back a different
    /// <c>IChannel</c> instance for a key the device already has a wrapper for: the replacement
    /// wrapper is created and subscribed while the outgoing wrapper is still subscribed. The two
    /// wrappers are distinct objects that compare equal, because
    /// <see cref="AbstractChannel"/> equality is <c>Name</c>-based — so they collide as keys in any
    /// default-equality dictionary.
    /// </summary>
    private static CollidingSubscriptions ArrangeCollidingSubscriptions()
    {
        var device = new SubscriptionTestDevice();
        var outgoingCore = CreateCoreChannel();
        var replacementCore = CreateCoreChannel();
        var outgoingWrapper = new AnalogChannel(device, outgoingCore.Channel);
        var replacementWrapper = new AnalogChannel(device, replacementCore.Channel);

        Assert.AreNotSame(outgoingWrapper, replacementWrapper);
        Assert.IsTrue(
            outgoingWrapper.Equals(replacementWrapper),
            "Precondition: the two wrappers must compare equal for this scenario to exercise the " +
            "key collision at all.");

        InvokeSubscribeChannelSamples(device, outgoingWrapper, outgoingCore.Channel);
        InvokeSubscribeChannelSamples(device, replacementWrapper, replacementCore.Channel);

        Assert.AreEqual(1, outgoingCore.Added.Count, "The outgoing wrapper should be subscribed once.");
        Assert.AreEqual(1, replacementCore.Added.Count, "The replacement wrapper should be subscribed once.");

        return new CollidingSubscriptions(
            device, outgoingWrapper, outgoingCore, replacementWrapper, replacementCore);
    }

    /// <summary>
    /// Builds a Core analog channel whose <c>SampleReceived</c> accessors record the exact delegates
    /// handed to them, so a test can tell <em>which</em> handler was attached or detached rather
    /// than just how many calls were made.
    /// </summary>
    private static RecordedCoreChannel CreateCoreChannel()
    {
        var added = new List<EventHandler<CoreSampleReceivedEventArgs>>();
        var removed = new List<EventHandler<CoreSampleReceivedEventArgs>>();

        var coreChannel = new Mock<Daqifi.Core.Channel.IAnalogChannel>();
        coreChannel.SetupGet(channel => channel.Name).Returns(SHARED_CHANNEL_NAME);
        coreChannel.SetupGet(channel => channel.Direction).Returns(CoreChannelDirection.Input);
        coreChannel
            .SetupAdd(channel => channel.SampleReceived += It.IsAny<EventHandler<CoreSampleReceivedEventArgs>>())
            .Callback<EventHandler<CoreSampleReceivedEventArgs>>(added.Add);
        coreChannel
            .SetupRemove(channel => channel.SampleReceived -= It.IsAny<EventHandler<CoreSampleReceivedEventArgs>>())
            .Callback<EventHandler<CoreSampleReceivedEventArgs>>(removed.Add);

        return new RecordedCoreChannel(coreChannel.Object, added, removed);
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

    /// <summary>A Core channel paired with the handlers added to and removed from it.</summary>
    private sealed record RecordedCoreChannel(
        Daqifi.Core.Channel.IAnalogChannel Channel,
        List<EventHandler<CoreSampleReceivedEventArgs>> Added,
        List<EventHandler<CoreSampleReceivedEventArgs>> Removed);

    /// <summary>Two subscribed wrappers for one logical channel, sharing a name.</summary>
    private sealed record CollidingSubscriptions(
        AbstractStreamingDevice Device,
        AnalogChannel OutgoingWrapper,
        RecordedCoreChannel OutgoingCore,
        AnalogChannel ReplacementWrapper,
        RecordedCoreChannel ReplacementCore);

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
