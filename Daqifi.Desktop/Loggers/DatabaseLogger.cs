using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.DataModel.Channel;
using Daqifi.Desktop.Device;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows.Input;
using Exception = System.Exception;
using TickStyle = OxyPlot.Axes.TickStyle;
using Microsoft.EntityFrameworkCore;
using EFCore.BulkExtensions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Daqifi.Desktop.Logger;

public partial class DatabaseLogger : ObservableObject, ILogger
{
    #region Private Data
    private readonly Dictionary<(string deviceSerial, string channelName), List<DataPoint>> _allSessionPoints = new Dictionary<(string deviceSerial, string channelName), List<DataPoint>>();
    private readonly BlockingCollection<DataSample> _buffer = new BlockingCollection<DataSample>();
    private readonly Dictionary<(string deviceSerial, string channelName), List<DataPoint>> _sessionPoints = new Dictionary<(string deviceSerial, string channelName), List<DataPoint>>();

    private DateTime? _firstTime;
    public AppLogger AppLogger = AppLogger.Instance;
    private readonly IDbContextFactory<LoggingContext> _loggingContext;
    #endregion

    #region Properties
    [ObservableProperty]
    private PlotModel _plotModel;
    #endregion

    #region Command Properties
    public ICommand ZoomOutXCommand { get; }
    private bool CanZoomOutX(object o)
    {
        return true;
    }

    public ICommand ZoomInXCommand { get; }
    private bool CanZoomInX(object o)
    {
        return true;
    }

    public ICommand ZoomOutYCommand { get; }
    private bool CanZoomOutY(object o)
    {
        return true;
    }

    public ICommand ZoomInYCommand { get; }
    private bool CanZoomInY(object o)
    {
        return true;
    }

    public ICommand SaveGraphCommand { get; }
    private bool CanSaveGraph(object o)
    {
        return true;
    }

    public ICommand ResetZoomCommand { get; }
    private bool CanResetZoom(object o)
    {
        return true;
    }
    #endregion

    #region Constructor
    public DatabaseLogger(IDbContextFactory<LoggingContext> loggingContext)
    {
        _loggingContext = loggingContext;
        SaveGraphCommand = new DelegateCommand(SaveGraph, CanSaveGraph);
        ResetZoomCommand = new DelegateCommand(ResetZoom, CanResetZoom);
        ZoomOutXCommand = new DelegateCommand(ZoomOutX, CanZoomOutX);
        ZoomInXCommand = new DelegateCommand(ZoomInX, CanZoomInX);
        ZoomOutYCommand = new DelegateCommand(ZoomOutY, CanZoomOutY);
        ZoomInYCommand = new DelegateCommand(ZoomInY, CanZoomInY);

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

        OxyPlot.Legends.Legend legend = new OxyPlot.Legends.Legend
        {
            LegendOrientation = OxyPlot.Legends.LegendOrientation.Vertical,
            LegendPlacement = OxyPlot.Legends.LegendPlacement.Outside
        };

        PlotModel.Axes.Add(analogAxis);
        PlotModel.Axes.Add(digitalAxis);
        PlotModel.Axes.Add(timeAxis);
        PlotModel.IsLegendVisible = true;
        PlotModel.Legends.Add(legend);

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
                AppLogger.Error(ex, "Failed in Consumer Thread");
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
            AppLogger.Error(ex, "Failed in DisplayLoggingSession");
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

                LoggingManager.Instance.LoggingSessions.Remove(session);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed in DeleteLoggingSession");
        }
        finally
        {
            stopwatch.Stop();
            Console.WriteLine(stopwatch.ElapsedMilliseconds / 1000);
        }
    }

    private void AddChannelSeries(string channelName, string DeviceSerialNo, ChannelType type, string color)
    {
        var key = (DeviceSerialNo, channelName);
        _sessionPoints.Add(key, new List<DataPoint>());
        _allSessionPoints.Add(key, new List<DataPoint>());

        var newLineSeries = new LineSeries()
        {
            Title = $"{channelName} : ({DeviceSerialNo})",
            ItemsSource = _sessionPoints.Last().Value,
            Color = OxyColor.Parse(color),

        };

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

    #region Command Methods
    private void SaveGraph(object o)
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                DefaultExt = ".csv",
                Filter = "Image|*.png"
            };

            // Show save file dialog box
            bool? result = dialog.ShowDialog();

            if (result == false) { return; }

            string filePath = dialog.FileName;
            OxyPlot.Wpf.PngExporter.Export(PlotModel, filePath, 1920, 1080);
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed in SaveGraph");
        }
    }

    private void ResetZoom(object o)
    {
        PlotModel.Axes[0].Reset();
        PlotModel.Axes[2].Reset();
        PlotModel.Axes[2].Maximum = double.NaN;
        PlotModel.InvalidatePlot(true);
    }

    private void ZoomOutX(object o)
    {
        PlotModel.Axes[2].ZoomAtCenter(1 / 1.5);
        PlotModel.InvalidatePlot(false);
    }

    private void ZoomInX(object o)
    {
        PlotModel.Axes[2].ZoomAtCenter(1.5);
        PlotModel.InvalidatePlot(false);
    }

    private void ZoomOutY(object o)
    {
        PlotModel.Axes[0].ZoomAtCenter(1 / 1.5);
        PlotModel.InvalidatePlot(false);
    }

    private void ZoomInY(object o)
    {
        PlotModel.Axes[0].ZoomAtCenter(1.5);
        PlotModel.InvalidatePlot(false);
    }
    #endregion
}