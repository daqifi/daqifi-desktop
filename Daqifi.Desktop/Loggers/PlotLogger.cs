using Daqifi.Desktop.Channel;
using ChannelType = Daqifi.Core.Channel.ChannelType;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Helpers;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TickStyle = OxyPlot.Axes.TickStyle;

namespace Daqifi.Desktop.Logger;

public partial class PlotLogger : ObservableObject, ILogger
{
    #region Private Data
    [ObservableProperty]
    private PlotModel _plotModel;
    private readonly Stopwatch _stopwatch = new();
    private long _lastUpdateMilliSeconds;
    private int _precision = 4;
    private Dictionary<(string deviceSerial, string channelName), List<DataPoint>> _loggedPoints = [];
    [ObservableProperty]
    private Dictionary<(string deviceSerial, string channelName), LineSeries> _loggedChannels = [];
    private readonly TimestampGapDetector _gapDetector = new();
    private string _plotStatsSummary = EMPTY_PLOT_STATS_SUMMARY;
    #endregion

    #region Plot-stats UIA hook
    // Ground-truth summary of what the live plot is rendering, surfaced as a single
    // machine-readable string so an out-of-process UI test can assert the plot is showing
    // believable data while streaming (issue #560). OxyPlot draws every point to one canvas
    // and exposes no per-point UI Automation elements, so the harness cannot walk the tree
    // for points; this property is bound to an (invisible) UIA element's Name in
    // LiveGraphPane.xaml, mirroring the LoggingStatusText hook. Format (invariant culture):
    //   "series={count};points={n};nonfinite={n};last={y};min={y};max={y};firstx={x};lastx={x}"
    // where series = PlotModel.Series.Count, points = real sample points across all series
    // (gap markers excluded), nonfinite = real samples whose VALUE is NaN/Inf (expected 0),
    // last/min/max are the latest-in-time / extent sample values ("NaN" when no data), and
    // firstx/lastx are the rendered axis-X span in elapsed ms — the time-axis anchor (issue #573).
    // Derived from the formatter so the empty value can never drift from the real format.
    private static readonly string EMPTY_PLOT_STATS_SUMMARY = BuildPlotStatsSummary(0, []);

    /// <summary>
    /// Machine-readable summary of the live plot's rendered content, updated about once a
    /// second while streaming. Exposed for out-of-process UI automation (issue #560); not
    /// shown to users. See the format note above.
    /// </summary>
    public string PlotStatsSummary
    {
        get => _plotStatsSummary;
        private set => SetProperty(ref _plotStatsSummary, value);
    }
    #endregion

    #region Properties
    public DateTime? FirstTime { get; set; }

    // LoggedPoints keeps its explicit setter because it is intentionally private-set;
    // [ObservableProperty] would widen it to a public setter (the source generator
    // can't preserve setter accessibility).
    public Dictionary<(string deviceSerial, string channelName), List<DataPoint>> LoggedPoints
    {
        get => _loggedPoints;
        private set { _loggedPoints = value; OnPropertyChanged(); }
    }

    public int Precision
    {
        get => _precision;
        set
        {
            _precision = value;
            PlotModel.Axes[0].StringFormat = "0." + new string('#', _precision);
            PlotModel.InvalidatePlot(true);
            OnPropertyChanged();
        }
    }

    public bool ShowingMajorXAxisGrid
    {
        get => PlotModel.Axes[2].MajorGridlineThickness > 0;
        set
        {
            PlotModel.Axes[2].MajorGridlineThickness = value ? 1 : 0;
            PlotModel.InvalidatePlot(true);
            OnPropertyChanged();
        }
    }

    public bool ShowingMinorXAxisGrid
    {
        get => PlotModel.Axes[2].MinorGridlineThickness > 0;
        set
        {
            PlotModel.Axes[2].MinorGridlineThickness = value ? 1 : 0;
            PlotModel.InvalidatePlot(true);

            OnPropertyChanged();
        }
    }

