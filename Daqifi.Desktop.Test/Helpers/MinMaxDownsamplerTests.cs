using Daqifi.Desktop.Helpers;
using OxyPlot;

namespace Daqifi.Desktop.Test.Helpers;

[TestClass]
public class MinMaxDownsamplerTests
{
    #region Downsample Tests

    [TestMethod]
    public void Downsample_EmptyList_ReturnsEmpty()
    {
        var result = MinMaxDownsampler.Downsample(new List<DataPoint>(), 10);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void Downsample_FewPointsBelowThreshold_ReturnsAll()
    {
        var points = new List<DataPoint>
        {
            new(0, 1), new(1, 2), new(2, 3)
        };
        var result = MinMaxDownsampler.Downsample(points, 10);
        Assert.AreEqual(3, result.Count);
    }

    [TestMethod]
    public void Downsample_LargeDataset_ProducesCorrectSize()
    {
        var points = new List<DataPoint>();
        for (var i = 0; i < 10000; i++)
        {
            points.Add(new DataPoint(i, Math.Sin(i * 0.01)));
        }

        var result = MinMaxDownsampler.Downsample(points, 100);
        Assert.IsTrue(result.Count <= 200);
        Assert.IsTrue(result.Count > 0);
    }

    [TestMethod]
    public void Downsample_PreservesMinMax()
    {
        var points = new List<DataPoint>();
        for (var i = 0; i < 10000; i++)
        {
            points.Add(new DataPoint(i, Math.Sin(i * 0.01)));
        }

        var result = MinMaxDownsampler.Downsample(points, 100);
        var resultMax = result.Max(p => p.Y);
        var resultMin = result.Min(p => p.Y);
        var sourceMax = points.Max(p => p.Y);
        var sourceMin = points.Min(p => p.Y);

        Assert.IsTrue(Math.Abs(resultMax - sourceMax) < 0.05, "Should preserve approximate max");
        Assert.IsTrue(Math.Abs(resultMin - sourceMin) < 0.05, "Should preserve approximate min");
    }

    #endregion

    #region Sub-range Downsample Tests

    [TestMethod]
    public void Downsample_SubRange_OperatesOnCorrectRange()
    {
        var points = new List<DataPoint>();
        for (var i = 0; i < 1000; i++)
        {
            points.Add(new DataPoint(i, i < 500 ? 0 : 100));
        }

        // Downsample only the second half (where values are 100)
        var result = MinMaxDownsampler.Downsample(points, 500, 1000, 50);
        Assert.IsTrue(result.All(p => p.Y == 100), "All downsampled points from second half should be 100");
    }

    [TestMethod]
    public void Downsample_SubRange_EmptyRange_ReturnsEmpty()
    {
        var points = new List<DataPoint> { new(0, 1), new(1, 2) };
        var result = MinMaxDownsampler.Downsample(points, 0, 0, 10);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void Downsample_SubRange_SmallRange_ReturnsAll()
    {
        var points = new List<DataPoint>();
        for (var i = 0; i < 100; i++)
        {
            points.Add(new DataPoint(i, i));
        }

        // Sub-range of 5 points with bucket count of 10 — should return all 5
        var result = MinMaxDownsampler.Downsample(points, 10, 15, 10);
        Assert.AreEqual(5, result.Count);
    }

    #endregion

    #region FindVisibleRange Tests

    [TestMethod]
    public void FindVisibleRange_EmptyList_ReturnsZeroRange()
    {
        var (start, end) = MinMaxDownsampler.FindVisibleRange(new List<DataPoint>(), 0, 10);
        Assert.AreEqual(0, start);
        Assert.AreEqual(0, end);
    }

    [TestMethod]
    public void FindVisibleRange_AllVisible_ReturnsFullRange()
    {
        var points = new List<DataPoint>
        {
            new(0, 0), new(1, 1), new(2, 2), new(3, 3), new(4, 4)
        };

        var (start, end) = MinMaxDownsampler.FindVisibleRange(points, -1, 5);
        Assert.AreEqual(0, start);
        Assert.AreEqual(5, end);
    }

    [TestMethod]
    public void FindVisibleRange_MiddleSection_ReturnsPaddedRange()
    {
        var points = new List<DataPoint>();
        for (var i = 0; i < 100; i++)
        {
            points.Add(new DataPoint(i, i));
        }

        var (start, end) = MinMaxDownsampler.FindVisibleRange(points, 30, 60);

        // Should include padding: one point before 30 and one point after 60
        Assert.IsTrue(start <= 29, $"Start ({start}) should be at or before index 29");
        Assert.IsTrue(end >= 62, $"End ({end}) should be at or after index 62");
    }

    [TestMethod]
    public void FindVisibleRange_NoPointsInRange_IncludesAdjacentPoints()
    {
        var points = new List<DataPoint>
        {
            new(0, 0), new(1, 1), new(10, 10), new(11, 11)
        };

        // Range 5-8 has no points; binary search lands between index 1 (X=1) and 2 (X=10)
        // With padding: start backs up 1 from index 2 → 1, end advances 1 from index 2 → 3
        var (start, end) = MinMaxDownsampler.FindVisibleRange(points, 5, 8);
        Assert.AreEqual(1, start, "Should include point at X=1 (one before gap)");
        Assert.AreEqual(3, end, "Should include point at X=10 (one after gap)");
    }

    [TestMethod]
    public void FindVisibleRange_AtBoundaries_ClampsCorrectly()
    {
        var points = new List<DataPoint>
        {
            new(0, 0), new(1, 1), new(2, 2)
        };

        // Range starts before data
        var (start, _) = MinMaxDownsampler.FindVisibleRange(points, -10, 1);
        Assert.AreEqual(0, start, "Start should be clamped to 0");

        // Range ends after data
        var (_, end) = MinMaxDownsampler.FindVisibleRange(points, 1, 100);
        Assert.AreEqual(3, end, "End should be clamped to list length");
    }

    [TestMethod]
    public void FindVisibleRange_SinglePoint_ReturnsIt()
    {
        var points = new List<DataPoint> { new(5, 10) };

        var (start, end) = MinMaxDownsampler.FindVisibleRange(points, 0, 10);
        Assert.AreEqual(0, start);
        Assert.AreEqual(1, end);
    }

    [TestMethod]
    public void FindVisibleRange_LargeDataset_Performance()
    {
        var points = new List<DataPoint>();
        for (var i = 0; i < 1_000_000; i++)
        {
            points.Add(new DataPoint(i, Math.Sin(i * 0.001)));
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++)
        {
            MinMaxDownsampler.FindVisibleRange(points, 400000, 600000);
        }
        sw.Stop();

        // 1000 binary searches on 1M points should be well under 1 second,
        // even on slow CI runners. Typical desktop: < 10ms.
        Assert.IsTrue(sw.ElapsedMilliseconds < 1000,
            $"1000 binary searches took {sw.ElapsedMilliseconds}ms, expected < 1000ms");
    }

    #endregion
}
