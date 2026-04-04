using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;

namespace Daqifi.Desktop.View;

/// <summary>
/// Handles mouse interactions on the minimap PlotModel to enable drag/resize
/// of the selection rectangle, synchronized with the main plot's time axis.
/// </summary>
public class MinimapInteractionController
{
    #region Private Fields
    private readonly PlotModel _mainPlotModel;
    private readonly PlotModel _minimapPlotModel;
    private readonly RectangleAnnotation _selectionRect;
    private readonly string _mainTimeAxisKey;
    private readonly string _minimapTimeAxisKey;

    private enum DragMode { None, Pan, ResizeLeft, ResizeRight }
    private DragMode _dragMode = DragMode.None;
    private double _dragStartDataX;
    private double _dragStartRectMin;
    private double _dragStartRectMax;

    private const double EDGE_TOLERANCE_FRACTION = 0.02;
    #endregion

    #region Constructor
    public MinimapInteractionController(
        PlotModel mainPlotModel,
        PlotModel minimapPlotModel,
        RectangleAnnotation selectionRect,
        string mainTimeAxisKey = "Time",
        string minimapTimeAxisKey = "MinimapTime")
    {
        _mainPlotModel = mainPlotModel;
        _minimapPlotModel = minimapPlotModel;
        _selectionRect = selectionRect;
        _mainTimeAxisKey = mainTimeAxisKey;
        _minimapTimeAxisKey = minimapTimeAxisKey;

        _minimapPlotModel.MouseDown += OnMouseDown;
        _minimapPlotModel.MouseMove += OnMouseMove;
        _minimapPlotModel.MouseUp += OnMouseUp;
    }
    #endregion

    #region Mouse Handlers
    private void OnMouseDown(object? sender, OxyMouseDownEventArgs e)
    {
        if (e.ChangedButton != OxyMouseButton.Left)
        {
            return;
        }

        var minimapTimeAxis = GetMinimapTimeAxis();
        if (minimapTimeAxis == null)
        {
            return;
        }

        var dataX = minimapTimeAxis.InverseTransform(e.Position.X);
        var rectMin = _selectionRect.MinimumX;
        var rectMax = _selectionRect.MaximumX;
        var rectRange = rectMax - rectMin;
        var edgeTolerance = Math.Max(rectRange * EDGE_TOLERANCE_FRACTION, GetMinimapDataRange() * 0.005);

        if (Math.Abs(dataX - rectMin) < edgeTolerance)
        {
            _dragMode = DragMode.ResizeLeft;
        }
        else if (Math.Abs(dataX - rectMax) < edgeTolerance)
        {
            _dragMode = DragMode.ResizeRight;
        }
        else if (dataX >= rectMin && dataX <= rectMax)
        {
            _dragMode = DragMode.Pan;
        }
        else
        {
            // Click outside: jump rectangle center to click position, then start panning
            var halfRange = rectRange / 2;
            var fullRange = GetMinimapDataRange();
            var fullMin = GetMinimapDataMin();
            var fullMax = fullMin + fullRange;

            var newMin = Math.Max(fullMin, dataX - halfRange);
            var newMax = Math.Min(fullMax, newMin + rectRange);
            newMin = newMax - rectRange;

            _selectionRect.MinimumX = newMin;
            _selectionRect.MaximumX = newMax;
            ApplyToMainPlot(newMin, newMax);
            _dragMode = DragMode.Pan;
        }

        _dragStartDataX = dataX;
        _dragStartRectMin = _selectionRect.MinimumX;
        _dragStartRectMax = _selectionRect.MaximumX;

        e.Handled = true;
    }

    private void OnMouseMove(object? sender, OxyMouseEventArgs e)
    {
        if (_dragMode == DragMode.None)
        {
            return;
        }

        var minimapTimeAxis = GetMinimapTimeAxis();
        if (minimapTimeAxis == null)
        {
            return;
        }

        var dataX = minimapTimeAxis.InverseTransform(e.Position.X);
        var delta = dataX - _dragStartDataX;
        var fullRange = GetMinimapDataRange();
        var fullMin = GetMinimapDataMin();
        var fullMax = fullMin + fullRange;

        double newMin, newMax;

        switch (_dragMode)
        {
            case DragMode.Pan:
                newMin = _dragStartRectMin + delta;
                newMax = _dragStartRectMax + delta;

                // Clamp to minimap bounds
                if (newMin < fullMin)
                {
                    newMax += fullMin - newMin;
                    newMin = fullMin;
                }
                if (newMax > fullMax)
                {
                    newMin -= newMax - fullMax;
                    newMax = fullMax;
                }

                _selectionRect.MinimumX = newMin;
                _selectionRect.MaximumX = newMax;
                ApplyToMainPlot(newMin, newMax);
                break;

            case DragMode.ResizeLeft:
                newMin = _dragStartRectMin + delta;
                newMax = _dragStartRectMax;
                var minWidth = fullRange * 0.005;

                if (newMin > newMax - minWidth)
                {
                    newMin = newMax - minWidth;
                }
                if (newMin < fullMin)
                {
                    newMin = fullMin;
                }

                _selectionRect.MinimumX = newMin;
                _selectionRect.MaximumX = newMax;
                ApplyToMainPlot(newMin, newMax);
                break;

            case DragMode.ResizeRight:
                newMin = _dragStartRectMin;
                newMax = _dragStartRectMax + delta;
                minWidth = fullRange * 0.005;

                if (newMax < newMin + minWidth)
                {
                    newMax = newMin + minWidth;
                }
                if (newMax > fullMax)
                {
                    newMax = fullMax;
                }

                _selectionRect.MinimumX = newMin;
                _selectionRect.MaximumX = newMax;
                ApplyToMainPlot(newMin, newMax);
                break;
        }

        e.Handled = true;
    }

    private void OnMouseUp(object? sender, OxyMouseEventArgs e)
    {
        if (_dragMode != DragMode.None)
        {
            _dragMode = DragMode.None;
            e.Handled = true;
        }
    }
    #endregion

    #region Private Methods
    private void ApplyToMainPlot(double min, double max)
    {
        var mainTimeAxis = _mainPlotModel.Axes.FirstOrDefault(a => a.Key == _mainTimeAxisKey);
        if (mainTimeAxis == null)
        {
            return;
        }

        mainTimeAxis.Zoom(min, max);
        _mainPlotModel.InvalidatePlot(false);
        _minimapPlotModel.InvalidatePlot(false);
    }

    private LinearAxis? GetMinimapTimeAxis()
    {
        return _minimapPlotModel.Axes.FirstOrDefault(a => a.Key == _minimapTimeAxisKey) as LinearAxis;
    }

    private double GetMinimapDataRange()
    {
        var axis = GetMinimapTimeAxis();
        if (axis == null)
        {
            return 1;
        }

        return axis.DataMaximum - axis.DataMinimum;
    }

    private double GetMinimapDataMin()
    {
        var axis = GetMinimapTimeAxis();
        return axis?.DataMinimum ?? 0;
    }
    #endregion
}
