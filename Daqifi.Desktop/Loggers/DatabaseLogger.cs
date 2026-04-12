using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Common.Loggers;
using ChannelDirection = Daqifi.Core.Channel.ChannelDirection;
using ChannelType = Daqifi.Core.Channel.ChannelType;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.Helpers;
using Daqifi.Desktop.View;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Exception = System.Exception;
using TickStyle = OxyPlot.Axes.TickStyle;
using Microsoft.EntityFrameworkCore;
using EFCore.BulkExtensions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Threading;
using Application = System.Windows.Application;
using FontWeights = OxyPlot.FontWeights;

namespace Daqifi.Desktop.Logger;

public partial class LoggedSeriesLegendItem : ObservableObject
{
    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private string _channelName;

    [ObservableProperty]
    private string _deviceSerialNo;

    /// <summary>
    /// Truncated serial number for compact legend display (e.g., "...4104").
    /// </summary>
    public string TruncatedSerialNo => _deviceSerialNo?.Length > 4
        ? $"...{_deviceSerialNo[^4..]}"
        : _deviceSerialNo ?? string.Empty;

    [ObservableProperty]
    private OxyColor _seriesColor;

    private bool _isVisible;
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (SetProperty(ref _isVisible, value) && ActualSeries != null)
            {
                ActualSeries.IsVisible = _isVisible;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _plotModel?.InvalidatePlot(true);
                    _databaseLogger?.SetMinimapSeriesVisibility(_deviceSerialNo, _channelName, _isVisible);
                });
            }
        }
    }

    public LineSeries ActualSeries { get; }
    private readonly PlotModel _plotModel;
    private readonly DatabaseLogger _databaseLogger;

    /// <summary>
    /// Initializes a new legend item linked to a plot series and optional minimap sync.
    /// </summary>
    /// <param name="displayName">Full display name including channel and device info.</param>
    /// <param name="channelName">Channel identifier (e.g., "AI0").</param>
    /// <param name="deviceSerialNo">Device serial number for grouping.</param>
    /// <param name="seriesColor">Color of the associated plot series.</param>
    /// <param name="isVisible">Initial visibility state of the series.</param>
    /// <param name="actualSeries">The OxyPlot LineSeries this legend item controls.</param>
    /// <param name="plotModel">The main PlotModel to invalidate on visibility changes.</param>
    /// <param name="databaseLogger">Optional logger for syncing minimap series visibility.</param>
    public LoggedSeriesLegendItem(
        string displayName,
        string channelName,
        string deviceSerialNo,
        OxyColor seriesColor,
        bool isVisible,
        LineSeries actualSeries,
        PlotModel plotModel,
        DatabaseLogger databaseLogger = null)
    {
        _displayName = displayName;
        _channelName = channelName;
        _deviceSerialNo = deviceSerialNo;
        _seriesColor = seriesColor;
        _isVisible = isVisible; // Initialize the backing field
        ActualSeries = actualSeries;
        ActualSeries.IsVisible = isVisible; // Ensure series visibility matches
        _plotModel = plotModel;
        _databaseLogger = databaseLogger;
    }
}

public partial class DatabaseLogger : ObservableObject, ILogger, IDisposable
{
    #region Constants
    private const int MINIMAP_BUCKET_COUNT = 800;
    private const int MAIN_PLOT_BUCKET_COUNT = 2000;
    private const int MAX_IN_MEMORY_POINTS = 10_000_000;
    private const int INITIAL_LOAD_POINTS = 100_000;
    #endregion

    #region Private Data
    public ObservableCollection<LoggedSeriesLegendItem> LegendItems { get; } = new();
    public ObservableCollection<DeviceLegendGroup> DeviceLegendGroups { get; } = new();
    private readonly Dictionary<(string deviceSerial, string channelName), List<DataPoint>> _allSessionPoints = new();
    private readonly Dictionary<(string deviceSerial, string channelName), List<DataPoint>> _downsampledCache = new();
    private readonly BlockingCollection<DataSample> _buffer = new();
    private readonly Dictionary<(string deviceSerial, string channelName), LineSeries> _minimapSeries = new();