    public bool ShowingMajorYAxisGrid
    {
        get => PlotModel.Axes[0].MajorGridlineThickness > 0;
        set
        {
            PlotModel.Axes[0].MajorGridlineThickness = value ? 1 : 0;
            PlotModel.InvalidatePlot(true);
            OnPropertyChanged();
        }
    }

    public bool ShowingMinorYAxisGrid
    {
        get => PlotModel.Axes[0].MinorGridlineThickness > 0;
        set
        {
            PlotModel.Axes[0].MinorGridlineThickness = value ? 1 : 0;
            PlotModel.InvalidatePlot(true);
            OnPropertyChanged();
        }
    }
    #endregion

    #region Channel visibility lookup
    /// <summary>
    /// Resolves a subscribed channel's current visibility so a newly created series starts out
    /// matching the channel's <c>IsVisible</c> state. Returns <see langword="null"/> when the
    /// channel is not subscribed, in which case the series keeps OxyPlot's default (visible).
    /// </summary>
    /// <remarks>
    /// Settable purely as a unit-test seam. The default implementation reads the
    /// <see cref="LoggingManager"/> singleton, whose constructor resolves services from
    /// <c>App.ServiceProvider</c> and therefore throws a <see cref="TypeInitializationException"/>
    /// outside a running application — which would otherwise make <see cref="Log(DataSample)"/>
    /// untestable.
    /// </remarks>
    internal Func<(string deviceSerial, string channelName), bool?> ChannelVisibilityLookup { get; set; }
        = LookUpSubscribedChannelVisibility;

    private static bool? LookUpSubscribedChannelVisibility((string deviceSerial, string channelName) key)
    {
        return LoggingManager.Instance?.SubscribedChannels
            .FirstOrDefault(ch => ch.DeviceSerialNo == key.deviceSerial && ch.Name == key.channelName)
            ?.IsVisible;
    }
    #endregion

    #region Constructor
    public PlotLogger()
    {
        LoggedPoints = [];
        PlotModel = new PlotModel();

        var analogAxis = new LinearAxis
        {
            Position = AxisPosition.Left,
            TickStyle = TickStyle.None,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Solid,
            TitleFontSize = 12,
            TitleFontWeight = FontWeights.Bold,
            MinimumPadding = 0.1,
            MaximumPadding = 0.1,
            StringFormat= "0.####",
            AxisDistance = 5,
            Key = "Analog",
            Title = "Analog (V)"
        };

        var digitalAxis = new LinearAxis
        {
            Position = AxisPosition.Right,
            TickStyle = TickStyle.None,
            MajorGridlineStyle = LineStyle.None,
            MinorGridlineStyle = LineStyle.None,
            MinorGridlineThickness=0,
            MajorGridlineThickness=0,
            MajorStep=1,
            MinorStep=1,
            TitleFontSize = 12,
            TitleFontWeight = FontWeights.Bold,
            AxisTitleDistance =- 10,
            Minimum = -0.1,
            Maximum = 1.1,
            MinimumPadding = 0.1,
            MaximumPadding = 0.1,
            AxisDistance = 5,
            Key = "Digital",
            Title = "Digital"
        };

        var timeAxis = new LinearAxis
        {
            Position = AxisPosition.Bottom,
            TickStyle = TickStyle.None,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Solid,
            TitleFontSize = 12,
            TitleFontWeight = FontWeights.Bold,
            Key = "Time",
            Title = "Time (ms)",

        };

        OxyPlotDarkTheme.ApplyTo(PlotModel);
        OxyPlotDarkTheme.ApplyTo(analogAxis);
        OxyPlotDarkTheme.ApplyTo(digitalAxis);
        OxyPlotDarkTheme.ApplyTo(timeAxis);

        PlotModel.Axes.Add(analogAxis);
        PlotModel.Axes.Add(digitalAxis);
        PlotModel.Axes.Add(timeAxis);

        // We use our own legend so disable theirs
        PlotModel.IsLegendVisible = false;

        CompositionTarget.Rendering += CompositionTargetRendering;
        _stopwatch.Start();
    }
    #endregion

