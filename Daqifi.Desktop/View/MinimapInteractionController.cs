using Daqifi.Desktop.Logger;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using System.Windows.Threading;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;
using Application = System.Windows.Application;
using FrameworkElement = System.Windows.FrameworkElement;

namespace Daqifi.Desktop.View;

/// <summary>
/// Handles mouse interactions on the minimap PlotModel to enable drag/resize
/// of the selection rectangle, synchronized with the main plot's time axis.
/// Provides cursor feedback: resize arrows on edges, grab hand inside selection,
/// and pointer outside. Renders are throttled to 60fps via a DispatcherTimer.
/// </summary>
public class MinimapInteractionController : IDisposable
{
    #region Private Fields
    private readonly PlotModel _mainPlotModel;
    private readonly PlotModel _minimapPlotModel;
    private readonly RectangleAnnotation _selectionRect;
    private readonly RectangleAnnotation _dimLeft;
    private readonly RectangleAnnotation _dimRight;
    private readonly string _mainTimeAxisKey;
    private readonly string _minimapTimeAxisKey;
    private readonly DatabaseLogger _databaseLogger;

    private enum DragMode { None, Pan, ResizeLeft, ResizeRight }
    private DragMode _dragMode = DragMode.None;
    private double _dragStartDataX;
    private double _dragStartRectMin;
    private double _dragStartRectMax;
    private Cursor _lastCursor;

    private const double EDGE_TOLERANCE_FRACTION = 0.02;

    private bool _isDirty;
    private readonly DispatcherTimer _renderTimer;
    #endregion

    #region Constructor
    public MinimapInteractionController(
        PlotModel mainPlotModel,
        PlotModel minimapPlotModel,
        RectangleAnnotation selectionRect,
        RectangleAnnotation dimLeft,
        RectangleAnnotation dimRight,
        DatabaseLogger databaseLogger,
        string mainTimeAxisKey = "Time",
        string minimapTimeAxisKey = "MinimapTime")
    {
        _mainPlotModel = mainPlotModel;
        _minimapPlotModel = minimapPlotModel;
        _selectionRect = selectionRect;
        _dimLeft = dimLeft;
        _dimRight = dimRight;
        _databaseLogger = databaseLogger;
        _mainTimeAxisKey = mainTimeAxisKey;
        _minimapTimeAxisKey = minimapTimeAxisKey;

        _minimapPlotModel.MouseDown += OnMouseDown;
        _minimapPlotModel.MouseMove += OnMouseMove;
        _minimapPlotModel.MouseUp += OnMouseUp;

        _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _renderTimer.Tick += OnRenderTick;
        _renderTimer.Start();
    }
    #endregion

    #region Cursor Management
    /// <summary>
    /// Determines the appropriate cursor based on the mouse position relative
    /// to the selection rectangle edges and interior.
    /// </summary>
    private Cursor GetCursorForPosition(double dataX)
    {
        var rectMin = _selectionRect.MinimumX;
        var rectMax = _selectionRect.MaximumX;
        var rectRange = rectMax - rectMin;
        var edgeTolerance = Math.Max(rectRange * EDGE_TOLERANCE_FRACTION, GetMinimapDataRange() * 0.005);

        if (Math.Abs(dataX - rectMin) < edgeTolerance || Math.Abs(dataX - rectMax) < edgeTolerance)
        {
            return Cursors.SizeWE;
        }

        if (dataX >= rectMin && dataX <= rectMax)
        {
            return Cursors.Hand;
        }

        return Cursors.Arrow;
    }

    /// <summary>
    /// Sets the cursor on the WPF PlotView element. Skips if unchanged from last value.
    /// </summary>
    private void SetCursor(Cursor cursor)
    {
        if (cursor == _lastCursor)
        {
            return;
        }

        _lastCursor = cursor;

        if (_minimapPlotModel.PlotView is FrameworkElement element)
        {
            Application.Current?.Dispatcher.BeginInvoke(() => element.Cursor = cursor);
        }
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
            SetCursor(Cursors.SizeWE);
        }
        else if (Math.Abs(dataX - rectMax) < edgeTolerance)
        {
            _dragMode = DragMode.ResizeRight;
            SetCursor(Cursors.SizeWE);
        }
        else if (dataX >= rectMin && dataX <= rectMax)
        {
            _dragMode = DragMode.Pan;
            SetCursor(Cursors.ScrollAll);
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
            _dimLeft.MaximumX = newMin;
            _dimRight.MinimumX = newMax;
            ApplyToMainPlot(newMin, newMax);
            _dragMode = DragMode.Pan;
            SetCursor(Cursors.ScrollAll);
        }

        _dragStartDataX = dataX;
        _dragStartRectMin = _selectionRect.MinimumX;
        _dragStartRectMax = _selectionRect.MaximumX;

        e.Handled = true;
    }

    private void OnMouseMove(object? sender, OxyMouseEventArgs e)
    {
        var minimapTimeAxis = GetMinimapTimeAxis();
        if (minimapTimeAxis == null)
        {
            return;
        }

        var dataX = minimapTimeAxis.InverseTransform(e.Position.X);

        // Update cursor on hover when not dragging
        if (_dragMode == DragMode.None)
        {
            SetCursor(GetCursorForPosition(dataX));
            return;
        }

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

            // Flush final render for accuracy
            if (_isDirty)
            {
                _isDirty = false;
                _databaseLogger.OnMinimapViewportChanged();
                _mainPlotModel.InvalidatePlot(false);
                _minimapPlotModel.InvalidatePlot(false);
            }

            // Update cursor based on final position
            var minimapTimeAxis = GetMinimapTimeAxis();
            if (minimapTimeAxis != null)
            {
                var dataX = minimapTimeAxis.InverseTransform(e.Position.X);
                SetCursor(GetCursorForPosition(dataX));
            }

            e.Handled = true;
        }
    }
    #endregion

    #region Render Throttling
    private void OnRenderTick(object? sender, EventArgs e)
    {
        if (!_isDirty)
        {
            return;
        }

        _isDirty = false;
        _databaseLogger.OnMinimapViewportChanged();
        _mainPlotModel.InvalidatePlot(false);
        _minimapPlotModel.InvalidatePlot(false);
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

        _databaseLogger.IsSyncingFromMinimap = true;
        mainTimeAxis.Zoom(min, max);
        _databaseLogger.IsSyncingFromMinimap = false;

        _dimLeft.MaximumX = min;
        _dimRight.MinimumX = max;
        _isDirty = true;
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

        var range = axis.DataMaximum - axis.DataMinimum;
        return double.IsNaN(range) || double.IsInfinity(range) || range <= 0 ? 1 : range;
    }

    private double GetMinimapDataMin()
    {
        var axis = GetMinimapTimeAxis();
        return axis?.DataMinimum ?? 0;
    }
    #endregion

    #region IDisposable
    /// <summary>
    /// Unsubscribes all event handlers from the minimap PlotModel and stops the render timer.
    /// </summary>
    public void Dispose()
    {
        _renderTimer.Stop();
        _renderTimer.Tick -= OnRenderTick;
        _minimapPlotModel.MouseDown -= OnMouseDown;
        _minimapPlotModel.MouseMove -= OnMouseMove;
        _minimapPlotModel.MouseUp -= OnMouseUp;
    }
    #endregion
}
