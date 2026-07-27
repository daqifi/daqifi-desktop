using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Device;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Daqifi.Desktop.Logger;

/// <summary>
/// Provaides summary data for incoming samples
/// </summary>
public partial class SummaryLogger : ObservableObject, ILogger
{
    #region "Constants"

    /// <summary>
    /// The smallest window this logger will hold, matching the <c>Minimum</c> on the Summary
    /// flyout's sample-size control (SummaryFlyout.xaml). A window of one message is legal - it
    /// publishes on every message and spans no interval - but a window of zero or fewer is not a
    /// window at all, and the property is public, so the floor is enforced here rather than left to
    /// the control.
    /// </summary>
    private const int MINIMUM_SAMPLE_SIZE = 1;

    /// <summary>
    /// The window length <see cref="ResetCommand"/> restores, and the one a logger built by the
    /// parameterless constructor starts with.
    /// </summary>
    private const int DEFAULT_SAMPLE_SIZE = 1000;

    #endregion

    #region "Private Data"

    public class ChannelSummary
    {
        private readonly ChannelBuffer _current;

        internal ChannelSummary(string name, ChannelBuffer current)
        {
            Name = name;
            _current = current;
        }

        /// <summary>
        /// The frequency sample rate
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The number of samples seen
        /// </summary>
        public int SampleCount => _current.SampleCount;

        /// <summary>
        /// The time of the last sample
        /// </summary>
        public DateTime LastUpdate => new(_current.LastSampleTicks);

        /// <summary>
        /// The frequency sample rate
        /// </summary>
        public double SampleRate
        {
            get
            {
                // FirstSampleTicks is measured from the end of the sample, so we need to drop the first sample
                var delta = new TimeSpan(_current.LastSampleTicks - _current.FirstSampleTicks);
                return delta.Ticks > 0 ? (_current.SampleCount - 1) / delta.TotalSeconds : 0.0;
            }
        }

        /// <summary>
        /// The maximum time between samples
        /// </summary>
        public double MaxDelta => _current.MaxDeltaTicks;

        /// <summary>
        /// The average time between samples
        /// </summary>
        public double AverageDelta => _current.AverageDeltaTicks;

        /// <summary>
        /// The minimum time between samples
        /// </summary>
        public double MinDelta => _current.MinDeltaTicks;

        /// <summary>
        /// The largest sample value seen on this channel
        /// </summary>
        public double MaxValue => _current.MaxValue;

        /// <summary>
        /// The smallest sample value seen on this channel
        /// </summary>
        public double MinValue => _current.MinValue;

        /// <summary>
        /// The mean of the sample values seen on this channel
        /// </summary>
        public double AverageValue => _current.AverageValue;
    }

    internal class ChannelBuffer
    {
        /// <summary>
        /// The number of samples seen
        /// </summary>
        public int SampleCount { get; set; }

        /// <summary>
        /// The total elapsed time
        /// </summary>
        public long FirstSampleTicks { get; set; }

        /// <summary>
        /// The total elapsed time
        /// </summary>
        public long LastSampleTicks { get; set; }

        /// <summary>
        /// The average time between samples
        /// </summary>
        public double AverageDeltaTicks { get; set; }

        /// <summary>
        /// The maximum time between samples
        /// </summary>
        public long MaxDeltaTicks { get; set; }

        /// <summary>
        /// The minimum time between samples
        /// </summary>
        public long MinDeltaTicks { get; set; }

        /// <summary>
        /// The running mean of these samples
        /// </summary>
        public double AverageValue { get; set; }

        /// <summary>
        /// The maximum value of these samples
        /// </summary>
        public double MaxValue { get; set; }

        /// <summary>
        /// The minimum value of these samples
        /// </summary>
        public double MinValue { get; set; }

        public void Reset()
        {
            SampleCount = 0;
            FirstSampleTicks = 0;
            LastSampleTicks = 0;
            AverageDeltaTicks = 0;
            MaxDeltaTicks = 0;
            MinDeltaTicks = 0;
            AverageValue = 0;
            MaxValue = 0;
            MinValue = 0;
        }
    }


