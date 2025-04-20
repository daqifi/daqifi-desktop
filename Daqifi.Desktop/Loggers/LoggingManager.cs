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

namespace Daqifi.Desktop.Logger;

public partial class LoggingManager : ObservableObject
{
    #region Private Variables
    private readonly AppLogger AppLogger = AppLogger.Instance;
    private static string ProfileAppDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + "\\DAQifi";
    private static readonly string ProfileSettingsXmlPath = ProfileAppDirectory + "\\DAQifiProfilesConfiguration.xml";
    private readonly IDbContextFactory<LoggingContext> _loggingContext;
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
            if (Session != null)
            {
                LoggingSessions.Add(Session);
            }
        }
        else if (newValue && !oldValue) // Was inactive, now starting
        {
            using (var context = _loggingContext.CreateDbContext())
            {
                var ids = context.Sessions.AsNoTracking().Select(s => s.ID).ToList();
                var newId = ids.Count > 0 ? ids.Max() + 1 : 0;
                var name = $"Session_{newId}";
                Session = new LoggingSession(newId, name);
                context.Sessions.Add(Session);
                context.SaveChanges();
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

            // Resubscribe channels
            foreach (var channel in SubscribedChannels.ToList())
            {
                channel.OnChannelUpdated -= HandleChannelUpdate;
                if (CurrentMode == LoggingMode.Stream)
                {
                    channel.OnChannelUpdated += HandleChannelUpdate;
                }
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
    private static readonly LoggingManager instance = new LoggingManager();

    private LoggingManager()
    {
        Loggers = new List<ILogger>();
        SubscribedChannels = new List<IChannel>();
        SubscribedProfiles = new List<Profile>();
        _loggingContext = App.ServiceProvider.GetRequiredService<IDbContextFactory<LoggingContext>>();
    }

    public static LoggingManager Instance => instance;

    #endregion

    #region Profile Subscription

    #region Profile feature Properties
    [ObservableProperty]
    private List<Profile> _subscribedProfiles = new List<Profile>();

    [ObservableProperty]
    private Profile _selectedProfile;

    [ObservableProperty]
    private bool _flag;

    [ObservableProperty]
    private ObservableCollection<ProfileChannel> _selectedProfileChannels = new ObservableCollection<ProfileChannel>();

    [ObservableProperty]
    private ObservableCollection<ProfileDevice> _selectedProfileDevices = new ObservableCollection<ProfileDevice>();
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
            AppLogger.Error(ex, $"Error Subscribe Profile");
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
            XDocument doc = XDocument.Load(ProfileSettingsXmlPath);
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
                    XElement deviceElement = new XElement("Device",
                        new XElement("DeviceName", device.DeviceName),
                        new XElement("DevicePartNumber", device.DevicePartName),
                        new XElement("MACAddress", device.MacAddress),
                        new XElement("DeviceSerialNo", device.DeviceSerialNo),
                        new XElement("SamplingFrequency", device.SamplingFrequency)
                    );

                    var activeChannels = device.Channels
                        .Where(channel => channel.IsChannelActive && channel.SerialNo == device.DeviceSerialNo)
                        .Select(channel => new XElement("Channel",
                            new XElement("Name", channel.Name),
                            new XElement("Type", channel.Type),
                            new XElement("IsActive", channel.IsChannelActive),
                            new XElement("SerialNo", device.DeviceSerialNo)
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
                XDocument newDoc = new XDocument(
                    new XElement("Profiles")
                );
                newDoc.Save(ProfileSettingsXmlPath);
            }
            XDocument doc = XDocument.Load(ProfileSettingsXmlPath);
            if (profile != null)
            {
                if (AddProfileFlag)
                {
                    XElement newProfile = new XElement("Profile",
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
                                    where channel.IsChannelActive && channel.SerialNo == device.DeviceSerialNo
                                    select new XElement("Channel",
                                        new XElement("Name", channel.Name),
                                        new XElement("Type", channel.Type),
                                        new XElement("IsActive", channel.IsChannelActive),
                                        new XElement("SerialNo", device.DeviceSerialNo)
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
        var profiles = new List<Profile>();

        try
        {
            // Check if the profile settings file exists
            if (File.Exists(ProfileSettingsXmlPath))
            {
                // Load the XML document
                XDocument doc = XDocument.Load(ProfileSettingsXmlPath);

                // Parse the XML and retrieve the profiles
                profiles = doc.Descendants("Profile").Select(p => new Profile
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
                            SerialNo = (string)c.Element("DeviceSerialNo")
                        }).ToList()
                    }).ToList())
                }).ToList();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Error Loading Profiles from XML");
        }
        SubscribedProfiles = profiles;
        return profiles;
    }

    public void UnsubscribeProfile(Profile profile)
    {
        try
        {
            var index = SubscribedProfiles
                .FindIndex(x => x.ProfileId == profile.ProfileId);
            if (index == -1)
            {
                return;
            }
            var subscribedProfile = SubscribedProfiles[index];
            AddAndRemoveProfileXml(subscribedProfile, false);
            ClearChannelList();
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"Error Unsubscribe Profile");
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
        var index = SubscribedChannels
            .FindIndex(x => x.DeviceSerialNo == channel.DeviceSerialNo && x.Name == channel.Name && x.IsActive);
            
        if (index == -1)
        {
            return;
        }
            
        var subscribedChannel = SubscribedChannels[index];
        subscribedChannel.IsActive = false;
            
        // Remove event handler if it's attached
        subscribedChannel.OnChannelUpdated -= HandleChannelUpdate;
            
        SubscribedChannels.RemoveAt(index);
        OnPropertyChanged(nameof(SubscribedChannels));
    }
    #endregion

    public void HandleDeviceMessage(object sender, DeviceMessage sample)
    {
        if (!Active)
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
        if (!Active)
        {
            return;
        }

        if (sample == null)
        {
            return;
        }

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

    private void ClearChannelList()
    {
        foreach (var channel in SubscribedChannels)
        {
            channel.IsActive = false;
            channel.OnChannelUpdated -= HandleChannelUpdate;
        }
        SubscribedChannels.Clear();
        OnPropertyChanged("SubscribedChannels");
    }
}