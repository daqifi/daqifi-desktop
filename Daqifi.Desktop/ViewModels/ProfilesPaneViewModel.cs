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
/// Backs the unified Profiles pane. Owns drawer state, profile activation,
/// creation, and deletion without going through DaqifiViewModel.
/// </summary>
public partial class ProfilesPaneViewModel : ObservableObject
{
    #region Private Fields
    private readonly AppLogger _logger = AppLogger.Instance;
    private TaskCompletionSource<bool>? _confirmTcs;
    #endregion

    #region Observable Properties
    /// <summary>True when there is at least one saved profile to display.</summary>
    [ObservableProperty] private bool _hasProfiles;

    /// <summary>True when the edit/new-profile drawer is visible.</summary>
    [ObservableProperty] private bool _isDrawerOpen;

    /// <summary>True when the drawer is showing the "new profile" form rather than editing an existing profile.</summary>
    [ObservableProperty] private bool _isNewProfile;

    /// <summary>The profile currently being edited in the drawer, if any.</summary>
    [ObservableProperty] private Profile? _selectedProfile;

    /// <summary>Mirrors <see cref="LoggingManager.Active"/> to drive the locked-out UI state.</summary>
    [ObservableProperty] private bool _isLoggingActive;

    /// <summary>User-facing error message surfaced inside the drawer. <see cref="HasDrawerError"/> recomputes from this.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDrawerError))]
    private string _drawerError = string.Empty;

    /// <summary>Name of the currently active profile (null when none is active).</summary>
    [ObservableProperty] private string? _activeProfileName;

    // In-pane confirm dialog state (used by ShowConfirm for profile switch, etc.).

    /// <summary>True while the in-pane confirm overlay is being displayed.</summary>
    [ObservableProperty] private bool _isConfirmOpen;

    /// <summary>Title shown on the confirm overlay.</summary>
    [ObservableProperty] private string _confirmTitle = string.Empty;

    /// <summary>Body message shown on the confirm overlay.</summary>
    [ObservableProperty] private string _confirmMessage = string.Empty;

    /// <summary>Text displayed on the affirmative (primary) button of the confirm overlay.</summary>
    [ObservableProperty] private string _confirmAffirmativeLabel = "OK";

    // New-profile form fields (active only when IsNewProfile = true)

    /// <summary>Name bound to the new-profile form.</summary>
    [ObservableProperty] private string _newProfileName = string.Empty;

    /// <summary>Sampling frequency (Hz) bound to the new-profile form. Capped at 1000Hz in the UI.</summary>
    [ObservableProperty] private int _newProfileFrequency = 1000;
    #endregion

    #region Public Properties
    /// <summary>
    /// Computed flag mirroring <see cref="DrawerError"/> being non-empty.
    /// Raised automatically via <see cref="NotifyPropertyChangedForAttribute"/>
    /// when <see cref="DrawerError"/> changes.
    /// </summary>
    public bool HasDrawerError => !string.IsNullOrEmpty(DrawerError);

    /// <summary>Live view of the saved profiles backed by <see cref="LoggingManager"/>.</summary>
    public ObservableCollection<Profile> Profiles => LoggingManager.Instance.SubscribedProfiles;

    /// <summary>Devices available to include when building a new profile.</summary>
    public ObservableCollection<NewProfileDeviceItem> NewDeviceItems { get; } = [];
    #endregion

    #region Commands
    /// <summary>Opens the drawer to edit the supplied profile.</summary>
    public IRelayCommand<Profile> OpenEditDrawerCommand { get; }

    /// <summary>Opens the drawer in new-profile mode with the device list pre-populated.</summary>
    public IRelayCommand OpenNewDrawerCommand { get; }

    /// <summary>Closes the drawer, persisting edits to the selected profile.</summary>
    public IRelayCommand CloseDrawerCommand { get; }

    /// <summary>Toggles the supplied profile on/off with a user confirmation when switching.</summary>
    public IAsyncRelayCommand<Profile> ActivateProfileCommand { get; }

    /// <summary>Deletes the supplied profile when it is not currently active.</summary>
    public IRelayCommand<Profile> DeleteProfileCommand { get; }

    /// <summary>Saves the new-profile form as a new <see cref="Profile"/>.</summary>
    public IRelayCommand SaveNewProfileCommand { get; }

    /// <summary>Snapshots the current device configuration into a new profile.</summary>
    public IRelayCommand SaveCurrentSettingsCommand { get; }

    /// <summary>Affirmative response for the in-pane confirm overlay.</summary>
    public IRelayCommand ConfirmAffirmativeCommand { get; }

    /// <summary>Negative response for the in-pane confirm overlay.</summary>
    public IRelayCommand ConfirmNegativeCommand { get; }
    #endregion

