# ADR 001: Viewport-Aware Downsampling for Large Dataset Rendering

**Status**: Accepted
**Date**: 2026-04
**PR**: #467 (supersedes #457)

## Context

The DAQiFi Desktop application needs to display logged data sessions that can be very large — a typical worst case is 16 channels at 1000 Hz for 24 hours, producing ~1.38 billion data points. PR #458 added an overview minimap for navigating these sessions, but the main plot still rendered every data point, making the UI sluggish with large sessions.

OxyPlot (our charting library) iterates every point in a series during render, even when zoomed into a tiny region. With 1M points per channel and 16 channels, that's 16M point iterations per frame — far too slow for interactive pan/zoom.

We needed a downsampling strategy that:

1. Keeps the main plot under ~4000 points per channel regardless of dataset size
2. Shows full detail when zoomed in (not a blurry approximation)
3. Works with the minimap's ability to select arbitrary time ranges
4. Maintains 60fps during drag/resize interactions

## Decision

Use **viewport-aware MinMax downsampling**: on every viewport change, binary search the source data for the visible time range, then downsample only that slice to ~4000 points using min/max aggregation per bucket.

### How it works

1. Each channel's full dataset is kept in memory as a sorted `List<DataPoint>` (`_allSessionPoints`)
2. When the viewport changes (minimap drag, zoom, pan), `UpdateMainPlotViewport()`:
   - Binary searches for the visible range indices — O(log n) via `MinMaxDownsampler.FindVisibleRange()`
   - If the visible slice is small enough (< 4000 points), uses it directly
   - Otherwise, downsamples via `MinMaxDownsampler.Downsample(points, startIdx, endIdx, 2000)` — divides into 2000 buckets, emits the min and max Y value per bucket (up to 4000 output points)
3. The downsampled data is written into a **reusable cached list** per series (not a new allocation) and set as the series' `ItemsSource`
4. Viewport updates are **throttled to 60fps** via DispatcherTimer + dirty flag, both for minimap-driven changes and main plot pan/zoom

### Key files

- `MinMaxDownsampler.cs` — binary search + min/max downsampling algorithm
- `DatabaseLogger.UpdateMainPlotViewport()` — viewport change handler
- `MinimapInteractionController.cs` — 60fps throttled minimap interaction

## Alternatives Considered

### 1. Global LTTB Decimation (PR #457)

PR #457 applied Largest Triangle Three Buckets (LTTB) decimation globally to ~5000 points per channel at load time.

**Rejected because**: Global decimation fundamentally conflicts with the minimap. If you decimate 24 hours of data to 5000 points and then zoom into a 1-minute window, only ~3 points would be visible — the plot would be empty or misleadingly sparse. The minimap's entire purpose is to let users zoom into arbitrary ranges, so the downsampling must be viewport-aware.

LTTB also has higher computational cost per point than MinMax (triangle area calculations vs simple comparisons), which matters when re-downsampling at 60fps.

### 2. Pre-computed Multi-Resolution Pyramid

Build multiple resolution levels at load time (e.g., 1:1, 1:10, 1:100, 1:1000) and select the appropriate level based on zoom.

**Rejected because**: Significant memory overhead (nearly 2x the raw data), complex invalidation if data changes, and overkill for our use case. The binary search + linear scan approach is fast enough (~1ms for 16 channels) that on-the-fly downsampling at 60fps is feasible without pre-computation.

### 3. GPU-Accelerated Rendering

Replace OxyPlot with a GPU-backed charting library (e.g., SciChart, LiveCharts2 with SkiaSharp).

**Rejected because**: Major dependency change with significant migration cost. OxyPlot is well-integrated with our WPF MVVM architecture. Viewport-aware downsampling reduces the point count enough (~64K total) that OxyPlot renders comfortably within a 16ms frame budget. If we outgrow this approach, GPU rendering remains a future option.

### 4. Virtual Scrolling / On-Demand DB Queries

Only load the visible time range from SQLite on each viewport change, avoiding keeping all data in memory.

**Rejected because**: SQLite query latency (~5-50ms depending on range size) is too high for 60fps interaction. Users would see visible lag during minimap drag. Keeping data in memory with a practical cap (50M points, ~800MB) is the right trade-off for interactive performance. The DB index we added (`IX_Samples_SessionTime`) supports this pattern if we ever need to implement paging for truly enormous datasets.

## Consequences

### Positive

- **60fps interaction**: ~10-15ms per frame with 16 channels × 1M points
- **Full zoom fidelity**: Zooming into a 1-second window of a 24-hour session shows every data point
- **Low complexity**: The core algorithm is ~80 lines (binary search + min/max loop)
- **No external dependencies**: Pure C# implementation

### Negative

- **Memory usage**: Full dataset lives in memory. Capped at 50M points (~800MB). Sessions exceeding this are truncated with a UI warning.
- **MinMax doesn't preserve exact X boundaries**: Downsampled first/last X values may differ from source data. This required explicit time axis ranging in `ResetZoom` instead of relying on OxyPlot auto-range (see `InvalidatePlot` gotchas in CLAUDE.md).
- **List mutation pattern**: Reusing cached lists and calling `InvalidatePlot(true)` is non-obvious. `InvalidatePlot(false)` silently renders stale data. This is documented in CLAUDE.md but remains a footgun for future changes.

### Follow-up Work

- If datasets exceed the 50M point memory cap, consider hybrid approach: keep a coarse in-memory overview + on-demand DB queries for zoomed-in detail
- Monitor whether the `GetRange()` copy in the "few enough points" path becomes a bottleneck — could be replaced with a `ListSegment` wrapper if needed
- PR #457 should be closed as superseded
