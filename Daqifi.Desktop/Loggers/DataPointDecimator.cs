using OxyPlot;

namespace Daqifi.Desktop.Logger;

/// <summary>
/// Provides data point decimation using the Largest-Triangle-Three-Buckets (LTTB) algorithm.
/// This preserves the visual shape of time-series data while dramatically reducing point count
/// for efficient chart rendering.
/// </summary>
public static class DataPointDecimator
{
    /// <summary>
    /// The maximum number of points to display per series before decimation is applied.
    /// </summary>
    public const int DEFAULT_THRESHOLD = 5000;

    /// <summary>
    /// Downsamples a list of data points using the LTTB algorithm.
    /// Returns the original list if the count is at or below the threshold.
    /// </summary>
    /// <param name="points">The source data points, assumed sorted by X (time).</param>
    /// <param name="threshold">The target number of output points (minimum 3).</param>
    /// <returns>A decimated list of data points preserving visual shape.</returns>
    public static List<DataPoint> Decimate(List<DataPoint> points, int threshold = DEFAULT_THRESHOLD)
    {
        if (points == null || points.Count <= threshold || threshold < 3)
        {
            return points;
        }

        var result = new List<DataPoint>(threshold);

        // Always keep the first point
        result.Add(points[0]);

        // Bucket size (excluding first and last points)
        var bucketSize = (double)(points.Count - 2) / (threshold - 2);

        var previousIndex = 0;

        for (var i = 1; i < threshold - 1; i++)
        {
            // Calculate the range for this bucket
            var bucketStart = (int)Math.Floor((i - 1) * bucketSize) + 1;
            var bucketEnd = (int)Math.Floor(i * bucketSize) + 1;
            if (bucketEnd > points.Count - 1) bucketEnd = points.Count - 1;

            // Calculate the range for the next bucket (used for the average point)
            var nextBucketStart = (int)Math.Floor(i * bucketSize) + 1;
            var nextBucketEnd = (int)Math.Floor((i + 1) * bucketSize) + 1;
            if (nextBucketEnd > points.Count - 1) nextBucketEnd = points.Count - 1;

            // Calculate the average point of the next bucket
            var avgX = 0.0;
            var avgY = 0.0;
            var nextBucketCount = 0;
            for (var j = nextBucketStart; j < nextBucketEnd; j++)
            {
                avgX += points[j].X;
                avgY += points[j].Y;
                nextBucketCount++;
            }

            if (nextBucketCount > 0)
            {
                avgX /= nextBucketCount;
                avgY /= nextBucketCount;
            }

            // Find the point in the current bucket that forms the largest triangle
            var maxArea = -1.0;
            var maxIndex = bucketStart;

            var prevX = points[previousIndex].X;
            var prevY = points[previousIndex].Y;

            for (var j = bucketStart; j < bucketEnd; j++)
            {
                // Triangle area (doubled, no need for absolute since we compare)
                var area = Math.Abs(
                    (prevX - avgX) * (points[j].Y - prevY) -
                    (prevX - points[j].X) * (avgY - prevY));

                if (area > maxArea)
                {
                    maxArea = area;
                    maxIndex = j;
                }
            }

            result.Add(points[maxIndex]);
            previousIndex = maxIndex;
        }

        // Always keep the last point
        result.Add(points[^1]);

        return result;
    }

    /// <summary>
    /// Decimates data that may contain DataPoint.Undefined gap markers.
    /// Splits at gap markers, decimates each segment independently, then reassembles
    /// with gap markers preserved between segments.
    /// </summary>
    /// <param name="points">The source data points, which may contain DataPoint.Undefined gap markers.</param>
    /// <param name="threshold">The total target number of output points (minimum 3), distributed proportionally across segments.</param>
    /// <returns>A decimated list with gap markers preserved.</returns>
    public static List<DataPoint> DecimateWithGaps(List<DataPoint> points, int threshold = DEFAULT_THRESHOLD)
    {
        if (points == null || points.Count <= threshold || threshold < 3)
        {
            return points;
        }

        // Split into segments at DataPoint.Undefined markers
        var segments = new List<List<DataPoint>>();
        var currentSegment = new List<DataPoint>();

        foreach (var point in points)
        {
            if (double.IsNaN(point.X) || double.IsNaN(point.Y))
            {
                if (currentSegment.Count > 0)
                {
                    segments.Add(currentSegment);
                    currentSegment = new List<DataPoint>();
                }
            }
            else
            {
                currentSegment.Add(point);
            }
        }

        if (currentSegment.Count > 0)
        {
            segments.Add(currentSegment);
        }

        if (segments.Count == 0)
        {
            return points;
        }

        // Distribute the threshold proportionally across segments
        var totalDataPoints = segments.Sum(s => s.Count);
        var gapMarkerCount = segments.Count - 1;
        var availableThreshold = threshold - gapMarkerCount;
        if (availableThreshold < segments.Count * 3)
        {
            availableThreshold = segments.Count * 3;
        }

        var result = new List<DataPoint>(threshold);

        for (var i = 0; i < segments.Count; i++)
        {
            if (i > 0)
            {
                result.Add(DataPoint.Undefined);
            }

            var segment = segments[i];
            var segmentThreshold = Math.Max(3, (int)Math.Round((double)segment.Count / totalDataPoints * availableThreshold));
            var decimated = Decimate(segment, segmentThreshold);
            result.AddRange(decimated);
        }

        return result;
    }
}
