namespace Daqifi.Desktop.Test.TestSupport;

/// <summary>
/// Reports wall-clock performance budgets, and enforces them only when explicitly opted into.
/// </summary>
/// <remarks>
/// <para>
/// Elapsed time and throughput measure the machine, not the code under test. A concurrent build or
/// test run in another worktree is enough to push the exporter below a "&gt;50K samples/second"
/// floor with nothing about the exporter changed — the assertion then fails, passes on re-run, and
/// teaches everyone to ignore it. That is worse than no assertion at all.
/// </para>
/// <para>
/// So timing budgets are reported by default and never fail the build. Set
/// <c>DAQIFI_ENFORCE_PERF_ASSERTIONS=1</c> to turn them back into hard failures when benchmarking
/// deliberately on an otherwise idle machine.
/// </para>
/// <para>
/// This applies to timing only. Load-independent assertions — output correctness, row counts, peak
/// memory, and the hard <c>[Timeout]</c>/<c>CancellationTokenSource</c> hang guards — stay
/// unconditional, and remain the real regression signal.
/// </para>
/// </remarks>
internal static class PerformanceBudget
{
    #region Constants
    private const string ENFORCE_VARIABLE = "DAQIFI_ENFORCE_PERF_ASSERTIONS";
    #endregion

    #region Properties
    /// <summary>Whether exceeded budgets fail the test rather than just being reported.</summary>
    public static bool IsEnforced { get; } =
        Environment.GetEnvironmentVariable(ENFORCE_VARIABLE) is "1" or "true" or "TRUE" or "yes" or "YES";
    #endregion

    #region Public Methods
    /// <summary>
    /// Records that an operation completed in <paramref name="elapsedMs"/> against a budget of
    /// <paramref name="budgetMs"/> milliseconds.
    /// </summary>
    public static void ExpectElapsedUnder(long budgetMs, long elapsedMs, string label)
    {
        Report(elapsedMs <= budgetMs, $"{label}: took {elapsedMs}ms, budget {budgetMs}ms");
    }

    /// <summary>
    /// Records that an operation sustained <paramref name="samplesPerSecond"/> against a floor of
    /// <paramref name="minSamplesPerSecond"/>.
    /// </summary>
    public static void ExpectThroughputOver(double minSamplesPerSecond, double samplesPerSecond, string label)
    {
        Report(
            samplesPerSecond >= minSamplesPerSecond,
            FormattableString.Invariant(
                $"{label}: {samplesPerSecond:F0} samples/second, floor {minSamplesPerSecond:F0} samples/second"));
    }
    #endregion

    #region Private Methods
    private static void Report(bool withinBudget, string message)
    {
        if (withinBudget)
        {
            Console.WriteLine($"[perf] OK — {message}");
            return;
        }

        if (IsEnforced)
        {
            Assert.Fail($"[perf] BUDGET EXCEEDED — {message}");
        }

        Console.WriteLine(
            $"[perf] BUDGET EXCEEDED — {message} (not enforced; set {ENFORCE_VARIABLE}=1 to fail on this)");
    }
    #endregion
}
