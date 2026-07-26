using Daqifi.Desktop.Helpers;
using Daqifi.Desktop.Logger;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using ChannelType = Daqifi.Core.Channel.ChannelType;

namespace Daqifi.Desktop.Test.Loggers;

/// <summary>
/// Behavior contract for <see cref="PlotModelFactory"/> — the pure OxyPlot construction extracted from
/// <c>DatabaseLogger</c> (issue #592). Every factory method is a side-effect-free constructor of OxyPlot
/// objects, so these tests run with no database, no background threads, and no WPF runtime: they simply
/// build the models/series and assert their axis keys, positions, theme, channel-to-axis mapping, color
/// parsing, and minimap annotation configuration. This is the first real test seam over plot
/// construction, which previously lived inline in the logger's constructor.
/// </summary>
[TestClass]
public class PlotModelFactoryTests
{
    private const string Serial = "9090684023231015079";
    private const string Color = "#FFD32F2F";

    #region Axis key contract

    [TestMethod]
    public void AxisKeys_MatchTheStringsTheViewportMachineryLooksUpBy()
    {
        // The logger's viewport code and the minimap controller find axes by these exact strings, so the
        // constants are a shared contract the factory must not drift from.
        // Read through locals so the comparisons happen at run time: comparing two compile-time
        // constants folds away and the assertions would no longer pin anything (MSTEST0032).
        string analog = PlotModelFactory.ANALOG_AXIS_KEY;
        string digital = PlotModelFactory.DIGITAL_AXIS_KEY;
        string time = PlotModelFactory.TIME_AXIS_KEY;
        string minimapTime = PlotModelFactory.MINIMAP_TIME_AXIS_KEY;
        string minimapY = PlotModelFactory.MINIMAP_Y_AXIS_KEY;

        Assert.AreEqual("Analog", analog);
        Assert.AreEqual("Digital", digital);
        Assert.AreEqual("Time", time);
        Assert.AreEqual("MinimapTime", minimapTime);
        Assert.AreEqual("MinimapY", minimapY);
    }

    #endregion

    #region CreateMainPlotModel

    [TestMethod]
    public void CreateMainPlotModel_AddsAnalogDigitalTimeAxes_WithExpectedKeysAndPositions()
    {
        var model = PlotModelFactory.CreateMainPlotModel();

        Assert.AreEqual(3, model.Axes.Count, "Main plot has exactly the analog, digital, and time axes.");

        var analog = GetAxis(model, PlotModelFactory.ANALOG_AXIS_KEY);
        Assert.IsInstanceOfType<LinearAxis>(analog);
        Assert.AreEqual(AxisPosition.Left, analog.Position);
        Assert.AreEqual("Analog (V)", analog.Title);
        Assert.AreEqual("0.###", analog.StringFormat);

        var digital = GetAxis(model, PlotModelFactory.DIGITAL_AXIS_KEY);
        Assert.IsInstanceOfType<LinearAxis>(digital);
        Assert.AreEqual(AxisPosition.Right, digital.Position);
        Assert.AreEqual("Digital", digital.Title);
        Assert.AreEqual(-0.1, digital.Minimum, "Digital axis is pinned to a fixed 0..1 range with padding.");
        Assert.AreEqual(1.1, digital.Maximum);

        var time = GetAxis(model, PlotModelFactory.TIME_AXIS_KEY);
        Assert.IsInstanceOfType<LinearAxis>(time);
        Assert.AreEqual(AxisPosition.Bottom, time.Position);
        Assert.AreEqual("Time (ms)", time.Title);
    }

    [TestMethod]
    public void CreateMainPlotModel_DisablesBuiltInLegend()
    {
        // The legend is rendered by the WPF panel, not OxyPlot's built-in legend.
        var model = PlotModelFactory.CreateMainPlotModel();

        Assert.IsFalse(model.IsLegendVisible);
    }

    [TestMethod]
    public void CreateMainPlotModel_AppliesDarkTheme()
    {
        var model = PlotModelFactory.CreateMainPlotModel();

        Assert.AreEqual(OxyPlotDarkTheme.Surface, model.Background, "Dark theme is applied to the model.");
        Assert.AreEqual(OxyPlotDarkTheme.TextSecondary, model.TextColor);

        // The theme is also applied to each axis (gridline color is a theme value, not the OxyPlot default).
        var time = GetAxis(model, PlotModelFactory.TIME_AXIS_KEY);
        Assert.AreEqual(OxyPlotDarkTheme.Gridline, time.MajorGridlineColor);
    }

