using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Commands;
using Daqifi.Desktop.DataModel.Channel;
using Daqifi.Desktop.Device;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using TickStyle = OxyPlot.Axes.TickStyle;

namespace Daqifi.Desktop.Logger;

public class PlotLogger : ObservableObject, ILogger
{
    #region Private Data
    private PlotModel _plotModel;
    private readonly Stopwatch _stopwatch = new Stopwatch();
    private long _lastUpdateMilliSeconds;
    private int _precision = 4;
    private Dictionary<(string deviceSerial, string channelName), List<DataPoint>> _loggedPoints = new Dictionary<(string deviceSerial, string channelName), List<DataPoint>>();
    private Dictionary<(string deviceSerial, string channelName), LineSeries> _loggedChannels = new Dictionary<(string deviceSerial, string channelName), LineSeries>();
    #endregion

    #region Properties
    public PlotModel PlotModel
    {
        get => _plotModel;
        set
        {
            _plotModel = value;
            NotifyPropertyChanged("PlotModel");
        }
    }

    public DateTime? FirstTime { get; set; }

    public Dictionary<(string deviceSerial, string channelName), List<DataPoint>> LoggedPoints
    {
        get => _loggedPoints;
        private set { _loggedPoints = value; NotifyPropertyChanged("LoggedPoints"); }
    }

    public Dictionary<(string deviceSerial, string channelName), LineSeries> LoggedChannels
    {
        get => _loggedChannels;
        set { _loggedChannels = value; NotifyPropertyChanged("LoggedChannels"); }
    }

    public int Precision
    {
        get => _precision;
        set
        {
            _precision = value;
            PlotModel.Axes[0].StringFormat = "0." + new string('#', _precision);
            PlotModel.InvalidatePlot(true);
            NotifyPropertyChanged("Precision");
        }
    }

    public bool ShowingMajorXAxisGrid
    {
        get => PlotModel.Axes[0].MajorGridlineThickness > 0;
        set
        {
            PlotModel.Axes[0].MajorGridlineThickness = value ? 1 : 0;
            PlotModel.InvalidatePlot(true);
            NotifyPropertyChanged("ShowingMajorXAxisGrid");
        }
    }

    public bool ShowingMinorXAxisGrid
    {
        get => PlotModel.Axes[0].MinorGridlineThickness > 0;
        set
        {
            PlotModel.Axes[0].MinorGridlineThickness = value ? 1 : 0;
            PlotModel.InvalidatePlot(true);

            NotifyPropertyChanged("ShowingMinorXAxisGrid");
        }
    }

    public bool ShowingMajorYAxisGrid
    {
        get => PlotModel.Axes[2].MajorGridlineThickness > 0;
        set
        {
            PlotModel.Axes[2].MajorGridlineThickness = value ? 1 : 0;
            PlotModel.InvalidatePlot(true);
            NotifyPropertyChanged("ShowingMajorYAxisGrid");
        }
    }

    public bool ShowingMinorYAxisGrid
    {
        get => PlotModel.Axes[2].MinorGridlineThickness > 0;
        set
        {
            PlotModel.Axes[2].MinorGridlineThickness = value ? 1 : 0;
            PlotModel.InvalidatePlot(true);
            NotifyPropertyChanged("ShowingMinorYAxisGrid");
        }
    }
    #endregion

    #region Command Properties
    public ICommand ZoomInXCommand { get; }
    private bool CanZoomInX(object o)
    {
        return true;
    }

    public ICommand ZoomOutXCommand { get; }
    private bool CanZoomOutX(object o)
    {
        return true;
    }

    public ICommand ZoomInYCommand { get; }
    private bool CanZoomInY(object o)
    {
        return true;
    }

    public ICommand ZoomOutYCommand { get; }
    private bool CanZoomOutY(object o)
    {
        return true;
    }

    public ICommand SaveLiveGraphCommand { get; }
    private bool CanSaveLiveGraph(object o)
    {
        return true;
    }

