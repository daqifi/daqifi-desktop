using Daqifi.Desktop.Device;
using ChannelDirection = Daqifi.Core.Channel.ChannelDirection;
using ChannelType = Daqifi.Core.Channel.ChannelType;

namespace Daqifi.Desktop.Channel;

public class DigitalChannel : AbstractChannel
{
    #region Private Fields
    /// <summary>
    /// Core channel implementation handling device communication
    /// </summary>
    private readonly Daqifi.Core.Channel.IDigitalChannel _coreChannel;
    #endregion

    #region Properties
    public override ChannelType Type => ChannelType.Digital;

    public override bool IsAnalog => false;

    public override bool IsDigital => true;

    /// <summary>
    /// Gets or sets the channel name. Delegates to core channel to avoid duplication.
    /// </summary>
    public override string Name
    {
        get => _coreChannel.Name;
        set
        {
            if (_coreChannel.Name != value)
            {
                _coreChannel.Name = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the channel direction. Delegates to core channel to avoid duplication.
    /// </summary>
    public override ChannelDirection Direction
    {
        get => _coreChannel.Direction;
        set
        {
            if (_coreChannel.Direction != value)
            {
                _coreChannel.Direction = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TypeString));

                // Update derived state
                IsOutput = value == ChannelDirection.Output;

                // Notify owner of direction change
                _owner?.SetChannelDirection(this, value);
            }
        }
    }

    /// <summary>
    /// Gets the channel index. Delegates to core ChannelNumber to avoid duplication.
    /// </summary>
    public override int Index => _coreChannel.ChannelNumber;

    /// <summary>
    /// Gets or sets whether the channel is active. Synchronized with core IsEnabled.
    /// </summary>
    public override bool IsActive
    {
        get => _coreChannel.IsEnabled;
        set
        {
            if (_coreChannel.IsEnabled != value)
            {
                _coreChannel.IsEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets the core channel for device communication
    /// </summary>
    internal Daqifi.Core.Channel.IDigitalChannel CoreChannel => _coreChannel;

    #endregion

    #region Constructors
    public DigitalChannel(IStreamingDevice owner, string name, int channelId, ChannelDirection direction, bool isBidirectional)
    {
        _owner = owner;

        // Create core channel for device communication
        _coreChannel = new Daqifi.Core.Channel.DigitalChannel(channelId)
        {
            Name = name,
            Direction = direction,
            IsEnabled = false
        };

        // Set desktop-specific properties (not in core)
        DeviceName = owner.DevicePartNumber;
        DeviceSerialNo = owner.DeviceSerialNo;
        IsBidirectional = isBidirectional;
        ChannelColorBrush = ChannelColorManager.Instance.NewColor();

        // Initialize derived desktop state based on core
        IsOutput = direction == ChannelDirection.Output;
    }
    #endregion

    #region Object Overrides
    public override string ToString()
    {
        return Name;
    }
    #endregion
}