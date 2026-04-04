using Daqifi.Desktop.Logger;
using OxyPlot;

namespace Daqifi.Desktop.Test.Loggers;

[TestClass]
public class DataPointDecimatorTests
{
    #region Null and Small Input Tests
    [TestMethod]
    public void Decimate_NullInput_ReturnsNull()
    {
        var result = DataPointDecimator.Decimate(null);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Decimate_EmptyList_ReturnsSameList()
    {
        var points = new List<DataPoint>();
        var result = DataPointDecimator.Decimate(points);
        Assert.AreSame(points, result);
    }

    [TestMethod]
    public void Decimate_PointsBelowThreshold_ReturnsSameList()
    {
        var points = GenerateLinearPoints(100);
        var result = DataPointDecimator.Decimate(points, 200);
        Assert.AreSame(points, result);
    }

    [TestMethod]
    public void Decimate_PointsEqualToThreshold_ReturnsSameList()
    {
        var points = GenerateLinearPoints(100);
        var result = DataPointDecimator.Decimate(points, 100);
        Assert.AreSame(points, result);
    }

    [TestMethod]
    public void Decimate_ThresholdLessThan3_ReturnsSameList()
    {
        var points = GenerateLinearPoints(100);
        var result = DataPointDecimator.Decimate(points, 2);
        Assert.AreSame(points, result);
    }
    #endregion

    #region Output Size Tests
    [TestMethod]
    public void Decimate_ReturnsExactlyThresholdPoints()
    {
        var points = GenerateLinearPoints(10000);
        const int threshold = 500;

        var result = DataPointDecimator.Decimate(points, threshold);

        Assert.AreEqual(threshold, result.Count);
    }

    [TestMethod]
    public void Decimate_DefaultThreshold_Returns5000Points()
    {
        var points = GenerateLinearPoints(20000);

        var result = DataPointDecimator.Decimate(points);

        Assert.AreEqual(DataPointDecimator.DEFAULT_THRESHOLD, result.Count);
    }
    #endregion

    #region First and Last Point Preservation
    [TestMethod]
    public void Decimate_PreservesFirstAndLastPoints()
    {
        var points = GenerateSineWavePoints(10000);
        const int threshold = 100;

        var result = DataPointDecimator.Decimate(points, threshold);

        Assert.AreEqual(points[0].X, result[0].X, "First point X should be preserved");
        Assert.AreEqual(points[0].Y, result[0].Y, "First point Y should be preserved");
        Assert.AreEqual(points[^1].X, result[^1].X, "Last point X should be preserved");
        Assert.AreEqual(points[^1].Y, result[^1].Y, "Last point Y should be preserved");
    }
    #endregion

    #region Visual Fidelity Tests
    [TestMethod]
    public void Decimate_SineWave_PreservesPeaksAndTroughs()
    {
        // Generate a sine wave with known peaks and troughs
        const int pointCount = 10000;
        const int threshold = 200;
        var points = GenerateSineWavePoints(pointCount);

        var result = DataPointDecimator.Decimate(points, threshold);

        // Find max and min Y in the decimated result
        var maxY = result.Max(p => p.Y);
        var minY = result.Min(p => p.Y);

        // The decimated data should preserve the amplitude of the sine wave
        Assert.IsTrue(maxY > 0.95, $"Max Y ({maxY}) should be close to 1.0 (sine peak)");
        Assert.IsTrue(minY < -0.95, $"Min Y ({minY}) should be close to -1.0 (sine trough)");
    }

    [TestMethod]
    public void Decimate_SpikeInFlatData_PreservesSpike()
    {
        // Create flat data with a single spike - LTTB should preserve the spike
        var points = new List<DataPoint>();
        for (var i = 0; i < 10000; i++)
        {
            var y = i == 5000 ? 100.0 : 0.0;
            points.Add(new DataPoint(i, y));
        }

        var result = DataPointDecimator.Decimate(points, 100);

        // The spike should be preserved because it forms the largest triangle
        var maxY = result.Max(p => p.Y);
        Assert.AreEqual(100.0, maxY, "Spike should be preserved in decimated output");
    }
    #endregion

    #region Ordering Tests
    [TestMethod]
    public void Decimate_OutputIsOrderedByX()
    {
        var points = GenerateSineWavePoints(10000);
        const int threshold = 500;

        var result = DataPointDecimator.Decimate(points, threshold);

        for (var i = 1; i < result.Count; i++)
        {
            Assert.IsTrue(result[i].X >= result[i - 1].X,
                $"Point at index {i} (X={result[i].X}) should be >= point at index {i - 1} (X={result[i - 1].X})");
        }
    }
    #endregion

    #region Large Dataset Performance
    [TestMethod]
    public void Decimate_LargeDataset_CompletesQuickly()
    {
        // 1 million points should decimate in well under a second
        var points = GenerateLinearPoints(1_000_000);
        const int threshold = 5000;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = DataPointDecimator.Decimate(points, threshold);
        stopwatch.Stop();

        Assert.AreEqual(threshold, result.Count);
        Assert.IsTrue(stopwatch.ElapsedMilliseconds < 1000,
            $"Decimation of 1M points took {stopwatch.ElapsedMilliseconds}ms, expected < 1000ms");
    }
    #endregion

    #region DecimateWithGaps Tests
    [TestMethod]
    public void DecimateWithGaps_NullInput_ReturnsNull()
    {
        var result = DataPointDecimator.DecimateWithGaps(null);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void DecimateWithGaps_NoGaps_BehavesLikeDecimate()
    {
        var points = GenerateLinearPoints(10000);
        const int threshold = 500;

        var result = DataPointDecimator.DecimateWithGaps(points, threshold);

        // Should produce roughly the same count as Decimate
        Assert.IsTrue(result.Count <= threshold + 1);
        Assert.IsTrue(result.Count > 0);
        // No NaN points in the result
        Assert.IsFalse(result.Any(p => double.IsNaN(p.X)));
    }

    [TestMethod]
    public void DecimateWithGaps_PreservesGapMarkers()
    {
        // Create data with 2 gaps (3 segments)
        var points = new List<DataPoint>();
        for (var i = 0; i < 5000; i++)
        {
            points.Add(new DataPoint(i, i * 0.1));
        }
        points.Add(DataPoint.Undefined);
        for (var i = 5001; i < 10000; i++)
        {
            points.Add(new DataPoint(i, i * 0.1));
        }
        points.Add(DataPoint.Undefined);
        for (var i = 10001; i < 15000; i++)
        {
            points.Add(new DataPoint(i, i * 0.1));
        }

        var result = DataPointDecimator.DecimateWithGaps(points, 300);

        // Count gap markers in result
        var gapCount = result.Count(p => double.IsNaN(p.X));
        Assert.AreEqual(2, gapCount, "Should preserve both gap markers");
    }

    [TestMethod]
    public void DecimateWithGaps_BelowThreshold_ReturnsSameList()
    {
        var points = new List<DataPoint>();
        for (var i = 0; i < 50; i++)
        {
            points.Add(new DataPoint(i, i));
        }
        points.Add(DataPoint.Undefined);
        for (var i = 51; i < 100; i++)
        {
            points.Add(new DataPoint(i, i));
        }

        var result = DataPointDecimator.DecimateWithGaps(points, 200);
        Assert.AreSame(points, result);
    }

    [TestMethod]
    public void DecimateWithGaps_RespectsGlobalThreshold()
    {
        // Create data with many gaps to stress the threshold enforcement
        var points = new List<DataPoint>();
        const int segmentSize = 2000;
        const int segmentCount = 10;
        for (var seg = 0; seg < segmentCount; seg++)
        {
            if (seg > 0) points.Add(DataPoint.Undefined);
            var offset = seg * (segmentSize + 100);
            for (var i = 0; i < segmentSize; i++)
            {
                points.Add(new DataPoint(offset + i, Math.Sin(i * 0.01)));
            }
        }

        const int threshold = 500;
        var result = DataPointDecimator.DecimateWithGaps(points, threshold);

        // Total output (data points + gap markers) should not exceed the threshold
        Assert.IsTrue(result.Count <= threshold,
            $"DecimateWithGaps returned {result.Count} points, expected <= {threshold}");

        // Gap markers should still be preserved
        var gapCount = result.Count(p => double.IsNaN(p.X));
        Assert.AreEqual(segmentCount - 1, gapCount, "All gap markers should be preserved");
    }

    [TestMethod]
    public void DecimateWithGaps_PreservesFirstAndLastOfEachSegment()
    {
        var points = new List<DataPoint>();
        // Segment 1: 0-4999
        for (var i = 0; i < 5000; i++)
        {
            points.Add(new DataPoint(i, Math.Sin(i * 0.01)));
        }
        points.Add(DataPoint.Undefined);
        // Segment 2: 6000-10999
        for (var i = 6000; i < 11000; i++)
        {
            points.Add(new DataPoint(i, Math.Cos(i * 0.01)));
        }

        var result = DataPointDecimator.DecimateWithGaps(points, 500);

        // Find the gap marker index
        var gapIndex = result.FindIndex(p => double.IsNaN(p.X));
        Assert.IsTrue(gapIndex > 0, "Gap marker should exist");

        // First point of first segment preserved
        Assert.AreEqual(0.0, result[0].X, "First point of segment 1 should be preserved");

        // Last point before gap preserved
        Assert.AreEqual(4999.0, result[gapIndex - 1].X, "Last point of segment 1 should be preserved");

        // First point after gap preserved
        Assert.AreEqual(6000.0, result[gapIndex + 1].X, "First point of segment 2 should be preserved");

        // Last point of second segment preserved
        Assert.AreEqual(10999.0, result[^1].X, "Last point of segment 2 should be preserved");
    }
    #endregion

    #region Helper Methods
    private static List<DataPoint> GenerateLinearPoints(int count)
    {
        var points = new List<DataPoint>(count);
        for (var i = 0; i < count; i++)
        {
            points.Add(new DataPoint(i, i * 0.1));
        }
        return points;
    }

    private static List<DataPoint> GenerateSineWavePoints(int count)
    {
        var points = new List<DataPoint>(count);
        for (var i = 0; i < count; i++)
        {
            var x = i * 0.01;
            var y = Math.Sin(x * 2 * Math.PI);
            points.Add(new DataPoint(x, y));
        }
        return points;
    }
    #endregion
}