    /// <summary>
    /// Buffers a sample for the live plot, creating the channel's series on first sight.
    /// </summary>
    /// <remarks>
    /// Runs on the device transport thread, so the whole read-then-mutate sequence is held under
    /// <c>PlotModel.SyncRoot</c> — the lock <see cref="ClearPlot"/> and the render tick also take.
    /// The membership check and the <see cref="FirstTime"/> seed used to sit outside it, which left
    /// two windows for a session-start <see cref="ClearPlot"/> on the UI thread: the key could be
    /// present for <c>ContainsKey</c> and gone by the indexed access below
    /// (<see cref="KeyNotFoundException"/> on a thread with no handler), and <see cref="FirstTime"/>
    /// could be nulled between the null check and the <c>.Value</c> dereference. See issue #759.
    /// </remarks>
    public void Log(DataSample dataSample)
    {
        var key = (dataSample.DeviceSerialNo, dataSample.ChannelName);
        var addedSeries = false;

        lock (PlotModel.SyncRoot)
        {
            if (!LoggedChannels.TryGetValue(key, out var series))
            {
                // The series may be absent because this is a new channel, or because ClearPlot()
                // removed it while this call waited for the lock. Either way it is re-created here
                // rather than dropping the sample.
                AddChannelSeries(
                    dataSample.ChannelName, dataSample.DeviceSerialNo, dataSample.Type, dataSample.Color);
                addedSeries = true;
            }
            // Check for a change in color. Hex color strings are compared ordinal/case-insensitively
            // rather than lower-cased under the current culture (which mangles ASCII letters in e.g.
            // the Turkish locale).
            else if (!string.Equals(series.Color.ToString(), dataSample.Color, StringComparison.OrdinalIgnoreCase))
            {
                series.Color = OxyColor.Parse(dataSample.Color.ToLowerInvariant());
            }

            FirstTime ??= new DateTime(dataSample.TimestampTicks);

            // Ticks is 100 nanoseconds
            var deltaTime = (dataSample.TimestampTicks - FirstTime.Value.Ticks) / 10000.0;
            var scaledSampleValue = dataSample.Value;
            var points = LoggedPoints[key];

            if (_gapDetector.IsGap(key, dataSample.FirmwareDeltaMs))
            {
                points.Add(DataPoint.Undefined);
                if (points.Count >= 5000)
                {
                    points.RemoveAt(0);
                }
            }

            points.Add(new DataPoint(deltaTime, scaledSampleValue));
            if (points.Count >= 5000)
            {
                points.RemoveAt(0);
            }
        }

        // Change notifications are raised outside the lock so a binding handler can never re-enter
        // the logger while it is held. AddChannelSeries used to raise the PlotModel one itself.
        if (addedSeries)
        {
            OnPropertyChanged(nameof(PlotModel));
        }

        OnPropertyChanged(nameof(LoggedPoints));
    }

    /// <summary>
    /// Consumes a device message
    /// </summary>
    /// <param name="dataSample"></param>
    public void Log(DeviceMessage dataSample)
    {
        // No-op
    }

    /// <summary>
    /// Creates a channel's line series and point buffer and registers both with the plot.
    /// </summary>
    /// <remarks>
    /// The caller must hold <c>PlotModel.SyncRoot</c>. These structural Adds race the render tick's
    /// enumeration (<see cref="CompositionTargetRendering"/>, <see cref="UpdatePlotStatsSummary"/>)
    /// and <see cref="ClearPlot"/>'s removals, so without the lock a concurrent OxyPlot render or
    /// stats recompute could observe a half-updated dictionary/series list. The caller also raises
    /// the <c>PlotModel</c> change notification, which must not be raised while the lock is held.
    /// </remarks>
    private void AddChannelSeries(string channelName, string DeviceSerialNo, ChannelType channelType, string newColor)
    {
        var key = (DeviceSerialNo, channelName);
        var newDataPoints = new List<DataPoint>();

        var serialSuffix = DeviceSerialNo?.Length > 4
            ? $"...{DeviceSerialNo[^4..]}"
            : DeviceSerialNo;

        var newLineSeries = new LineSeries
        {
            Title = channelName,
            ItemsSource = newDataPoints,
            Color = OxyColor.Parse(newColor),
            TrackerFormatString = $"{channelName} ({serialSuffix})\n{{1}}: {{2:0.###}}\n{{3}}: {{4:0.######}}"
        };

        // Synchronize IsVisible with the IChannel. When the channel is not subscribed the lookup
        // returns null and the series keeps OxyPlot's default IsVisible (true).
        var subscribedVisibility = ChannelVisibilityLookup(key);
        if (subscribedVisibility.HasValue)
        {
            newLineSeries.IsVisible = subscribedVisibility.Value;
        }

        newLineSeries.YAxisKey = channelType switch
        {
            ChannelType.Analog => "Analog",
            ChannelType.Digital => "Digital",
            _ => newLineSeries.YAxisKey
        };

        LoggedPoints.Add(key, newDataPoints);
        LoggedChannels.Add(key, newLineSeries);
        PlotModel.Series.Add(newLineSeries);
    }

