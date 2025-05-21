using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.DataModel.Channel;
using Daqifi.Desktop.Device;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.Collections.Concurrent;
using System.Diagnostics;
using Exception = System.Exception;
using TickStyle = OxyPlot.Axes.TickStyle;
using Microsoft.EntityFrameworkCore;
using EFCore.BulkExtensions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
                _plotModel?.InvalidatePlot(true);
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
        // PlotModel.Legends.Add(legend); // Remove legend from plot model

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
        _firstTime = null;
        _sessionPoints.Clear();
        _allSessionPoints.Clear();
        PlotModel.Series.Clear();
        PlotModel.Title = string.Empty;
        PlotModel.Subtitle = string.Empty;
        PlotModel.InvalidatePlot(true);
    }

    public void DisplayLoggingSession(LoggingSession session)
    {
        try
        {
            ClearPlot();
            PlotModel.Title = session.Name;

            using (var context = _loggingContext.CreateDbContext())
            {
                context.ChangeTracker.AutoDetectChangesEnabled = false;

                var samples = context.Samples.AsNoTracking()
                    .Where(s => s.LoggingSessionID == session.ID)
                    .Select(s => new { s.ChannelName, s.DeviceSerialNo, s.Type, s.Color, s.TimestampTicks, s.Value });

                var samplesCount = context.Samples
                    .AsNoTracking()
                    .Count(s => s.LoggingSessionID == session.ID);
                const int dataPointsToShow = 1000000;

                if (samplesCount > 1000000)
                {
                    PlotModel.Subtitle += $"\nOnly showing {dataPointsToShow:n0} out of {samplesCount:n0} data points";
                }

                var channelNames = samples.Select(s => new { s.ChannelName, s.DeviceSerialNo }).Distinct().ToList();

                // Sort channel-device pairs
                channelNames.Sort((x, y) => string.Compare(x.ChannelName, y.ChannelName, StringComparison.Ordinal));
                foreach (var pair in channelNames)
                {
                    var channel = samples
                        .FirstOrDefault(s => s.ChannelName == pair.ChannelName && s.DeviceSerialNo == pair.DeviceSerialNo);

                    if (channel != null)
                    {
                        AddChannelSeries(channel.ChannelName, channel.DeviceSerialNo, channel.Type, channel.Color);
                    }
                }

                var dataSampleCount = 0;
                foreach (var sample in samples)
                {
                    var key = (sample.DeviceSerialNo, sample.ChannelName);
                    if (_firstTime == null) { _firstTime = new DateTime(sample.TimestampTicks); }
                    var deltaTime = (sample.TimestampTicks - _firstTime.Value.Ticks) / 10000.0;

                    // Add new datapoint
                    _allSessionPoints[key].Add(new DataPoint(deltaTime, sample.Value));

                    dataSampleCount++;

                    if (dataSampleCount >= dataPointsToShow)
                    {
                        break;
                    }
                }
            }

            // Downsample
            for (var i = 0; i < _sessionPoints.Keys.Count; i++)
            {
                var channelName = _sessionPoints.Keys.ElementAt(i);
                //TODO Figure out best way to integrate LTTB
                //(PlotModel.Series[i] as LineSeries).ItemsSource = LargestTriangleThreeBucket.DownSample(_allSessionPoints[channelName], 1000);
                ((LineSeries)PlotModel.Series[i]).ItemsSource = _allSessionPoints[channelName];
            }

            OnPropertyChanged("SessionPoints");
            PlotModel.InvalidatePlot(true);
        }
        catch (Exception ex)
        {
            _appLogger.Error(ex, "Failed in DisplayLoggingSession");
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

    private void AddChannelSeries(string channelName, string deviceSerialNo, ChannelType type, string color)
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
            newLineSeries.Color,
            newLineSeries.IsVisible,
            newLineSeries,
            PlotModel);
        LegendItems.Add(legendItem);

        switch (type)
        {
            case ChannelType.Analog:
                newLineSeries.YAxisKey = "Analog";
                break;
            case ChannelType.Digital:
                newLineSeries.YAxisKey = "Digital";
                break;
        }

        PlotModel.Series.Add(newLineSeries);
        OnPropertyChanged("PlotModel");
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