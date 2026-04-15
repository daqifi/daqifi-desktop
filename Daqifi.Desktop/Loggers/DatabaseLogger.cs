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
    private const int INITIAL_LOAD_POINTS = 100_000;
    private const int SAMPLED_POINTS_PER_CHANNEL = 3000;
    #endregion

    #region Private Data
    public ObservableCollection<LoggedSeriesLegendItem> LegendItems { get; } = new();
    public ObservableCollection<DeviceLegendGroup> DeviceLegendGroups { get; } = new();
    private Dictionary<string, int> _sessionDeviceFrequencyHz = new();
    private Dictionary<(string deviceSerial, string channelName), List<DataPoint>> _allSessionPoints = new();
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
    private DispatcherTimer _settleTimer;
    private int? _currentSessionId;
    private CancellationTokenSource _fetchCts;
    private readonly CancellationTokenSource _consumerCts = new();
    private Thread _consumerThread;
    private volatile bool _disposed;
    private volatile bool _consumerBusy;

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
    /// The session currently displayed in the plot. Bound by the session info
    /// header to surface session-level metadata (frequency, etc.) above the
    /// chart. Null when no session is loaded.
    /// </summary>
    [ObservableProperty]
    private LoggingSession _currentSession;

    /// <summary>
    /// Total number of samples in the currently displayed session. Surfaced
    /// in the session info header. Zero while no session is loaded.
    /// </summary>
    [ObservableProperty]
    private long _currentSessionSampleCount;

    /// <summary>
    /// Compact magnitude rendering of <see cref="CurrentSessionSampleCount"/>
    /// (e.g. <c>1.23M</c>) for the chart header. Reuses the same formatter as
    /// <see cref="LoggingSession.SampleCountDisplay"/> so the header and the
    /// session list rows stay visually consistent.
    /// </summary>
    public string CurrentSessionSampleCountDisplay =>
        LoggingSession.FormatAbbreviated(CurrentSessionSampleCount);

    public string CurrentSessionSampleCountTooltip =>
        CurrentSessionSampleCount.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) + " samples";

    partial void OnCurrentSessionSampleCountChanged(long value)
    {
        OnPropertyChanged(nameof(CurrentSessionSampleCountDisplay));
        OnPropertyChanged(nameof(CurrentSessionSampleCountTooltip));
    }

    /// <summary>
    /// Indicates whether a session with data is currently loaded.
    /// Controls visibility of the minimap, legend, and empty state placeholder.
    /// </summary>
    [ObservableProperty]
    private bool _hasSessionData;

    /// <summary>
    /// True while fetching high-fidelity data from the database in the background.
    /// Bound to a subtle progress indicator in the UI.
    /// </summary>
    [ObservableProperty]
    private bool _isRefiningData;
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

        // Settle timer: triggers high-fidelity DB fetch 200ms after the last
        // viewport change (covers both main plot pan/zoom and minimap drag)
        _settleTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _settleTimer.Tick += OnSettleTick;

        // Initialize minimap PlotModel
        InitializeMinimapPlotModel();

        _consumerThread = new Thread(Consumer) { IsBackground = true };
        _consumerThread.Start();
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
        if (_disposed) { return; }

        try
        {
            _buffer.Add(dataSample);
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
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
        while (!_consumerCts.IsCancellationRequested)
        {
            try
            {
                Thread.Sleep(100);

                if (_consumerCts.IsCancellationRequested) { break; }

                bufferCount = _buffer.Count;

                if (bufferCount < 1) { continue; }

                // Wait if the consumer is suspended (e.g. during delete-all)
                _consumerGate.Wait(_consumerCts.Token);

                _consumerBusy = true;

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
                _consumerBusy = false;
            }
            catch (OperationCanceledException)
            {
                _consumerBusy = false;
                break;
            }
            catch (Exception ex)
            {
                _consumerBusy = false;
                if (_consumerCts.IsCancellationRequested) { break; }
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
            _currentSessionId = null;
            _lastViewportMin = double.NaN;
            _lastViewportMax = double.NaN;
            _fetchCts?.Cancel();
            _fetchCts?.Dispose();
            _fetchCts = null;
            IsRefiningData = false;
            _settleTimer.Stop();
            _allSessionPoints.Clear();
            _downsampledCache.Clear();
            _minimapSeries.Clear();
            _sessionDeviceFrequencyHz = new Dictionary<string, int>();
            CurrentSession = null;
            CurrentSessionSampleCount = 0;
            PlotModel.Series.Clear();
            LegendItems.Clear();
            DeviceLegendGroups.Clear();
            PlotModel.Title = string.Empty;
            PlotModel.Subtitle = string.Empty;

            // Reset all axes so the new session starts at full extent
            foreach (var axis in PlotModel.Axes)
            {
                axis.Reset();
            }

            PlotModel.InvalidatePlot(true);

            MinimapPlotModel.Series.Clear();
            MinimapPlotModel.InvalidatePlot(true);

            HasSessionData = false;
        });
    }

    /// <summary>
    /// Loads and displays a logging session on the plot. Designed to be called from a
    /// background thread (e.g., BackgroundWorker). Builds point data in local dictionaries
    /// on the calling thread, then swaps references atomically on the UI thread via
    /// Dispatcher.Invoke to avoid concurrent access to shared state.
    /// </summary>
    /// <param name="session">The logging session to display.</param>
    public void DisplayLoggingSession(LoggingSession session)
    {
        try
        {
            // ClearPlot is already dispatcher-wrapped
            ClearPlot();
            _currentSessionId = session.ID;
            Application.Current.Dispatcher.Invoke(() => CurrentSession = session);

            var sessionName = session.Name;
            var subtitle = string.Empty;
            var tempSeriesList = new List<LineSeries>();
            var tempLegendItemsList = new List<LoggedSeriesLegendItem>();
            int totalSamplesCount;

            // Build all point data in a local dictionary on this background
            // thread, then swap the reference on the UI thread atomically.
            // This eliminates shared mutable state between threads.
            var localPoints = new Dictionary<(string deviceSerial, string channelName), List<DataPoint>>();
            DateTime? localFirstTime = null;

            // Load per-device sampling frequency from session metadata.
            // Built on this background thread; swapped to shared state on the UI thread.
            var localDeviceFrequency = LoadSessionDeviceFrequency(session.ID);

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
                        _sessionDeviceFrequencyHz = localDeviceFrequency;
                        // Title is rendered in the WPF header strip, not by OxyPlot
                        PlotModel.Title = string.Empty;
                        CurrentSessionSampleCount = 0;
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
                    var (series, legendItem) = AddChannelSeries(chInfo.ChannelName, chInfo.DeviceSerialNo, chInfo.Type, chInfo.Color, localPoints);
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
                    if (localFirstTime == null) { localFirstTime = new DateTime(sample.TimestampTicks); }
                    var deltaTime = (sample.TimestampTicks - localFirstTime.Value.Ticks) / 10000.0;

                    if (localPoints.TryGetValue(key, out var points))
                    {
                        points.Add(new DataPoint(deltaTime, sample.Value));
                    }
                }

                totalSamplesCount = baseQuery.Count();
            }

            // Snapshot channel keys before the swap — after the swap, localPoints
            // becomes UI-owned and must not be accessed from this background thread.
            var channelKeys = localPoints.Keys.ToList();

            // Show the initial data immediately — swap local data to shared state on UI thread
            var initialMinimapData = PrepareMinimapData(tempSeriesList, localPoints);
            Application.Current.Dispatcher.Invoke(() =>
            {
                _allSessionPoints = localPoints;
                _firstTime = localFirstTime;
                _sessionDeviceFrequencyHz = localDeviceFrequency;

                // Session name is rendered in the WPF header strip; keep the
                // OxyPlot title clear so we don't double up on it.
                PlotModel.Title = string.Empty;
                PlotModel.Subtitle = totalSamplesCount > INITIAL_LOAD_POINTS
                    ? "\nLoading full dataset..."
                    : string.Empty;
                // Prefer the persisted SampleCount when available; fall back to
                // the live count computed during this load for sessions that
                // haven't been finalized yet.
                CurrentSessionSampleCount = session.SampleCount ?? totalSamplesCount;

                SetupUiCollections(tempSeriesList, tempLegendItemsList);
                SetupMinimapSeries(initialMinimapData);
                HasSessionData = tempSeriesList.Count > 0;
                PlotModel.InvalidatePlot(true);
            });

            // ── Phase 2: Load sampled data covering full time range (~1-3s) ──
            // Instead of streaming all 10M+ rows (30s), do N targeted index
            // seeks spread across the time range. Each seek reads one batch
            // of interleaved channel data at that timestamp position.
            // Result: ~96K rows covering the full range in ~1-3 seconds.
            if (totalSamplesCount > INITIAL_LOAD_POINTS)
            {
                // Build Phase 2 data in a fresh local dictionary using the
                // snapshotted keys (not localPoints, which is now UI-owned)
                var phase2Points = new Dictionary<(string deviceSerial, string channelName), List<DataPoint>>();
                foreach (var key in channelKeys)
                {
                    phase2Points.Add(key, []);
                }

                var phase2FirstTime = LoadSampledData(session.ID, tempSeriesList.Count, phase2Points);

                // Refresh UI with sampled full-range data — swap on UI thread
                var fullMinimapData = PrepareMinimapData(tempSeriesList, phase2Points);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _allSessionPoints = phase2Points;
                    _firstTime = phase2FirstTime;

                    PlotModel.Subtitle = string.Empty;

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

                    // Refresh minimap with full-range data
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
    /// Prepares downsampled minimap series data from the given point dictionary on the background thread.
    /// </summary>
    private List<(string channelName, string deviceSerial, OxyColor color, List<DataPoint> downsampled)>
        PrepareMinimapData(
            List<LineSeries> seriesList,
            Dictionary<(string deviceSerial, string channelName), List<DataPoint>> pointData)
    {
        var result = new List<(string channelName, string deviceSerial, OxyColor color, List<DataPoint> downsampled)>();
        foreach (var kvp in pointData)
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
    /// Loads a uniformly sampled subset of data covering the full time range
    /// using targeted index seeks. Instead of reading all N million rows,
    /// divides the time range into SAMPLED_POINTS_PER_CHANNEL segments and
    /// seeks to each segment boundary via the composite index. Each seek
    /// reads one batch of interleaved channel data (~channelCount rows).
    /// Result: ~3000 points per channel in ~1-3 seconds regardless of total dataset size.
    /// </summary>
    private DateTime? LoadSampledData(
        int sessionId,
        int channelCount,
        Dictionary<(string deviceSerial, string channelName), List<DataPoint>> localPoints)
    {
        using var context = _loggingContext.CreateDbContext();
        var connection = context.Database.GetDbConnection();
        connection.Open();

        // Get time bounds via index (instant)
        long minTicks, maxTicks;
        using (var boundsCmd = connection.CreateCommand())
        {
            boundsCmd.CommandText = @"
                SELECT MIN(TimestampTicks), MAX(TimestampTicks)
                FROM Samples
                WHERE LoggingSessionID = @id";
            var idParam = boundsCmd.CreateParameter();
            idParam.ParameterName = "@id";
            idParam.Value = sessionId;
            boundsCmd.Parameters.Add(idParam);

            using var reader = boundsCmd.ExecuteReader();
            if (!reader.Read() || reader.IsDBNull(0))
            {
                return null;
            }

            minTicks = reader.GetInt64(0);
            maxTicks = reader.GetInt64(1);
        }

        if (minTicks >= maxTicks)
        {
            return null;
        }

        var localFirstTime = new DateTime(minTicks);
        var tickStep = Math.Max(1, (maxTicks - minTicks) / SAMPLED_POINTS_PER_CHANNEL);
        // Read at least channelCount rows per seek to get one sample per channel
        var batchSize = Math.Max(channelCount * 2, 100);

        // Prepared statement for repeated seeks
        using var seekCmd = connection.CreateCommand();
        seekCmd.CommandText = @"
            SELECT ChannelName, DeviceSerialNo, TimestampTicks, Value
            FROM Samples
            WHERE LoggingSessionID = @id AND TimestampTicks >= @t
            ORDER BY TimestampTicks
            LIMIT @limit";

        var seekIdParam = seekCmd.CreateParameter();
        seekIdParam.ParameterName = "@id";
        seekIdParam.Value = sessionId;
        seekCmd.Parameters.Add(seekIdParam);

        var seekTParam = seekCmd.CreateParameter();
        seekTParam.ParameterName = "@t";
        seekTParam.Value = minTicks;
        seekCmd.Parameters.Add(seekTParam);

        var seekLimitParam = seekCmd.CreateParameter();
        seekLimitParam.ParameterName = "@limit";
        seekLimitParam.Value = batchSize;
        seekCmd.Parameters.Add(seekLimitParam);

        seekCmd.Prepare();

        // Track which timestamps we've already added to avoid duplicates
        // from overlapping batches
        var lastAddedTimestamp = new Dictionary<(string, string), long>();

        // Use <= so the final iteration (i == SAMPLED_POINTS_PER_CHANNEL)
        // seeks at maxTicks, ensuring the session tail is always included
        for (var i = 0; i <= SAMPLED_POINTS_PER_CHANNEL; i++)
        {
            var seekTimestamp = i < SAMPLED_POINTS_PER_CHANNEL
                ? minTicks + i * tickStep
                : maxTicks;
            seekTParam.Value = seekTimestamp;

            using var reader = seekCmd.ExecuteReader();
            while (reader.Read())
            {
                var channelName = reader.GetString(0);
                var deviceSerialNo = reader.GetString(1);
                var timestampTicks = reader.GetInt64(2);
                var value = reader.GetDouble(3);

                var key = (deviceSerialNo, channelName);

                // Skip duplicate timestamps from overlapping batches
                if (lastAddedTimestamp.TryGetValue(key, out var lastT) && timestampTicks <= lastT)
                {
                    continue;
                }

                lastAddedTimestamp[key] = timestampTicks;

                var deltaTime = (timestampTicks - localFirstTime.Ticks) / 10000.0;
                if (localPoints.TryGetValue(key, out var points))
                {
                    points.Add(new DataPoint(deltaTime, value));
                }
            }
        }

        return localFirstTime;
    }

    /// <summary>
    /// Loads per-device sampling frequency for a session from <c>SessionDeviceMetadata</c>.
    /// Returns an empty dictionary for legacy sessions logged before metadata was persisted —
    /// the legend will simply omit the frequency line for those.
    /// </summary>
    /// <param name="sessionId">The session whose device metadata to load.</param>
    /// <returns>Map of device serial number to configured sampling frequency in Hz.</returns>
    private Dictionary<string, int> LoadSessionDeviceFrequency(int sessionId)
    {
        var result = new Dictionary<string, int>();
        try
        {
            using var context = _loggingContext.CreateDbContext();
            var metadata = context.SessionDeviceMetadata.AsNoTracking()
                .Where(m => m.LoggingSessionID == sessionId)
                .Select(m => new { m.DeviceSerialNo, m.SamplingFrequencyHz })
                .ToList();

            foreach (var entry in metadata)
            {
                if (!string.IsNullOrEmpty(entry.DeviceSerialNo))
                {
                    result[entry.DeviceSerialNo] = entry.SamplingFrequencyHz;
                }
            }
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Failed to load SessionDeviceMetadata");
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
                if (_sessionDeviceFrequencyHz.TryGetValue(legendItem.DeviceSerialNo, out var freqHz) && freqHz > 0)
                {
                    group.SamplingFrequencyHz = freqHz;
                }
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

        // Set minimap axes from source data bounds (not auto-range, which
        // reads downsampled ItemsSource and may have shifted boundaries)
        var nonEmpty = minimapData.Where(d => d.downsampled.Count > 0).ToList();
        if (nonEmpty.Count > 0)
        {
            var dataMinX = nonEmpty.Min(d => d.downsampled[0].X);
            var dataMaxX = nonEmpty.Max(d => d.downsampled[^1].X);

            var minimapTimeAxis = MinimapPlotModel.Axes.FirstOrDefault(a => a.Key == "MinimapTime");
            minimapTimeAxis?.Zoom(dataMinX, dataMaxX);

            var minimapYAxis = MinimapPlotModel.Axes.FirstOrDefault(a => a.Key == "MinimapY");
            minimapYAxis?.Reset();

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
                cmd.CommandText = "DELETE FROM SessionDeviceMetadata WHERE LoggingSessionID = @id";
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
    /// Blocks until the buffered samples have been flushed to the database, or
    /// the timeout elapses. Used by <c>LoggingManager</c> when finalizing a
    /// session so the persisted <c>SampleCount</c> reflects every row that was
    /// actually written, not just the rows the consumer happened to have
    /// drained at the moment Active flipped to false.
    /// </summary>
    public void WaitForIdle(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_buffer.Count == 0 && !_consumerBusy)
            {
                // Sleep one consumer poll interval to ensure no in-flight item
                // slipped between TryTake and the busy flag being set.
                Thread.Sleep(120);
                if (_buffer.Count == 0 && !_consumerBusy) { return; }
            }
            Thread.Sleep(50);
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

    private (LineSeries series, LoggedSeriesLegendItem legendItem) AddChannelSeries(
        string channelName,
        string deviceSerialNo,
        ChannelType type,
        string color,
        Dictionary<(string deviceSerial, string channelName), List<DataPoint>> localPoints)
    {
        var key = (deviceSerial: deviceSerialNo, channelName);
        localPoints.Add(key, []);

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
        UpdateMainPlotViewport(highFidelity: false);
        PlotModel.InvalidatePlot(true);

        // Restart the settle timer — it will fire 200ms after the last change
        _settleTimer.Stop();
        _settleTimer.Start();
    }

    /// <summary>
    /// Fires 200ms after the last viewport change. Triggers a high-fidelity
    /// DB fetch so zoomed-in views show full-resolution data once interaction settles.
    /// </summary>
    private void OnSettleTick(object? sender, EventArgs e)
    {
        _settleTimer.Stop();
        _lastViewportMin = double.NaN;
        _lastViewportMax = double.NaN;
        UpdateMainPlotViewport(highFidelity: true);
        PlotModel.InvalidatePlot(true);
    }

    /// <summary>
    /// Re-downsamples each main plot series for the currently visible time range.
    /// When zoomed in far enough that the sampled in-memory data is too sparse,
    /// fetches full-resolution data from the database for the visible window.
    /// Skips the update if the viewport hasn't changed since the last call.
    /// </summary>
    private void UpdateMainPlotViewport(bool highFidelity = true)
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

        // Cancel any in-flight DB fetch — the viewport has changed, so its
        // results would be stale and could overwrite the current view
        if (_fetchCts != null)
        {
            _fetchCts.Cancel();
            _fetchCts.Dispose();
            _fetchCts = null;
            IsRefiningData = false;
        }

        // During drag (highFidelity=false), always use fast in-memory data to
        // maintain smooth 60fps. DB fetches only happen on settle (mouse up,
        // zoom buttons) when highFidelity=true.
        if (!highFidelity)
        {
            UpdateSeriesFromMemory(visibleMin, visibleMax);
            return;
        }

        // Check if ANY channel's sampled in-memory data is too sparse for
        // this zoom level. If so, fetch full-resolution data from the DB.
        var needsDbFetch = false;
        if (_currentSessionId.HasValue && _firstTime.HasValue)
        {
            foreach (var kvp in _allSessionPoints)
            {
                if (kvp.Value.Count == 0)
                {
                    continue;
                }

                // Only check channels that are actually sampled (not full datasets)
                if (kvp.Value.Count < SAMPLED_POINTS_PER_CHANNEL / 2)
                {
                    continue;
                }

                var (si, ei) = MinMaxDownsampler.FindVisibleRange(kvp.Value, visibleMin, visibleMax);
                var sampledVisible = ei - si;
                if (sampledVisible < MAIN_PLOT_BUCKET_COUNT)
                {
                    needsDbFetch = true;
                    break;
                }
            }
        }

        if (needsDbFetch)
        {
            FetchViewportDataFromDb(visibleMin, visibleMax);
        }
        else
        {
            UpdateSeriesFromMemory(visibleMin, visibleMax);
        }
    }

    /// <summary>
    /// Updates series ItemsSource from the in-memory sampled data.
    /// Used when the sampled data has sufficient density for the current viewport.
    /// </summary>
    private void UpdateSeriesFromMemory(double visibleMin, double visibleMax)
    {
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

            if (!_downsampledCache.TryGetValue(key, out var cached))
            {
                cached = new List<DataPoint>(MAIN_PLOT_BUCKET_COUNT * 2);
                _downsampledCache[key] = cached;
            }

            if (visibleCount <= MAIN_PLOT_BUCKET_COUNT * 2)
            {
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

            if (series.ItemsSource != cached)
            {
                series.ItemsSource = cached;
            }
        }
    }

    /// <summary>
    /// Fetches high-resolution data from the database for the visible time window
    /// using sampled index seeks (same technique as LoadSampledData). Runs on a
    /// background thread to keep the UI responsive. Cancels any in-flight fetch
    /// when a new one starts. Results are marshaled back to the UI thread.
    /// </summary>
    private void FetchViewportDataFromDb(double visibleMin, double visibleMax)
    {
        if (!_currentSessionId.HasValue || !_firstTime.HasValue)
        {
            return;
        }

        // Cancel any in-flight fetch
        _fetchCts?.Cancel();
        _fetchCts?.Dispose();
        _fetchCts = new CancellationTokenSource();
        var ct = _fetchCts.Token;

        var sessionId = _currentSessionId.Value;
        var firstTimeTicks = _firstTime.Value.Ticks;
        var channelKeys = _allSessionPoints.Keys.ToList();

        // Convert plot X (ms) back to DB ticks with padding
        var minTicks = firstTimeTicks + (long)(visibleMin * 10000.0);
        var maxTicks = firstTimeTicks + (long)(visibleMax * 10000.0);
        var tickRange = maxTicks - minTicks;
        var padding = Math.Max(tickRange / 100, 10000);
        minTicks -= padding;
        maxTicks += padding;

        IsRefiningData = true;

        Task.Run(() =>
        {
            var dbPoints = new Dictionary<(string, string), List<DataPoint>>();
            foreach (var key in channelKeys)
            {
                dbPoints[key] = new List<DataPoint>();
            }

            try
            {
                ct.ThrowIfCancellationRequested();

                using var context = _loggingContext.CreateDbContext();
                var connection = context.Database.GetDbConnection();
                connection.Open();

                // Use sampled seeks: divide the visible window into
                // MAIN_PLOT_BUCKET_COUNT segments and read a small batch
                // at each position. This reads ~4000 * channelCount rows
                // instead of potentially millions.
                var seekCount = MAIN_PLOT_BUCKET_COUNT;
                var seekTickStep = (maxTicks - minTicks) / seekCount;
                var batchSize = Math.Max(channelKeys.Count * 2, 100);

                using var seekCmd = connection.CreateCommand();
                seekCmd.CommandText = @"
                    SELECT ChannelName, DeviceSerialNo, TimestampTicks, Value
                    FROM Samples
                    WHERE LoggingSessionID = @id
                      AND TimestampTicks >= @t
                      AND TimestampTicks <= @maxT
                    ORDER BY TimestampTicks
                    LIMIT @limit";

                var idParam = seekCmd.CreateParameter();
                idParam.ParameterName = "@id";
                idParam.Value = sessionId;
                seekCmd.Parameters.Add(idParam);

                var tParam = seekCmd.CreateParameter();
                tParam.ParameterName = "@t";
                tParam.Value = minTicks;
                seekCmd.Parameters.Add(tParam);

                var maxTParam = seekCmd.CreateParameter();
                maxTParam.ParameterName = "@maxT";
                maxTParam.Value = maxTicks;
                seekCmd.Parameters.Add(maxTParam);

                var limitParam = seekCmd.CreateParameter();
                limitParam.ParameterName = "@limit";
                limitParam.Value = batchSize;
                seekCmd.Parameters.Add(limitParam);

                seekCmd.Prepare();

                var lastAddedTimestamp = new Dictionary<(string, string), long>();

                for (var i = 0; i < seekCount; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    tParam.Value = minTicks + i * seekTickStep;

                    using var reader = seekCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var channelName = reader.GetString(0);
                        var deviceSerialNo = reader.GetString(1);
                        var timestampTicks = reader.GetInt64(2);
                        var value = reader.GetDouble(3);

                        var key = (deviceSerialNo, channelName);

                        if (lastAddedTimestamp.TryGetValue(key, out var lastT) && timestampTicks <= lastT)
                        {
                            continue;
                        }

                        lastAddedTimestamp[key] = timestampTicks;
                        var deltaTime = (timestampTicks - firstTimeTicks) / 10000.0;

                        if (dbPoints.TryGetValue(key, out var points))
                        {
                            points.Add(new DataPoint(deltaTime, value));
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _appLogger.Error(ex, "Failed to fetch viewport data from DB");
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    IsRefiningData = false;
                    UpdateSeriesFromMemory(visibleMin, visibleMax);
                    PlotModel.InvalidatePlot(true);
                });
                return;
            }

            // Marshal results back to UI thread
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                foreach (var series in PlotModel.Series.OfType<LineSeries>())
                {
                    if (series.Tag is not (string deviceSerial, string channelName))
                    {
                        continue;
                    }

                    var key = (deviceSerial, channelName);
                    if (!dbPoints.TryGetValue(key, out var fetchedPoints) || fetchedPoints.Count == 0)
                    {
                        continue;
                    }

                    if (!_downsampledCache.TryGetValue(key, out var cached))
                    {
                        cached = new List<DataPoint>(MAIN_PLOT_BUCKET_COUNT * 2);
                        _downsampledCache[key] = cached;
                    }

                    cached.Clear();
                    if (fetchedPoints.Count <= MAIN_PLOT_BUCKET_COUNT * 2)
                    {
                        cached.AddRange(fetchedPoints);
                    }
                    else
                    {
                        cached.AddRange(MinMaxDownsampler.Downsample(fetchedPoints, MAIN_PLOT_BUCKET_COUNT));
                    }

                    if (series.ItemsSource != cached)
                    {
                        series.ItemsSource = cached;
                    }
                }

                IsRefiningData = false;
                PlotModel.InvalidatePlot(true);
            });
        }, ct);
    }

    /// <summary>
    /// Called by the minimap interaction controller during drag to update the
    /// main plot. Uses in-memory sampled data only (no DB queries) for smooth 60fps.
    /// </summary>
    public void OnMinimapViewportChanged()
    {
        UpdateMainPlotViewport(highFidelity: false);
    }

    /// <summary>
    /// Called when minimap interaction ends (mouse up). Fetches full-resolution
    /// data from the database if the zoom level warrants it.
    /// </summary>
    public void OnMinimapInteractionEnded()
    {
        _settleTimer.Stop();
        _lastViewportMin = double.NaN;
        _lastViewportMax = double.NaN;
        UpdateMainPlotViewport(highFidelity: true);
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
        var timeAxis = PlotModel.Axes.FirstOrDefault(a => a.Key == "Time");
        timeAxis?.ZoomAtCenter(0.8);
        UpdateMainPlotViewport();
        PlotModel.InvalidatePlot(true);
    }

    [RelayCommand]
    private void ZoomInX()
    {
        var timeAxis = PlotModel.Axes.FirstOrDefault(a => a.Key == "Time");
        timeAxis?.ZoomAtCenter(1.25);
        UpdateMainPlotViewport();
        PlotModel.InvalidatePlot(true);
    }

    [RelayCommand]
    private void ZoomOutY()
    {
        var analogAxis = PlotModel.Axes.FirstOrDefault(a => a.Key == "Analog");
        analogAxis?.ZoomAtCenter(0.8);
        PlotModel.InvalidatePlot(true);
    }

    [RelayCommand]
    private void ZoomInY()
    {
        var analogAxis = PlotModel.Axes.FirstOrDefault(a => a.Key == "Analog");
        analogAxis?.ZoomAtCenter(1.25);
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
        _settleTimer.Stop();
        _settleTimer.Tick -= OnSettleTick;
        _fetchCts?.Cancel();
        _fetchCts?.Dispose();

        var timeAxis = PlotModel.Axes.FirstOrDefault(a => a.Key == "Time");
        if (timeAxis != null)
        {
            timeAxis.AxisChanged -= OnMainTimeAxisChanged;
        }

        _minimapInteraction?.Dispose();
        _disposed = true;
        _consumerCts.Cancel();
        _buffer.CompleteAdding();
        _consumerThread?.Join(TimeSpan.FromSeconds(2));
        _buffer.Dispose();
        _consumerCts.Dispose();
        _consumerGate.Dispose();
    }
    #endregion
}