    private void CompositionTargetRendering(object? sender, EventArgs e)
    {
        if (_stopwatch.ElapsedMilliseconds > _lastUpdateMilliSeconds + 1000) // Or your existing update interval
        {
            lock (PlotModel.SyncRoot)
            {
                // Iterate through subscribed channels to update series visibility
                if (LoggingManager.Instance != null) // Ensure LoggingManager instance is available
                {
                    foreach (var channel in LoggingManager.Instance.SubscribedChannels)
                    {
                        var key = (channel.DeviceSerialNo, channel.Name);
                        if (LoggedChannels.TryGetValue(key, out LineSeries? series))
                        {
                            if (series.IsVisible != channel.IsVisible)
                            {
                                series.IsVisible = channel.IsVisible;
                            }
                        }
                    }
                }
                PlotModel.InvalidatePlot(true); // This will redraw the plot with updated series visibility
                UpdatePlotStatsSummary();
                _lastUpdateMilliSeconds = _stopwatch.ElapsedMilliseconds;
            }
        }
    }

    /// <summary>
    /// Recomputes <see cref="PlotStatsSummary"/> from the currently buffered points. Called on the
    /// once-a-second render tick while holding <c>PlotModel.SyncRoot</c>. Every mutation of the
    /// per-channel collections — <see cref="Log(DataSample)"/>'s point-append,
    /// <see cref="AddChannelSeries"/>'s series creation and <see cref="ClearPlot"/>'s removals —
    /// takes that same lock, so enumerating the point lists here is consistent (no torn reads, no
    /// structural-modification race).
    /// Gap markers are inserted as <c>DataPoint.Undefined</c> (NaN X); a real sample always has a
    /// finite X (elapsed ms), so an NaN X distinguishes a gap from data and lets a genuinely
    /// non-finite sample VALUE still be counted (nonfinite) rather than hidden.
    /// </summary>
    private void UpdatePlotStatsSummary()
    {
        PlotStatsSummary = BuildPlotStatsSummary(PlotModel.Series.Count, LoggedPoints.Values);
    }

