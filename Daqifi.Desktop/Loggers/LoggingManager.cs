using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Models;
using Daqifi.Desktop.Common.Loggers;
using System.IO;
using System.Xml.Linq;
using System.Collections.ObjectModel;
using Daqifi.Desktop.UpdateVersion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.ComponentModel;
using Daqifi.Desktop;

namespace Daqifi.Desktop.Logger;

public partial class LoggingManager : ObservableObject
{
    #region Private Variables
    private readonly AppLogger AppLogger = AppLogger.Instance;
    private static string ProfileAppDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + "\\DAQifi";
    private static readonly string ProfileSettingsXmlPath = ProfileAppDirectory + "\\DAQifiProfilesConfiguration.xml";
    private readonly IDbContextFactory<LoggingContext> _loggingContext;
    private bool _hasActiveApplicationSession;
    private bool _hasActiveApplicationSamples;
    #endregion

    #region Properties
    public List<ILogger> Loggers { get; }

    [ObservableProperty]
    private List<IChannel> _subscribedChannels = [];

    [ObservableProperty]
    private bool _active;

    [ObservableProperty]
    private LoggingMode _currentMode;

    [ObservableProperty]
    private LoggingSession _session;

    [ObservableProperty]
    private ObservableCollection<LoggingSession> _loggingSessions = [];
    #endregion

