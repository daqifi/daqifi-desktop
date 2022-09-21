using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.DataModel.Channel;
using Daqifi.Desktop.Device;
using EntityFramework.BulkInsert;
using EntityFramework.BulkInsert.Extensions;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Input;
using Daqifi.Desktop.Helpers;
using Exception = System.Exception;

namespace Daqifi.Desktop.Logger
{
    public class DatabaseLogger: ObservableObject, ILogger
    {
        #region Private Data
        private readonly Dictionary<string, List<DataPoint>> _allSessionPoints = new Dictionary<string, List<DataPoint>>();
        private readonly BlockingCollection<DataSample> _buffer = new BlockingCollection<DataSample>();
        private readonly Dictionary<string, List<DataPoint>> _sessionPoints = new Dictionary<string, List<DataPoint>>();

        private PlotModel _plotModel;
        private DateTime? _firstTime;
        public AppLogger AppLogger = AppLogger.Instance;
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
        public DatabaseLogger()
        {
            ProviderFactory.Register<SqlCeBulkInsertProvider>("System.Data.SqlServerCe.SqlCeConnection");

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

            var consumerThread = new Thread(Consumer) {IsBackground = true};
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

                    if (bufferCount < 1) continue;

                    using (var context = new LoggingContext())
                    {
                        // Remove the samples from the collection
                        for (var i = 0; i < bufferCount; i++)
                        {
                            if (_buffer.TryTake(out var sample)) samples.Add(sample);
                        }
                        context.BulkInsert(samples);
                        samples.Clear();
                    }
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

                using (var context = new LoggingContext())
                {
                    context.Configuration.AutoDetectChangesEnabled = false;

                    var samples = context.Samples.AsNoTracking()
                        .Where(s => s.LoggingSessionID == session.ID)
                        .Select(s => new {s.ChannelName, s.Type, s.Color, s.TimestampTicks, s.Value});

                    var samplesCount = context.Samples
                        .AsNoTracking()
                        .Count(s => s.LoggingSessionID == session.ID);
                    const int dataPointsToShow = 1000000;
                    
                    if (samplesCount > 1000000)
                    {
                        PlotModel.Subtitle += $"\nOnly showing {dataPointsToShow:n0} out of {samplesCount:n0} data points";
                    }
                    
                    var channelNames = samples
                        .Select(s => s.ChannelName)
                        .Distinct()
                        .ToList();
                    
                    channelNames.Sort(new OrdinalStringComparer());
                    foreach (var channelName in channelNames)
                    {
                        var channel = samples
                            .Select(s=> new {s.ChannelName, s.Type, s.Color})
                            .FirstOrDefault(s => s.ChannelName == channelName);

                        if (channel != null)
                        {
                            AddChannelSeries(channel.ChannelName, channel.Type, channel.Color);
                        }
                    }

                    var dataSampleCount = 0;
                    foreach (var sample in samples)
                    {                       
                        if (_firstTime == null) _firstTime = new DateTime(sample.TimestampTicks);
                        var deltaTime = (sample.TimestampTicks - _firstTime.Value.Ticks) / 10000.0;

                        // Add new datapoint
                        _allSessionPoints[sample.ChannelName].Add(new DataPoint(deltaTime, sample.Value));

                        dataSampleCount++;

                        if (dataSampleCount >= dataPointsToShow)
                        {
                            break;
                        }
                    }
                }

                // Downsample
                for (var i = 0; i < _sessionPoints.Keys.Count; i++ )
                {
                    var channelName = _sessionPoints.Keys.ElementAt(i);
                    //TODO Figure out best way to integrate LTTB
                    //(PlotModel.Series[i] as LineSeries).ItemsSource = LargestTriangleThreeBucket.DownSample(_allSessionPoints[channelName], 1000);
                    (PlotModel.Series[i] as LineSeries).ItemsSource = _allSessionPoints[channelName];
                }

                NotifyPropertyChanged("SessionPoints");
                PlotModel.InvalidatePlot(true);
            }
            catch(Exception ex)
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
                using (var context = new LoggingContext())
                {
                    context.Configuration.AutoDetectChangesEnabled = false;
                    
                    var loggingSession = context.Sessions.Find(session.ID);
                    // This will cascade delete and delete all corresponding data samples
                    context.Sessions.Remove(loggingSession);
                    context.ChangeTracker.DetectChanges();
                    context.SaveChanges();

                    LoggingManager.Instance.LoggingSessions.Remove(session);
                }
            }
            catch(Exception ex)
            {
                AppLogger.Error(ex, "Failed in DeleteLoggingSession");
            }
            finally
            {
                stopwatch.Stop();
                Console.WriteLine(stopwatch.ElapsedMilliseconds / 1000);
            }
        }

        private void AddChannelSeries(string channelName, ChannelType type, string color)
        {
            _sessionPoints.Add(channelName, new List<DataPoint>());
            _allSessionPoints.Add(channelName, new List<DataPoint>());

            var newLineSeries = new LineSeries()
            {
                Title = channelName,
                ItemsSource = _sessionPoints.Last().Value,
                Color = OxyColor.Parse(color)
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
            NotifyPropertyChanged("PlotModel");
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

                if (result == false) return;

                string filePath = dialog.FileName;
                OxyPlot.Wpf.PngExporter.Export(PlotModel, filePath, 1920, 1080);
            }
            catch(Exception ex)
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
            PlotModel.Axes[2].ZoomAtCenter(1/1.5);
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
}