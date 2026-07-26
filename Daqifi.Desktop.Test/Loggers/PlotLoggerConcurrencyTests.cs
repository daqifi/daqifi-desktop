using System;
using System.Threading;
using System.Threading.Tasks;
using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Logger;
using OxyPlot;
using ChannelType = Daqifi.Core.Channel.ChannelType;

namespace Daqifi.Desktop.Test.Loggers;

/// <summary>
/// Unit coverage for <see cref="PlotLogger"/>'s shared-state locking (issue #759).
/// <para>
/// <see cref="PlotLogger.Log(DataSample)"/> runs on the device transport thread while
/// <see cref="PlotLogger.ClearPlot"/> runs on the UI thread at session start, so every structural
/// mutation of the per-channel collections must happen under <c>PlotModel.SyncRoot</c>. These tests
/// pin that contract down deterministically — a test thread holds the lock to force the exact
/// interleaving rather than relying on a timing race — plus the behaviour the locking must not
/// change (clearing, the <c>FirstTime</c> re-seed, colour updates and initial series visibility).
/// </para>
/// </summary>
[TestClass]
public class PlotLoggerConcurrencyTests
{
    #region Constants
    private const string DEVICE_SERIAL = "SN-0001";
    private const string CHANNEL_NAME = "AI0";
    private const string OTHER_CHANNEL_NAME = "AI1";
    private const string RED_ARGB = "#FFFF0000";
    private const string GREEN_ARGB = "#FF00FF00";

    /// <summary>Upper bound on any wait for a background thread; only reached when a test fails.</summary>
    private const int BLOCK_WAIT_TIMEOUT_MS = 5000;

    /// <summary>
    /// How long a lock-blocked operation is given to (wrongly) complete before we conclude it really
    /// is blocked. Long enough that an unlocked <c>ClearPlot</c> always finishes inside it.
    /// </summary>
    private const int LOCK_HELD_OBSERVATION_MS = 250;

    /// <summary>How long the thread state must stay <c>WaitSleepJoin</c> to count as parked on the lock.</summary>
    private const int BLOCK_SETTLE_MS = 100;

    /// <summary>Wall-clock budget for the stress test. Bounded so the unit gate stays fast.</summary>
    private const int STRESS_DURATION_MS = 300;

    private static readonly long BASE_TICKS = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

    private static readonly string EMPTY_SUMMARY =
        "series=0;points=0;nonfinite=0;last=NaN;min=NaN;max=NaN;firstx=NaN;lastx=NaN";
    #endregion

    #region Helpers
    /// <summary>
    /// Builds a <see cref="PlotLogger"/> with the channel-visibility lookup stubbed out. A unit test
    /// must never let it reach <c>LoggingManager.Instance</c>: that singleton's constructor resolves
    /// services from <c>App.ServiceProvider</c>, which is null outside the running application, so
    /// touching it throws a <see cref="TypeInitializationException"/> that then sticks for the whole
    /// test process.
    /// </summary>
    private static PlotLogger CreatePlotter()
    {
        return new PlotLogger { ChannelVisibilityLookup = _ => null };
    }

    private static DataSample Sample(
        long elapsedMs, double value, string color = RED_ARGB, string channelName = CHANNEL_NAME)
    {
        return new DataSample
        {
            DeviceSerialNo = DEVICE_SERIAL,
            ChannelName = channelName,
            Color = color,
            Type = ChannelType.Analog,
            Value = value,
            TimestampTicks = BASE_TICKS + (elapsedMs * TimeSpan.TicksPerMillisecond)
        };
    }

    /// <summary>
    /// Blocks until <paramref name="thread"/> has been parked in <see cref="ThreadState.WaitSleepJoin"/>
    /// continuously for <see cref="BLOCK_SETTLE_MS"/> — i.e. it is waiting on the monitor rather than
    /// momentarily inside a runtime lock while starting up.
    /// </summary>
    private static void WaitUntilParkedOnLock(Thread thread)
    {
        var deadline = Environment.TickCount64 + BLOCK_WAIT_TIMEOUT_MS;
        while (Environment.TickCount64 < deadline)
        {
            if ((thread.ThreadState & ThreadState.WaitSleepJoin) == 0)
            {
                Thread.Sleep(5);
                continue;
            }

            var settleUntil = Environment.TickCount64 + BLOCK_SETTLE_MS;
            var stayedParked = true;
            while (Environment.TickCount64 < settleUntil)
            {
                if ((thread.ThreadState & ThreadState.WaitSleepJoin) == 0)
                {
                    stayedParked = false;
                    break;
                }

                Thread.Sleep(5);
            }

            if (stayedParked)
            {
                return;
            }
        }

        Assert.Fail("The background thread never parked on PlotModel.SyncRoot.");
    }
    #endregion