    /// <summary>
    /// Summary results object
    /// </summary>
    private class SummaryBuffer
    {
        public SummaryBuffer()
        {
            Channels = new Dictionary<string, ChannelBuffer>(64);
            StatusList = new HashSet<int>();
        }

        /// <summary>
        /// The number of samples seen
        /// </summary>
        public int SampleCount { get; set; }

        /// <summary>
        /// The total elapsed time
        /// </summary>
        public long FirstSampleTicks { get; set; }

        /// <summary>
        /// The total elapsed time
        /// </summary>
        public long LastSampleTicks { get; set; }

        /// <summary>
        /// The average time between samples
        /// </summary>
        public double AverageDeltaTicks { get; set; }

        /// <summary>
        /// The maximum time between samples
        /// </summary>
        public long MaxDeltaTicks { get; set; }

        /// <summary>
        /// The minimum time between samples
        /// </summary>
        public long MinDeltaTicks { get; set; }

        /// <summary>
        /// The longest time between when the board reported a message and when the app received it
        /// </summary>
        public long MaxLatencyTicks { get; set; }

        /// <summary>
        /// The shortest time between when the board reported a message and when the app received it
        /// </summary>
        public long MinLatencyTicks { get; set; }

        /// <summary>
        /// The running mean time between when the board reported a message and when the app
        /// received it
        /// </summary>
        public double AverageLatencyTicks { get; set; }

        /// <summary>
        /// The statuses seen
        /// </summary>
        public HashSet<int> StatusList { get; set; }

        /// <summary>
        /// Indicates whether the device timestamp rolled over in this sample set
        /// </summary>
        public bool HasRollover { get; set; }

        /// <summary>
        /// The channels seen
        /// </summary>
        public Dictionary<string, ChannelBuffer> Channels { get; set; }

        public void Reset()
        {
            SampleCount = 0;
            FirstSampleTicks = 0;
            LastSampleTicks = 0;
            AverageDeltaTicks = 0;
            MaxDeltaTicks = 0;
            MinDeltaTicks = 0;
            MaxLatencyTicks = 0;
            MinLatencyTicks = 0;
            AverageLatencyTicks = 0;
            StatusList.Clear();
            foreach (var pair in Channels)
            {
                pair.Value.Reset();
            }
        }
    }

    private int _sampleSize;

    [ObservableProperty]
    private bool _enabled;

    /// <summary>
    /// Serializes every <em>mutation</em> of <see cref="_buffer"/>, <see cref="_current"/> and
    /// <see cref="_sampleSize"/>, including the swap that exchanges the two buffers, so that a
    /// window is always accumulated and published against one sample size.
    /// </summary>
    /// <remarks>
    /// A dedicated object rather than the buffers themselves, because <see cref="SwapBuffer"/>
    /// exchanges what those two fields point at. <c>lock (_buffer)</c> guards whichever instance
    /// the field happened to hold when the lock was taken, so across a swap two threads can lock
    /// two different objects while mutating the same buffer - no mutual exclusion at all. This
    /// reference never changes, so every mutating path below is genuinely serialized against every
    /// other.
    /// <para>
    /// Deliberately <em>not</em> taken by the read side. <see cref="SampleSize"/>'s getter and the
    /// properties that project <see cref="_current"/> onto the flyout read without it: they run on
    /// the UI thread in response to the notifications raised after a swap, and gating them would
    /// put every repaint behind the streaming thread. The cost is that a swap landing between two
    /// of those getters can show two fields from two different windows for one frame - a display
    /// artifact, not corruption, because a published buffer is only ever replaced wholesale and the
    /// buffer being filled is never the one being read.
    /// </para>
    /// <para>
    /// No change notification is raised while this is held - not by <see cref="Log(DeviceMessage)"/>,
    /// the <see cref="SampleSize"/> setter, or any command below. A subscriber that reaches back
    /// into the logger, or a binding marshalling to a UI thread that is itself inside the setter,
    /// would otherwise do so against a gate the notifying thread still owns.
    /// </para>
    /// </remarks>
    private readonly object _gate = new();

