using Daqifi.Desktop.Helpers;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using ChannelType = Daqifi.Core.Channel.ChannelType;
using FontWeights = OxyPlot.FontWeights;
using TickStyle = OxyPlot.Axes.TickStyle;

namespace Daqifi.Desktop.Logger;

/// <summary>
/// Owns the pure OxyPlot <em>construction</em> extracted from <see cref="DatabaseLogger"/> (issue #592):
/// building the main logged-data <see cref="PlotModel"/> and its Analog/Digital/Time axes, the overview
/// minimap model with its axes and selection/dim annotations, a channel's <see cref="LineSeries"/> plus
/// its <see cref="LoggedSeriesLegendItem"/>, and the minimap's per-channel line series.
/// <para>
/// Every method is a side-effect-free constructor of OxyPlot objects — no EF, no threading, no
/// Dispatcher, no <c>InvalidatePlot</c>, no live-axis mutation — so the factory is unit-testable without
/// a WPF runtime, a database, or background threads, and has no dependency on desktop singletons
/// (<c>App.ServiceProvider</c>, <c>AppLogger.Instance</c>). <see cref="DatabaseLogger"/> remains the
/// composition root and the live-model owner: it calls the factory to build the models, then keeps every
/// viewport/minimap-sync mutation (axis subscription, <c>Zoom</c>, annotation bounds, <c>InvalidatePlot</c>)
/// to itself.
/// </para>
/// </summary>
public sealed class PlotModelFactory
{
    #region Axis Keys
    /// <summary>Key of the left-hand analog (volts) Y axis on the main plot.</summary>
    public const string ANALOG_AXIS_KEY = "Analog";

    /// <summary>Key of the right-hand digital Y axis on the main plot.</summary>
    public const string DIGITAL_AXIS_KEY = "Digital";

    /// <summary>Key of the bottom time (ms) X axis on the main plot.</summary>
    public const string TIME_AXIS_KEY = "Time";

    /// <summary>Key of the minimap's time X axis.</summary>
    public const string MINIMAP_TIME_AXIS_KEY = "MinimapTime";

    /// <summary>Key of the minimap's value Y axis.</summary>
    public const string MINIMAP_Y_AXIS_KEY = "MinimapY";
    #endregion

    #region Main Plot
    /// <summary>
    /// Builds the main logged-data plot model: a left analog (V) axis, a right digital axis, a bottom
    /// time (ms) axis, the shared dark theme applied to the model and each axis, and the built-in legend
    /// disabled (the legend is rendered by the WPF panel instead). The caller subscribes to the time
    /// axis' <c>AxisChanged</c> event for minimap sync — that wiring is viewport machinery and stays in
    /// <see cref="DatabaseLogger"/>.
    /// </summary>
    /// <returns>A configured main <see cref="PlotModel"/> with its three axes added.</returns>
    public PlotModel CreateMainPlotModel()
    {
        var plotModel = new PlotModel();

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
            Key = ANALOG_AXIS_KEY,
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
            Key = DIGITAL_AXIS_KEY,
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
            Key = TIME_AXIS_KEY,
            Title = "Time (ms)"
        };

        OxyPlotDarkTheme.ApplyTo(plotModel);
        OxyPlotDarkTheme.ApplyTo(analogAxis);
        OxyPlotDarkTheme.ApplyTo(digitalAxis);
        OxyPlotDarkTheme.ApplyTo(timeAxis);

        plotModel.Axes.Add(analogAxis);
        plotModel.Axes.Add(digitalAxis);
        plotModel.Axes.Add(timeAxis);
        plotModel.IsLegendVisible = false; // Disable the built-in legend

