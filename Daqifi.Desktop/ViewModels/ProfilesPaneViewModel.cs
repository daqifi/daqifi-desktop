using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Helpers;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.Models;

namespace Daqifi.Desktop.ViewModels;

/// <summary>
/// Wraps a connected device for the new-profile creation form so each item
/// carries its own IsSelected state and a nested channel list.
/// </summary>
public partial class NewProfileDeviceItem : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
    public required IStreamingDevice Device { get; init; }
    public string Name => Device.Name;
    public string SerialNo => Device.DeviceSerialNo ?? string.Empty;
    public ObservableCollection<NewProfileChannelItem> ChannelItems { get; } = [];
}

/// <summary>
/// Wraps a channel for the new-profile creation form.
/// </summary>
public partial class NewProfileChannelItem : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
    public required IChannel Channel { get; init; }
    public string Name => Channel.Name;
}

/// <summary>
/// Backs the unified Profiles pane. Owns drawer state, profile activation,
/// creation, and deletion without going through DaqifiViewModel.
/// </summary>
public partial class ProfilesPaneViewModel : ObservableObject
{
    private readonly AppLogger _logger = AppLogger.Instance;

    public ObservableCollection<Profile> Profiles => LoggingManager.Instance.SubscribedProfiles;

    [ObservableProperty] private bool _hasProfiles;
    [ObservableProperty] private bool _isDrawerOpen;
    [ObservableProperty] private bool _isNewProfile;
    [ObservableProperty] private Profile? _selectedProfile;
    [ObservableProperty] private bool _isLoggingActive;
    [ObservableProperty] private string _drawerError = string.Empty;
    [ObservableProperty] private bool _hasDrawerError;
    [ObservableProperty] private string? _activeProfileName;

    // New-profile form fields (active only when IsNewProfile = true)
    [ObservableProperty] private string _newProfileName = string.Empty;
    [ObservableProperty] private int _newProfileFrequency = 1000;
    public ObservableCollection<NewProfileDeviceItem> NewDeviceItems { get; } = [];

    public IRelayCommand<Profile> OpenEditDrawerCommand { get; }
    public IRelayCommand OpenNewDrawerCommand { get; }
    public IRelayCommand CloseDrawerCommand { get; }
    public IRelayCommand<Profile> ActivateProfileCommand { get; }
    public IRelayCommand<Profile> DeleteProfileCommand { get; }
    public IRelayCommand SaveNewProfileCommand { get; }
    public IRelayCommand SaveCurrentSettingsCommand { get; }

    public ProfilesPaneViewModel()
    {
        OpenEditDrawerCommand = new RelayCommand<Profile>(OpenEditDrawer);
        OpenNewDrawerCommand = new RelayCommand(OpenNewDrawer);
        CloseDrawerCommand = new RelayCommand(CloseDrawer);
        ActivateProfileCommand = new RelayCommand<Profile>(ActivateProfile);
        DeleteProfileCommand = new RelayCommand<Profile>(DeleteProfile);
        SaveNewProfileCommand = new RelayCommand(SaveNewProfile, CanSaveNewProfile);
        SaveCurrentSettingsCommand = new RelayCommand(SaveCurrentSettings);

        LoggingManager.Instance.PropertyChanged += OnLoggingManagerPropertyChanged;
        IsLoggingActive = LoggingManager.Instance.Active;
        HasProfiles = Profiles.Count > 0;
        foreach (var p in Profiles) p.PropertyChanged += OnProfilePropertyChanged;
        Profiles.CollectionChanged += OnProfilesCollectionChanged;
        RefreshActiveProfileName();
    }

