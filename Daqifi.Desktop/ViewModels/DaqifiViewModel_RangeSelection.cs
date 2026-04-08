using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Configuration;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Helpers;
using Daqifi.Desktop.Logger;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;
using System.Windows;
using MahApps.Metro.Controls.Dialogs;

namespace Daqifi.Desktop.ViewModels;

/// <summary>
/// Partial class extension of DaqifiViewModel to add range selection support
/// </summary>
public partial class DaqifiViewModel
{
    #region Range Selection Helpers

    /// <summary>
    /// Helper for managing channel range selection
    /// </summary>
    [ObservableProperty]
    private RangeSelectionHelper<IChannel> _channelSelectionHelper = new();

    /// <summary>
    /// Helper for managing device range selection  
    /// </summary>
    [ObservableProperty]
    private RangeSelectionHelper<IStreamingDevice> _deviceSelectionHelper = new();

    /// <summary>
    /// Helper for managing logging session range selection
    /// </summary>
    [ObservableProperty]
    private RangeSelectionHelper<LoggingSession> _loggingSessionSelectionHelper = new();

    #endregion

    #region Bulk Operations Commands

    /// <summary>
    /// Removes multiple selected channels
    /// </summary>
    [RelayCommand]
    private void RemoveSelectedChannels()
    {
        var channelsToRemove = ChannelSelectionHelper.SelectedItems.ToList();
        foreach (var channel in channelsToRemove)
        {
            RemoveChannel(channel);
        }
        ChannelSelectionHelper.ClearSelection();
    }

    /// <summary>
    /// Disconnects multiple selected devices
    /// </summary>
    [RelayCommand]
    private void DisconnectSelectedDevices()
    {
        var devicesToDisconnect = DeviceSelectionHelper.SelectedItems.ToList();
        foreach (var device in devicesToDisconnect)
        {
            DisconnectDevice(device);
        }
        DeviceSelectionHelper.ClearSelection();
    }

    /// <summary>
    /// Exports multiple selected logging sessions
    /// </summary>
    [RelayCommand]
    private void ExportSelectedLoggingSessions()
    {
        var sessionsToExport = LoggingSessionSelectionHelper.SelectedItems.ToList();
        if (sessionsToExport.Count == 0)
        {
            return;
        }

        var exportDialogViewModel = new ExportDialogViewModel(sessionsToExport);
        _dialogService.ShowDialog<ExportDialog>(this, exportDialogViewModel);
        LoggingSessionSelectionHelper.ClearSelection();
    }

    /// <summary>
    /// Deletes multiple selected logging sessions
    /// </summary>
    [RelayCommand]
    private async Task DeleteSelectedLoggingSessions()
    {
        var sessionsToDelete = LoggingSessionSelectionHelper.SelectedItems.ToList();
        if (sessionsToDelete.Count == 0)
        {
            return;
        }

        var sessionNames = string.Join(", ", sessionsToDelete.Take(3).Select(s => s.Name));
        if (sessionsToDelete.Count > 3)
        {
            sessionNames += $" and {sessionsToDelete.Count - 3} more";
        }

        var result = await ShowMessage("Delete Confirmation", 
            $"Are you sure you want to delete {sessionsToDelete.Count} logging sessions: {sessionNames}?", 
            MessageDialogStyle.AffirmativeAndNegative).ConfigureAwait(false);
            
        if (result != MessageDialogResult.Affirmative)
        {
            return;
        }

        IsLoggedDataBusy = true;
        LoggedDataBusyReason = $"Deleting {sessionsToDelete.Count} logging sessions...";

        var bw = new BackgroundWorker();
        bw.DoWork += delegate
        {
            var successfulDeletions = new List<LoggingSession>();
            
            foreach (var session in sessionsToDelete)
            {
                try
                {
                    DbLogger.DeleteLoggingSession(session);
                    successfulDeletions.Add(session);
                }
                catch (Exception dbEx)
                {
                    _appLogger.Error(dbEx, $"Failed to delete session {session.ID} from database.");
                }
            }

            // Remove successfully deleted sessions from the UI
            Application.Current.Dispatcher.Invoke(delegate
            {
                foreach (var session in successfulDeletions)
                {
                    LoggingManager.Instance.LoggingSessions.Remove(session);
                }
            });
        };

        bw.RunWorkerCompleted += (s, e) =>
        {
            IsLoggedDataBusy = false;
            LoggingSessionSelectionHelper.ClearSelection();
        };

        bw.RunWorkerAsync();
    }

