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
        if (points.Count <= bucketCount * 2)
        {
            return new List<DataPoint>(points);
        }

        var result = new List<DataPoint>(bucketCount * 2);

        var xMin = points[0].X;
        var xMax = points[points.Count - 1].X;
        var xRange = xMax - xMin;

        if (xRange <= 0)
        {
            return [points[0]];
        }

        var bucketWidth = xRange / bucketCount;
        var pointIndex = 0;

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
            while (pointIndex < points.Count && (isLastBucket || points[pointIndex].X < bucketEnd))
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
                result.Add(new DataPoint(minYX, minY));
                if (Math.Abs(minY - maxY) > double.Epsilon)
                {
                    result.Add(new DataPoint(maxYX, maxY));
                }
            }
            else
            {
                result.Add(new DataPoint(maxYX, maxY));
                if (Math.Abs(minY - maxY) > double.Epsilon)
                {
                    result.Add(new DataPoint(minYX, minY));
                }
            }
        }

        return result;
    }
}
