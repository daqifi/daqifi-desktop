using Daqifi.Desktop.Channel;
using System.Collections.Generic;
using System.Linq;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Models;
using System;
using Daqifi.Desktop.Common.Loggers;
using System.IO;
using System.Xml.Linq;
using System.Collections.ObjectModel;

namespace Daqifi.Desktop.Logger
{
    public class LoggingManager : ObservableObject
    {
        #region Private Variables
        private List<IChannel> _subscribedChannels;
        private List<LoggingSession> _loggingSessions;
        private List<Profile> _subscribedProfiles;
        private bool _active;
        public AppLogger AppLogger = AppLogger.Instance;
        private static string ProfileAppDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + "\\DAQifi";
        private static readonly string ProfileSettingsXmlPath = ProfileAppDirectory + "\\DAQifiProfilesConfiguration.xml";
        #endregion

        #region Properties
        public List<ILogger> Loggers { get; }

        public List<IChannel> SubscribedChannels
        {
            get => _subscribedChannels;
            private set
            {
                _subscribedChannels = value;
                NotifyPropertyChanged("SubscribedChannels");
            }
        }

        public bool Active
        {
            get => _active;
            set
            {
                //Set up the current logging session
                if (!_active)
                {
                    //Check if database has a previouse section
                    using (var context = new LoggingContext())
                    {
                        var ids = (from s in context.Sessions.AsNoTracking() select s.ID).ToList();
                        var newId = 0;
                        if (ids.Count > 0) newId = ids.Max() + 1;
                        Session = new LoggingSession(newId);
                        context.Sessions.Add(Session);
                        context.SaveChanges();
                    }
                }
                else
                {
                    //Logging session is ending
                    if (LoggingSessions == null) LoggingSessions = new List<LoggingSession>();
                    LoggingSessions.Add(Session);
                    NotifyPropertyChanged("LoggingSessions");
                }

                _active = value;
            }
        }

        public LoggingSession Session { get; private set; }

        public List<LoggingSession> LoggingSessions
        {
            get => _loggingSessions;
            set
            {
                _loggingSessions = value;
                NotifyPropertyChanged("LoggingSessions");
            }
        }
        #endregion

        #region Singleton Constructor / Initalization
        private static readonly LoggingManager instance = new LoggingManager();

        private LoggingManager()
        {
            Loggers = new List<ILogger>();
            SubscribedChannels = new List<IChannel>();
            SubscribedProfiles = new List<Profile>();
        }

        public static LoggingManager Instance => instance;

        #endregion

        #region Profile Subscription

