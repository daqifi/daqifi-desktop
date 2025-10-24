// This file re-exports the ChannelDirection enum from Daqifi.Core
// to maintain the Daqifi.Desktop.DataModel.Channel namespace for desktop code

namespace Daqifi.Desktop.DataModel.Channel;

/// <summary>
/// Represents the direction of data flow for a channel.
/// Re-exported from Daqifi.Core.Channel.ChannelDirection
/// </summary>
public enum ChannelDirection
{
    /// <summary>
    /// Input channel (reads data from device).
    /// </summary>
    Input = Daqifi.Core.Channel.ChannelDirection.Input,

    /// <summary>
    /// Output channel (writes data to device).
    /// </summary>
    Output = Daqifi.Core.Channel.ChannelDirection.Output,

    /// <summary>
    /// Unknown or uninitialized direction.
    /// </summary>
    Unknown = Daqifi.Core.Channel.ChannelDirection.Unknown
}