using Daqifi.Desktop.Logger;
using Microsoft.EntityFrameworkCore;

namespace Daqifi.Desktop.Test.Loggers;

[TestClass]
public class SessionDeviceMetadataTests
{
    private string _dbPath;
    private DbContextOptions<LoggingContext> _options;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"daqifi_metadata_{Guid.NewGuid():N}.db");
        _options = new DbContextOptionsBuilder<LoggingContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        using var ctx = new LoggingContext(_options);
        ctx.Database.EnsureCreated();
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath)) { File.Delete(_dbPath); }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [TestMethod]
    public void DeletingSession_CascadesToSessionDeviceMetadata()
    {
        // Arrange — create a session with two device metadata rows
        using (var ctx = new LoggingContext(_options))
        {
            var session = new LoggingSession { ID = 1, Name = "Multi-Device Session" };
            ctx.Sessions.Add(session);
            ctx.SessionDeviceMetadata.Add(new SessionDeviceMetadata
            {
                LoggingSessionID = 1,
                DeviceSerialNo = "DAQ-001",
                DeviceName = "Nyquist 1",
                SamplingFrequencyHz = 1000
            });
            ctx.SessionDeviceMetadata.Add(new SessionDeviceMetadata
            {
                LoggingSessionID = 1,
                DeviceSerialNo = "DAQ-002",
                DeviceName = "Nyquist 2",
                SamplingFrequencyHz = 100
            });
            ctx.SaveChanges();
        }

        // Act — delete the parent session via EF, which should cascade
        using (var ctx = new LoggingContext(_options))
        {
            var session = ctx.Sessions.Single(s => s.ID == 1);
            ctx.Sessions.Remove(session);
            ctx.SaveChanges();
        }

        // Assert — no metadata rows remain
        using (var ctx = new LoggingContext(_options))
        {
            Assert.AreEqual(0, ctx.SessionDeviceMetadata.Count(m => m.LoggingSessionID == 1));
            Assert.AreEqual(0, ctx.Sessions.Count(s => s.ID == 1));
        }
    }

    [TestMethod]
    public void SessionDeviceMetadata_SupportsMultipleDevicesPerSession()
    {
        // Arrange
        using (var ctx = new LoggingContext(_options))
        {
            var session = new LoggingSession { ID = 42, Name = "Multi-Device Session" };
            ctx.Sessions.Add(session);
            ctx.SessionDeviceMetadata.Add(new SessionDeviceMetadata
            {
                LoggingSessionID = 42,
                DeviceSerialNo = "DAQ-A",
                DeviceName = "Device A",
                SamplingFrequencyHz = 1000
            });
            ctx.SessionDeviceMetadata.Add(new SessionDeviceMetadata
            {
                LoggingSessionID = 42,
                DeviceSerialNo = "DAQ-B",
                DeviceName = "Device B",
                SamplingFrequencyHz = 100
            });
            ctx.SaveChanges();
        }

        // Assert — both rows persist with their distinct frequencies
        using (var ctx = new LoggingContext(_options))
        {
            var rows = ctx.SessionDeviceMetadata
                .Where(m => m.LoggingSessionID == 42)
                .OrderBy(m => m.DeviceSerialNo)
                .ToList();

            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual("DAQ-A", rows[0].DeviceSerialNo);
            Assert.AreEqual(1000, rows[0].SamplingFrequencyHz);
            Assert.AreEqual("DAQ-B", rows[1].DeviceSerialNo);
            Assert.AreEqual(100, rows[1].SamplingFrequencyHz);
        }
    }

    [TestMethod]
    public void SessionDeviceMetadata_CompositeKey_PreventsDuplicates()
    {
        using (var ctx = new LoggingContext(_options))
        {
            ctx.Sessions.Add(new LoggingSession { ID = 5, Name = "Dup Test" });
            ctx.SessionDeviceMetadata.Add(new SessionDeviceMetadata
            {
                LoggingSessionID = 5,
                DeviceSerialNo = "DAQ-DUP",
                DeviceName = "Original",
                SamplingFrequencyHz = 100
            });
            ctx.SaveChanges();
        }

        // Inserting a row with the same (sessionId, serial) should fail
        Assert.ThrowsExactly<DbUpdateException>(() =>
        {
            using var ctx = new LoggingContext(_options);
            ctx.SessionDeviceMetadata.Add(new SessionDeviceMetadata
            {
                LoggingSessionID = 5,
                DeviceSerialNo = "DAQ-DUP",
                DeviceName = "Duplicate",
                SamplingFrequencyHz = 200
            });
            ctx.SaveChanges();
        });
    }
}
