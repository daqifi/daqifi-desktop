using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Daqifi.Desktop.ViewModels
{
    public partial class MinimapViewModel : ObservableObject
    {
        /// <summary>
        /// Gets the plot model for the minimap.
        /// </summary>
        [ObservableProperty]
        private PlotModel _plotModel;

        private RectangleAnnotation _selectionRectangle;
        private LineSeries _downsampledSeries;

        private List<DataPoint> _fullResolutionData;
        private PlotModel _mainPlotModel;

        private enum InteractionType { None, Drag, ResizeLeft, ResizeRight }
        private InteractionType _currentInteraction = InteractionType.None;
        private ScreenPoint _initialMousePosition;
        private double _originalSelectionMinX;
        private double _originalSelectionMaxX;
        private const double EdgeResizeHotspot = 8.0; // Screen pixels for resize sensitivity

        /// <summary>
        /// Initializes a new instance of the <see cref="MinimapViewModel"/> class.
        /// </summary>
        public MinimapViewModel()
        {
            InitializePlotModel();
        }

        private void InitializePlotModel()
        {
            PlotModel = new PlotModel { IsLegendVisible = false, Title = "Overview" };

            PlotModel.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                IsAxisVisible = false
            });

            PlotModel.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                IsAxisVisible = false
            });

            _downsampledSeries = new LineSeries
            {
                Color = OxyColors.SlateGray,
                StrokeThickness = 1
            };
            PlotModel.Series.Add(_downsampledSeries);

            _selectionRectangle = new RectangleAnnotation
            {
                Fill = OxyColor.FromAColor(100, OxyColors.LightSkyBlue),
                Stroke = OxyColors.SkyBlue,
                StrokeThickness = 1,
                MinimumX = 0,
                MaximumX = 1,
                Layer = AnnotationLayer.AboveSeries // Render above series to be clearly visible
            };
            PlotModel.Annotations.Add(_selectionRectangle);

            // Ensure selection rectangle spans the entire Y-axis of the minimap plot area initially.
            // This will be properly set when data is available.
            PlotModel.Axes[1].AxisChanged += (s, e) => UpdateSelectionRectangleYSpan();
            UpdateSelectionRectangleYSpan();

            PlotModel.MouseDown += OnMinimapMouseDown;
            PlotModel.MouseMove += OnMinimapMouseMove;
            PlotModel.MouseUp += OnMinimapMouseUp;
            PlotModel.MouseLeave += OnMinimapMouseLeave; // Handle mouse leaving the plot area
        }
        
        private void UpdateSelectionRectangleYSpan()
        {
            var yAxis = PlotModel.Axes.FirstOrDefault(ax => ax.Position == AxisPosition.Left);
            if (yAxis != null)
            {
                _selectionRectangle.MinimumY = yAxis.ActualMinimum;
                _selectionRectangle.MaximumY = yAxis.ActualMaximum;
                PlotModel.InvalidatePlot(false);
            }
        }

        /// <summary>
        /// Sets the full resolution data for the minimap and links it to the main plot model.
        /// </summary>
        /// <param name="allDataPoints">The list of all data points from the primary data source.</param>
        /// <param name="mainPlot">The plot model of the main graph.</param>
        public void SetDataAndMainPlot(List<DataPoint> allDataPoints, PlotModel mainPlot)
        {
            _fullResolutionData = allDataPoints;
            _mainPlotModel = mainPlot;
            
            UpdateDownsampledSeries();

            if (_mainPlotModel != null)
            {
                var mainXAxis = _mainPlotModel.Axes.FirstOrDefault(ax => ax.Key == "Time" || ax.Position == AxisPosition.Bottom);
                if (mainXAxis != null)
                {
                    mainXAxis.AxisChanged += OnMainPlotAxisChanged;
                }
                UpdateSelectionRectangleFromMainPlot();
            }
        }

        private void UpdateDownsampledSeries()
        {
            if (_fullResolutionData == null || !_fullResolutionData.Any())
            {
                if (_downsampledSeries.ItemsSource is ICollection<DataPoint> currentItems)
                {
                    currentItems.Clear();
                }
                else
                {
                    _downsampledSeries.Points.Clear(); // Fallback for older OxyPlot or if ItemsSource is not a collection
                }
                _downsampledSeries.ItemsSource = null;
                PlotModel.InvalidatePlot(true);
                return;
            }

            var downsampledPoints = new List<DataPoint>();
            int targetMinimapPoints = 1000;

            if (_fullResolutionData.Count <= targetMinimapPoints)
            {
                // Not enough data to warrant complex downsampling, use all points
                downsampledPoints.AddRange(_fullResolutionData);
            }
            else
            {
                double bucketInterval = (double)_fullResolutionData.Count / targetMinimapPoints;

                for (int i = 0; i < targetMinimapPoints; i++)
                {
                    int startIndex = (int)(i * bucketInterval);
                    int endIndex = (int)((i + 1) * bucketInterval) -1; // -1 because it's an index
                    endIndex = Math.Min(endIndex, _fullResolutionData.Count - 1); // Ensure endIndex is within bounds
                    if (startIndex > endIndex) startIndex = endIndex; // Handle potential empty bucket if data count is very low relative to target points

                    if (startIndex < 0 || startIndex >= _fullResolutionData.Count) continue; // Should not happen with proper calculation

                    DataPoint bucketMinPoint = _fullResolutionData[startIndex];
                    DataPoint bucketMaxPoint = _fullResolutionData[startIndex];

                    for (int j = startIndex + 1; j <= endIndex; j++)
                    {
                        if (_fullResolutionData[j].Y < bucketMinPoint.Y)
                        {
                            bucketMinPoint = _fullResolutionData[j];
                        }
                        if (_fullResolutionData[j].Y > bucketMaxPoint.Y)
                        {
                            bucketMaxPoint = _fullResolutionData[j];
                        }
                    }

                    // Add points in chronological order to maintain line connectivity
                    if (bucketMinPoint.X <= bucketMaxPoint.X)
                    {
                        downsampledPoints.Add(bucketMinPoint);
                        if (bucketMinPoint != bucketMaxPoint) // Avoid adding duplicate point if min and max are the same point
                        {
                            downsampledPoints.Add(bucketMaxPoint);
                        }
                    }
                    else
                    {
                        downsampledPoints.Add(bucketMaxPoint);
                        if (bucketMinPoint != bucketMaxPoint)
                        {
                            downsampledPoints.Add(bucketMinPoint);
                        }
                    }
                }
            }
            
            // Use ObservableCollection for ItemsSource if dynamic updates are needed,
            // otherwise List<DataPoint> is fine for one-time update.
            // For performance with LineSeries, directly assigning to ItemsSource is often best.
            _downsampledSeries.ItemsSource = downsampledPoints;

            if (downsampledPoints.Any())
            {
                var xMin = downsampledPoints.Min(p => p.X);
                var xMax = downsampledPoints.Max(p => p.X);
                PlotModel.Axes.First(ax => ax.Position == AxisPosition.Bottom).Zoom(xMin, xMax);
            
                var yMin = downsampledPoints.Min(p => p.Y);
                var yMax = downsampledPoints.Max(p => p.Y);
                var yPadding = (yMax - yMin) * 0.05;
                yPadding = yPadding == 0 && yMax == 0 ? 1 : yPadding; // Add padding if range is zero (e.g. all points are 0)
                yPadding = yPadding == 0 && yMax != 0 ? Math.Abs(yMax * 0.05) : yPadding; // Handle if min and max are same but not zero
                PlotModel.Axes.First(ax => ax.Position == AxisPosition.Left).Zoom(yMin - yPadding, yMax + yPadding);
            }
            else
            {
                PlotModel.Axes.First(ax => ax.Position == AxisPosition.Bottom).Reset();
                PlotModel.Axes.First(ax => ax.Position == AxisPosition.Left).Reset();
            }
            
            UpdateSelectionRectangleYSpan();
            PlotModel.InvalidatePlot(true);
        }

        /// <summary>
        /// Updates the selection rectangle on the minimap based on the current X-axis view of the main plot.
        /// </summary>
        public void UpdateSelectionRectangleFromMainPlot()
        {
            if (_mainPlotModel == null) return;

            var mainXAxis = _mainPlotModel.Axes.FirstOrDefault(ax => ax.Key == "Time" || ax.Position == AxisPosition.Bottom);
            if (mainXAxis == null) return;

            _selectionRectangle.MinimumX = mainXAxis.ActualMinimum;
            _selectionRectangle.MaximumX = mainXAxis.ActualMaximum;
            
            PlotModel.InvalidatePlot(false); 
        }

        /// <summary>
        /// Handles the AxisChanged event from the main plot's X-axis to update the minimap's selection rectangle.
        /// </summary>
        /// <param name="sender">The sender of the event (typically the main plot's X-axis).</param>
        /// <param name="e">The event arguments.</param>
        public void OnMainPlotAxisChanged(object sender, AxisChangedEventArgs e)
        {
            UpdateSelectionRectangleFromMainPlot();
        }
        
        /// <summary>
        /// Cleans up resources, such as unsubscribing from event handlers.
        /// </summary>
        public void Cleanup()
        {
            if (PlotModel != null)
            {
                PlotModel.MouseDown -= OnMinimapMouseDown;
                PlotModel.MouseMove -= OnMinimapMouseMove;
                PlotModel.MouseUp -= OnMinimapMouseUp;
                PlotModel.MouseLeave -= OnMinimapMouseLeave;
            }
            if (_mainPlotModel != null)
            {
                var mainXAxis = _mainPlotModel.Axes.FirstOrDefault(ax => ax.Key == "Time" || ax.Position == AxisPosition.Bottom);
                if (mainXAxis != null)
                {
                    mainXAxis.AxisChanged -= OnMainPlotAxisChanged;
                }
            }
        }

        private void OnMinimapMouseDown(object sender, OxyMouseDownEventArgs e)
        {
            if (e.ChangedButton != OxyMouseButton.Left || _selectionRectangle == null) return;

            var minimapXAxis = PlotModel.Axes.FirstOrDefault(ax => ax.Position == AxisPosition.Bottom);
            var minimapYAxis = PlotModel.Axes.FirstOrDefault(ax => ax.Position == AxisPosition.Left);
            if (minimapXAxis == null || minimapYAxis == null) return;

            // Check if mouse is within the Y bounds of the selection rectangle visually (using screen coordinates for Y)
            // This assumes selection rectangle spans the whole Y data range of the minimap plot area.
            // A more precise check might involve transforming Y coordinates if needed, but often not necessary for full-Y-span rectangle.
            var selectionScreenMinY = minimapYAxis.Transform(_selectionRectangle.ActualMinimumY);
            var selectionScreenMaxY = minimapYAxis.Transform(_selectionRectangle.ActualMaximumY);
       
            // The following check for Y bounds is commented out as the current interaction model 
            // primarily focuses on X-axis manipulation and assumes the selection rectangle 
            // vertically spans the relevant data area of the minimap.
            // if (e.Position.Y < Math.Min(selectionScreenMinY, selectionScreenMaxY) ||
            // e.Position.Y > Math.Max(selectionScreenMinY, selectionScreenMaxY))
            // {
            // return;
            // }

            double mouseDataX = minimapXAxis.InverseTransform(e.Position.X);
            _initialMousePosition = e.Position;
            _originalSelectionMinX = _selectionRectangle.MinimumX;
            _originalSelectionMaxX = _selectionRectangle.MaximumX;

            // Determine interaction type based on screen coordinates for hotspot detection
            double screenSelMinX = minimapXAxis.Transform(_selectionRectangle.MinimumX);
            double screenSelMaxX = minimapXAxis.Transform(_selectionRectangle.MaximumX);

            if (Math.Abs(e.Position.X - screenSelMinX) < EdgeResizeHotspot)
            {
                _currentInteraction = InteractionType.ResizeLeft;
            }
            else if (Math.Abs(e.Position.X - screenSelMaxX) < EdgeResizeHotspot)
            {
                _currentInteraction = InteractionType.ResizeRight;
            }
            else if (e.Position.X > screenSelMinX && e.Position.X < screenSelMaxX) // Check if inside, using screen X
            {
                _currentInteraction = InteractionType.Drag;
            }
            else
            {
                _currentInteraction = InteractionType.None;
            }

            if (_currentInteraction != InteractionType.None)
            {
                e.Handled = true;
            }
        }

        private void OnMinimapMouseMove(object sender, OxyMouseEventArgs e)
        {
            if (_currentInteraction == InteractionType.None || _selectionRectangle == null) return;

            var minimapXAxis = PlotModel.Axes.FirstOrDefault(ax => ax.Position == AxisPosition.Bottom);
            if (minimapXAxis == null) return;

            double dataMinXBound = minimapXAxis.ActualMinimum; // Data bounds of the minimap content itself
            double dataMaxXBound = minimapXAxis.ActualMaximum;

            double initialMouseDataX = minimapXAxis.InverseTransform(_initialMousePosition.X);
            double currentMouseDataX = minimapXAxis.InverseTransform(e.Position.X);
            double deltaDataX = currentMouseDataX - initialMouseDataX;

            double newMinX = _selectionRectangle.MinimumX;
            double newMaxX = _selectionRectangle.MaximumX;

            switch (_currentInteraction)
            {
                case InteractionType.Drag:
                    newMinX = _originalSelectionMinX + deltaDataX;
                    newMaxX = _originalSelectionMaxX + deltaDataX;
                    // Keep selection within data bounds
                    if (newMinX < dataMinXBound) { double shift = dataMinXBound - newMinX; newMinX += shift; newMaxX += shift; }
                    if (newMaxX > dataMaxXBound) { double shift = newMaxX - dataMaxXBound; newMinX -= shift; newMaxX -= shift; }
                    // Ensure min/max don't cross due to extreme boundary shifts
                    newMinX = Math.Max(newMinX, dataMinXBound);
                    newMaxX = Math.Min(newMaxX, dataMaxXBound);
                    if (newMinX > newMaxX - (_originalSelectionMaxX - _originalSelectionMinX)) {
                        newMinX = newMaxX - (_originalSelectionMaxX - _originalSelectionMinX); // preserve width
                    }

                    break;

                case InteractionType.ResizeLeft:
                    newMinX = _originalSelectionMinX + deltaDataX;
                    newMinX = Math.Max(newMinX, dataMinXBound); // Don't go beyond data start
                    newMinX = Math.Min(newMinX, _originalSelectionMaxX - (minimapXAxis.Transform(EdgeResizeHotspot * 2) - minimapXAxis.Transform(0)) ); // Min width in data units
                    break;

                case InteractionType.ResizeRight:
                    newMaxX = _originalSelectionMaxX + deltaDataX;
                    newMaxX = Math.Min(newMaxX, dataMaxXBound); // Don't go beyond data end
                    newMaxX = Math.Max(newMaxX, _originalSelectionMinX + (minimapXAxis.Transform(EdgeResizeHotspot * 2) - minimapXAxis.Transform(0)) ); // Min width
                    break;
            }
       
            // Final check for valid range
            if (newMinX >= newMaxX)
            {
                if (_currentInteraction == InteractionType.ResizeLeft) newMinX = newMaxX - (minimapXAxis.Transform(EdgeResizeHotspot*0.5) - minimapXAxis.Transform(0)); // a very small separation
                else if (_currentInteraction == InteractionType.ResizeRight) newMaxX = newMinX + (minimapXAxis.Transform(EdgeResizeHotspot*0.5) - minimapXAxis.Transform(0));
                // For drag, this shouldn't happen if width is preserved correctly
            }


            _selectionRectangle.MinimumX = newMinX;
            _selectionRectangle.MaximumX = newMaxX;

            UpdateMainPlotXAxis(newMinX, newMaxX);
            PlotModel.InvalidatePlot(false);
            e.Handled = true;
        }

        private void OnMinimapMouseUp(object sender, OxyMouseEventArgs e)
        {
            if (_currentInteraction != InteractionType.None)
            {
                _currentInteraction = InteractionType.None;
                e.Handled = true;
            }
        }
   
        private void OnMinimapMouseLeave(object sender, OxyMouseEventArgs e)
        {
            // If mouse leaves plot while dragging/resizing, treat as MouseUp
            if (_currentInteraction != InteractionType.None)
            {
                _currentInteraction = InteractionType.None;
                // Optionally, could complete the current drag/resize operation here based on last known position
                // For simplicity, just cancel the interaction state.
            }
        }

        /// <summary>
        /// Updates the X-axis of the main plot model to the specified new minimum and maximum values.
        /// </summary>
        /// <param name="newMinX">The new minimum X-value for the main plot's X-axis.</param>
        /// <param name="newMaxX">The new maximum X-value for the main plot's X-axis.</param>
        public void UpdateMainPlotXAxis(double newMinX, double newMaxX)
        {
            if (_mainPlotModel == null) return;
            var mainXAxis = _mainPlotModel.Axes.FirstOrDefault(ax => ax.Key == "Time" || ax.Position == AxisPosition.Bottom);
            if (mainXAxis != null)
            {
                // Check to prevent re-entrant calls or tiny changes causing loops.
                double epsilon = 1e-6; // Adjust if needed based on typical data scales
                if (Math.Abs(mainXAxis.ActualMinimum - newMinX) > epsilon || Math.Abs(mainXAxis.ActualMaximum - newMaxX) > epsilon)
                {
                    mainXAxis.Zoom(newMinX, newMaxX);
                    _mainPlotModel.InvalidatePlot(true);
                }
            }
        }
    }
}
