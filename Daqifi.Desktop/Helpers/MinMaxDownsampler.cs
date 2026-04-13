using OxyPlot;

namespace Daqifi.Desktop.Helpers;

/// <summary>
/// Provides efficient min/max downsampling for large time-series datasets.
/// </summary>
public static class MinMaxDownsampler
{
    /// <summary>
    /// Downsamples a sorted list of data points using min/max aggregation per bucket.
    /// Produces at most <paramref name="bucketCount"/> * 2 output points.
    /// </summary>
    /// <param name="points">Time-sorted data points to downsample.</param>
    /// <param name="bucketCount">Number of buckets to divide the time range into.</param>
    /// <returns>A downsampled list of data points preserving the visual envelope.</returns>
    public static List<DataPoint> Downsample(IReadOnlyList<DataPoint> points, int bucketCount)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count == 0 || bucketCount <= 0)
        {
            return [];
        }

        return Downsample(points, 0, points.Count, bucketCount);
    }

    /// <summary>
    /// Downsamples a sub-range of a sorted list using min/max aggregation per bucket.
    /// Operates on indices [<paramref name="startIndex"/>, <paramref name="endIndex"/>)
    /// without copying the source list.
    /// </summary>
    /// <param name="points">Time-sorted data points.</param>
    /// <param name="startIndex">Inclusive start index of the sub-range.</param>
    /// <param name="endIndex">Exclusive end index of the sub-range.</param>
    /// <param name="bucketCount">Number of buckets to divide the time range into.</param>
    /// <returns>A downsampled list of data points preserving the visual envelope.</returns>
    public static List<DataPoint> Downsample(IReadOnlyList<DataPoint> points, int startIndex, int endIndex, int bucketCount)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(endIndex, points.Count);

        var count = endIndex - startIndex;
        if (count <= 0 || bucketCount <= 0)
        {
            return [];
        }

        if (count <= bucketCount * 2)
        {
            var result = new List<DataPoint>(count);
            for (var i = startIndex; i < endIndex; i++)
            {
                result.Add(points[i]);
            }
            return result;
        }

        var output = new List<DataPoint>(bucketCount * 2);

        var xMin = points[startIndex].X;
        var xMax = points[endIndex - 1].X;
        var xRange = xMax - xMin;

        if (xRange <= 0)
        {
            return [points[startIndex]];
        }

        var bucketWidth = xRange / bucketCount;
        var pointIndex = startIndex;

        for (var bucket = 0; bucket < bucketCount; bucket++)
        {
            var bucketStart = xMin + bucket * bucketWidth;
            var bucketEnd = bucketStart + bucketWidth;

            var minY = double.MaxValue;
            var maxY = double.MinValue;
            var minYX = bucketStart;
            var maxYX = bucketStart;
            var hasPoints = false;

            var isLastBucket = bucket == bucketCount - 1;
            while (pointIndex < endIndex && (isLastBucket || points[pointIndex].X < bucketEnd))
            {
                var p = points[pointIndex];
                hasPoints = true;

                if (p.Y < minY)
                {
                    minY = p.Y;
                    minYX = p.X;
                }

                if (p.Y > maxY)
                {
                    maxY = p.Y;
                    maxYX = p.X;
                }

                pointIndex++;
            }

            if (!hasPoints)
            {
                continue;
            }

            // Emit min and max in X-order to preserve visual continuity
            if (minYX <= maxYX)
            {
                output.Add(new DataPoint(minYX, minY));
                if (Math.Abs(minY - maxY) > double.Epsilon)
                {
                    output.Add(new DataPoint(maxYX, maxY));
                }
            }
            else
            {
                output.Add(new DataPoint(maxYX, maxY));
                if (Math.Abs(minY - maxY) > double.Epsilon)
                {
                    output.Add(new DataPoint(minYX, minY));
                }
            }
        }

        return output;
    }

    /// <summary>
    /// Finds the index range [startIndex, endIndex) of points whose X values
    /// fall within [xMin, xMax], with one-point padding on each side for visual continuity.
    /// Uses binary search for O(log n) performance on sorted data.
    /// </summary>
    /// <param name="sortedPoints">Time-sorted data points.</param>
    /// <param name="xMin">Minimum visible X value.</param>
    /// <param name="xMax">Maximum visible X value.</param>
    /// <returns>Tuple of (inclusive start index, exclusive end index).</returns>
    public static (int startIndex, int endIndex) FindVisibleRange(
        IReadOnlyList<DataPoint> sortedPoints, double xMin, double xMax)
    {
        if (sortedPoints.Count == 0)
        {
            return (0, 0);
        }

        // Binary search for first index where X >= xMin, then back up 1 for continuity
        var start = BinarySearchLower(sortedPoints, xMin);
        if (start > 0)
        {
            start--;
        }

        // Binary search for first index where X > xMax, then add 1 for continuity
        var end = BinarySearchUpper(sortedPoints, xMax);
        if (end < sortedPoints.Count)
        {
            end++;
        }

        return (start, end);
    }

    /// <summary>
    /// Returns the index of the first element whose X is >= value.
    /// </summary>
    private static int BinarySearchLower(IReadOnlyList<DataPoint> points, double value)
    {
        var lo = 0;
        var hi = points.Count;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (points[mid].X < value)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }
        return lo;
    }

    /// <summary>
    /// Returns the index of the first element whose X is > value.
    /// </summary>
    private static int BinarySearchUpper(IReadOnlyList<DataPoint> points, double value)
    {
        var lo = 0;
        var hi = points.Count;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (points[mid].X <= value)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }
        return lo;
    }
}
