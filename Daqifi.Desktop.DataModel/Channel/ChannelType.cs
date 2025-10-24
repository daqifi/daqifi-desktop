// This file re-exports the ChannelType enum from Daqifi.Core
// to maintain the Daqifi.Desktop.DataModel.Channel namespace for desktop code

namespace Daqifi.Desktop.DataModel.Channel;

/// <summary>
/// Represents the type of a channel.
/// Re-exported from Daqifi.Core.Channel.ChannelType
/// </summary>
public enum ChannelType
{
    /// <summary>
    /// Digital channel (binary on/off).
    /// </summary>
    Digital = Daqifi.Core.Channel.ChannelType.Digital,

    /// <summary>
    /// Analog channel (continuous value).
    /// </summary>
    Analog = Daqifi.Core.Channel.ChannelType.Analog
}