    #region Constructor
    /// <summary>
    /// Wires up commands, subscribes to <see cref="LoggingManager"/> and profile
    /// collection events, and seeds the initial <see cref="HasProfiles"/> /
    /// <see cref="ActiveProfileName"/> state.
    /// </summary>
    public ProfilesPaneViewModel()
    {
        OpenEditDrawerCommand = new RelayCommand<Profile>(OpenEditDrawer);
        OpenNewDrawerCommand = new RelayCommand(OpenNewDrawer);
        CloseDrawerCommand = new RelayCommand(CloseDrawer);
        ActivateProfileCommand = new AsyncRelayCommand<Profile>(ActivateProfile);
        DeleteProfileCommand = new RelayCommand<Profile>(DeleteProfile);
        SaveNewProfileCommand = new RelayCommand(SaveNewProfile, CanSaveNewProfile);
        SaveCurrentSettingsCommand = new RelayCommand(SaveCurrentSettings);
        ConfirmAffirmativeCommand = new RelayCommand(() => CompleteConfirm(true));
        ConfirmNegativeCommand = new RelayCommand(() => CompleteConfirm(false));

        LoggingManager.Instance.PropertyChanged += OnLoggingManagerPropertyChanged;
        IsLoggingActive = LoggingManager.Instance.Active;
        HasProfiles = Profiles.Count > 0;
        foreach (var p in Profiles) p.PropertyChanged += OnProfilePropertyChanged;
        Profiles.CollectionChanged += OnProfilesCollectionChanged;
        RefreshActiveProfileName();
    }
    #endregion

    #region Event Handlers
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