    private void OnProfilesCollectionChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        HasProfiles = Profiles.Count > 0;
        if (e.OldItems != null)
            foreach (Profile p in e.OldItems) p.PropertyChanged -= OnProfilePropertyChanged;
        if (e.NewItems != null)
            foreach (Profile p in e.NewItems) p.PropertyChanged += OnProfilePropertyChanged;
        RefreshActiveProfileName();
    }

    private void OnProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Profile.IsProfileActive))
            RefreshActiveProfileName();
    }

    private void RefreshActiveProfileName() =>
        ActiveProfileName = Profiles.FirstOrDefault(p => p.IsProfileActive)?.Name;

    partial void OnDrawerErrorChanged(string value) => HasDrawerError = !string.IsNullOrEmpty(value);

    partial void OnNewProfileNameChanged(string value) =>
        SaveNewProfileCommand.NotifyCanExecuteChanged();

    private bool CanSaveNewProfile() =>
        !string.IsNullOrWhiteSpace(NewProfileName) &&
        NewDeviceItems.Any(d => d.IsSelected) &&
        NewProfileFrequency > 0;

    private void OnLoggingManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoggingManager.Active))
            IsLoggingActive = LoggingManager.Instance.Active;
    }

    private void OpenEditDrawer(Profile? profile)
    {
        if (profile == null) return;
        if (LoggingManager.Instance.Active)
        {
            _logger.AddBreadcrumb("profile", "Blocked opening profile drawer while logging active");
            return;
        }
        DrawerError = string.Empty;
        SelectedProfile = profile;
        IsNewProfile = false;
        IsDrawerOpen = true;
    }

    private void OpenNewDrawer()
    {
        if (LoggingManager.Instance.Active)
        {
            _logger.AddBreadcrumb("profile", "Blocked opening new-profile drawer while logging active");
            return;
        }
        DrawerError = string.Empty;
        SelectedProfile = null;
        IsNewProfile = true;
        NewProfileName = $"DAQiFi Profile {DateTime.Now:M/d/yyyy h:mm tt}";
        NewProfileFrequency = 1000;

        foreach (var item in NewDeviceItems)
            item.PropertyChanged -= OnNewDeviceItemPropertyChanged;
        NewDeviceItems.Clear();

        foreach (var device in ConnectionManager.Instance.ConnectedDevices)
        {
            var item = new NewProfileDeviceItem { Device = device };
            item.PropertyChanged += OnNewDeviceItemPropertyChanged;
            NewDeviceItems.Add(item);
        }

        IsDrawerOpen = true;
    }

    private void OnNewDeviceItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not NewProfileDeviceItem deviceItem ||
            e.PropertyName != nameof(NewProfileDeviceItem.IsSelected))
            return;

        if (deviceItem.IsSelected)
        {
            foreach (var ch in deviceItem.Device.DataChannels.NaturalOrderBy(c => c.Name))
                deviceItem.ChannelItems.Add(new NewProfileChannelItem { Channel = ch });
        }
        else
        {
            deviceItem.ChannelItems.Clear();
        }

        SaveNewProfileCommand.NotifyCanExecuteChanged();
    }

    private void CloseDrawer()
    {
        if (!IsNewProfile && SelectedProfile != null &&
            !string.IsNullOrWhiteSpace(SelectedProfile.Name))
        {
            LoggingManager.Instance.UpdateProfileInXml(SelectedProfile);
        }

        IsDrawerOpen = false;
        DrawerError = string.Empty;
        SelectedProfile = null;
    }

    private void ActivateProfile(Profile? profile)
    {
        if (profile == null) return;

        if (LoggingManager.Instance.Active)
        {
            // Silent no-op: the status bar shows LOGGING · LOCKED; tiles are
            // dimmed with a "no" cursor. Opening the drawer to show this error
            // would contradict the gear button, which is silently blocked.
            _logger.AddBreadcrumb("profile", "Blocked activate while logging active");
            return;
        }

        var anyActive = Profiles.FirstOrDefault(p => p.IsProfileActive);
        if (anyActive != null && anyActive.ProfileId != profile.ProfileId)
        {
            ShowError(profile, "Deactivate the current active profile first.");
            return;
        }

        // Two-pass device matching: prefer exact serial, fall back to model
        var claimedDevices = new HashSet<IStreamingDevice>();
        var profileToDevice = new Dictionary<ProfileDevice, IStreamingDevice>();
        var connected = ConnectionManager.Instance.ConnectedDevices.ToList();

        foreach (var pd in profile.Devices)
        {
            var exact = connected.FirstOrDefault(cd =>
                !string.IsNullOrEmpty(pd.DeviceSerialNo) &&
                string.Equals(cd.DeviceSerialNo, pd.DeviceSerialNo, StringComparison.OrdinalIgnoreCase) &&
                !claimedDevices.Contains(cd));
            if (exact != null) { profileToDevice[pd] = exact; claimedDevices.Add(exact); }
        }

        foreach (var pd in profile.Devices)
        {
            if (profileToDevice.ContainsKey(pd)) continue;
            var modelMatch = connected.FirstOrDefault(cd =>
                cd.DevicePartNumber == pd.DevicePartName && !claimedDevices.Contains(cd));
            if (modelMatch != null) { profileToDevice[pd] = modelMatch; claimedDevices.Add(modelMatch); }
        }

        if (profileToDevice.Count == 0)
        {
            ShowError(profile, "No connected devices match this profile.");
            return;
        }

        DrawerError = string.Empty;

        foreach (var (pd, device) in profileToDevice)
        {
            device.StreamingFrequency = pd.SamplingFrequency;

            var toActivate = pd.Channels
                .Where(c => c.IsChannelActive)
                .Select(c => device.DataChannels.FirstOrDefault(x =>
                    x.Name == c.Name.Trim() && x.TypeString == c.Type.Trim()))
                .Where(c => c != null)
                .Cast<IChannel>()
                .ToList();

            if (profile.IsProfileActive)
            {
                device.RemoveAllChannels();
                foreach (var ch in toActivate) LoggingManager.Instance.Unsubscribe(ch);
            }
            else
            {
                device.AddChannels(toActivate);
                foreach (var ch in toActivate) LoggingManager.Instance.Subscribe(ch);
            }
        }

        profile.IsProfileActive = !profile.IsProfileActive;
        _logger.AddBreadcrumb("profile",
            $"Profile {(profile.IsProfileActive ? "activated" : "deactivated")}: {profile.Name}");
    }

    private void ShowError(Profile profile, string message)
    {
        SelectedProfile = profile;
        IsNewProfile = false;
        IsDrawerOpen = true;
        DrawerError = message;
    }

    private void DeleteProfile(Profile? profile)
    {
        if (profile == null) return;

        if (LoggingManager.Instance.Active)
        {
            DrawerError = "Cannot delete a profile while logging is active.";
            return;
        }

        if (profile.IsProfileActive || Profiles.Any(p => p.IsProfileActive))
        {
            DrawerError = "Deactivate the profile before deleting it.";
            return;
        }

        CloseDrawer();
        LoggingManager.Instance.UnsubscribeProfile(profile);
    }

    private void SaveNewProfile()
    {
        if (!CanSaveNewProfile()) return;

        var now = DateTime.Now;
        var profile = new Profile
        {
            Name = NewProfileName.Trim(),
            ProfileId = Guid.NewGuid(),
            CreatedOn = now,
            Devices = [],
        };

        foreach (var deviceItem in NewDeviceItems.Where(d => d.IsSelected))
        {
            var device = deviceItem.Device;
            var pd = new ProfileDevice
            {
                DeviceName = device.Name,
                DevicePartName = device.DevicePartNumber,
                DeviceSerialNo = device.DeviceSerialNo,
                MacAddress = device.MacAddress,
                SamplingFrequency = NewProfileFrequency,
                Channels = [],
            };

            foreach (var channelItem in deviceItem.ChannelItems.Where(c => c.IsSelected))
            {
                pd.Channels.Add(new ProfileChannel
                {
                    Name = channelItem.Channel.Name,
                    Type = channelItem.Channel.TypeString,
                    IsChannelActive = true,
                    SerialNo = channelItem.Channel.DeviceSerialNo,
                });
            }

            profile.Devices.Add(pd);
        }

        LoggingManager.Instance.SubscribeProfile(profile);
        CloseDrawer();
    }

    private void SaveCurrentSettings()
    {
        var connected = ConnectionManager.Instance.ConnectedDevices.ToList();
        if (connected.Count == 0)
        {
            DrawerError = "No devices connected.";
            return;
        }

        var now = DateTime.Now;
        var name = string.IsNullOrWhiteSpace(NewProfileName)
            ? $"DAQiFi Profile {now:M/d/yyyy h:mm tt}"
            : NewProfileName.Trim();
        var profile = new Profile
        {
            Name = name,
            ProfileId = Guid.NewGuid(),
            CreatedOn = now,
            Devices = [],
        };

        foreach (var device in connected)
        {
            if (device.DataChannels.Count == 0) continue;
            var pd = new ProfileDevice
            {
                DeviceName = device.Name,
                DevicePartName = device.DevicePartNumber,
                DeviceSerialNo = device.DeviceSerialNo,
                MacAddress = device.MacAddress,
                SamplingFrequency = device.StreamingFrequency,
                Channels = device.DataChannels
                    .Where(c => c.IsActive)
                    .Select(c => new ProfileChannel
                    {
                        Name = c.Name,
                        Type = c.TypeString,
                        IsChannelActive = true,
                        SerialNo = c.DeviceSerialNo,
                    })
                    .ToList(),
            };
            profile.Devices.Add(pd);
        }

        LoggingManager.Instance.SubscribeProfile(profile);
        CloseDrawer();
    }

    public void Cleanup()
    {
        LoggingManager.Instance.PropertyChanged -= OnLoggingManagerPropertyChanged;
        Profiles.CollectionChanged -= OnProfilesCollectionChanged;
        foreach (var p in Profiles) p.PropertyChanged -= OnProfilePropertyChanged;
        foreach (var item in NewDeviceItems)
            item.PropertyChanged -= OnNewDeviceItemPropertyChanged;
    }
}
