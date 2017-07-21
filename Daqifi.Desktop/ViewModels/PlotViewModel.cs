using System;
using Daqifi.Desktop.Channel;
using OxyPlot;
using OxyPlot.Series;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DAQifi.Desktop.ViewModels
{
    public class PlotViewModel
    {
        public IEventLogger EventLogger = WindowsEventLogger.Instance;
        public PlotModel PlotModel { get; set; }
        public Dictionary<string, List<DataPoint>> SessionPoints = new Dictionary<string, List<DataPoint>>();

        public void ClearPlot()
        {
            PlotModel.Series.Clear();
            PlotModel.InvalidatePlot(true);
        }

        public void AddChannel(string channelName, ChannelType type, string color)
        {
            var newLineSeries = new LineSeries()
            {
                Title = channelName,
                ItemsSource = SessionPoints.Last().Value,
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
        }

        private void SavePlot(object o)
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
                using (var stream = File.Create(filePath))
                {
                    OxyPlot.Wpf.PngExporter.Export(PlotModel, stream, 1920, 1080, OxyColors.White);
                }
            }
            catch (Exception ex)
            {
                EventLogger.Error("Failed in SaveGraph", ex);
            }
        }
    }
}