        return plotModel;
    }

    /// <summary>
    /// Builds a channel's main-plot <see cref="LineSeries"/> and its paired
    /// <see cref="LoggedSeriesLegendItem"/>, selecting the Analog or Digital Y axis from the channel
    /// type. The series carries a <c>(deviceSerialNo, channelName)</c> tag the viewport code keys on,
    /// and the legend item is given the live <paramref name="plotModel"/> and
    /// <paramref name="databaseLogger"/> so toggling its visibility still invalidates the plot and mirrors
    /// the change onto the matching minimap series.
    /// </summary>
    /// <param name="channelName">Channel identifier (e.g., "AI0"); also the series title.</param>
    /// <param name="deviceSerialNo">Serial number of the owning device, used for the tag and legend grouping.</param>
    /// <param name="type">Channel type; selects the <see cref="ANALOG_AXIS_KEY"/> or <see cref="DIGITAL_AXIS_KEY"/> Y axis.</param>
    /// <param name="color">Series color in a format <see cref="OxyColor.Parse"/> understands (e.g., "#FFD32F2F").</param>
    /// <param name="plotModel">The live main plot model the legend item invalidates on visibility changes.</param>
    /// <param name="databaseLogger">Logger the legend item uses to sync minimap series visibility, or null to skip that sync.</param>
    /// <returns>The configured series and its legend item.</returns>
    public (LineSeries series, LoggedSeriesLegendItem legendItem) CreateChannelSeries(
        string channelName,
        string? deviceSerialNo,
        ChannelType type,
        string color,
        PlotModel plotModel,
        DatabaseLogger? databaseLogger)
    {
        var serialSuffix = deviceSerialNo?.Length > 4
            ? $"...{deviceSerialNo[^4..]}"
            : deviceSerialNo;

        var newLineSeries = new LineSeries
        {
            Title = channelName,
            Tag = (deviceSerialNo, channelName),
            Color = OxyColor.Parse(color),
            IsVisible = true,
            TrackerFormatString = $"{channelName} ({serialSuffix})\n{{1}}: {{2:0.###}}\n{{3}}: {{4:0.######}}"
        };

        var legendItem = new LoggedSeriesLegendItem(
            newLineSeries.Title,
            channelName,
            deviceSerialNo,
            newLineSeries.Color,
            newLineSeries.IsVisible,
            newLineSeries,
            plotModel,
            databaseLogger);

        newLineSeries.YAxisKey = type switch
        {
            ChannelType.Analog => ANALOG_AXIS_KEY,
            ChannelType.Digital => DIGITAL_AXIS_KEY,
            _ => newLineSeries.YAxisKey
        };

        return (newLineSeries, legendItem);
    }
    #endregion

    #region Minimap
    /// <summary>
    /// Builds the overview minimap plot model: a non-interactive time and value axis pair, plus the
    /// three <see cref="RectangleAnnotation"/>s the logger drives as the viewport moves — two dim
    /// overlays shading the unselected regions and the accent-bordered selection rectangle. The
    /// annotations are returned alongside the model so the logger keeps its field references; setting
    /// their bounds and invalidating the model are live mutations that stay in
    /// <see cref="DatabaseLogger"/>.
    /// </summary>
    /// <returns>The minimap model with the selection and dim annotation handles.</returns>
    public MinimapPlotComponents CreateMinimapPlotModel()
    {
        var minimapPlotModel = new PlotModel
        {
            IsLegendVisible = false,
            PlotMargins = new OxyThickness(4, 2, 4, 2),
            Padding = new OxyThickness(0)
        };
        OxyPlotDarkTheme.ApplyTo(minimapPlotModel);

        var minimapTimeAxis = new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Key = MINIMAP_TIME_AXIS_KEY,
            TickStyle = TickStyle.None,
            MajorGridlineStyle = LineStyle.None,
            MinorGridlineStyle = LineStyle.None,
            TitleFontSize = 0,
            FontSize = 0,
            IsZoomEnabled = false,
            IsPanEnabled = false
        };

        var minimapYAxis = new LinearAxis
        {
            Position = AxisPosition.Left,
            Key = MINIMAP_Y_AXIS_KEY,
            TickStyle = TickStyle.None,
            MajorGridlineStyle = LineStyle.None,
            MinorGridlineStyle = LineStyle.None,
            TitleFontSize = 0,
            FontSize = 0,
            IsZoomEnabled = false,
            IsPanEnabled = false,
            MinimumPadding = 0.1,
            MaximumPadding = 0.1
        };

        minimapPlotModel.Axes.Add(minimapTimeAxis);
        minimapPlotModel.Axes.Add(minimapYAxis);

        // Dim overlays for areas outside the selected range
        var dimLeft = new RectangleAnnotation
        {
            Fill = OxyPlotDarkTheme.MinimapDim,
            Stroke = OxyColors.Transparent,
            StrokeThickness = 0,
            MinimumX = -1e18,
            MaximumX = 0,
            MinimumY = -1e18,
            MaximumY = 1e18,
            Layer = AnnotationLayer.AboveSeries,
            XAxisKey = MINIMAP_TIME_AXIS_KEY,
            YAxisKey = MINIMAP_Y_AXIS_KEY
        };

        var dimRight = new RectangleAnnotation
        {
            Fill = OxyPlotDarkTheme.MinimapDim,
            Stroke = OxyColors.Transparent,
            StrokeThickness = 0,
            MinimumX = 0,
            MaximumX = 1e18,
            MinimumY = -1e18,
            MaximumY = 1e18,
            Layer = AnnotationLayer.AboveSeries,
            XAxisKey = MINIMAP_TIME_AXIS_KEY,
            YAxisKey = MINIMAP_Y_AXIS_KEY
        };

        // Selection rectangle border
        var selectionRect = new RectangleAnnotation
        {
            Fill = OxyColors.Transparent,
            Stroke = OxyPlotDarkTheme.Accent,
            StrokeThickness = 2,
            MinimumY = -1e18,
            MaximumY = 1e18,
            Layer = AnnotationLayer.AboveSeries,
            XAxisKey = MINIMAP_TIME_AXIS_KEY,
            YAxisKey = MINIMAP_Y_AXIS_KEY
        };

        minimapPlotModel.Annotations.Add(dimLeft);
        minimapPlotModel.Annotations.Add(dimRight);
        minimapPlotModel.Annotations.Add(selectionRect);

        return new MinimapPlotComponents(minimapPlotModel, selectionRect, dimLeft, dimRight);
    }

    /// <summary>
    /// Builds a single minimap <see cref="LineSeries"/> bound to the minimap axes for a channel's
    /// downsampled overview points. The caller adds it to the live model, tracks it for visibility
    /// sync, and invalidates the plot — all live mutations that stay in <see cref="DatabaseLogger"/>.
    /// </summary>
    /// <param name="color">The series color, matched to its main-plot counterpart.</param>
    /// <param name="downsampled">The downsampled overview points to render.</param>
    /// <returns>A minimap line series bound to the <see cref="MINIMAP_TIME_AXIS_KEY"/>/<see cref="MINIMAP_Y_AXIS_KEY"/> axes.</returns>
    public LineSeries CreateMinimapSeries(OxyColor color, List<DataPoint> downsampled)
    {
        return new LineSeries
        {
            Color = color,
            StrokeThickness = 1,
            ItemsSource = downsampled,
            XAxisKey = MINIMAP_TIME_AXIS_KEY,
            YAxisKey = MINIMAP_Y_AXIS_KEY
        };
    }
    #endregion
}
