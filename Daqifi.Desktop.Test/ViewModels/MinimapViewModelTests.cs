using NUnit.Framework;
using Daqifi.Desktop.ViewModels;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Daqifi.Desktop.Test.ViewModels
{
    [TestFixture]
    public class MinimapViewModelTests
    {
        private MinimapViewModel _minimapViewModel;
        private PlotModel _mockMainPlotModel;

        [SetUp]
        public void SetUp()
        {
            // Assumes DaqifiViewModel is not strictly necessary to construct MinimapViewModel for unit tests
            // or that its dependencies can be mocked if needed. For now, direct instantiation.
            _minimapViewModel = new MinimapViewModel();
            
            _mockMainPlotModel = new PlotModel();
            _mockMainPlotModel.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Key = "Time" });
            _mockMainPlotModel.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Key = "Value" });
        }

        private List<DataPoint> GenerateTestData(int count, double amplitude = 10, double cycles = 1, double xStart = 0)
        {
            var data = new List<DataPoint>();
            for (int i = 0; i < count; i++)
            {
                double x = xStart + (double)i;
                // Ensure y is not always 0 for some counts for better testing of min/max
                double y = amplitude * Math.Sin(2 * Math.PI * cycles * (double)i / (count > 1 ? count -1 : 1) ); 
                data.Add(new DataPoint(x, y));
            }
            return data;
        }

        [Test]
        public void UpdateDownsampledSeries_NullData_ClearsSeries()
        {
            _minimapViewModel.SetDataAndMainPlot(null, _mockMainPlotModel);
            var series = _minimapViewModel.PlotModel.Series.FirstOrDefault() as LineSeries;
            
            Assert.IsNotNull(series, "Downsampled series should exist.");
            Assert.IsNull(series.ItemsSource, "ItemsSource should be null when data is null.");
        }

        [Test]
        public void UpdateDownsampledSeries_EmptyData_ClearsSeries()
        {
            _minimapViewModel.SetDataAndMainPlot(new List<DataPoint>(), _mockMainPlotModel);
            var series = _minimapViewModel.PlotModel.Series.FirstOrDefault() as LineSeries;

            Assert.IsNotNull(series);
            Assert.IsNull(series.ItemsSource, "ItemsSource should be null for empty data.");
        }

        [Test]
        public void UpdateDownsampledSeries_DataLessThanTarget_UsesAllPoints()
        {
            var testData = GenerateTestData(500); 
            _minimapViewModel.SetDataAndMainPlot(testData, _mockMainPlotModel);
            var series = _minimapViewModel.PlotModel.Series.FirstOrDefault() as LineSeries;

            Assert.IsNotNull(series);
            Assert.IsNotNull(series.ItemsSource, "ItemsSource should not be null.");
            var items = series.ItemsSource.Cast<DataPoint>().ToList();
            Assert.AreEqual(testData.Count, items.Count, "Should use all points if data count is less than target.");
            CollectionAssert.AreEqual(testData, items, "Data should be identical if all points are used.");
        }

        [Test]
        public void UpdateDownsampledSeries_DataGreaterThanTarget_Downsamples()
        {
            var testData = GenerateTestData(2000); 
            _minimapViewModel.SetDataAndMainPlot(testData, _mockMainPlotModel);
            var series = _minimapViewModel.PlotModel.Series.FirstOrDefault() as LineSeries;

            Assert.IsNotNull(series);
            Assert.IsNotNull(series.ItemsSource, "ItemsSource should not be null.");
            var items = series.ItemsSource.Cast<DataPoint>().ToList();
            
            int targetMinimapPoints = 1000; // As defined in MinimapViewModel
            Assert.IsTrue(items.Count <= targetMinimapPoints * 2, $"Downsampled points count ({items.Count}) should be at most {targetMinimapPoints * 2}.");
            Assert.IsTrue(items.Count > 0, "Downsampled points count should be greater than 0.");

            Assert.AreEqual(testData.Min(p => p.X), items.Min(p => p.X), 1e-9, "Min X should match.");
            Assert.AreEqual(testData.Max(p => p.X), items.Max(p => p.X), 1e-9, "Max X should match.");
        }
        
        [Test]
        public void UpdateDownsampledSeries_AllSameYValues_DownsamplesCorrectly()
        {
            var testData = new List<DataPoint>();
            for(int i=0; i<1500; i++) testData.Add(new DataPoint(i, 5.0));
            
            _minimapViewModel.SetDataAndMainPlot(testData, _mockMainPlotModel);
            var series = _minimapViewModel.PlotModel.Series.FirstOrDefault() as LineSeries;
            Assert.IsNotNull(series?.ItemsSource);
            var items = series.ItemsSource.Cast<DataPoint>().ToList();

            int targetMinimapPoints = 1000;
            Assert.IsTrue(items.Count <= targetMinimapPoints * 2); 
            // With current downsampler, if min and max Y are same, it might add only 1 point per bucket or 2 identical points.
            // If it adds 1, count is ~target. If 2, count is ~target*2.
            // Let's check that all Y values are correct.
            Assert.IsTrue(items.All(p => p.Y == 5.0), "All Y values in downsampled data should be 5.0.");
        }

        [Test]
        public void UpdateSelectionRectangleFromMainPlot_UpdatesRectangle()
        {
            var minimapData = GenerateTestData(100, xStart:0); // Data from X=0 to X=99
            _minimapViewModel.SetDataAndMainPlot(minimapData, _mockMainPlotModel);

            var mainXAxis = _mockMainPlotModel.Axes.First(ax => ax.Position == AxisPosition.Bottom);
            mainXAxis.Zoom(10, 20); 
            _minimapViewModel.OnMainPlotAxisChanged(mainXAxis, new AxisChangedEventArgs(AxisChangeTypes.Zoom));

            var selectionRectangle = _minimapViewModel.PlotModel.Annotations.OfType<RectangleAnnotation>().FirstOrDefault();
            Assert.IsNotNull(selectionRectangle);
            Assert.AreEqual(10, selectionRectangle.MinimumX, 1e-9);
            Assert.AreEqual(20, selectionRectangle.MaximumX, 1e-9);
        }

        [Test]
        public void UpdateMainPlotXAxis_UpdatesMainPlotModel()
        {
            var minimapData = GenerateTestData(100);
            _minimapViewModel.SetDataAndMainPlot(minimapData, _mockMainPlotModel);

            _minimapViewModel.UpdateMainPlotXAxis(30, 40);

            var mainXAxis = _mockMainPlotModel.Axes.First(ax => ax.Position == AxisPosition.Bottom);
            Assert.AreEqual(30, mainXAxis.ActualMinimum, 1e-9);
            Assert.AreEqual(40, mainXAxis.ActualMaximum, 1e-9);
        }
        
        [TearDown]
        public void TearDown()
        {
            _minimapViewModel?.Cleanup();
        }
    }
}