    private void OnLoggingManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoggingManager.Active))
            IsLoggingActive = LoggingManager.Instance.Active;
    }

    partial void OnNewProfileNameChanged(string value) =>
        SaveNewProfileCommand.NotifyCanExecuteChanged();

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
    #endregion

    #region Drawer Lifecycle
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
    #endregion

    #region Activation
    private async Task ActivateProfile(Profile? profile)
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

        // Case 1: same tile clicked — toggle off. (If no matched devices remain,
        // ApplyProfileToDevices still flips IsProfileActive so the UI stays consistent.)
        if (anyActive != null && anyActive.ProfileId == profile.ProfileId)
        {
            ApplyProfileToDevices(profile, MatchProfileToConnected(profile), activate: false);
            return;
        }

        // Case 2 & 3 share this validation: the new profile must match at least
        // one connected device before we touch anything.
        var newMatches = MatchProfileToConnected(profile);
        if (newMatches.Count == 0)
        {
            ShowError(profile, "No connected devices match this profile.");
            return;
        }

        // Case 2: a different profile is active — ask the user to switch.
        if (anyActive != null)
        {
            var confirmed = await ShowConfirm(
                "Switch profile?",
                $"'{anyActive.Name}' is currently active. Switch to '{profile.Name}'?",
                affirmativeLabel: "SWITCH");
            if (!confirmed) return;

            var oldMatches = MatchProfileToConnected(anyActive);
            ApplyProfileToDevices(anyActive, oldMatches, activate: false);
        }

        // Case 2 (post-confirm) and Case 3 (no profile active): activate the new one.
        DrawerError = string.Empty;
        ApplyProfileToDevices(profile, newMatches, activate: true);
    }

    /// <summary>
    /// Two-pass match: prefer exact serial-number match, fall back to model
    /// (part-number) match for each <see cref="ProfileDevice"/> in the profile.
    /// Each connected device is claimed at most once.
    /// </summary>
    private static Dictionary<ProfileDevice, IStreamingDevice> MatchProfileToConnected(Profile profile)
    {
        var claimed = new HashSet<IStreamingDevice>();
        var result = new Dictionary<ProfileDevice, IStreamingDevice>();
        var connected = ConnectionManager.Instance.ConnectedDevices.ToList();

        foreach (var pd in profile.Devices)
        {
            var exact = connected.FirstOrDefault(cd =>
                !string.IsNullOrEmpty(pd.DeviceSerialNo) &&
                string.Equals(cd.DeviceSerialNo, pd.DeviceSerialNo, StringComparison.OrdinalIgnoreCase) &&
                !claimed.Contains(cd));
            if (exact != null)
            {
                result[pd] = exact;
                claimed.Add(exact);
            }
        }

        foreach (var pd in profile.Devices)
        {
            if (result.ContainsKey(pd)) continue;
            var modelMatch = connected.FirstOrDefault(cd =>
                cd.DevicePartNumber == pd.DevicePartName && !claimed.Contains(cd));
            if (modelMatch != null)
            {
                result[pd] = modelMatch;
                claimed.Add(modelMatch);
            }
        }

        return result;
    }

    /// <summary>
    /// Apply the channel + frequency intent of a profile to its matched devices.
    /// When <paramref name="activate"/> is true, sets the streaming frequency,
    /// adds the profile's active channels, and subscribes them. When false,
    /// unsubscribes the profile's channels first, then removes all channels
    /// from the matched devices. Flips <see cref="Profile.IsProfileActive"/> last.
    /// </summary>
    /// <remarks>
    /// Order matters on deactivate: <see cref="AbstractStreamingDevice.RemoveAllChannels"/>
    /// clears <c>IChannel.IsActive</c> on every channel, and
    /// <see cref="LoggingManager.Unsubscribe"/> only removes channels whose
    /// <c>IsActive</c> is still true. Calling Unsubscribe after RemoveAllChannels
    /// would leave stale subscriptions behind.
    /// </remarks>
    private void ApplyProfileToDevices(
        Profile profile,
        Dictionary<ProfileDevice, IStreamingDevice> matches,
        bool activate)
    {
        foreach (var (pd, device) in matches)
        {
            var channels = pd.Channels
                .Where(c => c.IsChannelActive)
                .Select(c => device.DataChannels.FirstOrDefault(x =>
                    x.Name == c.Name.Trim() && x.TypeString == c.Type.Trim()))
                .Where(c => c != null)
                .Cast<IChannel>()
                .ToList();

            if (activate)
            {
                device.StreamingFrequency = pd.SamplingFrequency;
                device.AddChannels(channels);
                foreach (var ch in channels) LoggingManager.Instance.Subscribe(ch);
            }
            else
            {
                // Unsubscribe BEFORE RemoveAllChannels — Unsubscribe's lookup
                // filters by IsActive, and RemoveAllChannels sets IsActive=false
                // on every channel, which would make Unsubscribe a silent no-op.
                foreach (var ch in channels) LoggingManager.Instance.Unsubscribe(ch);
                device.RemoveAllChannels();
            }
        }

        profile.IsProfileActive = activate;
        _logger.AddBreadcrumb("profile",
            $"Profile {(activate ? "activated" : "deactivated")}: {profile.Name}");
    }
    #endregion

    #region Confirm Overlay
    /// <summary>
    /// Displays the in-pane dark confirm overlay and returns true if the user
    /// chose the affirmative button. The overlay is bound to
    /// <see cref="IsConfirmOpen"/>, <see cref="ConfirmTitle"/>,
    /// <see cref="ConfirmMessage"/>, and <see cref="ConfirmAffirmativeLabel"/>;
    /// the two button commands complete the underlying
    /// <see cref="TaskCompletionSource{TResult}"/>.
    /// </summary>
    private Task<bool> ShowConfirm(string title, string message, string affirmativeLabel = "OK")
    {
        // Defensive: if a prior confirm is somehow still pending, cancel it
        // with a negative so the previous awaiter unwinds cleanly.
        _confirmTcs?.TrySetResult(false);

        _confirmTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ConfirmTitle = title;
        ConfirmMessage = message;
        ConfirmAffirmativeLabel = affirmativeLabel;
        IsConfirmOpen = true;
        return _confirmTcs.Task;
    }

    private void CompleteConfirm(bool result)
    {
        IsConfirmOpen = false;
        var tcs = _confirmTcs;
        _confirmTcs = null;
        tcs?.TrySetResult(result);
    }
    #endregion

    #region Profile CRUD
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
    #endregion

    #region Helpers
    private void RefreshActiveProfileName() =>
        ActiveProfileName = Profiles.FirstOrDefault(p => p.IsProfileActive)?.Name;

    private bool CanSaveNewProfile() =>
        !string.IsNullOrWhiteSpace(NewProfileName) &&
        NewDeviceItems.Any(d => d.IsSelected) &&
        NewProfileFrequency > 0;
    #endregion

    #region Cleanup
    /// <summary>
    /// Detaches every event handler the view model subscribed to and releases
    /// any pending confirm-dialog awaiter. Call when the hosting view unloads.
    /// </summary>
    public void Cleanup()
    {
        LoggingManager.Instance.PropertyChanged -= OnLoggingManagerPropertyChanged;
        Profiles.CollectionChanged -= OnProfilesCollectionChanged;
        foreach (var p in Profiles) p.PropertyChanged -= OnProfilePropertyChanged;
        foreach (var item in NewDeviceItems)
            item.PropertyChanged -= OnNewDeviceItemPropertyChanged;
        // Release any awaiter on an unresolved confirm dialog.
        _confirmTcs?.TrySetResult(false);
        _confirmTcs = null;
    }
    #endregion
}