        #region Profile feature Properties
        public List<Profile> SubscribedProfiles
        {
            get => _subscribedProfiles;
            private set
            {
                _subscribedProfiles = value;
                NotifyPropertyChanged("SubscribedProfiles");
            }
        }
        private Profile _selectedProfile;
        public Profile SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                _selectedProfile = value;
                NotifyPropertyChanged("SelectedProfile");

            }
        }
        private bool _flag;
        public bool Flag
        {
            get => _flag;
            set
            {
                _flag = value;
                NotifyPropertyChanged("Flag");

            }
        }
        private ObservableCollection<ProfileChannel> _SelectedProfileChannels = new ObservableCollection<ProfileChannel>();
        public ObservableCollection<ProfileChannel> SelectedProfileChannels
        {
            get => _SelectedProfileChannels;
            set
            {
                _SelectedProfileChannels = value;
                NotifyPropertyChanged("SelectedProfileChannels");
            }
        }
        private ObservableCollection<ProfileDevice> _SelectedProfileDevices = new ObservableCollection<ProfileDevice>();
        public ObservableCollection<ProfileDevice> SelectedProfileDevices
        {
            get => _SelectedProfileDevices;
            set
            {
                _SelectedProfileDevices = value;
                NotifyPropertyChanged("SelectedProfileChannels");
            }
        }
        #endregion


        public void SubscribeProfile(Profile profile)
        {
            try
            {
                if (SubscribedProfiles.Contains(profile)) return;
                AddAndRemoveProfileXml(profile, true);
                SubscribedProfiles.Add(profile);
                NotifyPropertyChanged("SubscribedProfiles");
            }
            catch (Exception ex)
            {

                AppLogger.Error(ex, $"Error Subscribe Profile");
            }
            
        }
        public void callPropertyChange()
        {
            NotifyPropertyChanged("SelectedProfileChannels");
            
        }
        public void UpdateProfileInXml(Profile profile)
        {
            try
            {
                // Ensure the file exists before attempting to update
                if (!File.Exists(ProfileSettingsXmlPath))
                {
                    AppLogger.Error("Profile settings XML file does not exist.");
                    return;
                }

                // Load the XML document
                XDocument doc = XDocument.Load(ProfileSettingsXmlPath);

                // Find the profile by ProfileId
                var profileToUpdate = doc.Descendants("Profile")
                                         .FirstOrDefault(p => (Guid)p.Element("ProfileID") == profile.ProfileId);

                if (profileToUpdate != null)
                {
                    // Update the profile fields
                    profileToUpdate.Element("Name")?.SetValue(profile.Name);
                    profileToUpdate.Element("CreatedOn")?.SetValue(profile.CreatedOn);

                    // Update devices
                    var devicesElement = profileToUpdate.Element("Devices");
                    devicesElement?.RemoveAll();  // Clear existing devices

                    foreach (var device in profile.Devices)
                    {
                        XElement deviceElement = new XElement("Device",
                            new XElement("DeviceName", device.DeviceName),
                            new XElement("DevicePartNumber", device.DevicePartName),
                            new XElement("MACAddress", device.MACAddress),
                            new XElement("DeviceSerialNo", device.DeviceSerialNo),
                            new XElement("SamplingFrequency", device.SamplingFrequency),
                            new XElement("Channels",
                                from channel in device.Channels
                                select new XElement("Channel",
                                    new XElement("Name", channel.Name),
                                    new XElement("Type", channel.Type),
                                    new XElement("IsActive", channel.IsChannelActive)
                                )
                            )
                        );

                        devicesElement?.Add(deviceElement);
                    }

                    // Save the updated document
                    doc.Save(ProfileSettingsXmlPath);
                    //AppLogger.Info($"Profile '{profile.Name}' has been updated successfully.");
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

        private void AddAndRemoveProfileXml(Profile profile, bool AddProfileFlag)
        {
            try
            {
                // Ensure the directory exists
                if (!Directory.Exists(ProfileAppDirectory))
                {
                    Directory.CreateDirectory(ProfileAppDirectory);
                }

                // Ensure the file exists, create an empty structure if it doesn't
                if (!File.Exists(ProfileSettingsXmlPath))
                {
                    // Create the initial structure if the file doesn't exist
                    XDocument newDoc = new XDocument(
                        new XElement("Profiles")
                    );
                    newDoc.Save(ProfileSettingsXmlPath);
                }

                // Load the XML document
                XDocument doc = XDocument.Load(ProfileSettingsXmlPath);

                if (AddProfileFlag)
                {
                    // Add the profile information to the XML file
                    XElement newProfile = new XElement("Profile",
                        new XElement("Name", profile.Name),
                        new XElement("ProfileID", profile.ProfileId),
                        new XElement("CreatedOn", profile.CreatedOn),
                        new XElement("Devices",
                            from device in profile.Devices
                            select new XElement("Device",
                                new XElement("DeviceName", device.DeviceName),
                                new XElement("DevicePartNumber", device.DevicePartName),
                                new XElement("MACAddress", device.MACAddress),
                                new XElement("DeviceSerialNo", device.DeviceSerialNo),
                                new XElement("Channels",
                                    from channel in device.Channels
                                    select new XElement("Channel",
                                        new XElement("Name", channel.Name),
                                        new XElement("Type",channel.Type),   
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
                    // Logic for removing the profile (e.g., by profile name)
                    var profileToRemove = doc.Descendants("Profile")
                                             .FirstOrDefault(p => (Guid)p.Element("ProfileID") == profile.ProfileId);
                    profileToRemove?.Remove();
                    SubscribedProfiles.Remove(profile);
                }

                // Save the updated document
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
                            MACAddress = (string)d.Element("MACAddress"),
                            DeviceSerialNo = (string)d.Element("DeviceSerialNo"),
                            SamplingFrequency = (int)d.Element("SamplingFrequency"),
                            Channels = d.Element("Channels")?.Elements("Channel").Select(c => new ProfileChannel
                            {
                                Name = (string)c.Element("Name"),
                                Type = (string)c.Element("Type"),
                                IsChannelActive=(bool)c.Element("IsActive")
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
            //if (SubscribedProfiles != null && SubscribedProfiles.Count > 0)
            //    NotifyPropertyChanged("SubscribedProfiles");
            return profiles;
        }

        public void UnsubscribeProfile(Profile profile)
        {
            try
            {
                // Don't unsubscribe a channel that isn't subscribed
                if (!SubscribedProfiles.Where(x => x.ProfileId == profile.ProfileId).Any()) return;
                AddAndRemoveProfileXml(profile, false);
                SubscribedProfiles.Remove(profile);
                NotifyPropertyChanged("SubscribedProfiles");
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
            // Don't subscribe a channel that is already subscribed
            if (SubscribedChannels.Contains(channel)) return;

            channel.IsActive = true;
            channel.OnChannelUpdated += HandleChannelUpdate;
            SubscribedChannels.Add(channel);
            NotifyPropertyChanged("SubscribedChannels");
        }

        public void Unsubscribe(IChannel channel)
        {
            // Don't unsubscribe a channel that isn't subscribed
            if (!SubscribedChannels.Contains(channel)) return;

            channel.IsActive = false;
            channel.OnChannelUpdated -= HandleChannelUpdate;
            SubscribedChannels.Remove(channel);
            NotifyPropertyChanged("SubscribedChannels");
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
    }
}