    #endregion

    #region CreateChannelSeries

    [TestMethod]
    public void CreateChannelSeries_AnalogChannel_UsesAnalogYAxis()
    {
        var (series, _) = PlotModelFactory.CreateChannelSeries(
            "AI0", Serial, ChannelType.Analog, Color, PlotModelFactory.CreateMainPlotModel(), null);

        Assert.AreEqual(PlotModelFactory.ANALOG_AXIS_KEY, series.YAxisKey);
    }

    [TestMethod]
    public void CreateChannelSeries_DigitalChannel_UsesDigitalYAxis()
    {
        var (series, _) = PlotModelFactory.CreateChannelSeries(
            "DIO0", Serial, ChannelType.Digital, Color, PlotModelFactory.CreateMainPlotModel(), null);

        Assert.AreEqual(PlotModelFactory.DIGITAL_AXIS_KEY, series.YAxisKey);
    }

    [TestMethod]
    public void CreateChannelSeries_ParsesColor_AndSetsTitleTagAndVisibility()
    {
        var (series, _) = PlotModelFactory.CreateChannelSeries(
            "AI0", Serial, ChannelType.Analog, Color, PlotModelFactory.CreateMainPlotModel(), null);

        Assert.AreEqual("AI0", series.Title);
        Assert.AreEqual(OxyColor.Parse(Color), series.Color, "Series color is parsed from the stored hex string.");
        Assert.IsTrue(series.IsVisible, "Newly built channel series start visible.");
        Assert.AreEqual((Serial, "AI0"), series.Tag, "The (serial, channel) tag is what the viewport code keys on.");
        StringAssert.Contains(series.TrackerFormatString, "AI0");
        StringAssert.Contains(series.TrackerFormatString, "...5079", "Tracker shows the truncated serial suffix.");
    }

    [TestMethod]
    public void CreateChannelSeries_BuildsLegendItem_MirroringTheSeries()
    {
        var plotModel = PlotModelFactory.CreateMainPlotModel();

        var (series, legendItem) = PlotModelFactory.CreateChannelSeries(
            "AI0", Serial, ChannelType.Analog, Color, plotModel, null);

        Assert.AreSame(series, legendItem.ActualSeries, "Legend item controls the series it was built with.");
        Assert.AreEqual("AI0", legendItem.DisplayName);
        Assert.AreEqual("AI0", legendItem.ChannelName);
        Assert.AreEqual(Serial, legendItem.DeviceSerialNo);
        Assert.AreEqual(OxyColor.Parse(Color), legendItem.SeriesColor);
        Assert.IsTrue(legendItem.IsVisible);
        Assert.AreEqual("...5079", legendItem.TruncatedSerialNo);
    }

    [TestMethod]
    public void CreateChannelSeries_TogglingLegendVisibility_WithoutWpfRuntime_TogglesSeriesAndDoesNotThrow()
    {
        // The legend item's visibility setter normally hops to Application.Current.Dispatcher; in this
        // WPF-runtime-free test host Application.Current is null, so the setter must run its work inline
        // rather than dereferencing a null dispatcher. Proves the construction seam stays exercisable
        // headless (a null databaseLogger means no minimap sync is attempted).
        var (series, legendItem) = PlotModelFactory.CreateChannelSeries(
            "AI0", Serial, ChannelType.Analog, Color, PlotModelFactory.CreateMainPlotModel(), null);

        legendItem.IsVisible = false;
        Assert.IsFalse(series.IsVisible, "Toggling the legend item flips the underlying series visibility.");

        legendItem.IsVisible = true;
        Assert.IsTrue(series.IsVisible);
    }

    #endregion

    #region CreateMinimapPlotModel

