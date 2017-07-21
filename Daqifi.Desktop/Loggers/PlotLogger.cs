using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Commands;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;

namespace Daqifi.Desktop.Logger
{
    public class PlotLogger : ObservableObject, ILogger
    {
        #region Private Data
        private PlotModel _plotModel;
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private long _lastUpdateMilliSeconds;
        private int _precision = 4;
        private Dictionary<string, List<DataPoint>> _loggedPoints = new Dictionary<string, List<DataPoint>>();
        private Dictionary<string, LineSeries> _loggedChannels = new Dictionary<string, LineSeries>();
        #endregion

        #region Properties
        public PlotModel PlotModel
        {
            get { return _plotModel; }
            set
            {
                _plotModel = value;
                NotifyPropertyChanged("PlotModel");
            }
        }

        public DateTime? FirstTime { get; set; }

        public Dictionary<string, List<DataPoint>> LoggedPoints
        {
            get { return _loggedPoints; }
            private set { _loggedPoints = value; NotifyPropertyChanged("LoggedPoints"); }
        }

        public Dictionary<string, LineSeries> LoggedChannels
        {
            get { return _loggedChannels; }
            set { _loggedChannels = value; NotifyPropertyChanged("LoggedChannels"); }
        }

        public int Precision
        {
            get { return _precision; }
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
            get { return PlotModel.Axes[0].MajorGridlineThickness > 0; }
            set
            {
                PlotModel.Axes[0].MajorGridlineThickness = value ? 1 : 0;
                PlotModel.InvalidatePlot(true);
                NotifyPropertyChanged("ShowingMajorXAxisGrid");
            }
        }

        public bool ShowingMinorXAxisGrid
        {
            get { return PlotModel.Axes[0].MinorGridlineThickness > 0; }
            set
            {
                PlotModel.Axes[0].MinorGridlineThickness = value ? 1 : 0;
                PlotModel.InvalidatePlot(true);

                NotifyPropertyChanged("ShowingMinorXAxisGrid");
            }
        }

        public bool ShowingMajorYAxisGrid
        {
            get { return PlotModel.Axes[2].MajorGridlineThickness > 0; }
            set
            {
                PlotModel.Axes[2].MajorGridlineThickness = value ? 1 : 0;
                PlotModel.InvalidatePlot(true);
                NotifyPropertyChanged("ShowingMajorYAxisGrid");
            }
        }

        public bool ShowingMinorYAxisGrid
        {
            get { return PlotModel.Axes[2].MinorGridlineThickness > 0; }
            set
            {
                PlotModel.Axes[2].MinorGridlineThickness = value ? 1 : 0;
                PlotModel.InvalidatePlot(true);
                NotifyPropertyChanged("ShowingMinorYAxisGrid");
            }
        }
        #endregion

        #region Command Properties
        public ICommand ZoomInXCommand { get; private set; }
        private bool CanZoomInX(object o)
        {
            return true;
        }

        public ICommand ZoomOutXCommand { get; private set; }
        private bool CanZoomOutX(object o)
        {
            return true;
        }

        public ICommand ZoomInYCommand { get; private set; }
        private bool CanZoomInY(object o)
        {
            return true;
        }

        public ICommand ZoomOutYCommand { get; private set; }
        private bool CanZoomOutY(object o)
        {
            return true;
        }

        public ICommand SaveLiveGraphCommand { get; private set; }
        private bool CanSaveLiveGraph(object o)
        {
            return true;
        }

        public ICommand ResetZoomLiveGraphCommand { get; private set; }
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
            
            LoggedPoints = new Dictionary<string, List<DataPoint>>();
            PlotModel = new PlotModel();

            LinearAxis analogAxis = new LinearAxis
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

            LinearAxis digitalAxis = new LinearAxis
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

            LinearAxis timeAxis = new LinearAxis
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

            CompositionTarget.Rendering += CompositionTargetRendering;
            _stopwatch.Start();
        }
        #endregion

        public void Log(DataSample dataSample)
        {
            //Check if we already have a series for this Channel.  If not, then create one
            if (!LoggedChannels.Keys.Contains(dataSample.ChannelName))
            {
                AddChannelSeries(dataSample.ChannelName, dataSample.Type, dataSample.Color);
            }
            else
            {
                //Check for a change in color
                if (LoggedChannels[dataSample.ChannelName].Color.ToString().ToLower() != dataSample.Color.ToLower())
                {
                    LoggedChannels[dataSample.ChannelName].Color = OxyColor.Parse(dataSample.Color.ToLower());
                }
            }

            if (FirstTime == null) FirstTime = new DateTime(dataSample.TimestampTicks);

            double deltaTime = (dataSample.TimestampTicks - FirstTime.Value.Ticks) / 10000.0; //Ticks is 100 nanoseconds
            double scaledSampleValue = dataSample.Value;

            lock (PlotModel.SyncRoot)
            {
                LoggedPoints[dataSample.ChannelName].Add(new DataPoint(deltaTime, scaledSampleValue));
                if (LoggedPoints[dataSample.ChannelName].Count >= 5000)
                {
                    LoggedPoints[dataSample.ChannelName].RemoveAt(0);
                }
            }

            NotifyPropertyChanged("LoggedPoints");
        }

        private void AddChannelSeries(string channelName, ChannelType channelType, string newColor)
        {
            var newDataPoints = new List<DataPoint>();
            LoggedPoints.Add(channelName, newDataPoints);

            var newLineSeries = new LineSeries()
            {
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

            LoggedChannels.Add(channelName, newLineSeries);
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
                    if (!File.Exists(picturesDirectory + "\\" + fileName + ".png")) break;
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
}