    #region ClearPlot takes the lock
    [TestMethod]
    public void ClearPlot_DoesNotComplete_WhileThePlotLockIsHeld()
    {
        var plotter = CreatePlotter();
        plotter.Log(Sample(0, 1.0));

        var entered = new ManualResetEventSlim(false);
        Task clear;

        lock (plotter.PlotModel.SyncRoot)
        {
            clear = Task.Run(() =>
            {
                entered.Set();
                plotter.ClearPlot();
            });

            Assert.IsTrue(entered.Wait(BLOCK_WAIT_TIMEOUT_MS), "The clearing task never started.");
            Assert.IsFalse(
                clear.Wait(LOCK_HELD_OBSERVATION_MS),
                "ClearPlot() ran to completion while PlotModel.SyncRoot was held, so it never took the "
                + "lock that guards the collections it empties.");
        }

        Assert.IsTrue(clear.Wait(BLOCK_WAIT_TIMEOUT_MS), "ClearPlot() did not complete once the lock was released.");
        Assert.AreEqual(0, plotter.LoggedPoints.Count, "ClearPlot() should still have emptied the plot.");
    }
    #endregion

    #region Log survives a concurrent clear
    /// <summary>
    /// The reported failure mode: <c>Log</c> decided the channel was already known, then a session-start
    /// <c>ClearPlot</c> emptied the dictionaries before <c>Log</c> reached its indexed access. With the
    /// membership check outside the lock this threw <see cref="System.Collections.Generic.KeyNotFoundException"/>
    /// on the device transport thread, where nothing catches it.
    /// </summary>
    [TestMethod]
    public void Log_DoesNotThrow_WhenClearPlotEmptiesTheChannelsWhileItWaitsForTheLock()
    {
        var plotter = CreatePlotter();
        plotter.Log(Sample(0, 1.0));

        Exception? escaped = null;
        var transport = new Thread(() =>
        {
            try
            {
                plotter.Log(Sample(1, 2.0));
            }
            catch (Exception ex)
            {
                escaped = ex;
            }
        })
        {
            IsBackground = true,
            Name = "fake-transport"
        };

        lock (plotter.PlotModel.SyncRoot)
        {
            transport.Start();

            // Park the "transport thread" mid-Log, then let the UI thread's session-start clear land.
            WaitUntilParkedOnLock(transport);
            plotter.ClearPlot();
        }

        Assert.IsTrue(transport.Join(BLOCK_WAIT_TIMEOUT_MS), "The logging thread never finished.");
        Assert.IsNull(escaped, $"Log() let {escaped?.GetType().Name} escape onto the transport thread: {escaped}");
        Assert.AreEqual(
            1,
            plotter.LoggedPoints.Count,
            "Log() should have re-created the series ClearPlot() removed rather than dropping the sample.");
    }

    /// <summary>
    /// Secondary, non-deterministic guard covering the failure modes the interleaved test cannot force:
    /// <c>Dictionary</c>/<c>HashSet</c> corruption from a concurrent structural <c>Clear</c>, which can
    /// spin or return garbage rather than throw. The producer is started and given a moment to reach
    /// steady state so the two threads genuinely overlap.
    /// </summary>
    [TestMethod]
    public void LogAndClearPlot_FromTwoThreads_DoNotThrow()
    {
        var plotter = CreatePlotter();
        using var stop = new CancellationTokenSource();
        var running = new ManualResetEventSlim(false);
        Exception? escaped = null;

        var transport = new Thread(() =>
        {
            try
            {
                long elapsedMs = 0;
                while (!stop.IsCancellationRequested)
                {
                    plotter.Log(Sample(elapsedMs++, 1.0));
                    running.Set();
                }
            }
            catch (Exception ex)
            {
                escaped = ex;
            }
        })
        {
            IsBackground = true,
            Name = "fake-transport"
        };

        transport.Start();
        try
        {
            Assert.IsTrue(running.Wait(BLOCK_WAIT_TIMEOUT_MS), "The logging thread never produced a sample.");

            var deadline = Environment.TickCount64 + STRESS_DURATION_MS;
            while (Environment.TickCount64 < deadline)
            {
                plotter.ClearPlot();
            }
        }
        finally
        {
            stop.Cancel();
        }

        Assert.IsTrue(transport.Join(BLOCK_WAIT_TIMEOUT_MS), "The logging thread never finished.");
        Assert.IsNull(escaped, $"A concurrent Log()/ClearPlot() threw {escaped?.GetType().Name}: {escaped}");
    }
    #endregion