    /// <summary>
    /// Shows/hides selected channels in the plot
    /// </summary>
    [RelayCommand]
    private void ToggleSelectedChannelVisibility()
    {
        var selectedChannels = ChannelSelectionHelper.SelectedItems.ToList();
        if (selectedChannels.Count == 0)
        {
            return;
        }

        // Determine target visibility state - if any selected channel is visible, hide all; otherwise show all
        var anyVisible = selectedChannels.Any(c => c.IsVisible);
        var targetVisibility = !anyVisible;

        foreach (var channel in selectedChannels)
        {
            channel.IsVisible = targetVisibility;
        }
    }

    /// <summary>
    /// Enables/disables selected channels
    /// </summary>
    [RelayCommand]
    private void ToggleSelectedChannelActiveState()
    {
        var selectedChannels = ChannelSelectionHelper.SelectedItems.ToList();
        if (selectedChannels.Count == 0)
        {
            return;
        }

        // Determine target active state - if any selected channel is active, deactivate all; otherwise activate all
        var anyActive = selectedChannels.Any(c => c.IsActive);
        var targetActiveState = !anyActive;

        foreach (var channel in selectedChannels)
        {
            if (targetActiveState && !channel.IsActive)
            {
                // Subscribe/activate the channel
                LoggingManager.Instance.Subscribe(channel);
            }
            else if (!targetActiveState && channel.IsActive)
            {
                // Unsubscribe/deactivate the channel
                LoggingManager.Instance.Unsubscribe(channel);
            }
        }
    }

    #endregion

    #region Selection State Properties

    /// <summary>
    /// Gets whether any channels are currently selected
    /// </summary>
    public bool HasSelectedChannels => ChannelSelectionHelper.SelectedCount > 0;

    /// <summary>
    /// Gets whether any devices are currently selected
    /// </summary>
    public bool HasSelectedDevices => DeviceSelectionHelper.SelectedCount > 0;

    /// <summary>
    /// Gets whether any logging sessions are currently selected
    /// </summary>
    public bool HasSelectedLoggingSessions => LoggingSessionSelectionHelper.SelectedCount > 0;

    /// <summary>
    /// Gets the count of selected channels
    /// </summary>
    public int SelectedChannelCount => ChannelSelectionHelper.SelectedCount;

    /// <summary>
    /// Gets the count of selected devices
    /// </summary>
    public int SelectedDeviceCount => DeviceSelectionHelper.SelectedCount;

    /// <summary>
    /// Gets the count of selected logging sessions
    /// </summary>
    public int SelectedLoggingSessionCount => LoggingSessionSelectionHelper.SelectedCount;

    #endregion

    /// <summary>
    /// Initializes range selection helpers - should be called from constructor
    /// </summary>
    private void InitializeRangeSelection()
    {
        // Wire up property change notifications for selection count properties
        ChannelSelectionHelper.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(RangeSelectionHelper<IChannel>.SelectedItems))
            {
                OnPropertyChanged(nameof(HasSelectedChannels));
                OnPropertyChanged(nameof(SelectedChannelCount));
            }
        };

        DeviceSelectionHelper.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(RangeSelectionHelper<IStreamingDevice>.SelectedItems))
            {
                OnPropertyChanged(nameof(HasSelectedDevices));
                OnPropertyChanged(nameof(SelectedDeviceCount));
            }
        };

        LoggingSessionSelectionHelper.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(RangeSelectionHelper<LoggingSession>.SelectedItems))
            {
                OnPropertyChanged(nameof(HasSelectedLoggingSessions));
                OnPropertyChanged(nameof(SelectedLoggingSessionCount));
            }
        };
    }
}