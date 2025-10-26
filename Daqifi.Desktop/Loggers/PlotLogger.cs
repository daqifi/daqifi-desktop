using Daqifi.Desktop.Channel;
using ChannelDirection = Daqifi.Core.Channel.ChannelDirection;
using ChannelType = Daqifi.Core.Channel.ChannelType;
using Daqifi.Desktop.Device;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TickStyle = OxyPlot.Axes.TickStyle;

namespace Daqifi.Desktop.Logger;

public partial class PlotLogger : ObservableObject, ILogger
{
    #region Private Data
    private PlotModel _plotModel;
    private readonly Stopwatch _stopwatch = new();
    private long _lastUpdateMilliSeconds;
    private int _precision = 4;
    private Dictionary<(string deviceSerial, string channelName), List<DataPoint>> _loggedPoints = [];
    private Dictionary<(string deviceSerial, string channelName), LineSeries> _loggedChannels = [];
    #endregion

    #region Properties
    public PlotModel PlotModel
    {
        get => _plotModel;
        set
        {
            _plotModel = value;
            OnPropertyChanged();
        }
    }

    public DateTime? FirstTime { get; set; }

    public Dictionary<(string deviceSerial, string channelName), List<DataPoint>> LoggedPoints
    {
        get => _loggedPoints;
        private set { _loggedPoints = value; OnPropertyChanged(); }
    }

    public Dictionary<(string deviceSerial, string channelName), LineSeries> LoggedChannels
    {
        get => _loggedChannels;
        set { _loggedChannels = value; OnPropertyChanged(); }
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
            if (LoggedChannels[key].Color.ToString().ToLower() != dataSample.Color.ToLower())
            {
                LoggedChannels[key].Color = OxyColor.Parse(dataSample.Color.ToLower());
            }
        }

        if (FirstTime == null) { FirstTime = new DateTime(dataSample.TimestampTicks); }

        var deltaTime = (dataSample.TimestampTicks - FirstTime.Value.Ticks) / 10000.0; //Ticks is 100 nanoseconds
        var scaledSampleValue = dataSample.Value;

        lock (PlotModel.SyncRoot)
        {
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
        LoggedPoints.Add(key, newDataPoints);

        var newLineSeries = new LineSeries
        {
            Title = channelName,
            ItemsSource = newDataPoints,
            Color = OxyColor.Parse(newColor)
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

        LoggedChannels.Add(key, newLineSeries);
        PlotModel.Series.Add(newLineSeries);

        OnPropertyChanged(nameof(PlotModel));
    }

    private void CompositionTargetRendering(object sender, EventArgs e)
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
                        if (LoggedChannels.TryGetValue(key, out LineSeries series))
                        {
                            if (series.IsVisible != channel.IsVisible)
                            {
                                series.IsVisible = channel.IsVisible;
                            }
                        }
                    }
                }
                PlotModel.InvalidatePlot(true); // This will redraw the plot with updated series visibility
                _lastUpdateMilliSeconds = _stopwatch.ElapsedMilliseconds;
            }
        }
    }

    public void ClearPlot()
    {
        LoggedChannels.Clear();
        LoggedPoints.Clear();
        PlotModel.Series.Clear();
        PlotModel.InvalidatePlot(true);
        FirstTime = null;
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