    #region Behaviour the locking must preserve
    [TestMethod]
    public void ClearPlot_ResetsEveryPlotCollectionAndTheStatsSummary()
    {
        var plotter = CreatePlotter();
        plotter.Log(Sample(0, 1.0));
        plotter.Log(Sample(1, 2.0, channelName: OTHER_CHANNEL_NAME));

        Assert.AreEqual(2, plotter.LoggedChannels.Count, "Arrange failed: both channels should have a series.");
        Assert.AreEqual(2, plotter.PlotModel.Series.Count, "Arrange failed: both series should be on the model.");
        Assert.IsNotNull(plotter.FirstTime, "Arrange failed: the first sample should have seeded FirstTime.");

        plotter.ClearPlot();

        Assert.AreEqual(0, plotter.LoggedChannels.Count);
        Assert.AreEqual(0, plotter.LoggedPoints.Count);
        Assert.AreEqual(0, plotter.PlotModel.Series.Count);
        Assert.IsNull(plotter.FirstTime, "ClearPlot() must drop the time-axis anchor so the next session re-seeds it.");
        Assert.AreEqual(EMPTY_SUMMARY, plotter.PlotStatsSummary);
    }

    [TestMethod]
    public void Log_ReSeedsFirstTime_AfterClearPlot()
    {
        var plotter = CreatePlotter();
        plotter.Log(Sample(0, 1.0));
        plotter.Log(Sample(5, 2.0));

        Assert.AreEqual(
            5.0,
            plotter.LoggedPoints[(DEVICE_SERIAL, CHANNEL_NAME)][1].X,
            "Arrange failed: X is elapsed ms measured from the first sample.");

        plotter.ClearPlot();
        plotter.Log(Sample(100, 3.0));

        var points = plotter.LoggedPoints[(DEVICE_SERIAL, CHANNEL_NAME)];
        Assert.AreEqual(1, points.Count);
        Assert.AreEqual(
            0.0,
            points[0].X,
            "The first sample after a clear becomes the new time-axis anchor, so it plots at X = 0.");
    }

    [TestMethod]
    public void Log_UpdatesTheSeriesColour_WithoutCreatingASecondSeries()
    {
        var plotter = CreatePlotter();
        plotter.Log(Sample(0, 1.0, RED_ARGB));
        plotter.Log(Sample(1, 2.0, GREEN_ARGB));

        Assert.AreEqual(1, plotter.PlotModel.Series.Count, "A colour change must not add a second series.");
        Assert.AreEqual(
            OxyColor.FromArgb(0xFF, 0x00, 0xFF, 0x00),
            plotter.LoggedChannels[(DEVICE_SERIAL, CHANNEL_NAME)].Color,
            "The series should track the channel's current colour.");
        Assert.AreEqual(2, plotter.LoggedPoints[(DEVICE_SERIAL, CHANNEL_NAME)].Count);
    }

    [TestMethod]
    public void Log_AppliesTheChannelVisibilityLookup_ToANewSeries()
    {
        var plotter = new PlotLogger
        {
            ChannelVisibilityLookup = key => key.channelName == CHANNEL_NAME ? false : null
        };

        plotter.Log(Sample(0, 1.0));
        plotter.Log(Sample(1, 2.0, channelName: OTHER_CHANNEL_NAME));

        Assert.IsFalse(
            plotter.LoggedChannels[(DEVICE_SERIAL, CHANNEL_NAME)].IsVisible,
            "A hidden subscribed channel's series should start hidden.");
        Assert.IsTrue(
            plotter.LoggedChannels[(DEVICE_SERIAL, OTHER_CHANNEL_NAME)].IsVisible,
            "An unsubscribed channel has no recorded visibility, so its series keeps OxyPlot's default.");
    }
    #endregion
}
