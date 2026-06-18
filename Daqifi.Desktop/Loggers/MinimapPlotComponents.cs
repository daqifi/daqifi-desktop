using OxyPlot;
using OxyPlot.Annotations;

namespace Daqifi.Desktop.Logger;

/// <summary>
/// The minimap <see cref="PlotModel"/> together with the three annotation handles
/// <see cref="DatabaseLogger"/> keeps mutating as the viewport changes: the selection rectangle and
/// the two dim overlays that shade the unselected regions. Returned by
/// <see cref="PlotModelFactory.CreateMinimapPlotModel"/> so the logger can hold the field references
/// while the construction lives in the factory.
/// </summary>
/// <param name="Model">The fully configured minimap plot model (axes + annotations added).</param>
/// <param name="SelectionRect">The accent-bordered rectangle marking the visible time range.</param>
/// <param name="DimLeft">The dim overlay shading the range to the left of the selection.</param>
/// <param name="DimRight">The dim overlay shading the range to the right of the selection.</param>
public sealed record MinimapPlotComponents(
    PlotModel Model,
    RectangleAnnotation SelectionRect,
    RectangleAnnotation DimLeft,
    RectangleAnnotation DimRight);
