using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.DataModel.Channel;
using Daqifi.Desktop.Device;
using OxyPlot;
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
                Application.Current.Dispatcher.Invoke(() => _plotModel?.InvalidatePlot(true));
            }
        }
    }

    public LineSeries ActualSeries { get; }
    private readonly PlotModel _plotModel;

    public LoggedSeriesLegendItem(string displayName, OxyColor seriesColor, bool isVisible, LineSeries actualSeries, PlotModel plotModel)
    {
        _displayName = displayName;
        _seriesColor = seriesColor;
        _isVisible = isVisible; // Initialize the backing field
        ActualSeries = actualSeries;
        ActualSeries.IsVisible = isVisible; // Ensure series visibility matches
        _plotModel = plotModel;
    }
}

public partial class DatabaseLogger : ObservableObject, ILogger
{
    #region Private Data
    public ObservableCollection<LoggedSeriesLegendItem> LegendItems { get; } = new();
    private readonly Dictionary<(string deviceSerial, string channelName), List<DataPoint>> _allSessionPoints = new();
    private readonly BlockingCollection<DataSample> _buffer = new();
    private readonly Dictionary<(string deviceSerial, string channelName), List<DataPoint>> _sessionPoints = new();

    private DateTime? _firstTime;
    private readonly AppLogger _appLogger = AppLogger.Instance;
    private readonly IDbContextFactory<LoggingContext> _loggingContext;
    
    [ObservableProperty]
    private PlotModel _plotModel;
    public PlotModel MinimapPlotModel { get; private set; }
    internal OxyPlot.Annotations.RectangleAnnotation SelectionRectangle { get; private set; }
    private int? CurrentSessionId;
    #endregion