    /// <summary>
    /// Builds the <see cref="PlotStatsSummary"/> string from a series count and the per-series
    /// point lists. Pure and side-effect-free so it can be unit-tested without a live PlotModel.
    /// Gap markers (<c>DataPoint.Undefined</c>, i.e. NaN X) are excluded from the point count and
    /// never mistaken for a non-finite sample value; <c>nonfinite</c> counts only real samples
    /// (finite X) whose VALUE (Y) is NaN/Inf; <c>last</c> is the value at the greatest X seen.
    /// <c>firstx</c>/<c>lastx</c> are the smallest/greatest axis X (elapsed ms) across finite-valued
    /// samples — the harness's window onto the time-axis anchor (issue #573).
    /// </summary>
    internal static string BuildPlotStatsSummary(int seriesCount, IEnumerable<List<DataPoint>> pointLists)
    {
        long points = 0;
        long nonFinite = 0;
        var min = double.NaN;
        var max = double.NaN;
        var last = double.NaN;
        var firstX = double.NaN;
        var lastX = double.NegativeInfinity;
        var any = false;

        foreach (var pointList in pointLists)
        {
            for (var i = 0; i < pointList.Count; i++)
            {
                var point = pointList[i];
                if (double.IsNaN(point.X))
                {
                    // Gap marker (DataPoint.Undefined), not data.
                    continue;
                }

                points++;

                var y = point.Y;
                if (double.IsNaN(y) || double.IsInfinity(y))
                {
                    nonFinite++;
                    continue;
                }

                if (!any)
                {
                    min = max = y;
                    any = true;
                }
                else
                {
                    if (y < min)
                    {
                        min = y;
                    }

                    if (y > max)
                    {
                        max = y;
                    }
                }

                if (double.IsNaN(firstX) || point.X < firstX)
                {
                    firstX = point.X;
                }

                if (point.X >= lastX)
                {
                    lastX = point.X;
                    last = y;
                }
            }
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "series={0};points={1};nonfinite={2};last={3:R};min={4:R};max={5:R};firstx={6:R};lastx={7:R}",
            seriesCount, points, nonFinite, last, min, max,
            firstX, double.IsNegativeInfinity(lastX) ? double.NaN : lastX);
    }

    /// <summary>
    /// Drops every buffered point, series and gap-detector entry, returning the plot to its empty
    /// state and releasing the time-axis anchor so the next session re-seeds it.
    /// </summary>
    /// <remarks>
    /// Runs on the UI thread (<c>LoggingManager.OnActiveChanged</c> at session start) while
    /// <see cref="Log(DataSample)"/> runs on the device transport thread, so these removals take
    /// <c>PlotModel.SyncRoot</c> for exactly the reason the additions do. Unsubscribing the channel
    /// handlers just before this call narrows the window but cannot close it — an invocation already
    /// in flight still completes, because C# snapshots a delegate's invocation list before invoking.
    /// See issue #759.
    /// </remarks>
    public void ClearPlot()
    {
        lock (PlotModel.SyncRoot)
        {
            LoggedChannels.Clear();
            LoggedPoints.Clear();
            _gapDetector.Clear();
            PlotModel.Series.Clear();
            PlotModel.InvalidatePlot(true);
            FirstTime = null;
        }

        // Outside the lock, like every other change notification here: no binding handler may run
        // while the plot lock is held. PlotStatsSummary is derived state that the render tick
        // recomputes about once a second, so publishing it a moment later is harmless.
        PlotStatsSummary = EMPTY_PLOT_STATS_SUMMARY;
        OnPropertyChanged(nameof(LoggedChannels));
        OnPropertyChanged(nameof(LoggedPoints));
        OnPropertyChanged(nameof(PlotModel));
    }

    #region Commands
    [RelayCommand]
    private void ZoomInX()
    {
        PlotModel.Axes[2].ZoomAtCenter(1.25);
        PlotModel.InvalidatePlot(true);
    }

    [RelayCommand]
    private void ZoomOutX()
    {
        PlotModel.Axes[2].ZoomAtCenter(0.8);
        PlotModel.InvalidatePlot(true);
    }

    [RelayCommand]
    private void ZoomInY()
    {
        PlotModel.Axes[0].ZoomAtCenter(1.25);
        PlotModel.InvalidatePlot(true);
    }

    [RelayCommand]
    private void ZoomOutY()
    {
        PlotModel.Axes[0].ZoomAtCenter(0.8);
        PlotModel.InvalidatePlot(true);
    }

    [RelayCommand]
    private void ResetZoomLiveGraph()
    {
        PlotModel.ResetAllAxes();
        PlotModel.InvalidatePlot(true);
    }

    [RelayCommand]
    private void SaveLiveGraph()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            DefaultExt = ".png",
            Filter = "PNG|*.png"
        };

        var result = dialog.ShowDialog();

        if (result == false) { return; }

        var pngExporter = new OxyPlot.Wpf.PngExporter { Width = 1024, Height = 768 };
        using var stream = File.Create(dialog.FileName);
        pngExporter.Export(PlotModel, stream);
    }
    #endregion
}