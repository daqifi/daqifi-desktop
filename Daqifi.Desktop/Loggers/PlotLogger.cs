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

    public void Log(DataSample dataSample)
    {
        var key = (dataSample.DeviceSerialNo, dataSample.ChannelName);

        if (!LoggedChannels.ContainsKey(key))
        {
            AddChannelSeries(dataSample.ChannelName,dataSample.DeviceSerialNo, dataSample.Type, dataSample.Color);
        }
        else
        {
            //Check for a change in color
            // Hex color strings: compare ordinal/case-insensitively rather than lower-casing under the
            // current culture (which mangles ASCII letters in e.g. the Turkish locale).
            if (!string.Equals(LoggedChannels[key].Color.ToString(), dataSample.Color, StringComparison.OrdinalIgnoreCase))
            {
                LoggedChannels[key].Color = OxyColor.Parse(dataSample.Color.ToLowerInvariant());
            }
        }

        if (FirstTime == null) { FirstTime = new DateTime(dataSample.TimestampTicks); }

        var deltaTime = (dataSample.TimestampTicks - FirstTime.Value.Ticks) / 10000.0; //Ticks is 100 nanoseconds
        var scaledSampleValue = dataSample.Value;

        lock (PlotModel.SyncRoot)
        {
            if (_gapDetector.IsGap(key, dataSample.FirmwareDeltaMs))
            {
                LoggedPoints[key].Add(DataPoint.Undefined);
                if (LoggedPoints[key].Count >= 5000)
                {
                    LoggedPoints[key].RemoveAt(0);
                }
            }

            LoggedPoints[key].Add(new DataPoint(deltaTime, scaledSampleValue));
            if (LoggedPoints[key].Count >= 5000)
            {
                LoggedPoints[key].RemoveAt(0);
            }
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

        // Synchronize IsVisible with the IChannel
        if (LoggingManager.Instance != null)
        {
            var subscribedChannel = LoggingManager.Instance.SubscribedChannels
                .FirstOrDefault(ch => ch.DeviceSerialNo == DeviceSerialNo && ch.Name == channelName);

            if (subscribedChannel != null)
            {
                newLineSeries.IsVisible = subscribedChannel.IsVisible;
            }
            // Optional: else, default to true or log a warning if channel not found
            // For now, if not found, it will use the default LineSeries.IsVisible (which is true)
        }

        newLineSeries.YAxisKey = channelType switch
        {
            ChannelType.Analog => "Analog",
            ChannelType.Digital => "Digital",
            _ => newLineSeries.YAxisKey
        };

        // Mutate the shared collections under PlotModel.SyncRoot — the same lock held by Log()'s
        // point-append and by the render tick (InvalidatePlot + plot-stats recompute). Without it
        // these structural Adds raced the render-tick enumeration, so a concurrent OxyPlot render
        // or stats recompute could observe a half-updated dictionary/series list.
        lock (PlotModel.SyncRoot)
        {
            LoggedPoints.Add(key, newDataPoints);
            LoggedChannels.Add(key, newLineSeries);
            PlotModel.Series.Add(newLineSeries);
        }

        OnPropertyChanged(nameof(PlotModel));
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
    /// per-channel collections — <see cref="Log(DataSample)"/>'s point-append and
    /// <see cref="AddChannelSeries"/>'s series creation — takes that same lock, so enumerating the
    /// point lists here is consistent (no torn reads, no structural-modification race).
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

    public void ClearPlot()
    {
        LoggedChannels.Clear();
        LoggedPoints.Clear();
        _gapDetector.Clear();
        PlotModel.Series.Clear();
        PlotModel.InvalidatePlot(true);
        FirstTime = null;
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