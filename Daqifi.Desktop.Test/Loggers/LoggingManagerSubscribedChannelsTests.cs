using System.ComponentModel;
using System.Diagnostics;
using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Configuration;
using Daqifi.Desktop.Logger;
using Microsoft.EntityFrameworkCore;
using Moq;
using ChannelDirection = Daqifi.Core.Channel.ChannelDirection;
using ChannelType = Daqifi.Core.Channel.ChannelType;

namespace Daqifi.Desktop.Test.Loggers;

/// <summary>
/// Covers <see cref="LoggingManager.SubscribedChannels"/> and its mutators.
/// <para>
/// The point of these tests is the copy-on-write contract: <c>SubscribedChannels</c> is read from
/// the device transport thread (<c>PlotLogger</c> resolves series visibility there) while the UI
/// thread subscribes and unsubscribes, so a mutator must publish a new list rather than change the
/// one a reader may be enumerating.
/// </para>
/// </summary>
[TestClass]
public class LoggingManagerSubscribedChannelsTests
{
    #region Constants
    private const string DEVICE_SERIAL = "SN-0001";
    private const string OTHER_DEVICE_SERIAL = "SN-0002";

    /// <summary>
    /// Wall-clock budget for the mutation loop in the concurrency test. Against the unfixed code
    /// the reader faulted in single-digit milliseconds, so this is generous.
    /// </summary>
    private const int MUTATION_BUDGET_MS = 300;

    /// <summary>Ceiling for any wait that only a broken build should ever exhaust.</summary>
    private const int THREAD_WAIT_TIMEOUT_MS = 10_000;
    #endregion

    #region Setup
    private static LoggingManager NewManager()
    {
        // The parameterless constructor resolves its context factory from App.ServiceProvider,
        // which does not exist in the test host. The internal constructor is the seam.
        return new LoggingManager(new Mock<IDbContextFactory<LoggingContext>>().Object);
    }

    private static FakeChannel NewChannel(string name, string deviceSerial = DEVICE_SERIAL)
    {
        return new FakeChannel { Name = name, DeviceSerialNo = deviceSerial };
    }
    #endregion

    #region Copy-on-write contract
    [TestMethod]
    public void Subscribe_PublishesANewList_LeavingAnAlreadyHandedOutSnapshotUntouched()
    {
        // Arrange
        var manager = NewManager();
        var snapshotTakenByAReader = manager.SubscribedChannels;

        // Act
        manager.Subscribe(NewChannel("AI0"));

        // Assert - the reader's list must not have grown under it, and the property must now
        // hand out a different list containing the new channel.
        Assert.AreEqual(0, snapshotTakenByAReader.Count);
        Assert.AreNotSame(snapshotTakenByAReader, manager.SubscribedChannels);
        Assert.AreEqual(1, manager.SubscribedChannels.Count);
    }

    [TestMethod]
    public void Unsubscribe_PublishesANewList_LeavingAnAlreadyHandedOutSnapshotUntouched()
    {
        // Arrange
        var manager = NewManager();
        var channel = NewChannel("AI0");
        manager.Subscribe(channel);
        var snapshotTakenByAReader = manager.SubscribedChannels;

        // Act
        manager.Unsubscribe(channel);

        // Assert
        Assert.AreEqual(1, snapshotTakenByAReader.Count);
        Assert.AreNotSame(snapshotTakenByAReader, manager.SubscribedChannels);
        Assert.AreEqual(0, manager.SubscribedChannels.Count);
    }

    [TestMethod]
    public void ClearChannelList_PublishesANewList_LeavingAnAlreadyHandedOutSnapshotUntouched()
    {
        // Arrange
        var manager = NewManager();
        manager.Subscribe(NewChannel("AI0"));
        manager.Subscribe(NewChannel("AI1"));
        var snapshotTakenByAReader = manager.SubscribedChannels;

        // Act
        manager.ClearChannelList();

        // Assert
        Assert.AreEqual(2, snapshotTakenByAReader.Count);
        Assert.AreEqual(0, manager.SubscribedChannels.Count);
    }