    public ICommand ResetZoomLiveGraphCommand { get; }
    private bool CanResetZoomLiveGraph(object o)
    {
        return true;
    }
    #endregion

    #region Constructor
    public PlotLogger()
    {
        ZoomInXCommand = new DelegateCommand(ZoomInX, CanZoomInX);
        ZoomOutXCommand = new DelegateCommand(ZoomOutX, CanZoomOutX);
        ZoomInYCommand = new DelegateCommand(ZoomInY, CanZoomInY);
        ZoomOutYCommand = new DelegateCommand(ZoomOutY, CanZoomOutY);
        ResetZoomLiveGraphCommand = new DelegateCommand(ResetZoomLiveGraph, CanResetZoomLiveGraph);
        SaveLiveGraphCommand = new DelegateCommand(SaveLiveGraph, CanSaveLiveGraph);
            
        LoggedPoints = new Dictionary<(string deviceSerial, string channelName), List<DataPoint>>();
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

        NotifyPropertyChanged("LoggedPoints");
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

        switch(channelType)
        {
            case ChannelType.Analog:
                newLineSeries.YAxisKey = "Analog";
                break;
            case ChannelType.Digital:
                newLineSeries.YAxisKey = "Digital";
                break;
        }

        LoggedChannels.Add(key, newLineSeries);
        PlotModel.Series.Add(newLineSeries);
            
        NotifyPropertyChanged("PlotModel");
    }

    private void CompositionTargetRendering(object sender, EventArgs e)
    {
        if (_stopwatch.ElapsedMilliseconds > _lastUpdateMilliSeconds + 1000)
        {
            lock (PlotModel.SyncRoot)
            {
                PlotModel.InvalidatePlot(true);
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
        NotifyPropertyChanged("LoggedChannels");
        NotifyPropertyChanged("LoggedPoints");
        NotifyPropertyChanged("PlotModel");
    }

    #region Command Methods
    private void ZoomInX(object o)
    {
        PlotModel.Axes[2].ZoomAtCenter(1.5);
        PlotModel.InvalidatePlot(false);
    }

    private void ZoomOutX(object o)
    {
        PlotModel.Axes[2].ZoomAtCenter(1 / 1.5);
        PlotModel.InvalidatePlot(false);
    }

    private void ZoomInY(object o)
    {
        PlotModel.Axes[0].ZoomAtCenter(1.5);
        PlotModel.InvalidatePlot(false);
    }

    private void ZoomOutY(object o)
    {
        PlotModel.Axes[0].ZoomAtCenter(1 / 1.5);
        PlotModel.InvalidatePlot(false);
    }
    private void ResetZoomLiveGraph(object o)
    {
        PlotModel.Axes[0].Reset();
        PlotModel.Axes[2].Reset();
    }

    private void SaveLiveGraph(object o)
    {
        //For copying to memeory and need to put on clipboard
        /*using (var stream = new MemoryStream())
        {
            var pngExporter = new PngExporter();
            pngExporter.Export(_plotter.PlotModel, stream);
        }*/

        string picturesDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) + "\\DAQifi";

        //Check if the folder exists
        if (!Directory.Exists(picturesDirectory))
        {
            Directory.CreateDirectory(picturesDirectory);
        }

        //Check if File Name Exists. If so, find a unique name.
        string fileName = "Live Graph";
        int count = 1;

        while (true)
        {
            if (count == 1)
            {
                if (!File.Exists(picturesDirectory + "\\" + fileName + ".png")) { break; }
            }

            count++;

            if (!File.Exists(picturesDirectory + "\\" + fileName + count + ".png"))
            {
                fileName += count;
                break;
            }
        }

        using (var stream = File.Create(picturesDirectory + "\\" + fileName + ".png"))
        {
            var pngExporter = new OxyPlot.Wpf.PngExporter();
            pngExporter.Export(PlotModel, stream);
        }
    }

    #endregion 
}