    [TestMethod]
    public void CreateMinimapPlotModel_AddsTwoNonInteractiveAxes()
    {
        var minimap = PlotModelFactory.CreateMinimapPlotModel();

        Assert.AreEqual(2, minimap.Model.Axes.Count);

        var timeAxis = GetAxis(minimap.Model, PlotModelFactory.MINIMAP_TIME_AXIS_KEY);
        Assert.AreEqual(AxisPosition.Bottom, timeAxis.Position);
        Assert.IsFalse(timeAxis.IsZoomEnabled, "The minimap axis is driven programmatically, not by user zoom.");
        Assert.IsFalse(timeAxis.IsPanEnabled);

        var yAxis = GetAxis(minimap.Model, PlotModelFactory.MINIMAP_Y_AXIS_KEY);
        Assert.AreEqual(AxisPosition.Left, yAxis.Position);
        Assert.IsFalse(yAxis.IsZoomEnabled);
        Assert.IsFalse(yAxis.IsPanEnabled);
    }

    [TestMethod]
    public void CreateMinimapPlotModel_AddsTheThreeAnnotations_AndReturnsTheSameHandles()
    {
        var minimap = PlotModelFactory.CreateMinimapPlotModel();

        // The logger keeps the returned handles to mutate selection/dim bounds as the viewport moves,
        // so the returned objects must be exactly the ones added to the model.
        CollectionAssert.AreEqual(
            new Annotation[] { minimap.DimLeft, minimap.DimRight, minimap.SelectionRect },
            minimap.Model.Annotations.ToArray(),
            "Dim overlays render beneath the selection rectangle, so z-order (add order) must be preserved.");
    }

    [TestMethod]
    public void CreateMinimapPlotModel_ConfiguresSelectionAndDimAnnotations()
    {
        var minimap = PlotModelFactory.CreateMinimapPlotModel();

        Assert.AreEqual(OxyPlotDarkTheme.Accent, minimap.SelectionRect.Stroke, "Selection rectangle uses the accent border.");
        Assert.AreEqual(2d, minimap.SelectionRect.StrokeThickness);
        Assert.AreEqual(OxyColors.Transparent, minimap.SelectionRect.Fill);

        Assert.AreEqual(OxyPlotDarkTheme.MinimapDim, minimap.DimLeft.Fill, "Dim overlays shade the unselected region.");
        Assert.AreEqual(OxyPlotDarkTheme.MinimapDim, minimap.DimRight.Fill);

        // Every annotation is bound to the minimap axes and drawn above the series.
        foreach (var annotation in new[] { minimap.SelectionRect, minimap.DimLeft, minimap.DimRight })
        {
            Assert.AreEqual(PlotModelFactory.MINIMAP_TIME_AXIS_KEY, annotation.XAxisKey);
            Assert.AreEqual(PlotModelFactory.MINIMAP_Y_AXIS_KEY, annotation.YAxisKey);
            Assert.AreEqual(AnnotationLayer.AboveSeries, annotation.Layer);
        }
    }

    [TestMethod]
    public void CreateMinimapPlotModel_DisablesLegend_AndAppliesTheme()
    {
        var minimap = PlotModelFactory.CreateMinimapPlotModel();

        Assert.IsFalse(minimap.Model.IsLegendVisible);
        Assert.AreEqual(OxyPlotDarkTheme.Surface, minimap.Model.Background);
    }

    #endregion

    #region CreateMinimapSeries

    [TestMethod]
    public void CreateMinimapSeries_BindsToMinimapAxes_AndUsesTheGivenPointsAndColor()
    {
        var color = OxyColor.Parse(Color);
        var points = new List<DataPoint> { new(0, 1), new(1, 2) };

        var series = PlotModelFactory.CreateMinimapSeries(color, points);

        Assert.AreEqual(color, series.Color);
        Assert.AreEqual(1d, series.StrokeThickness);
        Assert.AreEqual(PlotModelFactory.MINIMAP_TIME_AXIS_KEY, series.XAxisKey);
        Assert.AreEqual(PlotModelFactory.MINIMAP_Y_AXIS_KEY, series.YAxisKey);
        Assert.AreSame(points, series.ItemsSource, "The downsampled list is used directly as the items source.");
    }

    #endregion

    #region Helpers

    private static Axis GetAxis(PlotModel model, string key) =>
        model.Axes.First(a => a.Key == key);

    #endregion
}
