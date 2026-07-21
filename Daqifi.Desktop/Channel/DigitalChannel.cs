using Daqifi.Desktop.Device;
using ChannelDirection = Daqifi.Core.Channel.ChannelDirection;
using ChannelType = Daqifi.Core.Channel.ChannelType;

namespace Daqifi.Desktop.Channel;

public class DigitalChannel : AbstractChannel
{
    #region Constants
    /// <summary>
    /// Duty shown (and later commanded) before the user picks one. Core's bookkeeping starts
    /// at 0, which is not a commandable duty — the firmware stores but never applies 0.
    /// </summary>
    private const int DEFAULT_PWM_DUTY_CYCLE_PERCENT = 50;

    private const int MIN_PWM_DUTY_CYCLE_PERCENT = 1;
    private const int MAX_PWM_DUTY_CYCLE_PERCENT = 100;
    #endregion

    #region Private Fields
    /// <summary>
    /// Core channel implementation handling device communication
    /// </summary>
    private Daqifi.Core.Channel.IDigitalChannel _coreChannel;
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
                Owner?.SetChannelDirection(this, value);
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
    /// Gets whether this channel's hardware supports PWM output. Delegates to Core,
    /// which populates it from the firmware board mask.
    /// </summary>
    public override bool IsPwmCapable => _coreChannel.IsPwmCapable;

    /// <summary>
    /// Gets or sets whether PWM output is enabled. Setting delegates to the owner device,
    /// which commands Core in the documented duty → frequency → enable order; Core mirrors
    /// the resulting state into its channel bookkeeping, so the getter always reflects what
    /// was actually commanded (a failed command leaves it unchanged and the UI snaps back).
    /// </summary>
    public override bool IsPwmEnabled
    {
        get => _coreChannel.IsPwmEnabled;
        set
        {
            if (_coreChannel.IsPwmEnabled == value)
            {
                return;
            }

            Owner?.SetChannelPwmEnabled(this, value);

            // Core zeroes the channel's stored output value when PWM disables (the pin goes
            // transiently high-impedance); keep the desktop commanded-state flag in lockstep
            // without re-issuing a drive command.
            HydrateIsDigitalOn(_coreChannel.OutputValue);
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets the PWM duty cycle in whole percent (1-100, coerced). While PWM is
    /// enabled the change is commanded live through the owner device (Core mirrors it into
    /// bookkeeping on success); otherwise only the bookkeeping is updated and the value is
    /// applied on the next enable.
    /// </summary>
    public override int PwmDutyCyclePercent
    {
        get => _coreChannel.PwmDutyCyclePercent;
        set
        {
            var clamped = Math.Clamp(value, MIN_PWM_DUTY_CYCLE_PERCENT, MAX_PWM_DUTY_CYCLE_PERCENT);
            if (_coreChannel.PwmDutyCyclePercent != clamped)
            {
                if (_coreChannel.IsPwmEnabled)
                {
                    Owner?.SetChannelPwmDutyCycle(this, clamped);
                }
                else
                {
                    _coreChannel.PwmDutyCyclePercent = clamped;
                }
            }

            // Always notify so an out-of-range edit snaps the bound control back.
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets the core channel for device communication
    /// </summary>
    internal Daqifi.Core.Channel.IDigitalChannel CoreChannel => _coreChannel;

    #endregion

    #region Constructors
    /// <summary>
    /// Creates a new DigitalChannel by wrapping a Core channel instance.
    /// </summary>
    /// <param name="owner">The device that owns this channel.</param>
    /// <param name="coreChannel">The Core channel instance to wrap.</param>
    /// <exception cref="ArgumentNullException">Thrown when owner or coreChannel is null.</exception>
    public DigitalChannel(IStreamingDevice owner, Daqifi.Core.Channel.IDigitalChannel coreChannel)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(coreChannel);

        Owner = owner;
        _coreChannel = coreChannel;

        // Set desktop-specific properties (not in core)
        DeviceName = owner.DevicePartNumber;
        DeviceSerialNo = owner.DeviceSerialNo;
        IsBidirectional = true; // Digital channels are typically bidirectional
        ChannelColorBrush = ChannelColorManager.Instance.NewColor();

        // Initialize derived desktop state based on core
        IsOutput = coreChannel.Direction == ChannelDirection.Output;
        HydrateIsDigitalOn(coreChannel.OutputValue);
        EnsureCommandableDutyDefault(coreChannel);
    }

    /// <summary>
    /// Seeds Core's duty bookkeeping with a usable default so the drawer never shows —
    /// or commands — the 0 that Core rejects. Bookkeeping-only: no device command.
    /// </summary>
    private static void EnsureCommandableDutyDefault(Daqifi.Core.Channel.IDigitalChannel coreChannel)
    {
        if (coreChannel.IsPwmCapable && coreChannel.PwmDutyCyclePercent < MIN_PWM_DUTY_CYCLE_PERCENT)
        {
            coreChannel.PwmDutyCyclePercent = DEFAULT_PWM_DUTY_CYCLE_PERCENT;
        }
    }

    internal void ReplaceCoreChannel(Daqifi.Core.Channel.IDigitalChannel coreChannel)
    {
        ArgumentNullException.ThrowIfNull(coreChannel);

        var wasEnabled = _coreChannel.IsEnabled;
        var direction = _coreChannel.Direction;
        var outputValue = _coreChannel.OutputValue;
        var isPwmEnabled = _coreChannel.IsPwmEnabled;
        var pwmDutyCyclePercent = _coreChannel.PwmDutyCyclePercent;

        _coreChannel = coreChannel;
        _coreChannel.IsEnabled = wasEnabled;
        _coreChannel.Direction = direction;
        _coreChannel.OutputValue = outputValue;
        _coreChannel.IsPwmEnabled = isPwmEnabled;
        _coreChannel.PwmDutyCyclePercent = pwmDutyCyclePercent;
        EnsureCommandableDutyDefault(_coreChannel);

        // Keep the desktop commanded-state flag in lockstep with Core's mirror so the
        // tile/toggle cannot desync after a refresh (no device command is re-issued).
        HydrateIsDigitalOn(outputValue);

        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Direction));
        OnPropertyChanged(nameof(Index));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsPwmCapable));
        OnPropertyChanged(nameof(IsPwmEnabled));
        OnPropertyChanged(nameof(PwmDutyCyclePercent));
    }
    #endregion

    #region Object Overrides
    public override string ToString()
    {
        return Name;
    }
    #endregion
}