    /// <summary>
    /// The in-progress sample set
    /// </summary>
    private SummaryBuffer _buffer;

    /// <summary>
    /// The last completed sample set
    /// </summary>
    private SummaryBuffer _current;

    #endregion

    #region "Properties"

    /// <summary>
    /// The number of device messages that make up one summary window.
    /// </summary>
    /// <remarks>
    /// Live: two-way bound to the Summary flyout's sample-size control, so it can move while a
    /// window is still filling. Changing it restarts the in-progress window, so a window is always
    /// exactly as long as the size it will be measured against. Carrying the partial accumulation
    /// over would publish it against a length it was never gathered under, and lowering the size
    /// below the count already reached would leave the window with no way to close at all. Only
    /// the in-progress buffer is cleared - the last completed window stays on screen until a new
    /// one finishes.
    /// <para>
    /// Written out rather than generated by <c>[ObservableProperty]</c> because the assignment and
    /// the restart have to be a single atomic step, which is the "nontrivial side effect" case
    /// AGENTS.md reserves explicit properties for. The generated setter writes the field and only
    /// afterwards calls its changed-hook, so the streaming thread can read the new size while the
    /// old accumulation is still in place and publish a window spanning both regimes. Holding
    /// <see cref="_gate"/> across both statements closes that gap, because
    /// <see cref="Log(DeviceMessage)"/> only ever reads the size under the same lock.
    /// </para>
    /// <para>
    /// Both constructors assign the backing field directly, so the restart cannot run before the
    /// buffers exist.
    /// </para>
    /// <para>
    /// Values below <see cref="MINIMUM_SAMPLE_SIZE"/> are clamped rather than rejected. The
    /// property is two-way bound, and throwing out of a bound setter leaves the control and the
    /// model disagreeing with no way for the user to tell; clamping keeps them in step, because
    /// the notification below pushes the coerced value straight back to the control.
    /// </para>
    /// </remarks>
    public int SampleSize
    {
        get => _sampleSize;
        set
        {
            var coerced = Math.Max(MINIMUM_SAMPLE_SIZE, value);
            bool changed;

            lock (_gate)
            {
                changed = _sampleSize != coerced;
                if (changed)
                {
                    _sampleSize = coerced;
                    _buffer.Reset();
                }
            }

            // Raised outside the lock: a binding's change handler must never run while this
            // instance is holding the gate the streaming thread needs.
            //
            // Notified on a coercion even when the stored size did not move, so a control that
            // pushed a value this property refused is told to re-read and snap back to the one it
            // actually holds. Without that, a size of 0 offered against a stored 1 would leave the
            // flyout displaying 0 forever.
            if (changed || coerced != value)
            {
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// The total elapsed time
    /// </summary>
    public double ElapsedTime => TimeSpan.FromTicks(_current.LastSampleTicks - _current.FirstSampleTicks).TotalMilliseconds;

    /// <summary>
    /// The time of the last sample
    /// </summary>
    public DateTime LastUpdate => new(_current.LastSampleTicks);

    /// <summary>
    /// The frequency sample rate
    /// </summary>
    public double SampleRate
    {
        get
        {
            var delta = new TimeSpan(_current.LastSampleTicks - _current.FirstSampleTicks);
            return delta.Ticks > 0 ? (_current.SampleCount - 1) / delta.TotalSeconds : 0.0;
        }
    }

    /// <summary>
    /// The maximum time between samples
    /// </summary>
    public double MaxDelta => _current.MaxDeltaTicks;

    /// <summary>
    /// The minimum time between samples
    /// </summary>
    public double MinDelta => _current.MinDeltaTicks;

    /// <summary>
    /// The average time between samples
    /// </summary>
    public double AverageDelta => _current.AverageDeltaTicks;

    /// <summary>
    /// The longest message latency in the sample set
    /// </summary>
    public double MaxLatency => _current.MaxLatencyTicks;

    /// <summary>
    /// The shortest message latency in the sample set
    /// </summary>
    public double MinLatency => _current.MinLatencyTicks;

    /// <summary>
    /// The mean message latency of the sample set
    /// </summary>
    public double AverageLatency => _current.AverageLatencyTicks;

    /// <summary>
    /// Display info for the channels
    /// </summary>
    public IEnumerable<ChannelSummary> Channels
    {
        get
        {
            var result = new List<ChannelSummary>();
            foreach (var pair in _current.Channels)
            {
                result.Add(new ChannelSummary(pair.Key, pair.Value));
            }
            return result;
        }
    }

    public string StatusList
    {
        get
        {
            var sb = new StringBuilder();
            if (_current.StatusList.Count > 0)
            {
                var first = true;
                foreach (var status in _current.StatusList)
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    sb.Append(status);
                }
            }
            else
            {
                sb.Append('-');
            }
            return sb.ToString();
        }
    }

    #endregion

    #region "Constructor"

    public SummaryLogger()
    {
        _sampleSize = DEFAULT_SAMPLE_SIZE;
        _buffer = new SummaryBuffer();
        _current = new SummaryBuffer();
    }

    /// <summary>
    /// Creates a new instance
    /// </summary>
    /// <param name="sampleSize">
    /// The size of the sample set, clamped to <see cref="MINIMUM_SAMPLE_SIZE"/> on the same terms
    /// as <see cref="SampleSize"/> - assigning the field directly here would otherwise let a
    /// caller start the logger at a size the property will not accept.
    /// </param>
    public SummaryLogger(int sampleSize)
    {
        _sampleSize = Math.Max(MINIMUM_SAMPLE_SIZE, sampleSize);
        _buffer = new SummaryBuffer();
        _current = new SummaryBuffer();
        _enabled = true;
        Start();
    }

    #endregion

    #region "Logging"

    public void Log(DataSample dataSample)
    {
        if (!Enabled)
        {
            return;
        }

        lock (_gate)
        {
            if (!_buffer.Channels.TryGetValue(dataSample.ChannelName, out var buffer))
            {
                buffer = new ChannelBuffer();
                _buffer.Channels[dataSample.ChannelName] = buffer;
            }

            if (buffer.SampleCount == 0)
            {
                buffer.FirstSampleTicks = dataSample.TimestampTicks;

                // Seed the running extremes from the first sample of the window, the same way
                // the delta and latency accumulators below seed themselves. ChannelBuffer starts
                // (and Reset()s) at 0, so without this the extremes stay anchored at 0 and
                // Math.Min/Math.Max report 0 for any channel that never crosses zero.
                buffer.MinValue = dataSample.Value;
                buffer.MaxValue = dataSample.Value;
            }
            else
            {
                buffer.MinValue = Math.Min(dataSample.Value, buffer.MinValue);
                buffer.MaxValue = Math.Max(dataSample.Value, buffer.MaxValue);
            }

            // Accumulate the mean incrementally over the samples this channel actually received.
            // SampleSize counts device *messages*, not per-channel samples, and the two differ:
            // a frame's DeviceMessage is dispatched before that frame's channel samples, and the
            // buffer swap fires from Log(DeviceMessage), so a completed window holds one fewer
            // sample per channel than SampleSize. SampleSize is also a live, user-editable field
            // on the Summary flyout, so dividing by it rescaled an in-progress average. This form
            // is exact for every window length and independent of SampleSize.
            buffer.AverageValue += (dataSample.Value - buffer.AverageValue) / (buffer.SampleCount + 1);

            if (buffer.SampleCount > 0)
            {
                var elapsed = dataSample.TimestampTicks - buffer.LastSampleTicks;
                if (buffer.SampleCount == 1)
                {
                    buffer.MinDeltaTicks = elapsed;
                    buffer.MaxDeltaTicks = elapsed;
                }
                else
                {
                    buffer.MinDeltaTicks = Math.Min(buffer.MinDeltaTicks, elapsed);
                    buffer.MaxDeltaTicks = Math.Max(buffer.MaxDeltaTicks, elapsed);
                }
                // Mean over the intervals this channel actually saw, for the same reason
                // AverageValue above is: SampleSize bounds the window in device *messages*, so it
                // is neither the channel's sample count nor its interval count. At the k-th
                // interval SampleCount is exactly k, so this form self-seeds on the first interval,
                // stays exact for every window length, and cannot divide by zero when SampleSize
                // is 1. The device-level accumulator below now uses the same form, for the same
                // reason: a window is only SampleSize messages long while nobody moves SampleSize.
                buffer.AverageDeltaTicks += (elapsed - buffer.AverageDeltaTicks) / buffer.SampleCount;
            }
            buffer.LastSampleTicks = dataSample.TimestampTicks;

            ++buffer.SampleCount;
        }
    }

    /// <summary>
    /// Consumes a device message
    /// </summary>
    /// <param name="dataSample"></param>
    public void Log(DeviceMessage dataSample)
    {
        if (!Enabled)
        {
            return;
        }

        var published = false;

        lock (_gate)
        {
            _buffer.StatusList.Add(dataSample.DeviceStatus);
            _buffer.HasRollover = dataSample.Rollover;

            var latency = dataSample.AppTicks - dataSample.TimestampTicks;
            if (_buffer.SampleCount == 0)
            {
                _buffer.FirstSampleTicks = dataSample.AppTicks;
                _buffer.MinLatencyTicks = latency;
                _buffer.MaxLatencyTicks = latency;
            }
            else
            {
                _buffer.MinLatencyTicks = Math.Min(latency, _buffer.MinLatencyTicks);
                _buffer.MaxLatencyTicks = Math.Max(latency, _buffer.MaxLatencyTicks);
            }
            // Mean over the messages this window actually holds. Unlike the delta accumulator
            // below, this one has no first-message guard, so it runs for all SampleSize messages
            // of a completed window -- one more than the (SampleSize - 1) it used to divide by,
            // which read the average high by SampleSize / (SampleSize - 1) and produced Infinity
            // at the SampleSize of 1 the Summary flyout's NumericUpDown permits. SampleCount is
            // still the pre-increment count here, so this incremental form divides the first
            // value by 1 (seeding exactly) and the n-th by n, for any window length.
            _buffer.AverageLatencyTicks += (latency - _buffer.AverageLatencyTicks) / (_buffer.SampleCount + 1);

            if (_buffer.SampleCount > 0)
            {
                var elapsed = dataSample.AppTicks - _buffer.LastSampleTicks;
                if (_buffer.SampleCount == 1)
                {
                    _buffer.MinDeltaTicks = elapsed;
                    _buffer.MaxDeltaTicks = elapsed;
                }
                else
                {
                    _buffer.MinDeltaTicks = Math.Min(_buffer.MinDeltaTicks, elapsed);
                    _buffer.MaxDeltaTicks = Math.Max(_buffer.MaxDeltaTicks, elapsed);
                }

                // Mean over the intervals this window actually holds, in the same incremental form
                // as its three siblings. SampleCount is still the pre-increment count here, and a
                // window's k-th message closes its (k - 1)-th interval, so dividing by SampleCount
                // divides the first interval by 1 (seeding exactly) and the n-th by n. The
                // (SampleSize - 1) denominator this replaces was correct only while a window was
                // guaranteed to be exactly SampleSize messages long; SampleSize is live and
                // user-editable, so a size lowered to 1 mid-window left this branch open and
                // divided by zero.
                _buffer.AverageDeltaTicks += (elapsed - _buffer.AverageDeltaTicks) / _buffer.SampleCount;
            }
            _buffer.LastSampleTicks = dataSample.AppTicks;

            ++_buffer.SampleCount;

            // Closed on >=, not ==. Changing the size restarts the window, so the count should
            // never overshoot; equality alone made that an unguarded assumption, and a count that
            // got past its target - as it did for every size lowered mid-window before the restart
            // existed - stranded the summary forever, because this is the only path that publishes
            // a completed window.
            //
            // Read off the field rather than the property: this is the single comparison a window
            // turns on, and taking it under the same lock the setter writes under is what makes a
            // live resize atomic instead of a race between the new size and the old accumulation.
            if (_buffer.SampleCount >= _sampleSize)
            {
                SwapBuffer();
                published = true;
            }
        }

        if (published)
        {
            // Deliberately outside the lock. This runs on the streaming thread, and a subscriber
            // that reaches back into the logger - or a binding marshalling to a UI thread that is
            // itself inside the SampleSize setter - would otherwise do so while this thread still
            // holds the gate.
            NotifyResultsChanged();
        }
    }

    /// <summary>
    /// Publishes the in-progress window and starts a fresh one.
    /// </summary>
    /// <remarks>
    /// Must be called with <see cref="_gate"/> held: it reassigns both buffer fields, so any
    /// unsynchronized reader would be left pointing at the wrong one. Notification is the caller's
    /// job, so it can be raised after the lock is released.
    /// </remarks>
    private void SwapBuffer()
    {
        (_current, _buffer) = (_buffer, _current);
        _buffer.Reset();
    }

    private void NotifyResultsChanged()
    {
        OnPropertyChanged(nameof(Channels));
        OnPropertyChanged(nameof(ElapsedTime));
        OnPropertyChanged(nameof(LastUpdate));
        OnPropertyChanged(nameof(SampleRate));
        OnPropertyChanged(nameof(MaxDelta));
        OnPropertyChanged(nameof(MinDelta));
        OnPropertyChanged(nameof(AverageDelta));
        OnPropertyChanged(nameof(MaxLatency));
        OnPropertyChanged(nameof(MinLatency));
        OnPropertyChanged(nameof(AverageLatency));
        OnPropertyChanged(nameof(StatusList));
    }

    #endregion

    #region "Commands"

    [RelayCommand]
    private void ToggleEnabled()
    {
        if (Enabled)
        {
            Stop();
        }
        else
        {
            Start();
        }
    }

    /// <remarks>
    /// <see cref="Enabled"/> is written outside <see cref="_gate"/> on purpose. It is not gated
    /// state - both <c>Log</c> overloads read it without the lock - and assigning it raises
    /// <c>PropertyChanged</c>, which must not happen while the gate is held. Writing it before the
    /// gate is taken also makes the intent real: a message already past the <c>Enabled</c> check
    /// and waiting on the gate cannot be turned back, so stopping first is the only thing that
    /// narrows the window at all.
    /// </remarks>
    private void Start()
    {
        Enabled = false;

        lock (_gate)
        {
            _buffer.Reset();
        }

        Enabled = true;
    }

    /// <remarks>
    /// Touches nothing <see cref="_gate"/> guards, so it does not take it. See
    /// <see cref="Start"/> for why <see cref="Enabled"/> is not gated state.
    /// </remarks>
    private void Stop()
    {
        Enabled = false;
    }

    /// <remarks>
    /// Restores the default window length and clears both buffers as one step, then notifies.
    /// The sample size is written through the backing field rather than
    /// <see cref="SampleSize"/> because that setter notifies, and doing so from here would raise
    /// <c>PropertyChanged</c> with <see cref="_gate"/> still held by this method - a lock is
    /// re-entrant to its own thread, so the setter releasing its own <c>lock</c> block does not
    /// release the gate. See <see cref="_gate"/>.
    /// </remarks>
    [RelayCommand]
    private void Reset()
    {
        Enabled = false;

        bool sampleSizeChanged;

        lock (_gate)
        {
            sampleSizeChanged = _sampleSize != DEFAULT_SAMPLE_SIZE;
            _sampleSize = DEFAULT_SAMPLE_SIZE;
            _buffer.Reset();
            _current.Reset();
        }

        if (sampleSizeChanged)
        {
            OnPropertyChanged(nameof(SampleSize));
        }

        NotifyResultsChanged();
    }

    #endregion
}