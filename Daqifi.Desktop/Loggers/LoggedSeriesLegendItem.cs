using CommunityToolkit.Mvvm.ComponentModel;
using OxyPlot;
using OxyPlot.Series;
using Application = System.Windows.Application;

namespace Daqifi.Desktop.Logger;

/// <summary>
/// Legend item linked to a single plot <see cref="LineSeries"/>. Toggling <see cref="IsVisible"/>
/// flips the series visibility, invalidates the main plot, and (when a <see cref="DatabaseLogger"/>
/// is supplied) mirrors the change onto the matching minimap series. Built by
/// <see cref="PlotModelFactory.CreateChannelSeries"/>; the typed logger reference is retained so the
/// minimap-sync callback keeps working exactly as before this type moved out of
/// <see cref="DatabaseLogger"/> (issue #592).
/// </summary>
public partial class LoggedSeriesLegendItem : ObservableObject
{
    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private string _channelName;

    [ObservableProperty]
    private string _deviceSerialNo;

    /// <summary>
    /// Truncated serial number for compact legend display (e.g., "...4104").
    /// </summary>
    public string TruncatedSerialNo => _deviceSerialNo?.Length > 4
        ? $"...{_deviceSerialNo[^4..]}"
        : _deviceSerialNo ?? string.Empty;

    [ObservableProperty]
    private OxyColor _seriesColor;

    private bool _isVisible;
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (SetProperty(ref _isVisible, value) && ActualSeries != null)
            {
                ActualSeries.IsVisible = _isVisible;

                void ApplyVisibility()
                {
                    _plotModel?.InvalidatePlot(true);
                    _databaseLogger?.SetMinimapSeriesVisibility(_deviceSerialNo, _channelName, _isVisible);
                }

                // In the live app Application.Current is always set, so this is a UI-thread dispatch as
                // before. In a headless/unit-test host it is null; run the work inline instead of
                // dereferencing a null dispatcher, so the extracted construction stays exercisable
                // without a WPF runtime.
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    dispatcher.Invoke(ApplyVisibility);
                }
                else
                {
                    ApplyVisibility();
                }
            }
        }
    }

    public LineSeries ActualSeries { get; }
    private readonly PlotModel _plotModel;
    private readonly DatabaseLogger? _databaseLogger;

    /// <summary>
    /// Initializes a new legend item linked to a plot series and optional minimap sync.
    /// </summary>
    /// <param name="displayName">Full display name including channel and device info.</param>
    /// <param name="channelName">Channel identifier (e.g., "AI0").</param>
    /// <param name="deviceSerialNo">Device serial number for grouping.</param>
    /// <param name="seriesColor">Color of the associated plot series.</param>
    /// <param name="isVisible">Initial visibility state of the series.</param>
    /// <param name="actualSeries">The OxyPlot LineSeries this legend item controls.</param>
    /// <param name="plotModel">The main PlotModel to invalidate on visibility changes.</param>
    /// <param name="databaseLogger">Optional logger for syncing minimap series visibility.</param>
    public LoggedSeriesLegendItem(
        string displayName,
        string channelName,
        string deviceSerialNo,
        OxyColor seriesColor,
        bool isVisible,
        LineSeries actualSeries,
        PlotModel plotModel,
        DatabaseLogger? databaseLogger = null)
    {
        _displayName = displayName;
        _channelName = channelName;
        _deviceSerialNo = deviceSerialNo;
        _seriesColor = seriesColor;
        _isVisible = isVisible; // Initialize the backing field
        ActualSeries = actualSeries;
        ActualSeries.IsVisible = isVisible; // Ensure series visibility matches
        _plotModel = plotModel;
        _databaseLogger = databaseLogger;
    }
}
