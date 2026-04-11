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

public partial class DatabaseLogger : ObservableObject, ILogger
{
    #region Constants
    private const int MINIMAP_BUCKET_COUNT = 800;
    #endregion

    #region Private Data
    public ObservableCollection<LoggedSeriesLegendItem> LegendItems { get; } = new();
    private readonly Dictionary<(string deviceSerial, string channelName), List<DataPoint>> _allSessionPoints = new();
    private readonly BlockingCollection<DataSample> _buffer = new();
    private readonly Dictionary<(string deviceSerial, string channelName), List<DataPoint>> _sessionPoints = new();
    private readonly Dictionary<(string deviceSerial, string channelName), LineSeries> _minimapSeries = new();

    private DateTime? _firstTime;
    private readonly AppLogger _appLogger = AppLogger.Instance;
    private readonly IDbContextFactory<LoggingContext> _loggingContext;
    private readonly ManualResetEventSlim _consumerGate = new(true);
    private RectangleAnnotation _minimapSelectionRect;
    private RectangleAnnotation _minimapDimLeft;
    private RectangleAnnotation _minimapDimRight;
    private MinimapInteractionController _minimapInteraction;

    [ObservableProperty]
    private PlotModel _plotModel;

    /// <summary>
    /// PlotModel for the overview minimap showing downsampled data and a selection rectangle.
    /// </summary>
    [ObservableProperty]
    private PlotModel _minimapPlotModel;
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
            PlotMargins = new OxyThickness(50, 2, 20, 20),
            Padding = new OxyThickness(0)
        };

        var minimapTimeAxis = new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Key = "MinimapTime",
            TickStyle = TickStyle.Inside,
            MajorGridlineStyle = LineStyle.None,
            MinorGridlineStyle = LineStyle.None,
            TitleFontSize = 0,
            FontSize = 9,
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
            Stroke = OxyColor.FromRgb(0, 120, 215),
            StrokeThickness = 2,
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
            _minimapDimRight);
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
            _sessionPoints.Clear();
            _allSessionPoints.Clear();
            _minimapSeries.Clear();
            PlotModel.Series.Clear();
            LegendItems.Clear();
            PlotModel.Title = string.Empty;
            PlotModel.Subtitle = string.Empty;
            PlotModel.InvalidatePlot(true);

            MinimapPlotModel.Series.Clear();
            MinimapPlotModel.InvalidatePlot(true);
        });
    }

    public void DisplayLoggingSession(LoggingSession session)
    {
        try
        {
            // ClearPlot is already dispatcher-wrapped
            ClearPlot();

            // Data fetching and processing (can be on background thread)
            var sessionName = session.Name;
            var subtitle = string.Empty;

            var tempSeriesList = new List<LineSeries>();
            var tempLegendItemsList = new List<LoggedSeriesLegendItem>();

            using (var context = _loggingContext.CreateDbContext())
            {
                context.ChangeTracker.AutoDetectChangesEnabled = false;

                var dbSamples = context.Samples.AsNoTracking()
                    .Where(s => s.LoggingSessionID == session.ID)
                    .OrderBy(s => s.TimestampTicks)
                    .Select(s => new { s.ChannelName, s.DeviceSerialNo, s.Type, s.Color, s.TimestampTicks, s.Value })
                    .ToList(); // Bring data into memory

                var samplesCount = dbSamples.Count;
                const int dataPointsToShow = 1000000;

                if (samplesCount > dataPointsToShow)
                {
                    subtitle = $"\nOnly showing {dataPointsToShow:n0} out of {samplesCount:n0} data points";
                }

                var channelInfoList = dbSamples
                    .Select(s => new { s.ChannelName, s.DeviceSerialNo, s.Type, s.Color })
                    .Distinct()
                    .NaturalOrderBy(s => s.ChannelName)
                    .ToList();

                foreach (var chInfo in channelInfoList)
                {
                    var (series, legendItem) = AddChannelSeries(chInfo.ChannelName, chInfo.DeviceSerialNo, chInfo.Type, chInfo.Color);
                    tempSeriesList.Add(series);
                    tempLegendItemsList.Add(legendItem);
                }

                // This part still needs to be careful about _allSessionPoints access if it's used by UI directly
                // For now, _allSessionPoints is used to populate series ItemsSource later on UI thread
                var dataSampleCount = 0;
                foreach (var sample in dbSamples)
                {
                    var key = (sample.DeviceSerialNo, sample.ChannelName);
                    if (_firstTime == null) { _firstTime = new DateTime(sample.TimestampTicks); }
                    var deltaTime = (sample.TimestampTicks - _firstTime.Value.Ticks) / 10000.0;

                    if (_allSessionPoints.TryGetValue(key, out var points))
                    {
                        points.Add(new DataPoint(deltaTime, sample.Value));
                    }

                    dataSampleCount++;
                    if (dataSampleCount >= dataPointsToShow)
                    {
                        break;
                    }
                }
            }

            // Prepare downsampled minimap data on the background thread
            var minimapSeriesData = new List<(string channelName, string deviceSerial, OxyColor color, List<DataPoint> downsampled)>();
            foreach (var kvp in _allSessionPoints)
            {
                if (kvp.Value.Count > 0)
                {
                    var downsampled = MinMaxDownsampler.Downsample(kvp.Value, MINIMAP_BUCKET_COUNT);
                    var matchingSeries = tempSeriesList.FirstOrDefault(s =>
                    {
                        var parts = s.Title.Split([" : ("], StringSplitOptions.None);
                        return parts.Length == 2 && parts[0] == kvp.Key.channelName && parts[1].TrimEnd(')') == kvp.Key.deviceSerial;
                    });
                    minimapSeriesData.Add((kvp.Key.channelName, kvp.Key.deviceSerial, matchingSeries?.Color ?? OxyColors.Gray, downsampled));
                }
            }

            // Update UI-bound collections and properties on the UI thread
            Application.Current.Dispatcher.Invoke(() =>
            {
                PlotModel.Title = sessionName;
                PlotModel.Subtitle = subtitle;

                foreach (var legendItem in tempLegendItemsList)
                {
                    LegendItems.Add(legendItem);
                }

                foreach (var series in tempSeriesList)
                {
                    PlotModel.Series.Add(series);
                    // Assign data to series (ItemsSource)
                    // The key for _allSessionPoints must match how it was populated
                    var key = (series.Title.Split([" : ("], StringSplitOptions.None)[1].TrimEnd(')'), series.Title.Split([" : ("], StringSplitOptions.None)[0]);
                    if (_allSessionPoints.TryGetValue(key, out var points))
                    {
                        series.ItemsSource = points;
                    }
                }

                // Populate minimap with downsampled series
                MinimapPlotModel.Series.Clear();
                _minimapSeries.Clear();
                foreach (var (channelName, deviceSerial, color, downsampled) in minimapSeriesData)
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

                // Initialize selection rectangle to full data range
                // Use data bounds directly since ActualMinimum/Maximum aren't set until render
                if (minimapSeriesData.Count > 0)
                {
                    var dataMinX = minimapSeriesData.Where(d => d.downsampled.Count > 0).Min(d => d.downsampled[0].X);
                    var dataMaxX = minimapSeriesData.Where(d => d.downsampled.Count > 0).Max(d => d.downsampled[^1].X);
                    _minimapSelectionRect.MinimumX = dataMinX;
                    _minimapSelectionRect.MaximumX = dataMaxX;
                    _minimapDimLeft.MaximumX = dataMinX;
                    _minimapDimRight.MinimumX = dataMaxX;
                }

                MinimapPlotModel.InvalidatePlot(true);

                OnPropertyChanged("SessionPoints"); // If SessionPoints is still relevant
                PlotModel.InvalidatePlot(true);
            });
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Failed in DisplayLoggingSession");
        }
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
        _sessionPoints.Add(key, []);
        _allSessionPoints.Add(key, []);

        var newLineSeries = new LineSeries
        {
            Title = $"{channelName} : ({deviceSerialNo})",
            ItemsSource = _sessionPoints.Last().Value, // This will be empty initially, data is added later
            Color = OxyColor.Parse(color),
            IsVisible = true // Default to visible
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
        // LegendItems.Add(legendItem); // Removed: To be added in DisplayLoggingSession on UI thread

        newLineSeries.YAxisKey = type switch
        {
            ChannelType.Analog => "Analog",
            ChannelType.Digital => "Digital",
            _ => newLineSeries.YAxisKey
        };

        // PlotModel.Series.Add(newLineSeries); // Removed: To be added in DisplayLoggingSession on UI thread
        // OnPropertyChanged("PlotModel"); // Removed: To be called in DisplayLoggingSession on UI thread
        return (newLineSeries, legendItem);
    }

    #region Minimap Synchronization
    private void OnMainTimeAxisChanged(object? sender, AxisChangedEventArgs e)
    {
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
        PlotModel.ResetAllAxes();
        PlotModel.InvalidatePlot(true);
    }

    [RelayCommand]
    private void ZoomOutX()
    {
        PlotModel.Axes[2].ZoomAtCenter(0.8);
        PlotModel.InvalidatePlot(true);
    }

    [RelayCommand]
    private void ZoomInX()
    {
        PlotModel.Axes[2].ZoomAtCenter(1.25);
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
}