    partial void OnActiveChanged(bool oldValue, bool newValue)
    {
        if (!newValue && oldValue) // Was active, now stopping
        {
            if (_hasActiveApplicationSession && Session != null)
            {
                if (_hasActiveApplicationSamples)
                {
                    // Finalize the session by recording its sample count so the
                    // list view never has to count rows. One COUNT(*) per
                    // session, run at most once when the session ends.
                    PersistSessionSampleCount(Session);

                    if (!LoggingSessions.Any(s => s.ID == Session.ID))
                    {
                        LoggingSessions.Add(Session);
                    }
                }
                else
                {
                    DeleteLoggingSessionIfPresent(Session.ID);
                }
            }

            _hasActiveApplicationSession = false;
            _hasActiveApplicationSamples = false;
        }
        else if (newValue && !oldValue) // Was inactive, now starting
        {
            foreach (var channel in SubscribedChannels.ToList())
            {
                channel.OnChannelUpdated -= HandleChannelUpdate;
            }

            // Clear loggers
            foreach (var logger in Loggers)
            {
                if (logger is PlotLogger plotLogger)
                {
                    plotLogger.ClearPlot();
                }
                else if (logger is DatabaseLogger dbLogger)
                {
                    dbLogger.ClearPlot();
                }
            }

            if (CurrentMode != LoggingMode.Stream)
            {
                _hasActiveApplicationSession = false;
                _hasActiveApplicationSamples = false;
                return;
            }

            using (var context = _loggingContext.CreateDbContext())
            {
                var ids = context.Sessions.AsNoTracking().Select(s => s.ID).ToList();
                var newId = ids.Count > 0 ? ids.Max() + 1 : 0;
                var name = $"Session_{newId}";
                Session = new LoggingSession(newId, name);
                context.Sessions.Add(Session);

                // Capture per-device metadata (sampling frequency, name) for the
                // devices that own the subscribed channels, so the session UI can
                // display configuration without re-deriving it from sample data.
                // Failures here must not block session creation — the session is
                // still usable without metadata; the legend just won't show
                // sampling frequency.
                try
                {
                    foreach (var metadata in BuildDeviceMetadataForSession(newId))
                    {
                        context.SessionDeviceMetadata.Add(metadata);
                        // Also attach to the in-memory session so the list view
                        // and any header binding picks up FrequencyDisplay
                        // immediately without waiting for a reload.
                        Session.DeviceMetadata.Add(metadata);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error(ex, $"Failed to capture device metadata for session {newId}; continuing without it.");
                }

                context.SaveChanges();
            }

            _hasActiveApplicationSession = true;
            _hasActiveApplicationSamples = false;

            // Resubscribe channels
            foreach (var channel in SubscribedChannels.ToList())
            {
                channel.OnChannelUpdated += HandleChannelUpdate;
            }
        }
    }

    partial void OnCurrentModeChanged(LoggingMode value)
    {
        // If switching to SD card mode, stop streaming
        if (value == LoggingMode.SdCard && Active)
        {
            Active = false; // Setting Active will trigger OnActiveChanged
        }
    }

    #region Singleton Constructor / Initalization
    private static readonly LoggingManager instance = new();

    private LoggingManager()
    {
        Loggers = [];
        SubscribedChannels = [];
        _loggingContext = App.ServiceProvider.GetRequiredService<IDbContextFactory<LoggingContext>>();
    }

    public static LoggingManager Instance => instance;

    #endregion

    #region Profile Subscription

    #region Profile feature Properties

    [ObservableProperty] private ObservableCollection<Profile> _subscribedProfiles = [];

    [ObservableProperty]
    private Profile _selectedProfile;

    [ObservableProperty]
    private bool _flag;

    [ObservableProperty]
    private ObservableCollection<ProfileChannel> _selectedProfileChannels = [];

    [ObservableProperty]
    private ObservableCollection<ProfileDevice> _selectedProfileDevices = [];
    #endregion

    public void SubscribeProfile(Profile profile)
    {
        try
        {
            if (SubscribedProfiles.Contains(profile)) { return; }
            AddAndRemoveProfileXml(profile, true);
            SubscribedProfiles.Add(profile);
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Error Subscribe Profile");
        }
    }

    public void callPropertyChange()
    {
        OnPropertyChanged("SelectedProfileChannels");
    }

    public void UpdateProfileInXml(Profile profile)
    {
        try
        {
            if (!File.Exists(ProfileSettingsXmlPath))
            {
                AppLogger.Error("Profile settings XML file does not exist.");
                return;
            }
            var doc = XDocument.Load(ProfileSettingsXmlPath);
            var profileToUpdate = doc.Descendants("Profile")
                .FirstOrDefault(p => (Guid)p.Element("ProfileID") == profile.ProfileId);
            if (profileToUpdate != null)
            {
                profileToUpdate.Element("Name")?.SetValue(profile.Name);
                profileToUpdate.Element("CreatedOn")?.SetValue(profile.CreatedOn);
                var devicesElement = profileToUpdate.Element("Devices");
                devicesElement?.RemoveAll();
                foreach (var device in profile.Devices)
                {
                    var deviceElement = new XElement("Device",
                        new XElement("DeviceName", device.DeviceName),
                        new XElement("DevicePartNumber", device.DevicePartName),
                        new XElement("MACAddress", device.MacAddress),
                        new XElement("DeviceSerialNo", device.DeviceSerialNo),
                        new XElement("SamplingFrequency", device.SamplingFrequency)
                    );

                    var activeChannels = device.Channels
                        .Where(channel => channel.IsChannelActive)
                        .Select(channel => new XElement("Channel",
                            new XElement("Name", channel.Name),
                            new XElement("Type", channel.Type),
                            new XElement("IsActive", channel.IsChannelActive)
                        )).ToList();

                    if (activeChannels.Any())
                    {
                        deviceElement.Add(new XElement("Channels", activeChannels));
                    }

                    devicesElement?.Add(deviceElement);
                }
                doc.Save(ProfileSettingsXmlPath);
            }
            else
            {
                AppLogger.Error($"Profile with ID {profile.ProfileId} not found for updating.");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Error updating profile in XML");
        }
    }

    public void AddAndRemoveProfileXml(Profile profile, bool AddProfileFlag)
    {
        try
        {
            if (!Directory.Exists(ProfileAppDirectory))
            {
                Directory.CreateDirectory(ProfileAppDirectory);
            }
            if (!File.Exists(ProfileSettingsXmlPath))
            {
                var newDoc = new XDocument(
                    new XElement("Profiles")
                );
                newDoc.Save(ProfileSettingsXmlPath);
            }
            var doc = XDocument.Load(ProfileSettingsXmlPath);
            if (profile != null)
            {
                if (AddProfileFlag)
                {
                    var newProfile = new XElement("Profile",
                        new XElement("Name", profile.Name),
                        new XElement("ProfileID", profile.ProfileId),
                        new XElement("CreatedOn", profile.CreatedOn),
                        new XElement("Devices",
                            from device in profile.Devices
                            select new XElement("Device",
                                new XElement("DeviceName", device.DeviceName),
                                new XElement("DevicePartNumber", device.DevicePartName),
                                new XElement("MACAddress", device.MacAddress),
                                new XElement("DeviceSerialNo", device.DeviceSerialNo),
                                new XElement("Channels",
                                    from channel in device.Channels
                                    where channel.IsChannelActive
                                    select new XElement("Channel",
                                        new XElement("Name", channel.Name),
                                        new XElement("Type", channel.Type),
                                        new XElement("IsActive", channel.IsChannelActive)
                                    )
                                ),
                                new XElement("SamplingFrequency", device.SamplingFrequency)
                            )
                        )
                    );

                    doc.Root?.Add(newProfile);
                }
                else
                {
                    var profileToRemove = doc.Descendants("Profile")
                        .FirstOrDefault(p => (Guid)p.Element("ProfileID") == profile.ProfileId);
                    profileToRemove?.Remove();
                    SubscribedProfiles.Remove(profile);
                }
            }
            doc.Save(ProfileSettingsXmlPath);
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Error Setting Selected profile");
        }
    }

    public List<Profile> LoadProfilesFromXml()
    {
        SubscribedProfiles.Clear();

        try
        {
            // Check if the profile settings file exists
            if (File.Exists(ProfileSettingsXmlPath))
            {
                // Load the XML document
                var doc = XDocument.Load(ProfileSettingsXmlPath);

                // Parse the XML and retrieve the profiles
                var loadedProfiles = doc.Descendants("Profile").Select(p => new Profile
                {
                    Name = (string)p.Element("Name"),
                    ProfileId = (Guid)p.Element("ProfileID"),
                    CreatedOn = (DateTime)p.Element("CreatedOn"),
                    Devices = new ObservableCollection<ProfileDevice>(p.Element("Devices")?.Elements("Device").Select(d => new ProfileDevice
                    {
                        DeviceName = (string)d.Element("DeviceName"),
                        DevicePartName = (string)d.Element("DevicePartNumber"),
                        MacAddress = (string)d.Element("MACAddress"),
                        DeviceSerialNo = (string)d.Element("DeviceSerialNo"),
                        SamplingFrequency = (int)d.Element("SamplingFrequency"),
                        Channels = d.Element("Channels")?.Elements("Channel").Select(c => new ProfileChannel
                        {
                            Name = (string)c.Element("Name"),
                            Type = (string)c.Element("Type"),
                            IsChannelActive = (bool)c.Element("IsActive"),
                            SerialNo = (string)d.Element("DeviceSerialNo")
                        }).ToList()
                    }).ToList())
                }).ToList();

                // Add each profile to the existing collection
                foreach (var profile in loadedProfiles)
                {
                    SubscribedProfiles.Add(profile);
                }

                return loadedProfiles;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Error Loading Profiles from XML");
        }

        return SubscribedProfiles.ToList();
    }

    public void UnsubscribeProfile(Profile profile)
    {
        try
        {
            var subscribedProfile = SubscribedProfiles.FirstOrDefault(x => x.ProfileId == profile.ProfileId);
            if (subscribedProfile == null)
            {
                return;
            }

            AddAndRemoveProfileXml(subscribedProfile, false);
            ClearChannelList();
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Error Unsubscribe Profile");
        }
    }
    #endregion

    #region Channel Subscription
    public void Subscribe(IChannel channel)
    {
        if (SubscribedChannels.Any(x => x.DeviceSerialNo == channel.DeviceSerialNo && x.Name == channel.Name))
        {
            return;
        }

        channel.IsActive = true;

        // Only attach streaming handlers if in Stream mode
        if (CurrentMode == LoggingMode.Stream)
        {
            channel.OnChannelUpdated += HandleChannelUpdate;
        }

        SubscribedChannels.Add(channel);
        OnPropertyChanged(nameof(SubscribedChannels));
    }

    public void Unsubscribe(IChannel channel)
    {
        var subscribedChannel = SubscribedChannels
            .FirstOrDefault(x => x.DeviceSerialNo == channel.DeviceSerialNo && x.Name == channel.Name && x.IsActive);

        if (subscribedChannel == null)
        {
            return;
        }

        subscribedChannel.IsActive = false;

        // Remove event handler if it's attached
        subscribedChannel.OnChannelUpdated -= HandleChannelUpdate;

        SubscribedChannels.Remove(subscribedChannel);
        OnPropertyChanged(nameof(SubscribedChannels));
    }
    #endregion

    /// <summary>
    /// Builds <see cref="SessionDeviceMetadata"/> rows for every device that has
    /// at least one subscribed channel at the start of a logging session.
    /// </summary>
    /// <param name="sessionId">The id of the session being created.</param>
    /// <returns>One materialized metadata entry per distinct device serial number.</returns>
    /// <remarks>
    /// Snapshots <see cref="SubscribedChannels"/> and
    /// <see cref="ConnectionManager.ConnectedDevices"/> up front so the result is
    /// computed against stable copies — these collections can otherwise mutate
    /// concurrently (e.g., from device-connection background threads) and throw
    /// during enumeration. Returns a fully materialized list rather than yielding
    /// lazily so the caller is never iterating against the live collections.
    /// Uses <see cref="StringComparer.Ordinal"/> for serial-number deduplication
    /// because device serials are opaque identifiers, not culture-sensitive text.
    /// </remarks>
    private List<SessionDeviceMetadata> BuildDeviceMetadataForSession(int sessionId)
    {
        var result = new List<SessionDeviceMetadata>();

        // Snapshot inputs before iterating to avoid concurrent-modification
        // exceptions on the underlying mutable lists.
        var subscribedChannelsSnapshot = SubscribedChannels?.ToList() ?? new List<IChannel>();
        var connectedDevicesSnapshot = ConnectionManager.Instance.ConnectedDevices?.ToList()
            ?? new List<IStreamingDevice>();

        var subscribedSerials = subscribedChannelsSnapshot
            .Select(c => c.DeviceSerialNo)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToHashSet(StringComparer.Ordinal);

        if (subscribedSerials.Count == 0)
        {
            return result;
        }

        var emittedSerials = new HashSet<string>(StringComparer.Ordinal);

        foreach (var device in connectedDevicesSnapshot)
        {
            if (device == null || string.IsNullOrEmpty(device.DeviceSerialNo))
            {
                continue;
            }

            if (!subscribedSerials.Contains(device.DeviceSerialNo))
            {
                continue;
            }

            // Defensive: ConnectedDevices can briefly contain two entries with the
            // same serial (e.g., during USB re-enumeration). The composite primary
            // key on (LoggingSessionID, DeviceSerialNo) would otherwise reject the
            // second row and abort the session start.
            if (!emittedSerials.Add(device.DeviceSerialNo))
            {
                continue;
            }

            result.Add(new SessionDeviceMetadata
            {
                LoggingSessionID = sessionId,
                DeviceSerialNo = device.DeviceSerialNo,
                DeviceName = device.Name ?? string.Empty,
                SamplingFrequencyHz = device.StreamingFrequency
            });
        }

        return result;
    }

    public void HandleDeviceMessage(object sender, DeviceMessage sample)
    {
        if (!Active || CurrentMode != LoggingMode.Stream || !_hasActiveApplicationSession)
        {
            return;
        }

        sample.LoggingSessionID = Session.ID;

        //Log channel value to whatever loggers are being managed
        foreach (var logger in Loggers)
        {
            logger.Log(sample);
        }
    }

    public void HandleChannelUpdate(object sender, DataSample sample)
    {
        if (!Active || CurrentMode != LoggingMode.Stream || !_hasActiveApplicationSession)
        {
            return;
        }

        if (sample == null)
        {
            return;
        }

        _hasActiveApplicationSamples = true;
        sample.LoggingSessionID = Session.ID;

        // Log channel value to whatever loggers are being managed
        foreach (var logger in Loggers)
        {
            logger.Log(sample);
        }
    }

    public void AddLogger(ILogger logger)
    {
        Loggers.Add(logger);
    }

    public async Task CheckApplicationVersion(VersionNotification versionNotification)
    {
        await versionNotification.CheckForUpdatesAsync();
        OnPropertyChanged("NotificationCount");
        OnPropertyChanged("VersionNumber");
    }

    public ObservableCollection<LoggingSession> LoadPersistedLoggingSessions()
    {
        using var context = _loggingContext.CreateDbContext();

        var emptySessions = context.Sessions
            .Where(session => !context.Samples.Any(sample => sample.LoggingSessionID == session.ID))
            .ToList();

        if (emptySessions.Count > 0)
        {
            context.Sessions.RemoveRange(emptySessions);
            context.SaveChanges();
        }

        // One-time backfill: any session created before the SampleCount column
        // existed (or whose count was lost mid-run) gets counted in a single
        // GROUP BY query, then UPDATEd. Subsequent loads pay no cost.
        BackfillMissingSampleCounts(context);

        return new ObservableCollection<LoggingSession>(
            context.Sessions
                .AsNoTracking()
                .Include(session => session.DeviceMetadata)
                .Where(session => context.Samples.Any(sample => sample.LoggingSessionID == session.ID))
                .OrderBy(session => session.ID)
                .ToList());
    }

    /// <summary>
    /// Persists <see cref="LoggingSession.SampleCount"/> for the given session
    /// by running a single COUNT against the Samples table. Called when a
    /// session ends so the list view can surface the count without a query.
    /// </summary>
    private void PersistSessionSampleCount(LoggingSession session)
    {
        try
        {
            using var context = _loggingContext.CreateDbContext();
            var count = context.Samples.LongCount(s => s.LoggingSessionID == session.ID);

            var tracked = context.Sessions.FirstOrDefault(s => s.ID == session.ID);
            if (tracked != null)
            {
                tracked.SampleCount = count;
                context.SaveChanges();
            }
            // Also surface immediately on the in-memory entity bound by the UI.
            session.SampleCount = count;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"Failed to persist sample count for session {session?.ID}");
        }
    }

    /// <summary>
    /// Lazily backfills <see cref="LoggingSession.SampleCount"/> for any
    /// session whose count is null. Uses a single GROUP BY query covering all
    /// missing sessions at once, then issues UPDATEs in one transaction. Runs
    /// at most once per session over the lifetime of the database.
    /// </summary>
    private void BackfillMissingSampleCounts(LoggingContext context)
    {
        try
        {
            var sessionsMissingCount = context.Sessions
                .Where(s => s.SampleCount == null)
                .Select(s => s.ID)
                .ToList();

            if (sessionsMissingCount.Count == 0) { return; }

            var counts = context.Samples
                .Where(sample => sessionsMissingCount.Contains(sample.LoggingSessionID))
                .GroupBy(sample => sample.LoggingSessionID)
                .Select(g => new { SessionId = g.Key, Count = g.LongCount() })
                .ToList();

            var trackedSessions = context.Sessions
                .Where(s => sessionsMissingCount.Contains(s.ID))
                .ToList();

            foreach (var tracked in trackedSessions)
            {
                var match = counts.FirstOrDefault(c => c.SessionId == tracked.ID);
                tracked.SampleCount = match?.Count ?? 0;
            }

            context.SaveChanges();
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to backfill SampleCount for legacy sessions");
        }
    }

    /// <summary>
    /// Reloads persisted logging sessions from storage and repopulates the current collection in place.
    /// </summary>
    public void ReloadPersistedLoggingSessions()
    {
        var persistedSessions = LoadPersistedLoggingSessions();
        LoggingSessions.Clear();

        foreach (var session in persistedSessions)
        {
            LoggingSessions.Add(session);
        }
    }

    private void DeleteLoggingSessionIfPresent(int sessionId)
    {
        var existingSession = LoggingSessions.FirstOrDefault(session => session.ID == sessionId);
        if (existingSession != null)
        {
            LoggingSessions.Remove(existingSession);
        }

        try
        {
            using var context = _loggingContext.CreateDbContext();
            var connection = context.Database.GetDbConnection();
            connection.Open();

            using var transaction = connection.BeginTransaction();

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM Samples WHERE LoggingSessionID = @id";
                var param = cmd.CreateParameter();
                param.ParameterName = "@id";
                param.Value = sessionId;
                cmd.Parameters.Add(param);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM SessionDeviceMetadata WHERE LoggingSessionID = @id";
                var param = cmd.CreateParameter();
                param.ParameterName = "@id";
                param.Value = sessionId;
                cmd.Parameters.Add(param);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM Sessions WHERE ID = @id";
                var param = cmd.CreateParameter();
                param.ParameterName = "@id";
                param.Value = sessionId;
                cmd.Parameters.Add(param);
                cmd.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"Failed to delete logging session {sessionId}");
        }
    }

    private void ClearChannelList()
    {
        foreach (var channel in SubscribedChannels)
        {
            channel.IsActive = false;
            channel.OnChannelUpdated -= HandleChannelUpdate;
        }
        SubscribedChannels.Clear();
        OnPropertyChanged(nameof(SubscribedChannels));
    }
}
