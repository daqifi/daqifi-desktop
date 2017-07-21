using Daqifi.Desktop;
using OxyPlot;
using OxyPlot.Axes;
using System;
using System.IO;
using System.Windows.Input;

namespace DAQifi.Desktop.ViewModels
{
    public class PlottingViewModel
    {
        #region Properties
        public IEventLogger EventLogger = WindowsEventLogger.Instance;
        public PlotModel PlotModel { get; set; }
        #endregion

        #region Command Properties
        public ICommand ZoomOutXCommand { get; private set; }
        private bool CanZoomOutX(object o)
        {
            return true;
        }

        public ICommand ZoomInXCommand { get; private set; }
        private bool CanZoomInX(object o)
        {
            return true;
        }

        public ICommand ZoomOutYCommand { get; private set; }
        private bool CanZoomOutY(object o)
        {
            return true;
        }

        public ICommand ZoomInYCommand { get; private set; }
        private bool CanZoomInY(object o)
        {
            return true;
        }

        public ICommand SaveGraphCommand { get; private set; }
        private bool CanSaveGraph(object o)
        {
            return true;
        }

        public ICommand ResetZoomCommand { get; private set; }
        private bool CanResetZoom(object o)
        {
            return true;
        }
        #endregion

        #region Constructor
        public PlottingViewModel()
        {
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

            PlotModel.Axes.Add(analogAxis);
            PlotModel.Axes.Add(digitalAxis);
            PlotModel.Axes.Add(timeAxis);

            PlotModel.IsLegendVisible = true;
            PlotModel.LegendOrientation = LegendOrientation.Vertical;
            PlotModel.LegendPlacement = LegendPlacement.Outside;
        }
        #endregion

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
}