    private DateTime? _firstTime;
    private readonly AppLogger _appLogger = AppLogger.Instance;
    private readonly IDbContextFactory<LoggingContext> _loggingContext;
    private readonly ManualResetEventSlim _consumerGate = new(true);
    private RectangleAnnotation _minimapSelectionRect;
    private RectangleAnnotation _minimapDimLeft;
    private RectangleAnnotation _minimapDimRight;
    private MinimapInteractionController _minimapInteraction;
    internal bool IsSyncingFromMinimap;
    private double _lastViewportMin = double.NaN;
    private double _lastViewportMax = double.NaN;
    private bool _viewportDirty;
    private DispatcherTimer _viewportThrottleTimer;

    [ObservableProperty]
    private PlotModel _plotModel;

    /// <summary>
    /// PlotModel for the overview minimap showing downsampled data and a selection rectangle.
    /// </summary>
    [ObservableProperty]
    private PlotModel _minimapPlotModel;

    /// <summary>
    /// Controls visibility of the channel legend panel.
    /// </summary>
    [ObservableProperty]
    private bool _isLegendPanelVisible = true;

    /// <summary>
    /// Indicates whether a session with data is currently loaded.
    /// Controls visibility of the minimap, legend, and empty state placeholder.
    /// </summary>
    [ObservableProperty]
    private bool _hasSessionData;
    #endregion

    #region Legend
    [RelayCommand]
    private void ToggleLegendPanel()
    {
        IsLegendPanelVisible = !IsLegendPanelVisible;
    }
    #endregion

    #region Constructor
    public DatabaseLogger(IDbContextFactory<LoggingContext> loggingContext)
    {
        _loggingContext = loggingContext;

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
            StringFormat = "0.###",
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
            MinorGridlineThickness = 0,
            MajorGridlineThickness = 0,
            MajorStep = 1,
            MinorStep = 1,
            TitleFontSize = 12,
            TitleFontWeight = FontWeights.Bold,
            AxisTitleDistance = -10,
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
            Title = "Time (ms)"
        };

        // OxyPlot.Legends.Legend legend = new OxyPlot.Legends.Legend
        // {
        //     LegendOrientation = OxyPlot.Legends.LegendOrientation.Vertical,
        //     LegendPlacement = OxyPlot.Legends.LegendPlacement.Outside,
        //     LegendItemMode = LegendItemMode.ToggleVisibility // Attempt to set direct interactivity
        // };

        PlotModel.Axes.Add(analogAxis);
        PlotModel.Axes.Add(digitalAxis);
        PlotModel.Axes.Add(timeAxis);
        PlotModel.IsLegendVisible = false; // Disable the built-in legend

        // Subscribe to main time axis changes for minimap sync
        timeAxis.AxisChanged += OnMainTimeAxisChanged;

        // Throttle viewport updates from main plot interaction to 60fps
        _viewportThrottleTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _viewportThrottleTimer.Tick += OnViewportThrottleTick;
        _viewportThrottleTimer.Start();

        // Initialize minimap PlotModel
        InitializeMinimapPlotModel();

