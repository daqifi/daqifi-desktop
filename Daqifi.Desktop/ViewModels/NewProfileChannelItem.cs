using CommunityToolkit.Mvvm.ComponentModel;
using Daqifi.Desktop.Channel;

namespace Daqifi.Desktop.ViewModels;

/// <summary>
/// Wraps a channel for the new-profile creation form so each channel row
/// carries its own IsSelected state independent of the underlying channel.
/// </summary>
public partial class NewProfileChannelItem : ObservableObject
{
    #region Observable Properties
    [ObservableProperty] private bool _isSelected;
    #endregion

    #region Public Properties
    /// <summary>
    /// The underlying channel this row represents.
    /// </summary>
    public required IChannel Channel { get; init; }

    /// <summary>
    /// Display name of the underlying channel.
    /// </summary>
    public string Name => Channel.Name;
    #endregion
}
