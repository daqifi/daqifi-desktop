using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Logger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using OxyPlot;
using ChannelType = Daqifi.Core.Channel.ChannelType;

namespace Daqifi.Desktop.Test.Loggers;

/// <summary>
/// Behavior contract for <see cref="SessionDataRepository"/> — the session read/delete path extracted
/// from <c>DatabaseLogger</c> (issue #592). The repository is driven directly against a real temp
/// SQLite <see cref="IDbContextFactory{TContext}"/> (no mock DB), so the channel-discovery query, the
/// fast initial batch, the full-range sampled load, per-device frequency, and the transactional delete
/// all run through the actual EF/ADO-to-SQLite translation. Covers, in particular, the #592 bug fix:
/// a failed delete now propagates instead of being swallowed, so the session-list view model can keep
/// the bound row when the data still exists.
/// </summary>
[TestClass]
public class SessionDataRepositoryTests
{
    private const string Serial = "9090684023231015079";
    private const string SerialB = "1111222233334444555";
    private const long BaseTick = 638_000_000_000_000_000;
    private const int SessionId = 1;

    private readonly List<string> _tempDbPaths = [];

    [TestCleanup]
    public void TestCleanup()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var path in _tempDbPaths)
        {
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                try
                {
                    if (File.Exists(path + suffix)) { File.Delete(path + suffix); }
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }
        }
    }

    #region LoadInitialSession

    [TestMethod]
    public void LoadInitialSession_EmptySession_ReturnsEmpty()
    {
        using var factory = NewFactory();
        SeedSession(factory);
        var repository = new SessionDataRepository(factory, Mock.Of<IAppLogger>());

        var result = repository.LoadInitialSession(SessionId);

        Assert.IsTrue(result.IsEmpty, "A session with no samples must report empty.");
        Assert.AreEqual(0, result.Channels.Count);
        Assert.AreEqual(0, result.Points.Count);
        Assert.IsNull(result.FirstTime);
        Assert.AreEqual(0, result.TotalSampleCount);
    }

    [TestMethod]
    public void LoadInitialSession_DiscoversChannels_LoadsInitialBatch_AndCountsSamples()
    {
        using var factory = NewFactory();
        SeedSession(factory);
        // Two channels across five timestamps (1ms apart). Both channels are present at the first
        // timestamp, so both must be discovered; delta-time is measured from that first timestamp.
        using (var context = factory.CreateDbContext())
        {
            for (var i = 0; i < 5; i++)
            {
                context.Samples.Add(MakeSample("AI0", i, BaseTick + i * 10_000));
                context.Samples.Add(MakeSample("AI1", i * 2, BaseTick + i * 10_000));
            }
            context.SaveChanges();
        }

        var repository = new SessionDataRepository(factory, Mock.Of<IAppLogger>());
        var result = repository.LoadInitialSession(SessionId);

        Assert.IsFalse(result.IsEmpty);
        CollectionAssert.AreEqual(
            new[] { "AI0", "AI1" },
            result.Channels.Select(c => c.ChannelName).ToArray(),
            "Both channels at the first timestamp must be discovered in natural order.");
        Assert.AreEqual(new DateTime(BaseTick), result.FirstTime);
        Assert.AreEqual(10, result.TotalSampleCount);

        var ai0 = result.Points[(Serial, "AI0")];
        Assert.AreEqual(5, ai0.Count);
        Assert.AreEqual(0.0, ai0[0].X, "First point sits at delta-time zero.");
        Assert.AreEqual(0.0, ai0[0].Y);
        Assert.AreEqual(4.0, ai0[4].X, "Fifth sample is 4ms after the first.");
        Assert.AreEqual(4.0, ai0[4].Y);
    }

    [TestMethod]
    public void LoadInitialSession_DuplicateChannelAtFirstTimestamp_DiscoversOnePerChannel_AndWarns()
    {
        using var factory = NewFactory();
        SeedSession(factory);
        // Issue #572 shape: the same channel sampled twice at the first timestamp.
        using (var context = factory.CreateDbContext())
        {
            context.Samples.Add(MakeSample("AI0", 1.0, BaseTick));
            context.Samples.Add(MakeSample("AI0", 1.0, BaseTick));
            context.Samples.Add(MakeSample("AI1", 2.0, BaseTick));
            context.SaveChanges();
        }
        var logger = new Mock<IAppLogger>();
        var repository = new SessionDataRepository(factory, logger.Object);

        var result = repository.LoadInitialSession(SessionId);

        CollectionAssert.AreEqual(
            new[] { "AI0", "AI1" },
            result.Channels.Select(c => c.ChannelName).ToArray(),
            "Duplicate (serial, channel) at the first timestamp must collapse to one entry per channel.");
        logger.Verify(
            l => l.Warning(It.Is<string>(m => m.Contains("duplicate"))),
            Times.Once(),
            "Discovery must warn once when the first timestamp has duplicate channel samples.");
    }

    #endregion

    #region LoadSampledData

    [TestMethod]
    public void LoadSampledData_CoversFullTimeRange_AndReturnsFirstTime()
    {
        using var factory = NewFactory();
        SeedSession(factory);
        using (var context = factory.CreateDbContext())
        {
            for (var i = 0; i < 5; i++)
            {
                context.Samples.Add(MakeSample("AI0", i, BaseTick + i * 10_000));
                context.Samples.Add(MakeSample("AI1", i * 2, BaseTick + i * 10_000));
            }
            context.SaveChanges();
        }
        var repository = new SessionDataRepository(factory, Mock.Of<IAppLogger>());
        var points = new Dictionary<(string deviceSerial, string channelName), List<DataPoint>>
        {
            [(Serial, "AI0")] = [],
            [(Serial, "AI1")] = []
        };

        var firstTime = repository.LoadSampledData(SessionId, channelCount: 2, points);

        Assert.AreEqual(new DateTime(BaseTick), firstTime);
        var ai0 = points[(Serial, "AI0")];
        Assert.AreEqual(5, ai0.Count, "Every distinct timestamp must be represented for the channel.");
        Assert.AreEqual(0.0, ai0[0].X);
        Assert.AreEqual(4.0, ai0[4].X, "The session tail (last timestamp) must always be included.");
    }

    [TestMethod]
    public void LoadSampledData_SingleTimestamp_ReturnsNull()
    {
        using var factory = NewFactory();
        SeedSession(factory);
        using (var context = factory.CreateDbContext())
        {
            // All samples share one timestamp → no usable time range.
            for (var i = 0; i < 10; i++)
            {
                context.Samples.Add(MakeSample("AI0", i, BaseTick));
            }
            context.SaveChanges();
        }
        var repository = new SessionDataRepository(factory, Mock.Of<IAppLogger>());
        var points = new Dictionary<(string deviceSerial, string channelName), List<DataPoint>>
        {
            [(Serial, "AI0")] = []
        };

        var firstTime = repository.LoadSampledData(SessionId, channelCount: 1, points);

        Assert.IsNull(firstTime, "A single-timestamp session has no usable range and returns null.");
    }

    #endregion

    #region LoadSessionDeviceFrequency

    [TestMethod]
    public void LoadSessionDeviceFrequency_ReturnsConfiguredFrequenciesPerDevice()
    {
        using var factory = NewFactory();
        SeedSession(factory);
        using (var context = factory.CreateDbContext())
        {
            context.SessionDeviceMetadata.Add(new SessionDeviceMetadata
            {
                LoggingSessionID = SessionId,
                DeviceSerialNo = Serial,
                DeviceName = "Nq1",
                SamplingFrequencyHz = 1000
            });
            context.SessionDeviceMetadata.Add(new SessionDeviceMetadata
            {
                LoggingSessionID = SessionId,
                DeviceSerialNo = SerialB,
                DeviceName = "Nq2",
                SamplingFrequencyHz = 500
            });
            context.SaveChanges();
        }
        var repository = new SessionDataRepository(factory, Mock.Of<IAppLogger>());

        var result = repository.LoadSessionDeviceFrequency(SessionId);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(1000, result[Serial]);
        Assert.AreEqual(500, result[SerialB]);
    }

    [TestMethod]
    public void LoadSessionDeviceFrequency_LegacySessionWithoutMetadata_ReturnsEmpty()
    {
        using var factory = NewFactory();
        SeedSession(factory);
        var repository = new SessionDataRepository(factory, Mock.Of<IAppLogger>());

        var result = repository.LoadSessionDeviceFrequency(SessionId);

        Assert.AreEqual(0, result.Count, "A session predating metadata persistence yields an empty map.");
    }

    #endregion

    #region DeleteSession

    [TestMethod]
    public void DeleteSession_RemovesSamplesMetadataAndSessionRow()
    {
        using var factory = NewFactory();
        SeedSession(factory);
        using (var context = factory.CreateDbContext())
        {
            for (var i = 0; i < 10; i++)
            {
                context.Samples.Add(MakeSample("AI0", i, BaseTick + i * 10_000));
            }
            context.SessionDeviceMetadata.Add(new SessionDeviceMetadata
            {
                LoggingSessionID = SessionId,
                DeviceSerialNo = Serial,
                DeviceName = "Nq1",
                SamplingFrequencyHz = 1000
            });
            context.SaveChanges();
        }
        var repository = new SessionDataRepository(factory, Mock.Of<IAppLogger>());

        repository.DeleteSession(new LoggingSession(SessionId, "to-delete"));

        using var verify = factory.CreateDbContext();
        Assert.AreEqual(0, verify.Samples.Count(s => s.LoggingSessionID == SessionId), "Samples must be gone.");
        Assert.AreEqual(0, verify.SessionDeviceMetadata.Count(m => m.LoggingSessionID == SessionId), "Metadata must be gone.");
        Assert.AreEqual(0, verify.Sessions.Count(s => s.ID == SessionId), "The session row must be gone.");
    }

    [TestMethod]
    public void DeleteSession_WhenDatabaseFails_ThrowsSoCallerCanKeepTheRow()
    {
        // The #592 bug fix: the pre-extraction DeleteLoggingSession logged and swallowed its
        // exceptions, so the session-list view model removed the bound row even when the delete
        // failed and the data still existed. The repository now rethrows so the caller's gating keeps
        // the row. The completion timing is still logged.
        var logger = new Mock<IAppLogger>();
        var repository = new SessionDataRepository(new ThrowingContextFactory(), logger.Object);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => repository.DeleteSession(new LoggingSession(SessionId, "fails")),
            "A failed delete must propagate so the caller can keep the bound row.");

        logger.Verify(l => l.Error(It.IsAny<Exception>(), It.Is<string>(m => m.Contains("DeleteLoggingSession"))), Times.Once());
        logger.Verify(l => l.Information(It.Is<string>(m => m.Contains("DeleteLoggingSession completed"))), Times.Once());
    }

    #endregion

    #region Helpers

    private static void SeedSession(IDbContextFactory<LoggingContext> factory)
    {
        using var context = factory.CreateDbContext();
        context.Sessions.Add(new LoggingSession(SessionId, "repo-test"));
        context.SaveChanges();
    }

    private static DataSample MakeSample(string channelName, double value, long timestampTicks)
    {
        return new DataSample
        {
            LoggingSessionID = SessionId,
            ChannelName = channelName,
            DeviceName = "Nq1",
            DeviceSerialNo = Serial,
            Color = "#FFD32F2F",
            Type = ChannelType.Analog,
            Value = value,
            TimestampTicks = timestampTicks
        };
    }

    private TempSqliteLoggingContextFactory NewFactory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"daqifi_sessionrepo_{Guid.NewGuid():N}.db");
        _tempDbPaths.Add(path);
        return new TempSqliteLoggingContextFactory(path);
    }

    /// <summary>
    /// A real file-backed SQLite <see cref="IDbContextFactory{TContext}"/> so queries run through the
    /// actual EF/ADO-to-SQLite translation, not a mock. Mirrors the production factory registration
    /// (App.xaml.cs) by suppressing the PendingModelChangesWarning. Files are removed by the test cleanup.
    /// </summary>
    private sealed class TempSqliteLoggingContextFactory : IDbContextFactory<LoggingContext>, IDisposable
    {
        private readonly DbContextOptions<LoggingContext> _options;

        public TempSqliteLoggingContextFactory(string dbPath)
        {
            _options = new DbContextOptionsBuilder<LoggingContext>()
                .UseSqlite($"Data Source={dbPath}")
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
                .Options;

            using var ctx = new LoggingContext(_options);
            ctx.Database.EnsureCreated();
        }

        public LoggingContext CreateDbContext() => new(_options);

        public void Dispose() => Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    /// <summary>
    /// Always throws from <see cref="CreateDbContext"/>, simulating a database that cannot be opened so
    /// <see cref="SessionDataRepository.DeleteSession"/>'s failure path can be exercised deterministically.
    /// </summary>
    private sealed class ThrowingContextFactory : IDbContextFactory<LoggingContext>
    {
        public LoggingContext CreateDbContext() =>
            throw new InvalidOperationException("Injected database failure.");
    }

    #endregion
}