        var consumerThread = new Thread(Consumer) { IsBackground = true };
        consumerThread.Start();
    }
    #endregion

    #region Minimap Initialization
    private void InitializeMinimapPlotModel()
    {
        MinimapPlotModel = new PlotModel
        {
            IsLegendVisible = false,
            PlotMargins = new OxyThickness(4, 2, 4, 2),
            Padding = new OxyThickness(0)
        };

        var minimapTimeAxis = new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Key = "MinimapTime",
            TickStyle = TickStyle.None,
            MajorGridlineStyle = LineStyle.None,
            MinorGridlineStyle = LineStyle.None,
            TitleFontSize = 0,
            FontSize = 0,
            IsZoomEnabled = false,
            IsPanEnabled = false
        };

        var minimapYAxis = new LinearAxis
        {
            Position = AxisPosition.Left,
            Key = "MinimapY",
            TickStyle = TickStyle.None,
            MajorGridlineStyle = LineStyle.None,
            MinorGridlineStyle = LineStyle.None,
            TitleFontSize = 0,
            FontSize = 0,
            IsZoomEnabled = false,
            IsPanEnabled = false,
            MinimumPadding = 0.1,
            MaximumPadding = 0.1
        };

        MinimapPlotModel.Axes.Add(minimapTimeAxis);
        MinimapPlotModel.Axes.Add(minimapYAxis);

        // Dim overlays for areas outside the selected range
        _minimapDimLeft = new RectangleAnnotation
        {
            Fill = OxyColor.FromArgb(150, 200, 200, 200),
            Stroke = OxyColors.Transparent,
            StrokeThickness = 0,
            MinimumX = -1e18,
            MaximumX = 0,
            MinimumY = -1e18,
            MaximumY = 1e18,
            Layer = AnnotationLayer.AboveSeries,
            XAxisKey = "MinimapTime",
            YAxisKey = "MinimapY"
        };

        _minimapDimRight = new RectangleAnnotation
        {
            Fill = OxyColor.FromArgb(150, 200, 200, 200),
            Stroke = OxyColors.Transparent,
            StrokeThickness = 0,
            MinimumX = 0,
            MaximumX = 1e18,
            MinimumY = -1e18,
            MaximumY = 1e18,
            Layer = AnnotationLayer.AboveSeries,
            XAxisKey = "MinimapTime",
            YAxisKey = "MinimapY"
        };

        // Selection rectangle border
        _minimapSelectionRect = new RectangleAnnotation
        {
            Fill = OxyColors.Transparent,
            Stroke = OxyColor.FromRgb(0, 90, 180),
            StrokeThickness = 3,
            MinimumY = -1e18,
            MaximumY = 1e18,
            Layer = AnnotationLayer.AboveSeries,
            XAxisKey = "MinimapTime",
            YAxisKey = "MinimapY"
        };

        MinimapPlotModel.Annotations.Add(_minimapDimLeft);
        MinimapPlotModel.Annotations.Add(_minimapDimRight);
        MinimapPlotModel.Annotations.Add(_minimapSelectionRect);

        _minimapInteraction = new MinimapInteractionController(
            PlotModel,
            MinimapPlotModel,
            _minimapSelectionRect,
            _minimapDimLeft,
            _minimapDimRight,
            this);
    }
    #endregion

    /// <summary>
    /// Producer
    /// </summary>
    /// <param name="dataSample"></param>
    public void Log(DataSample dataSample)
    {
        _buffer.Add(dataSample);
    }

    /// <summary>
    /// Consumes a device message
    /// </summary>
    /// <param name="dataSample"></param>
    public void Log(DeviceMessage dataSample)
    {
        // No-op
    }

    #region Private Data
    private void Consumer()
    {
        var samples = new List<DataSample>();
        int bufferCount;
        while (true)
        {
            try
            {
                Thread.Sleep(100);

                bufferCount = _buffer.Count;

                if (bufferCount < 1) { continue; }

                // Wait if the consumer is suspended (e.g. during delete-all)
                _consumerGate.Wait();

                // Remove the samples from the collection
                for (var i = 0; i < bufferCount; i++)
                {
                    if (_buffer.TryTake(out var sample)) { samples.Add(sample); }
                }

                using (var context = _loggingContext.CreateDbContext())
                {

                    // Start a new transaction for bulk insert
                    using var transaction = context.Database.BeginTransaction();
                    // Perform the bulk insert
                    context.BulkInsert(samples);

                    // Commit the transaction after the bulk insert
                    transaction.Commit();
                }
                samples.Clear();
            }
            catch (Exception ex)
            {
                _appLogger.Error(ex, "Failed in Consumer Thread");
            }
        }
    }
    #endregion

    public void ClearPlot()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _firstTime = null;
            _lastViewportMin = double.NaN;
            _lastViewportMax = double.NaN;
            _allSessionPoints.Clear();
            _downsampledCache.Clear();
            _minimapSeries.Clear();
            PlotModel.Series.Clear();
            LegendItems.Clear();
            DeviceLegendGroups.Clear();
            PlotModel.Title = string.Empty;
            PlotModel.Subtitle = string.Empty;
            PlotModel.InvalidatePlot(true);

            MinimapPlotModel.Series.Clear();
            MinimapPlotModel.InvalidatePlot(true);

            HasSessionData = false;
        });
    }

    public void DisplayLoggingSession(LoggingSession session)
    {
        try
        {
            // ClearPlot is already dispatcher-wrapped
            ClearPlot();

            var sessionName = session.Name;
            var subtitle = string.Empty;
            var tempSeriesList = new List<LineSeries>();
            var tempLegendItemsList = new List<LoggedSeriesLegendItem>();
            int totalSamplesCount;

            // ── Phase 1: Fast initial load (<1s) ──────────────────────────
            // Get channel metadata from first timestamp (6ms via index)
            // and load a small initial batch for immediate display
            using (var context = _loggingContext.CreateDbContext())
            {
                context.ChangeTracker.AutoDetectChangesEnabled = false;

                var baseQuery = context.Samples.AsNoTracking()
                    .Where(s => s.LoggingSessionID == session.ID);

                // Get the first timestamp to extract channel info (instant via composite index)
                var firstSample = baseQuery
                    .OrderBy(s => s.TimestampTicks)
                    .Select(s => new { s.TimestampTicks })
                    .FirstOrDefault();

                if (firstSample == null)
                {
                    // Empty session
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        PlotModel.Title = sessionName;
                        HasSessionData = false;
                        PlotModel.InvalidatePlot(true);
                    });
                    return;
                }

                // Get all channels from the first timestamp (32 rows, instant)
                var channelInfoList = baseQuery
                    .Where(s => s.TimestampTicks == firstSample.TimestampTicks)
                    .Select(s => new { s.ChannelName, s.DeviceSerialNo, s.Type, s.Color })
                    .ToList()
                    .NaturalOrderBy(s => s.ChannelName)
                    .ToList();

                foreach (var chInfo in channelInfoList)
                {
                    var (series, legendItem) = AddChannelSeries(chInfo.ChannelName, chInfo.DeviceSerialNo, chInfo.Type, chInfo.Color);
                    tempSeriesList.Add(series);
                    tempLegendItemsList.Add(legendItem);
                }

                // Load initial batch for fast display (100K rows, ~16ms via index)
                foreach (var sample in baseQuery
                    .OrderBy(s => s.TimestampTicks)
                    .Select(s => new { s.ChannelName, s.DeviceSerialNo, s.TimestampTicks, s.Value })
                    .Take(INITIAL_LOAD_POINTS)
                    .AsEnumerable())
                {
                    var key = (sample.DeviceSerialNo, sample.ChannelName);
                    if (_firstTime == null) { _firstTime = new DateTime(sample.TimestampTicks); }
                    var deltaTime = (sample.TimestampTicks - _firstTime.Value.Ticks) / 10000.0;

                    if (_allSessionPoints.TryGetValue(key, out var points))
                    {
                        points.Add(new DataPoint(deltaTime, sample.Value));
                    }
                }

                totalSamplesCount = baseQuery.Count();
            }

            // Show the initial data immediately
            var initialMinimapData = PrepareMinimapData(tempSeriesList);
            Application.Current.Dispatcher.Invoke(() =>
            {
                PlotModel.Title = sessionName;
                PlotModel.Subtitle = totalSamplesCount > INITIAL_LOAD_POINTS
                    ? "\nLoading full dataset..."
                    : string.Empty;

                SetupUiCollections(tempSeriesList, tempLegendItemsList);
                SetupMinimapSeries(initialMinimapData);
                HasSessionData = tempSeriesList.Count > 0;
                PlotModel.InvalidatePlot(true);
            });

            // ── Phase 2: Load remaining data in background ────────────────
            if (totalSamplesCount > INITIAL_LOAD_POINTS)
            {
                if (totalSamplesCount > MAX_IN_MEMORY_POINTS)
                {
                    subtitle = $"\nShowing first {MAX_IN_MEMORY_POINTS:n0} of {totalSamplesCount:n0} data points";
                }

                // Clear phase 1 data and reload the full set
                foreach (var kvp in _allSessionPoints)
                {
                    kvp.Value.Clear();
                }
                _firstTime = null;

                using (var context = _loggingContext.CreateDbContext())
                {
                    context.ChangeTracker.AutoDetectChangesEnabled = false;

                    foreach (var sample in context.Samples.AsNoTracking()
                        .Where(s => s.LoggingSessionID == session.ID)
                        .OrderBy(s => s.TimestampTicks)
                        .Select(s => new { s.ChannelName, s.DeviceSerialNo, s.TimestampTicks, s.Value })
                        .Take(MAX_IN_MEMORY_POINTS)
                        .AsEnumerable())
                    {
                        var key = (sample.DeviceSerialNo, sample.ChannelName);
                        if (_firstTime == null) { _firstTime = new DateTime(sample.TimestampTicks); }
                        var deltaTime = (sample.TimestampTicks - _firstTime.Value.Ticks) / 10000.0;

                        if (_allSessionPoints.TryGetValue(key, out var points))
                        {
                            points.Add(new DataPoint(deltaTime, sample.Value));
                        }
                    }
                }

                // Refresh UI with full data
                var fullMinimapData = PrepareMinimapData(tempSeriesList);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    PlotModel.Subtitle = subtitle;

                    // Update main plot series with full downsampled data
                    foreach (var series in PlotModel.Series.OfType<LineSeries>())
                    {
                        if (series.Tag is (string deviceSerial, string channelName)
                            && _allSessionPoints.TryGetValue((deviceSerial, channelName), out var points))
                        {
                            var key = (deviceSerial, channelName);
                            if (!_downsampledCache.TryGetValue(key, out var cached))
                            {
                                cached = new List<DataPoint>(MAIN_PLOT_BUCKET_COUNT * 2);
                                _downsampledCache[key] = cached;
                            }

                            cached.Clear();
                            if (points.Count > MAIN_PLOT_BUCKET_COUNT * 2)
                            {
                                cached.AddRange(MinMaxDownsampler.Downsample(points, MAIN_PLOT_BUCKET_COUNT));
                            }
                            else
                            {
                                cached.AddRange(points);
                            }

                            series.ItemsSource = cached;
                        }
                    }

                    // Refresh minimap with full data
                    SetupMinimapSeries(fullMinimapData);
                    _lastViewportMin = double.NaN;
                    _lastViewportMax = double.NaN;
                    PlotModel.InvalidatePlot(true);
                });
            }
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Failed in DisplayLoggingSession");
        }
    }

    /// <summary>
    /// Prepares downsampled minimap series data from _allSessionPoints on the background thread.
    /// </summary>
    private List<(string channelName, string deviceSerial, OxyColor color, List<DataPoint> downsampled)>
        PrepareMinimapData(List<LineSeries> seriesList)
    {
        var result = new List<(string channelName, string deviceSerial, OxyColor color, List<DataPoint> downsampled)>();
        foreach (var kvp in _allSessionPoints)
        {
            if (kvp.Value.Count > 0)
            {
                var downsampled = MinMaxDownsampler.Downsample(kvp.Value, MINIMAP_BUCKET_COUNT);
                var matchingSeries = seriesList.FirstOrDefault(s =>
                    s.Tag is (string ds, string cn) && ds == kvp.Key.deviceSerial && cn == kvp.Key.channelName);
                result.Add((kvp.Key.channelName, kvp.Key.deviceSerial, matchingSeries?.Color ?? OxyColors.Gray, downsampled));
            }
        }
        return result;
    }

    /// <summary>
    /// Sets up UI collections (legend items, device groups, series) on the UI thread.
    /// </summary>
    private void SetupUiCollections(List<LineSeries> seriesList, List<LoggedSeriesLegendItem> legendItems)
    {
        foreach (var legendItem in legendItems)
        {
            LegendItems.Add(legendItem);
        }

        DeviceLegendGroups.Clear();
        var groupDict = new Dictionary<string, DeviceLegendGroup>();
        foreach (var legendItem in legendItems)
        {
            if (!groupDict.TryGetValue(legendItem.DeviceSerialNo, out var group))
            {
                group = new DeviceLegendGroup(legendItem.DeviceSerialNo);
                groupDict[legendItem.DeviceSerialNo] = group;
                DeviceLegendGroups.Add(group);
            }
            group.Channels.Add(legendItem);
        }

        foreach (var series in seriesList)
        {
            PlotModel.Series.Add(series);
            if (series.Tag is (string deviceSerial, string channelName)
                && _allSessionPoints.TryGetValue((deviceSerial, channelName), out var points))
            {
                var key = (deviceSerial, channelName);
                if (!_downsampledCache.TryGetValue(key, out var cached))
                {
                    cached = new List<DataPoint>(MAIN_PLOT_BUCKET_COUNT * 2);
                    _downsampledCache[key] = cached;
                }

                cached.Clear();
                if (points.Count > MAIN_PLOT_BUCKET_COUNT * 2)
                {
                    cached.AddRange(MinMaxDownsampler.Downsample(points, MAIN_PLOT_BUCKET_COUNT));
                }
                else
                {
                    cached.AddRange(points);
                }

                series.ItemsSource = cached;
            }
        }
    }

    /// <summary>
    /// Populates minimap series and sets up the selection rectangle on the UI thread.
    /// </summary>
    private void SetupMinimapSeries(
        List<(string channelName, string deviceSerial, OxyColor color, List<DataPoint> downsampled)> minimapData)
    {
        MinimapPlotModel.Series.Clear();
        _minimapSeries.Clear();
        foreach (var (channelName, deviceSerial, color, downsampled) in minimapData)
        {
            var minimapLine = new LineSeries
            {
                Color = color,
                StrokeThickness = 1,
                ItemsSource = downsampled,
                XAxisKey = "MinimapTime",
                YAxisKey = "MinimapY"
            };
            MinimapPlotModel.Series.Add(minimapLine);
            _minimapSeries[(deviceSerial, channelName)] = minimapLine;
        }

        MinimapPlotModel.ResetAllAxes();

        if (minimapData.Count > 0)
        {
            var dataMinX = minimapData.Where(d => d.downsampled.Count > 0).Min(d => d.downsampled[0].X);
            var dataMaxX = minimapData.Where(d => d.downsampled.Count > 0).Max(d => d.downsampled[^1].X);
            _minimapSelectionRect.MinimumX = dataMinX;
            _minimapSelectionRect.MaximumX = dataMaxX;
            _minimapDimLeft.MaximumX = dataMinX;
            _minimapDimRight.MinimumX = dataMaxX;
        }

        MinimapPlotModel.InvalidatePlot(true);
    }

    public void DeleteLoggingSession(LoggingSession session)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var context = _loggingContext.CreateDbContext();
            var connection = context.Database.GetDbConnection();
            connection.Open();

            using var transaction = connection.BeginTransaction();

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM Samples WHERE LoggingSessionID = @id";
                var param = cmd.CreateParameter();
                param.ParameterName = "@id";
                param.Value = session.ID;
                cmd.Parameters.Add(param);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM Sessions WHERE ID = @id";
                var param = cmd.CreateParameter();
                param.ParameterName = "@id";
                param.Value = session.ID;
                cmd.Parameters.Add(param);
                cmd.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Failed in DeleteLoggingSession");
        }
        finally
        {
            stopwatch.Stop();
            _appLogger.Information($"DeleteLoggingSession completed in {stopwatch.ElapsedMilliseconds}ms");
        }
    }

    /// <summary>
    /// Drains the sample buffer to prevent stale data from being inserted after a database reset.
    /// </summary>
    public void ClearBuffer()
    {
        while (_buffer.TryTake(out _))
        {
        }
    }

    /// <summary>
    /// Suspends the background consumer thread so no new database connections are opened.
    /// Must be followed by <see cref="ResumeConsumer"/>.
    /// </summary>
    public void SuspendConsumer()
    {
        _consumerGate.Reset();
        // Give the consumer time to finish any in-flight DB operation
        Thread.Sleep(200);
    }

    /// <summary>
    /// Resumes the background consumer thread after a <see cref="SuspendConsumer"/> call.
    /// </summary>
    public void ResumeConsumer()
    {
        _consumerGate.Set();
    }

    private (LineSeries series, LoggedSeriesLegendItem legendItem) AddChannelSeries(string channelName, string deviceSerialNo, ChannelType type, string color)
    {
        var key = (DeviceSerialNo: deviceSerialNo, channelName);
        _allSessionPoints.Add(key, []);

        var newLineSeries = new LineSeries
        {
            Title = $"{channelName} : ({deviceSerialNo})",
            Tag = (deviceSerialNo, channelName),
            Color = OxyColor.Parse(color),
            IsVisible = true
        };

        var legendItem = new LoggedSeriesLegendItem(
            newLineSeries.Title,
            channelName,
            deviceSerialNo,
            newLineSeries.Color,
            newLineSeries.IsVisible,
            newLineSeries,
            PlotModel,
            this);

        newLineSeries.YAxisKey = type switch
        {
            ChannelType.Analog => "Analog",
            ChannelType.Digital => "Digital",
            _ => newLineSeries.YAxisKey
        };

        return (newLineSeries, legendItem);
    }

    #region Minimap Synchronization
    private void OnMainTimeAxisChanged(object? sender, AxisChangedEventArgs e)
    {
        if (IsSyncingFromMinimap)
        {
            return;
        }

        // Mark viewport dirty — the throttle timer will handle the actual update at 60fps
        _viewportDirty = true;

        var timeAxis = PlotModel.Axes.FirstOrDefault(a => a.Key == "Time");
        if (timeAxis == null)
        {
            return;
        }

        _minimapSelectionRect.MinimumX = timeAxis.ActualMinimum;
        _minimapSelectionRect.MaximumX = timeAxis.ActualMaximum;
        _minimapDimLeft.MaximumX = timeAxis.ActualMinimum;
        _minimapDimRight.MinimumX = timeAxis.ActualMaximum;
        MinimapPlotModel.InvalidatePlot(false);
    }

    /// <summary>
    /// Throttled viewport update tick — processes dirty flag at 60fps to avoid
    /// re-downsampling on every mouse move during main plot pan/zoom.
    /// </summary>
    private void OnViewportThrottleTick(object? sender, EventArgs e)
    {
        if (!_viewportDirty)
        {
            return;
        }

        _viewportDirty = false;
        UpdateMainPlotViewport();
        PlotModel.InvalidatePlot(true);
    }

    /// <summary>
    /// Re-downsamples each main plot series for the currently visible time range.
    /// Skips the update if the viewport hasn't changed since the last call.
    /// </summary>
    private void UpdateMainPlotViewport()
    {
        var timeAxis = PlotModel.Axes.FirstOrDefault(a => a.Key == "Time");
        if (timeAxis == null)
        {
            return;
        }

        var visibleMin = timeAxis.ActualMinimum;
        var visibleMax = timeAxis.ActualMaximum;

        // Skip if viewport hasn't changed
        if (visibleMin == _lastViewportMin && visibleMax == _lastViewportMax)
        {
            return;
        }

        _lastViewportMin = visibleMin;
        _lastViewportMax = visibleMax;

        foreach (var series in PlotModel.Series.OfType<LineSeries>())
        {
            if (series.Tag is not (string deviceSerial, string channelName))
            {
                continue;
            }

            var key = (deviceSerial, channelName);
            if (!_allSessionPoints.TryGetValue(key, out var allPoints) || allPoints.Count == 0)
            {
                continue;
            }

            var (startIdx, endIdx) = MinMaxDownsampler.FindVisibleRange(allPoints, visibleMin, visibleMax);
            var visibleCount = endIdx - startIdx;

            // Reuse cached list to avoid GC pressure during interaction
            if (!_downsampledCache.TryGetValue(key, out var cached))
            {
                cached = new List<DataPoint>(MAIN_PLOT_BUCKET_COUNT * 2);
                _downsampledCache[key] = cached;
            }

            if (visibleCount <= MAIN_PLOT_BUCKET_COUNT * 2)
            {
                // Few enough points to render directly — copy into cached list
                cached.Clear();
                for (var i = startIdx; i < endIdx; i++)
                {
                    cached.Add(allPoints[i]);
                }
            }
            else
            {
                var downsampled = MinMaxDownsampler.Downsample(allPoints, startIdx, endIdx, MAIN_PLOT_BUCKET_COUNT);
                cached.Clear();
                cached.AddRange(downsampled);
            }

            // Only set ItemsSource once per series — subsequent updates reuse the same list
            if (series.ItemsSource != cached)
            {
                series.ItemsSource = cached;
            }
        }
    }

    /// <summary>
    /// Called by the minimap interaction controller to update the main plot's
    /// viewport downsampling after a minimap-driven zoom/pan.
    /// </summary>
    public void OnMinimapViewportChanged()
    {
        UpdateMainPlotViewport();
    }

    /// <summary>
    /// Updates the visibility of a minimap series to match its main plot counterpart.
    /// </summary>
    public void SetMinimapSeriesVisibility(string deviceSerialNo, string channelName, bool visible)
    {
        if (_minimapSeries.TryGetValue((deviceSerialNo, channelName), out var series))
        {
            series.IsVisible = visible;
            MinimapPlotModel.InvalidatePlot(false);
        }
    }
    #endregion

    #region Commands
    [RelayCommand]
    private void SaveGraph()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            DefaultExt = ".png",
            Filter = "PNG|*.png"
        };

        var result = dialog.ShowDialog();

        if (result == false) { return; }

        var pngExporter = new OxyPlot.Wpf.PngExporter { Width = 1024, Height = 768 };
        using var stream = System.IO.File.Create(dialog.FileName);
        pngExporter.Export(PlotModel, stream);
    }

    [RelayCommand]
    private void ResetZoom()
    {
        // Reset Y axes to auto-range for amplitude
        foreach (var axis in PlotModel.Axes)
        {
            if (axis.Key != "Time")
            {
                axis.Reset();
            }
        }

        // Compute the full data range from source data (not downsampled) and
        // explicitly set the time axis rather than relying on auto-range, which
        // would use the current ItemsSource extent (potentially narrowed by
        // viewport downsampling).
        var fullMin = double.MaxValue;
        var fullMax = double.MinValue;
        foreach (var kvp in _allSessionPoints)
        {
            if (kvp.Value.Count > 0)
            {
                fullMin = Math.Min(fullMin, kvp.Value[0].X);
                fullMax = Math.Max(fullMax, kvp.Value[^1].X);
            }
        }

        var timeAxis = PlotModel.Axes.FirstOrDefault(a => a.Key == "Time");
        if (timeAxis != null && fullMin < fullMax)
        {
            timeAxis.Zoom(fullMin, fullMax);
        }

        _lastViewportMin = double.NaN;
        _lastViewportMax = double.NaN;
        UpdateMainPlotViewport();
        PlotModel.InvalidatePlot(true);
    }

    [RelayCommand]
    private void ZoomOutX()
    {
        PlotModel.Axes[2].ZoomAtCenter(0.8);
        UpdateMainPlotViewport();
        PlotModel.InvalidatePlot(true);
    }

    [RelayCommand]
    private void ZoomInX()
    {
        PlotModel.Axes[2].ZoomAtCenter(1.25);
        UpdateMainPlotViewport();
        PlotModel.InvalidatePlot(true);
    }

    [RelayCommand]
    private void ZoomOutY()
    {
        PlotModel.Axes[0].ZoomAtCenter(0.8);
        PlotModel.InvalidatePlot(true);
    }

    [RelayCommand]
    private void ZoomInY()
    {
        PlotModel.Axes[0].ZoomAtCenter(1.25);
        PlotModel.InvalidatePlot(true);
    }
    #endregion

    #region IDisposable
    /// <summary>
    /// Stops timers and unsubscribes event handlers to prevent leaks.
    /// </summary>
    public void Dispose()
    {
        _viewportThrottleTimer.Stop();
        _viewportThrottleTimer.Tick -= OnViewportThrottleTick;
        _minimapInteraction?.Dispose();
        _buffer.Dispose();
        _consumerGate.Dispose();
    }
    #endregion
}