    #region Constructor
    public DatabaseLogger(IDbContextFactory<LoggingContext> loggingContext)
    {
        _loggingContext = loggingContext;

        // Main PlotModel
        PlotModel = new PlotModel();
        var mainAnalogAxis = new LinearAxis
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
        var mainDigitalAxis = new LinearAxis
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
        var mainTimeAxis = new LinearAxis
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
        PlotModel.Axes.Add(mainAnalogAxis);
        PlotModel.Axes.Add(mainDigitalAxis);
        PlotModel.Axes.Add(mainTimeAxis);
        PlotModel.IsLegendVisible = false;

        // Minimap PlotModel
        MinimapPlotModel = new PlotModel();
        var minimapTimeAxis = new LinearAxis
        {
            Position = AxisPosition.Bottom,
            IsAxisVisible = false,
            Key = "Time"
        };
        var minimapYAxis = new LinearAxis
        {
            Position = AxisPosition.Left,
            IsAxisVisible = false,
            Key = "Analog" // Assuming analog data for minimap for now
        };
        MinimapPlotModel.Axes.Add(minimapTimeAxis);
        MinimapPlotModel.Axes.Add(minimapYAxis);
        MinimapPlotModel.IsLegendVisible = false;
        MinimapPlotModel.PlotMargins = new OxyThickness(0);
        MinimapPlotModel.Padding = new OxyThickness(0);

        SelectionRectangle = new OxyPlot.Annotations.RectangleAnnotation
        {
            Fill = OxyColor.FromAColor(80, OxyColors.SkyBlue), // Semi-transparent fill
            Stroke = OxyColors.Black,
            StrokeThickness = 1,
            Layer = OxyPlot.Annotations.AnnotationLayer.BelowSeries // Render below series for better visibility of data
        };
        MinimapPlotModel.Annotations.Add(SelectionRectangle);
        
        var consumerThread = new Thread(Consumer) { IsBackground = true };
        consumerThread.Start();
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

                // Remove the samples from the collection
                for (var i = 0; i < bufferCount; i++)
                {
                    if (_buffer.TryTake(out var sample)) { samples.Add(sample); }
                }

                using (var context = _loggingContext.CreateDbContext())
                {

                    // Start a new transaction for bulk insert
                    using (var transaction = context.Database.BeginTransaction())
                    {
                        // Perform the bulk insert
                        context.BulkInsert(samples);

                        // Commit the transaction after the bulk insert
                        transaction.Commit();
                    }
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
            PlotModel.Series.Clear();
            LegendItems.Clear();
            PlotModel.Title = string.Empty;
            PlotModel.Subtitle = string.Empty;
            PlotModel.InvalidatePlot(true);

            MinimapPlotModel.Series.Clear();
            MinimapPlotModel.Annotations.Clear(); // Clear existing annotations, mainly the SelectionRectangle
            MinimapPlotModel.Annotations.Add(SelectionRectangle); // Re-add the configured one
            MinimapPlotModel.InvalidatePlot(true);
        });
    }

    public void DisplayLoggingSession(LoggingSession session)
    {
        try
        {
            ClearPlot(); // This already handles dispatcher for PlotModel and MinimapPlotModel clearing

            string sessionName = session.Name;
            string mainPlotSubtitle = string.Empty;
            const int dataPointsToShowInMainPlot = 100000; // Max points in the main plot view (reduced for performance)
            const int maxMinimapInitialFetchPoints = 20000; // Fetch more points initially for minimap
            const int maxMinimapDisplayPoints = 2000;     // Max points to actually display in minimap

            long sessionMinTimestampTicks = 0;
            long sessionMaxTimestampTicks = 0;
            double sessionMinTimeMs = 0;
            double sessionMaxTimeMs = 0;

            // Temporary lists for series and legend items to be added on UI thread
            var tempMainPlotSeriesList = new List<LineSeries>();
            var tempLegendItemsList = new List<LoggedSeriesLegendItem>();
            var tempMinimapSeriesList = new List<LineSeries>();
            var minimapData = new Dictionary<(string deviceSerial, string channelName), List<DataPoint>>();

            using (var context = _loggingContext.CreateDbContext())
            {
                context.ChangeTracker.AutoDetectChangesEnabled = false;

                var allSessionSamplesQuery = context.Samples.AsNoTracking()
                    .Where(s => s.LoggingSessionID == session.ID)
                    .OrderBy(s => s.TimestampTicks);

                if (!allSessionSamplesQuery.Any())
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        PlotModel.Title = sessionName;
                        PlotModel.Subtitle = "No data in this session.";
                        MinimapPlotModel.InvalidatePlot(true);
                        PlotModel.InvalidatePlot(true);
                        CurrentSessionId = null;
                    });
                    return;
                }
                CurrentSessionId = session.ID;
                
                sessionMinTimestampTicks = allSessionSamplesQuery.First().TimestampTicks;
                // To get the last timestamp, we need to order descending for a moment or fetch all and take last.
                // For performance, let's try to get it efficiently. If OrderBy().Last() is slow, consider alternative.
                sessionMaxTimestampTicks = context.Samples.AsNoTracking()
                                           .Where(s => s.LoggingSessionID == session.ID)
                                           .OrderByDescending(s => s.TimestampTicks)
                                           .Select(s=>s.TimestampTicks)
                                           .FirstOrDefault();

                if (sessionMaxTimestampTicks == 0 && sessionMinTimestampTicks !=0) sessionMaxTimestampTicks = sessionMinTimestampTicks;


                _firstTime = new DateTime(sessionMinTimestampTicks); 
                
                sessionMinTimeMs = 0; 
                sessionMaxTimeMs = (sessionMaxTimestampTicks - sessionMinTimestampTicks) / 10000.0;

                var channelInfoList = allSessionSamplesQuery
                    .Select(s => new { s.ChannelName, s.DeviceSerialNo, s.Type, s.Color })
                    .Distinct()
                    .OrderBy(s => s.ChannelName)
                    .ToList();

                foreach (var chInfo in channelInfoList)
                {
                    var (mainSeries, legendItem) = AddChannelSeries(chInfo.ChannelName, chInfo.DeviceSerialNo, chInfo.Type, chInfo.Color, isForMinimap: false);
                    tempMainPlotSeriesList.Add(mainSeries);
                    if(legendItem != null) tempLegendItemsList.Add(legendItem);

                    var (minimapSeries, _) = AddChannelSeries(chInfo.ChannelName, chInfo.DeviceSerialNo, chInfo.Type, chInfo.Color, isForMinimap: true);
                    tempMinimapSeriesList.Add(minimapSeries);
                    minimapData[(chInfo.DeviceSerialNo, chInfo.ChannelName)] = new List<DataPoint>();
                }

                // Load and downsample data for Minimap using interval-based sampling
                const int targetMinimapSamples = 2000;
                long totalDurationTicks = sessionMaxTimestampTicks - sessionMinTimestampTicks;
                long intervalTicks = 1; // Default to 1 to avoid division by zero if duration is 0 or very small

                if (totalDurationTicks > 0 && targetMinimapSamples > 0)
                {
                    intervalTicks = totalDurationTicks / targetMinimapSamples;
                    if (intervalTicks == 0) intervalTicks = 1; // Ensure interval is at least 1 tick if there's duration
                }
                
                foreach (var chInfo in channelInfoList)
                {
                    var currentChannelMinimapPoints = new List<DataPoint>();
                    minimapData[(chInfo.DeviceSerialNo, chInfo.ChannelName)] = currentChannelMinimapPoints;

                    if (totalDurationTicks == 0) // Handle sessions with zero duration (e.g. single data point)
                    {
                        var singleSample = context.Samples.AsNoTracking()
                            .Where(s => s.LoggingSessionID == CurrentSessionId.Value &&
                                        s.DeviceSerialNo == chInfo.DeviceSerialNo &&
                                        s.ChannelName == chInfo.ChannelName &&
                                        s.TimestampTicks == sessionMinTimestampTicks)
                            .FirstOrDefault();
                        if (singleSample != null)
                        {
                            currentChannelMinimapPoints.Add(new DataPoint(0, singleSample.Value)); // DeltaTime is 0
                        }
                    }
                    else
                    {
                        for (int i = 0; i < targetMinimapSamples; i++)
                        {
                            long currentIntervalStartTicks = sessionMinTimestampTicks + (i * intervalTicks);
                            long currentIntervalEndTicks = currentIntervalStartTicks + intervalTicks;
                            // Ensure the last interval doesn't exceed sessionMaxTimestampTicks for the query
                            // but allow it to reach sessionMaxTimestampTicks for including the last point.
                            if (i == targetMinimapSamples -1) currentIntervalEndTicks = sessionMaxTimestampTicks +1;


                            var sampleInInterval = context.Samples.AsNoTracking()
                                .Where(s => s.LoggingSessionID == CurrentSessionId.Value &&
                                            s.DeviceSerialNo == chInfo.DeviceSerialNo &&
                                            s.ChannelName == chInfo.ChannelName &&
                                            s.TimestampTicks >= currentIntervalStartTicks &&
                                            s.TimestampTicks < currentIntervalEndTicks)
                                .OrderBy(s => s.TimestampTicks)
                                .FirstOrDefault();

                            if (sampleInInterval != null)
                            {
                                double deltaTimeMs = (sampleInInterval.TimestampTicks - sessionMinTimestampTicks) / 10000.0;
                                currentChannelMinimapPoints.Add(new DataPoint(deltaTimeMs, sampleInInterval.Value));
                            }
                        }
                    }
                }
                
                // Determine initial window for Main Plot (e.g., first 10% or max 20 seconds)
                double initialWindowDurationMs = Math.Min(sessionMaxTimeMs * 0.1, 20000);
                if (sessionMaxTimeMs == 0) initialWindowDurationMs = 1000; // Default for single point or very short
                double initialWindowEndTimeMs = sessionMinTimeMs + initialWindowDurationMs;
                if (initialWindowEndTimeMs > sessionMaxTimeMs) initialWindowEndTimeMs = sessionMaxTimeMs;

                long initialWindowStartTicks = _firstTime.Value.Ticks + (long)(sessionMinTimeMs * 10000.0);
                long initialWindowEndTicks = _firstTime.Value.Ticks + (long)(initialWindowEndTimeMs * 10000.0);

                // Load data for Main Plot (initial window)
                var mainPlotDbSamplesQuery = allSessionSamplesQuery // Re-use the base query
                    .Where(s => s.TimestampTicks >= initialWindowStartTicks && s.TimestampTicks <= initialWindowEndTicks)
                    .Select(s => new { s.ChannelName, s.DeviceSerialNo, s.TimestampTicks, s.Value });

                var mainPlotSamplesInWindow = mainPlotDbSamplesQuery.ToList(); // Materialize the window
                var mainPlotSamplesCount = mainPlotSamplesInWindow.Count;
                
                List<dynamic> samplesToDisplayInMainPlot;
                if (mainPlotSamplesCount > dataPointsToShowInMainPlot)
                {
                     mainPlotSubtitle = $"\nDisplaying {dataPointsToShowInMainPlot:n0} of {mainPlotSamplesCount:n0} data points in current window. Zoom in for more detail.";
                     // Simple take for now. Could do smarter downsampling here too.
                     samplesToDisplayInMainPlot = mainPlotSamplesInWindow.Take(dataPointsToShowInMainPlot).ToList<dynamic>();
                }
                else
                {
                    mainPlotSubtitle = $"\nDisplaying {mainPlotSamplesCount:n0} data points in current window.";
                    samplesToDisplayInMainPlot = mainPlotSamplesInWindow.ToList<dynamic>();
                }

                // Clear existing points from _allSessionPoints before populating with new window data
                foreach(var list in _allSessionPoints.Values) { list.Clear(); }

                foreach (var sample in samplesToDisplayInMainPlot)
                {
                    var key = (sample.DeviceSerialNo, sample.ChannelName);
                    var deltaTime = (sample.TimestampTicks - _firstTime.Value.Ticks) / 10000.0;
                    if (_allSessionPoints.TryGetValue(key, out var points))
                    {
                        points.Add(new DataPoint(deltaTime, sample.Value));
                    }
                }
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                PlotModel.Title = sessionName;
                PlotModel.Subtitle = mainPlotSubtitle;

                LegendItems.Clear();
                foreach (var legendItem in tempLegendItemsList) { LegendItems.Add(legendItem); }
                
                PlotModel.Series.Clear();
                foreach (var series in tempMainPlotSeriesList)
                {
                    PlotModel.Series.Add(series);
                    // ItemsSource for main plot series should already be connected to _allSessionPoints lists by AddChannelSeries
                    // and those lists have been updated. We just need to notify the series.
                    series.ItemsSource = _allSessionPoints[GetKeyFromSeriesTitle(series.Title)]; // Re-assign to trigger update if necessary
                }
                
                // Reset axes before invalidating to ensure they pick up new data ranges
                PlotModel.ResetAllAxes();
                PlotModel.InvalidatePlot(true);

                MinimapPlotModel.Series.Clear();
                foreach (var minimapSeries in tempMinimapSeriesList)
                {
                    MinimapPlotModel.Series.Add(minimapSeries);
                    var key = GetKeyFromSeriesTitle(minimapSeries.Title);
                    if (minimapData.TryGetValue(key, out var points)) { minimapSeries.ItemsSource = points; }
                }
                
                MinimapPlotModel.Axes.First(a => a.Key == "Time").Zoom(sessionMinTimeMs, sessionMaxTimeMs);
                MinimapPlotModel.Axes.First(a => a.Key == "Analog").Reset(); // Auto-adjust Y for minimap
                MinimapPlotModel.Axes.First(a => a.Key == "Analog").Zoom(MinimapPlotModel.Axes.First(a => a.Key == "Analog").ActualMinimum, MinimapPlotModel.Axes.First(a => a.Key == "Analog").ActualMaximum);


                // Update SelectionRectangle based on the initially loaded main plot data
                var mainTimeAxis = PlotModel.Axes.First(a => a.Key == "Time");
                SelectionRectangle.MinimumX = mainTimeAxis.ActualMinimum;
                SelectionRectangle.MaximumX = mainTimeAxis.ActualMaximum;
                
                var minimapYAxis = MinimapPlotModel.Axes.First(a => a.Key == "Analog");
                if (minimapYAxis.IsPanEnabled && minimapYAxis.ActualMaximum > minimapYAxis.ActualMinimum) 
                {
                    SelectionRectangle.MinimumY = minimapYAxis.ActualMinimum;
                    SelectionRectangle.MaximumY = minimapYAxis.ActualMaximum;
                } else {
                     // Fallback if Y axis has no range yet (e.g. no data in minimap or single point)
                    var yFallbackMin = MinimapPlotModel.Series.OfType<LineSeries>().SelectMany(s => s.Points).DefaultIfEmpty(new DataPoint(0,0)).Min(p => p.Y);
                    var yFallbackMax = MinimapPlotModel.Series.OfType<LineSeries>().SelectMany(s => s.Points).DefaultIfEmpty(new DataPoint(0,1)).Max(p => p.Y);
                    if (yFallbackMin == yFallbackMax) { yFallbackMax = yFallbackMin +1; } // Ensure some height

                    SelectionRectangle.MinimumY = yFallbackMin;
                    SelectionRectangle.MaximumY = yFallbackMax;
                }


                MinimapPlotModel.InvalidatePlot(true); // Refresh minimap to show selection and series
            });
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Failed in DisplayLoggingSession");
            // Optionally, update UI to show error state
            Application.Current.Dispatcher.Invoke(() => {
                PlotModel.Title = session.Name;
                PlotModel.Subtitle = "Error loading session data.";
                PlotModel.Series.Clear();
                MinimapPlotModel.Series.Clear();
                PlotModel.InvalidatePlot(true);
                MinimapPlotModel.InvalidatePlot(true);
            });
        }
    }

    private (string deviceSerial, string channelName) GetKeyFromSeriesTitle(string title)
    {
        var parts = title.Split(new[] { " : (" }, StringSplitOptions.None);
        if (parts.Length == 2)
        {
            var channelName = parts[0].Trim();
            var deviceSerial = parts[1].TrimEnd(')').Trim();
            return (deviceSerial, channelName);
        }
        // Handle cases where title might not be in the expected format, though it should be.
        _appLogger.Warning($"Could not parse series title: {title}");
        return (string.Empty, string.Empty);
    }

    public void UpdateMainPlotData(double newMinX, double newMaxX)
    {
        if (_firstTime == null || !CurrentSessionId.HasValue)
        {
            _appLogger.Warning("UpdateMainPlotData called without a loaded session, first timestamp, or session ID.");
            return;
        }

        string mainPlotSubtitle = string.Empty;
        const int dataPointsToShowInMainPlot = 100000; // Consistent with DisplayLoggingSession

        long selectionStartTicks = _firstTime.Value.Ticks + (long)(newMinX * 10000.0);
        long selectionEndTicks = _firstTime.Value.Ticks + (long)(newMaxX * 10000.0);

        // Prepare a temporary dictionary to hold the new data for the main plot window
        var newWindowData = new Dictionary<(string deviceSerial, string channelName), List<DataPoint>>();
        // Initialize lists for all known series keys to ensure they are present even if no new data for them
        foreach (var key in _allSessionPoints.Keys)
        {
            newWindowData[key] = new List<DataPoint>();
        }

        try
        {
            using (var context = _loggingContext.CreateDbContext())
            {
                context.ChangeTracker.AutoDetectChangesEnabled = false;

                var samplesInWindowQuery = context.Samples.AsNoTracking()
                    .Where(s => s.LoggingSessionID == CurrentSessionId.Value &&
                                s.TimestampTicks >= selectionStartTicks &&
                                s.TimestampTicks <= selectionEndTicks)
                    .OrderBy(s => s.TimestampTicks)
                    .Select(s => new { s.ChannelName, s.DeviceSerialNo, s.TimestampTicks, s.Value });

                var samplesInWindow = samplesInWindowQuery.ToList();
                var totalSamplesInWindow = samplesInWindow.Count;
                
                List<dynamic> samplesToDisplayInMainPlot;

                if (totalSamplesInWindow > dataPointsToShowInMainPlot)
                {
                    mainPlotSubtitle = $"\nDisplaying {dataPointsToShowInMainPlot:n0} of {totalSamplesInWindow:n0} data points in selected window. Zoom in for more detail.";
                    // Simple downsampling: take points at regular intervals
                    int step = totalSamplesInWindow / dataPointsToShowInMainPlot;
                    if (step <= 0) step = 1; // Ensure step is at least 1
                    samplesToDisplayInMainPlot = new List<dynamic>();
                    for(int i=0; i < totalSamplesInWindow; i += step)
                    {
                        samplesToDisplayInMainPlot.Add(samplesInWindow[i]);
                        if (samplesToDisplayInMainPlot.Count >= dataPointsToShowInMainPlot) break;
                    }
                }
                else
                {
                    mainPlotSubtitle = $"\nDisplaying {totalSamplesInWindow:n0} data points in selected window.";
                    samplesToDisplayInMainPlot = samplesInWindow.ToList<dynamic>();
                }

                foreach (var sample in samplesToDisplayInMainPlot)
                {
                    var key = (sample.DeviceSerialNo, sample.ChannelName);
                    var deltaTime = (sample.TimestampTicks - _firstTime.Value.Ticks) / 10000.0;
                    if (newWindowData.TryGetValue(key, out var points))
                    {
                        points.Add(new DataPoint(deltaTime, sample.Value));
                    }
                }
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                // Update _allSessionPoints which is the source for main plot series
                foreach (var key in _allSessionPoints.Keys.ToList()) // ToList to avoid modification issues if keys could change
                {
                    if (_allSessionPoints.TryGetValue(key, out var list) && newWindowData.TryGetValue(key, out var newData))
                    {
                        list.Clear();
                        list.AddRange(newData);
                    }
                }
                
                PlotModel.Subtitle = mainPlotSubtitle;

                // Update X-axis actual range. This ensures panning/zooming on main plot reflects the selection.
                var timeAxis = PlotModel.Axes.FirstOrDefault(a => a.Key == "Time");
                if (timeAxis != null)
                {
                    timeAxis.Zoom(newMinX, newMaxX);
                }
                
                // Reset Y axes to autoscale based on the new data
                PlotModel.Axes.FirstOrDefault(a => a.Key == "Analog")?.Reset();
                PlotModel.Axes.FirstOrDefault(a => a.Key == "Digital")?.Reset();

                PlotModel.InvalidatePlot(true); // This should make OxyPlot redraw series with updated ItemsSource
            });
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Failed in UpdateMainPlotData");
            Application.Current.Dispatcher.Invoke(() => {
                PlotModel.Subtitle = "Error updating plot data.";
                PlotModel.InvalidatePlot(true);
            });
        }
    }
    
    public void DeleteLoggingSession(LoggingSession session)
    {
        var stopwatch = new Stopwatch();
        stopwatch.Start();
        try
        {
            using (var context = _loggingContext.CreateDbContext())
            {
                context.ChangeTracker.AutoDetectChangesEnabled = false;

                var loggingSession = context.Sessions.Find(session.ID);
                // This will cascade delete and delete all corresponding data samples
                context.Sessions.Remove(loggingSession);
                context.ChangeTracker.DetectChanges();
                context.SaveChanges();
            }
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Failed in DeleteLoggingSession");
        }
        finally
        {
            stopwatch.Stop();
            Console.WriteLine(stopwatch.ElapsedMilliseconds / 1000);
        }
    }

    private (LineSeries series, LoggedSeriesLegendItem legendItem) AddChannelSeries(string channelName, string deviceSerialNo, ChannelType type, string color, bool isForMinimap)
    {
        var key = (deviceSerialNo, channelName);
       
        // Ensure dictionary entry exists for _allSessionPoints (main plot data) if it's for the main plot
        if (!isForMinimap && !_allSessionPoints.ContainsKey(key))
        {
            _allSessionPoints.Add(key, new List<DataPoint>());
        }
        // _sessionPoints is not used in the new logic.
        // Minimap data will be stored in a local variable within DisplayLoggingSession and assigned directly.

        var newLineSeries = new LineSeries
        {
            Title = $"{channelName} : ({deviceSerialNo})", // Consistent title format
            Color = OxyColor.Parse(color),
            IsVisible = true, 
            StrokeThickness = isForMinimap ? 1 : 1.5, // Thinner lines for minimap
            MarkerSize = isForMinimap ? 0 : 2, // No markers for minimap
            MarkerType = isForMinimap ? MarkerType.None : MarkerType.Circle // No markers for minimap
        };

        LoggedSeriesLegendItem legendItem = null;
        if (!isForMinimap)
        {
            // This ItemsSource will be updated in DisplayLoggingSession after data is fetched.
            // For now, it can be null or an empty list if _allSessionPoints was just initialized.
            newLineSeries.ItemsSource = _allSessionPoints[key];
            
            legendItem = new LoggedSeriesLegendItem(
                newLineSeries.Title,
                newLineSeries.Color,
                newLineSeries.IsVisible,
                newLineSeries,
                PlotModel); // Main plot model for legend interaction
        }
        // For minimap series, ItemsSource will be set directly in DisplayLoggingSession.

        switch (type)
        {
            case ChannelType.Analog:
                newLineSeries.YAxisKey = "Analog";
                break;
            case ChannelType.Digital:
                // For minimap, digital channels will also use the "Analog" Y axis to be overlaid.
                // Their 0/1 values will be scaled with other analog channels.
                newLineSeries.YAxisKey = isForMinimap ? "Analog" : "Digital";
                // Optionally hide digital channels on the minimap if they are too noisy or not useful:
                // if (isForMinimap) newLineSeries.IsVisible = false;
                break;
        }
        return (newLineSeries, legendItem);
    }

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
        using (var stream = System.IO.File.Create(dialog.FileName))
        {
            pngExporter.Export(PlotModel, stream);
        }
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