    /// <summary>
    /// The regression this file exists for. A reader enumerating <c>SubscribedChannels</c> off the
    /// UI thread must never see the list mutate mid-enumeration. Against the unfixed code the
    /// reader throws <see cref="InvalidOperationException"/> ("Collection was modified") almost
    /// immediately; nothing in the real transport call chain catches it, so it escapes onto Core's
    /// decode thread.
    /// </summary>
    /// <remarks>
    /// The reader signals from inside the <c>foreach</c> body, so when the mutation loop starts the
    /// reader is provably mid-enumeration rather than merely alive — a stress test whose threads
    /// never run at the same time passes against broken code. It then keeps enumerating for the
    /// whole budget, so the overlap is sustained rather than a single lucky instant.
    /// </remarks>
    [TestMethod]
    public void SubscribedChannels_IsSafeToEnumerate_WhileAnotherThreadSubscribesAndUnsubscribes()
    {
        // Arrange - seed enough entries that an enumeration spans a meaningful window.
        var manager = NewManager();
        for (var i = 0; i < 8; i++)
        {
            manager.Subscribe(NewChannel($"AI{i}"));
        }

        Exception? escaped = null;
        var stop = 0;
        var readerIsEnumerating = new ManualResetEventSlim(false);

        var reader = new Thread(() =>
        {
            try
            {
                while (Volatile.Read(ref stop) == 0)
                {
                    foreach (var channel in manager.SubscribedChannels)
                    {
                        _ = channel.Name;

                        // Signalled from *inside* the enumeration, not after it: the waiter's
                        // guarantee has to be "the reader is mid-enumeration right now", not merely
                        // "the reader has run at least once".
                        if (!readerIsEnumerating.IsSet)
                        {
                            readerIsEnumerating.Set();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Volatile.Write(ref escaped, ex);
            }
        })
        {
            IsBackground = true,
            Name = "SubscribedChannels reader"
        };

        reader.Start();
        Assert.IsTrue(
            readerIsEnumerating.Wait(THREAD_WAIT_TIMEOUT_MS),
            "The reader thread never started enumerating, so the test would not have overlapped anything.");

        // Act - churn the collection for a fixed budget while the reader keeps enumerating.
        var churn = NewChannel("AI-churn", OTHER_DEVICE_SERIAL);
        var elapsed = Stopwatch.StartNew();
        var mutations = 0;
        while (elapsed.ElapsedMilliseconds < MUTATION_BUDGET_MS && Volatile.Read(ref escaped) == null)
        {
            manager.Subscribe(churn);
            manager.Unsubscribe(churn);
            mutations++;
        }

        Volatile.Write(ref stop, 1);
        Assert.IsTrue(reader.Join(THREAD_WAIT_TIMEOUT_MS), "The reader thread did not finish.");

        // Assert
        Assert.IsNull(
            Volatile.Read(ref escaped),
            $"A reader on another thread faulted after {mutations} subscribe/unsubscribe cycles: " +
            $"{Volatile.Read(ref escaped)}");
        Assert.IsTrue(mutations > 0, "The mutation loop never ran.");
    }
    #endregion

    #region Subscribe / Unsubscribe behaviour
    [TestMethod]
    public void Subscribe_AddsTheChannel_AndActivatesIt()
    {
        // Arrange
        var manager = NewManager();
        var channel = NewChannel("AI0");

        // Act
        manager.Subscribe(channel);

        // Assert
        CollectionAssert.AreEqual(new[] { channel }, manager.SubscribedChannels.ToList());
        Assert.IsTrue(channel.IsActive);
    }

    [TestMethod]
    public void Subscribe_IsANoOp_WhenTheSameDeviceAndChannelIsAlreadySubscribed()
    {
        // Arrange
        var manager = NewManager();
        manager.Subscribe(NewChannel("AI0"));
        var duplicate = NewChannel("AI0");

        // Act
        manager.Subscribe(duplicate);

        // Assert
        Assert.AreEqual(1, manager.SubscribedChannels.Count);
        Assert.IsFalse(duplicate.IsActive, "The rejected duplicate must not have been activated.");
    }

    [TestMethod]
    public void Subscribe_AddsBothChannels_WhenTheNameMatchesButTheDeviceDoesNot()
    {
        // Arrange
        var manager = NewManager();
        manager.Subscribe(NewChannel("AI0"));

        // Act
        manager.Subscribe(NewChannel("AI0", OTHER_DEVICE_SERIAL));

        // Assert
        Assert.AreEqual(2, manager.SubscribedChannels.Count);
    }

    [TestMethod]
    public void Unsubscribe_RemovesTheChannel_AndDeactivatesIt()
    {
        // Arrange
        var manager = NewManager();
        var channel = NewChannel("AI0");
        manager.Subscribe(channel);

        // Act - matched by serial and name, so a distinct instance still resolves.
        manager.Unsubscribe(NewChannel("AI0"));

        // Assert
        Assert.AreEqual(0, manager.SubscribedChannels.Count);
        Assert.IsFalse(channel.IsActive);
    }

    [TestMethod]
    public void Unsubscribe_IsANoOp_WhenTheChannelWasNeverSubscribed()
    {
        // Arrange
        var manager = NewManager();
        var subscribed = manager.SubscribedChannels;

        // Act
        manager.Unsubscribe(NewChannel("AI0"));

        // Assert
        Assert.AreEqual(0, manager.SubscribedChannels.Count);
        Assert.AreSame(subscribed, manager.SubscribedChannels, "A no-op must not publish a new list.");
    }

    [TestMethod]
    public void ClearChannelList_DeactivatesEveryChannel_AndEmptiesTheList()
    {
        // Arrange
        var manager = NewManager();
        var first = NewChannel("AI0");
        var second = NewChannel("AI1");
        manager.Subscribe(first);
        manager.Subscribe(second);

        // Act
        manager.ClearChannelList();

        // Assert
        Assert.AreEqual(0, manager.SubscribedChannels.Count);
        Assert.IsFalse(first.IsActive);
        Assert.IsFalse(second.IsActive);
    }
    #endregion

    #region Change notification
    [TestMethod]
    public void Subscribe_RaisesPropertyChanged_ForSubscribedChannels()
    {
        // Arrange
        var manager = NewManager();
        var raised = new List<string?>();
        manager.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // Act
        manager.Subscribe(NewChannel("AI0"));

        // Assert
        CollectionAssert.Contains(raised, nameof(LoggingManager.SubscribedChannels));
    }

    [TestMethod]
    public void Unsubscribe_RaisesPropertyChanged_ForSubscribedChannels()
    {
        // Arrange
        var manager = NewManager();
        var channel = NewChannel("AI0");
        manager.Subscribe(channel);

        var raised = new List<string?>();
        manager.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // Act
        manager.Unsubscribe(channel);

        // Assert
        CollectionAssert.Contains(raised, nameof(LoggingManager.SubscribedChannels));
    }

    /// <summary>
    /// Reading the property must hand back the same published snapshot every time until something
    /// actually changes. A getter that materialized a fresh copy per read would also be safe, but
    /// it would allocate on the transport thread for every series creation and would silently break
    /// the reference comparisons the copy-on-write tests above rely on.
    /// </summary>
    [TestMethod]
    public void SubscribedChannels_ReturnsTheSameSnapshot_UntilSomethingChanges()
    {
        // Arrange
        var manager = NewManager();
        manager.Subscribe(NewChannel("AI0"));

        // Act
        var first = manager.SubscribedChannels;
        var second = manager.SubscribedChannels;

        // Assert
        Assert.AreSame(first, second);
        manager.Subscribe(NewChannel("AI1"));
        Assert.AreNotSame(first, manager.SubscribedChannels);
    }
    #endregion

    #region Helper Types
    /// <summary>
    /// A hand-rolled <see cref="IChannel"/> so the concurrency test exercises the collection under
    /// test rather than a mocking framework's own internal state, which is not documented as safe
    /// to touch from two threads at once.
    /// </summary>
    private sealed class FakeChannel : IChannel
    {
        public int ID { get; set; }
        public string Name { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string DeviceSerialNo { get; set; } = string.Empty;
        public int Index => 0;
        public double OutputValue { get; set; }
        public ChannelType Type => ChannelType.Analog;
        public ChannelDirection Direction { get; set; }
        public string TypeString => Type.ToString();
        public string ScaleExpression { get; set; } = string.Empty;
        public System.Windows.Media.Brush ChannelColorBrush { get; set; } = System.Windows.Media.Brushes.Transparent;
        public bool IsBidirectional { get; set; }
        public bool IsOutput { get; set; }
        public bool HasAdc { get; set; }
        public bool IsActive { get; set; }
        public bool IsDigital => false;
        public bool IsAnalog => true;
        public bool IsDigitalOn { get; set; }
        public bool IsPwmCapable => false;
        public bool IsPwmEnabled { get; set; }
        public int PwmDutyCyclePercent { get; set; }
        public bool IsScalingActive { get; set; }
        public bool HasValidExpression { get; set; }
        public DataSample ActiveSample { get; set; } = null!;
        public bool IsVisible { get; set; } = true;

        public event OnChannelUpdatedHandler OnChannelUpdated = null!;

        public void NotifyChannelUpdated(object sender, DataSample e) => OnChannelUpdated?.Invoke(sender, e);

        public void SetColor(System.Windows.Media.Brush color) => ChannelColorBrush = color;
    }
    #endregion
}
