using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Daqifi.Desktop.Logger;
using OxyPlot;

namespace Daqifi.Desktop.Test.Loggers;

/// <summary>
/// Unit coverage for <see cref="PlotLogger.BuildPlotStatsSummary"/>, the pure formatter behind the
/// live-plot stats hook the UI-automation harness reads to assert the plot renders believable data
/// (issue #560). Verifies the subtle parts deterministically, without a live PlotModel or hardware:
/// gap markers are excluded from the point count, a non-finite sample VALUE is counted (not hidden),
/// <c>last</c> tracks the greatest-X sample, and numbers are always invariant-culture.
/// </summary>
[TestClass]
public class PlotStatsSummaryTests
{
    private static List<List<DataPoint>> Series(params List<DataPoint>[] series) => [.. series];

    [TestMethod]
    public void Build_NoSeries_ReportsEmptySummary()
    {
        var summary = PlotLogger.BuildPlotStatsSummary(0, Series());

        Assert.AreEqual(
            "series=0;points=0;nonfinite=0;last=NaN;min=NaN;max=NaN",
            summary,
            "An empty plot should report zero series/points and NaN value stats.");
    }

    [TestMethod]
    public void Build_FinitePoints_ReportsCountsAndExtents()
    {
        var summary = PlotLogger.BuildPlotStatsSummary(
            1,
            Series([new DataPoint(0, 1.5), new DataPoint(1, -0.5), new DataPoint(2, 2.25)]));

        // last = value at greatest X (X=2 -> 2.25); min/max span the values.
        Assert.AreEqual("series=1;points=3;nonfinite=0;last=2.25;min=-0.5;max=2.25", summary);
    }

    [TestMethod]
    public void Build_GapMarkers_AreExcludedFromPointsAndNotCountedNonFinite()
    {
        // DataPoint.Undefined is the gap marker (NaN X, NaN Y) the logger inserts between
        // discontiguous samples. It is not data and must not inflate points or nonfinite.
        var summary = PlotLogger.BuildPlotStatsSummary(
            1,
            Series([new DataPoint(0, 1.5), DataPoint.Undefined, new DataPoint(1, 2.5)]));

        Assert.AreEqual("series=1;points=2;nonfinite=0;last=2.5;min=1.5;max=2.5", summary);
    }

    [TestMethod]
    public void Build_NonFiniteSampleValues_AreCountedButExcludedFromExtents()
    {
        // Real samples (finite X) whose VALUE is NaN/Inf are genuine bad data — counted in both
        // points and nonfinite, but excluded from min/max/last so they cannot corrupt the extents.
        var summary = PlotLogger.BuildPlotStatsSummary(
            1,
            Series(
            [
                new DataPoint(0, 1.5),
                new DataPoint(1, double.NaN),
                new DataPoint(2, double.PositiveInfinity),
                new DataPoint(3, 2.5)
            ]));

        Assert.AreEqual("series=1;points=4;nonfinite=2;last=2.5;min=1.5;max=2.5", summary);
    }

    [TestMethod]
    public void Build_MultipleSeries_AggregatesPointsAndTracksGreatestXForLast()
    {
        // last must reflect the greatest X across ALL series, regardless of enumeration order.
        var summary = PlotLogger.BuildPlotStatsSummary(
            2,
            Series(
                [new DataPoint(0, 10.5), new DataPoint(5, 50.5)],
                [new DataPoint(2, 20.5), new DataPoint(10, 99.5)]));

        Assert.AreEqual("series=2;points=4;nonfinite=0;last=99.5;min=10.5;max=99.5", summary);
    }

    [TestMethod]
    public void Build_FormatsNumbersInvariant_RegardlessOfCurrentCulture()
    {
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            // A culture whose decimal separator is ',' would corrupt the ';'-delimited summary if
            // the formatter were culture-sensitive; the harness parses it as invariant.
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

            var summary = PlotLogger.BuildPlotStatsSummary(
                1, Series([new DataPoint(0, -1.5), new DataPoint(1, 2.5)]));

            Assert.AreEqual("series=1;points=2;nonfinite=0;last=2.5;min=-1.5;max=2.5", summary);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }
}
